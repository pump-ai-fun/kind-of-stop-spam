using kind_of_stop_spam;
using Microsoft.Playwright;
using System;
using System.IO;
using System.Text.RegularExpressions;

// --------------------------------------------------------------------------------------
// PumpFunTool
// Opens a Pump.fun coin page, captures live chat messages, filters them (keywords, rate
// limit, dedup) and writes accepted messages to filtered-chat.html for use in overlays.
// --------------------------------------------------------------------------------------
// Usage:
//   PumpFunTool <token_address> [path-to-config-json]
// Notes:
//   * Optional config JSON (chat-filter-cfg.json when omitted) defines banned keywords,
//     mentions and wallet->icon mappings.
//   * Press any key in the console window to stop.
//   * Output file: ./filtered-chat.html (includes auto-refresh + toggles at bottom).
//   * First pass (historical backlog) is relaxed and only applies banned-word checks so
//     you get context quickly; subsequent passes use strict live mode.
// --------------------------------------------------------------------------------------
class Program
{
    // Maximum number of most recent DOM messages examined per polling iteration.
    private const int MaxMessagesPerScan = 23;

    static async Task Main(string[] args)
    {
        // Step 1: Parse & validate arguments
        if (args.Length is < 1 or > 2)
        {
            Console.WriteLine("Usage: PumpFunTool <token_address> [path-to-config-json]");
            return;
        }
        string tokenAddress = args[0];
        string? configPath = args.Length == 2 ? args[1] : "./chat-filter-cfg.json";

        // Basic token sanity check (trim early to avoid needless Playwright startup)
        var pattern = @"^[1-9A-HJ-NP-Za-km-z]{20,50}pump$";
        if (!Regex.IsMatch(tokenAddress, pattern))
        {
            Console.WriteLine("Error: Invalid PumpFun token address format.");
            return;
        }

        // Step 2: Initialize Playwright (one lifetime scope)
        using var playwright = await Playwright.CreateAsync();

        // Step 3: Create pages (viewer + source)
        var chatPage = await SetupChatPage(playwright);       // local filtered view
        var streamPage = await SetupStreamPage(playwright, tokenAddress); // live site

        // Step 4: Load configuration & instantiate filter
        var cfg = ChatFilterConfig.LoadOrDefault(configPath);
        var filter = new SpamFilter(
            perUserWindow: TimeSpan.FromSeconds(2),
            dedupTtl: TimeSpan.FromMinutes(3),
            config: cfg);

        // Step 5: Create HTML output view (slightly above scan window so we keep a buffer)
        using var view = new HtmlFileChatView(
            filePath: "./filtered-chat.html",
            capacity: MaxMessagesPerScan + 5,
            title: $"Filtered Chat - {tokenAddress}");

        // Step 6: Prepare chat DOM (mute, trim heavy panels)
        await LoadChat(streamPage);

        // Phase tracking
        bool historicalPhase = true;              // relaxed first sweep
        var historicalIds = new HashSet<string>();
        var liveIds = new HashSet<string>();

        // Step 7: Poll loop (ESC style key-stop simplified to any key press)
        while (true)
        {
            if (Console.KeyAvailable)
            {
                Console.ReadKey(intercept: true);
                break;
            }

            ILocator allMessages;
            int allMessagesCount;
            try
            {
                allMessages = streamPage.Locator("div[data-message-id]");
                allMessagesCount = await allMessages.CountAsync();

                if (allMessagesCount == 0)
                {
                    var html = await streamPage.ContentAsync();
                    if (html.Contains("Application error: a client-side exception", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Pump.fun client-side exception detected – reloading page.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warn] Page issue detected – attempting reload. Detail: {ex.Message}");
                await streamPage.ReloadAsync();
                await LoadChat(streamPage);
                continue;
            }

            if (allMessagesCount == 0)
            {
                await Task.Delay(250);
                continue;
            }

            int startAt = Math.Max(0, allMessagesCount - MaxMessagesPerScan);
            for (int idx = startAt; idx < allMessagesCount; idx++)
            {
                try
                {
                    var node = allMessages.Nth(idx);
                    string? msgId = await node.GetAttributeAsync("data-message-id");
                    if (string.IsNullOrEmpty(msgId)) continue;

                    if (historicalPhase)
                    {
                        if (!historicalIds.Add(msgId))
                            continue; // already processed in relaxed mode
                    }
                    else
                    {
                        if (liveIds.Contains(msgId))
                            continue; // already emitted
                    }

                    // Full block text (user\n\ncontent\n\nclockTime)
                    var rawBlock = await node.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 2000 });

                    // Reply snippet extraction (if structure present)
                    string? replyingTo = null;
                    try
                    {
                        var replySpans = node.Locator("span.leading-snug");
                        var rc = await replySpans.CountAsync();
                        if (rc > 0)
                        {
                            replyingTo = await replySpans.Nth(rc - 1).InnerTextAsync();
                            var firstBreak = rawBlock.IndexOf('\n');
                            if (firstBreak > 0 && firstBreak + 1 < rawBlock.Length)
                                rawBlock = rawBlock[(firstBreak + 1)..];
                        }
                    }
                    catch { replyingTo = null; }

                    var mode = historicalPhase ? SpamFilter.Mode.Historical : SpamFilter.Mode.Live;
                    if (filter.TryAcceptRaw(rawBlock, replyingTo, mode, out var accepted))
                    {
                        if (!historicalPhase)
                            liveIds.Add(msgId);
                        view.Add(accepted);
                    }
                }
                catch
                {
                    // Ignore transient DOM / timing issues for individual messages
                    continue;
                }
            }

            if (historicalPhase)
            {
                historicalPhase = false;
                historicalIds.Clear();
                Console.WriteLine("[Info] Historical backlog ingested. Switching to strict live filtering.");
            }

            await Task.Delay(150);
        }
    }

    /// <summary>
    /// Ensure chat container is present, mute video/audio and remove heavy non-chat sections.
    /// </summary>
    public static async Task LoadChat(IPage page)
    {
        try
        {
            var chatArea = page.Locator("div:has-text('live chat'):has(div[data-message-id])").First;
            await chatArea.WaitForAsync();
            await DomHelpers.EnsureVideoMutedAndStoppedAsync(page.Locator("video"));
            await page.Context.StorageStateAsync(new() { Path = "./sessions.data" });
            await DomHelpers.RemoveElementAsync(page.Locator("#coin-content-container"));
        }
        catch (Exception)
        {
            Console.WriteLine("Error: Timeout waiting for chat area. Check token address and login state.");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Create the local viewer page hosting filtered-chat.html.
    /// </summary>
    public static async Task<IPage> SetupChatPage(IPlaywright playwright)
    {
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
            Headless = false,
            Args = new[] { "--disable-blink-features=AutomationControlled", "--mute-audio", "--window-size=600,1000" }
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = ViewportSize.NoViewport,
            StorageStatePath = "./sessions.data",
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36",
        });
        var page = await context.NewPageAsync();
        string htmlPath = Path.GetFullPath("./filtered-chat.html");
        await page.GotoAsync($"file:///{htmlPath.Replace("\\", "/")}");
        page.PageError += (_, error) => Console.WriteLine($"[Page Error] {error}");
        page.RequestFailed += async (_, __) => { try { await page.GotoAsync($"file:///{htmlPath.Replace("\\", "/")}"); } catch { } };
        return page;
    }

    /// <summary>
    /// Create the scraping page pointed at the Pump.fun coin URL.
    /// </summary>
    public static async Task<IPage> SetupStreamPage(IPlaywright playwright, string tokenAddress)
    {
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
            Headless = false,
            Args = new[] { "--disable-blink-features=AutomationControlled", "--mute-audio" }
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = null, // allow manual resize
            StorageStatePath = "./sessions.data",
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36",
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"https://pump.fun/coin/{tokenAddress}");
        return page;
    }
}

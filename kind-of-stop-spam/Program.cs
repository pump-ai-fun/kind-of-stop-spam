using kind_of_stop_spam;
using Microsoft.Playwright;
using System;
using System.IO;
using System.Runtime.Intrinsics.Arm;
using System.Text.RegularExpressions;

/// <summary>
/// PumpFunTool: opens a Pump.fun coin page, extracts live chat messages,
/// filters them using SpamFilter + ChatFilterConfig, and writes a live HTML view of kept messages.
///
/// Usage:
///   PumpFunTool <token_address> [path-to-config-json]
///
/// Notes:
/// - Optional config JSON (chat-filter-cfg.json by default when omitted) supports banned keywords/mentions.
/// - Press any key in the console to stop.
/// - Output HTML: ./filtered-chat.html (auto-refresh, optional auto-scroll toggle)
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // 1) Parse command line arguments for live mode
        if (args.Length < 1 || args.Length > 2)
        {
            Console.WriteLine("Usage: PumpFunTool <token_address> [path-to-config-json]");
            Environment.Exit(1);
        }
        string tokenAddress = args[0];
        string? configPath = args.Length == 2 ? args[1] : "./chat-filter-cfg.json";

        var pattern = @"^[1-9A-HJ-NP-Za-km-z]{20,50}pump$"; // Example: 2McSmYfSEKUMQEq4JZbb9wq2SeyLrxkd9831EB9Vpump
        if (!Regex.IsMatch(tokenAddress, pattern))
        {
            Console.WriteLine("Error: Invalid PumpFun token address format.");
            Environment.Exit(1);
        }

        // 2) Initialize Playwright and open browser
        using var playwright = await Playwright.CreateAsync();
        

        // Open the local filtered HTML view in a separate tab for convenience
        var chatPage = await SetupChatPage(playwright);

        // Open the Pump.fun live chat page
        var streamPage = await SetupStreamPage(playwright, tokenAddress);

        // 3) Instantiate filter(s)
        var cfg = ChatFilterConfig.LoadOrDefault(configPath);
        var filter = new SpamFilter(
            perUserWindow: TimeSpan.FromSeconds(2),
            dedupTtl: TimeSpan.FromMinutes(3),
            config: cfg
        );

        // View that auto-refreshes and can auto-scroll to show filtered-only messages
        using var view = new HtmlFileChatView(filePath: "./filtered-chat.html", capacity: 500, title: $"Filtered Chat - {tokenAddress}");

        // 4) Wait for chat to be visible and trim the page for performance
        await LoadChat(streamPage);

        // 5) Loop: collect, filter and render
        Dictionary<string, bool> ParsedMsgs = new(); // true if aproved by filter
        while (true)
        {
            if (Console.KeyAvailable) // detects key press
            {
                Console.ReadKey(intercept: true); // consume the key
                break;
            }

            ILocator allMessages = null;
            int allMessagesCount = 0;
            try
            {
                // Traverse all messages (limit to the last 1000 for performance)
                allMessages = streamPage.Locator("div[data-message-id]");
                allMessagesCount = await allMessages.CountAsync();

                if (allMessagesCount == 0)
                {
                    if ((await streamPage.ContentAsync()).Contains("Application error: a client-side exception"))
                    {
                        throw new Exception("Pump.fun application error detected, reloading the page.");
                    }
                }
            }
            catch (Exception)
            {
                await streamPage.ReloadAsync();
                await LoadChat(streamPage);
                continue;
            }

            int startAt = Math.Max(1, allMessagesCount - 1000);
            for (int m = startAt; m < allMessagesCount; m++)
            {
                try
                {
                    var message = allMessages.Nth(m);
                    // Use the full element text so SpamFilter parsing (user\n\ncontent\n\ntime) remains intact
                    var messageContent = await message.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 2000 });
                    if (ParsedMsgs.ContainsKey(messageContent))
                    {
                        continue; // already processed
                    }
                    else
                    {
                        ParsedMsgs.Add(messageContent, false); // mark as processed (default to not accepted)
                    }
                    // Detect and handle messages replying to something
                    string? replyingTo = null;
                    try
                    {
                        var replySpans = message.Locator("span.leading-snug");
                        var count = await replySpans.CountAsync();
                        if (count > 0)
                        {
                            replyingTo = await replySpans.Nth(count - 1).InnerTextAsync();
                            messageContent = messageContent.Substring(messageContent.IndexOf("\n") + 1);
                        }
                    }
                    catch { replyingTo = null; }

                    if (filter.TryAcceptRaw(messageContent, replyingTo, out var msg))
                    {
                        ParsedMsgs[messageContent] = true; // mark as accepted
                        view.Add(msg);
                    }
                }
                catch
                {
                    // Ignore transient DOM issues (detached nodes, timeouts) and continue
                    continue;
                }
            }

            System.Threading.Thread.Sleep(250); // Avoid busy loop
        }
    }

    public static async Task LoadChat(IPage page)
    {
        try
        {
            var chatArea = page.Locator("div:has-text('live chat'):has(div[data-message-id])").First;
            await chatArea.WaitForAsync();
            await DomHelpers.EnsureVideoMutedAndStoppedAsync(page.Locator("video"));
            await page.Context.StorageStateAsync(new() { Path = "./sessions.data" }); // keep session updated
            await DomHelpers.RemoveElementAsync(page.Locator("#coin-content-container")); // keep only chat
        }
        catch (Exception)
        {
            Console.WriteLine("Error: Timeout waiting for chat area to load. Is the token address correct? Are you signed in?");
            Environment.Exit(1);
        }
    }

    public static async Task<IPage> SetupChatPage(IPlaywright playwright)
    {
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
            Headless = false,
            Args = new[] { "--disable-blink-features=AutomationControlled", "--mute-audio", "--window-size=600,1000" }
        });
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions()
        {
            ViewportSize = ViewportSize.NoViewport,
            StorageStatePath = "./sessions.data",
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36",
        });

        // Open the local filtered HTML view in a separate tab for convenience
        var chatPage = await browserContext.NewPageAsync();
        string chatHtmlFile = Path.GetFullPath("./filtered-chat.html");
        await chatPage.GotoAsync($"file:///{chatHtmlFile.Replace("\\", "/")}");
        chatPage.PageError += (_, error) =>
        {
            Console.WriteLine($"[Page Error] {error}");
        };
        chatPage.RequestFailed += async (_, request) =>
        {
            try
            {
                await chatPage.GotoAsync($"file:///{chatHtmlFile.Replace("\\", "/")}");
            }
            catch { /*ignore*/ }
        };

        return chatPage;
    }

    public static async Task<IPage> SetupStreamPage(IPlaywright playwright, string tokenAddress)
    {
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
            Headless = false,
            Args = new[] { "--disable-blink-features=AutomationControlled", "--mute-audio" }
        });
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions()
        {
            ViewportSize = null, // allows window resize to affect page size
            StorageStatePath = "./sessions.data",
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36",
        });

        var streamPage = await browserContext.NewPageAsync();
        await streamPage.GotoAsync($"https://pump.fun/coin/{tokenAddress}");
        return streamPage;
    }
}

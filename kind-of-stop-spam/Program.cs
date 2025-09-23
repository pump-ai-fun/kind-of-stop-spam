using kind_of_stop_spam;
using Microsoft.Playwright;
using System;
using System.IO;
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
        string? configPath = args.Length == 2 ? args[1] : null;

        var pattern = @"^[1-9A-HJ-NP-Za-km-z]{20,50}pump$"; // Example: 2McSmYfSEKUMQEq4JZbb9wq2SeyLrxkd9831EB9Vpump
        if (!Regex.IsMatch(tokenAddress, pattern))
        {
            Console.WriteLine("Error: Invalid PumpFun token address format.");
            Environment.Exit(1);
        }

        // 2) Initialize Playwright and open browser
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
            Headless = false,
            Args = new[] { "--disable-blink-features=AutomationControlled" }
        });
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions()
        {
            StorageStatePath = "./sessions.data",
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36",
        });

        // Open the local filtered HTML view in a separate tab for convenience
        var chatPage = await browserContext.NewPageAsync();
        string chatHtmlFile = Path.GetFullPath("./filtered-chat.html");
        await chatPage.GotoAsync($"file:///{chatHtmlFile.Replace("\\", "/")}");

        // Open the Pump.fun live chat page
        var page = await browserContext.NewPageAsync();
        await page.GotoAsync($"https://pump.fun/coin/{tokenAddress}");

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
        var chatArea = page.Locator("div:has-text('live chat'):has(div[data-message-id])").First;
        await chatArea.WaitForAsync();
        await DomHelpers.EnsureVideoMutedAndStoppedAsync(page.Locator("video"));
        await page.Context.StorageStateAsync(new() { Path = "./sessions.data" }); // keep session updated
        await DomHelpers.RemoveElementAsync(page.Locator("#coin-content-container")); // keep only chat

        // 5) Loop: collect, filter and render
        while (true)
        {
            if (Console.KeyAvailable) // detects key press
            {
                Console.ReadKey(intercept: true); // consume the key
                break;
            }

            // Traverse all messages (limit to the last 1000 for performance)
            var allMessages = page.Locator("div[data-message-id]");
            var allMessagesCount = await allMessages.CountAsync();
            int startAt = Math.Max(1, allMessagesCount - 1000);
            for (int m = startAt; m < allMessagesCount; m++)
            {
                try
                {
                    var message = allMessages.Nth(m);
                    var messageContent = await message.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 2000 });
                    if (filter.TryAcceptRaw(messageContent, out var msg))
                    {
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

        // 6) Cleanup
        await browser.CloseAsync();
    }
}

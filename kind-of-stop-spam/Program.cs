using kind_of_stop_spam;
using Microsoft.Playwright;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

public sealed class PumpFunSettings : CommandSettings
{
    [CommandOption("--token <TOKEN>")]
    [Description("Pump.fun token address (expects base58 + 'pump' suffix)")]
    public string Token { get; init; } = string.Empty;

    [CommandOption("--config <PATH>")]
    [Description("Path to chat filter config JSON (default: ./chat-filter-cfg.json)")]
    public string ConfigPath { get; init; } = "./chat-filter-cfg.json";

    [CommandOption("--session")]
    [Description("Session capture mode (interactive login, saves ./sessions.data then exits)")]
    public bool SessionMode { get; init; }

    [CommandOption("--verbose")]
    [Description("Verbose diagnostics output (per accepted message content)")]
    public bool Verbose { get; init; }

    [CommandOption("--show-viewer")]
    [Description("Launch the local overlay viewer window (interactive)")]
    public bool ShowViewer { get; init; }

    [CommandOption("--port <PORT>")]
    [Description("Port for built-in static file server used for viewer/polling (default 17999)")]
    public int Port { get; init; } = 17999;

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Token))
            return ValidationResult.Error("--token is required");
        var pattern = new Regex(@"^[1-9A-HJ-NP-Za-km-z]{20,50}pump$", RegexOptions.Compiled);
        if (!pattern.IsMatch(Token))
            return ValidationResult.Error("Token format invalid (must be 20-50 base58 chars followed by 'pump')");
        if (Port is < 1024 or > 65535) return ValidationResult.Error("Port must be between 1024 and 65535");
        return ValidationResult.Success();
    }
}

internal sealed class StaticFileServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _root;
    private readonly CancellationTokenSource _cts = new();
    public int Port { get; }

    private StaticFileServer(int port, string root)
    {
        Port = port;
        _root = root;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public static StaticFileServer Start(int port, string root)
    {
        var srv = new StaticFileServer(port, root);
        srv._listener.Start();
        _ = srv.AcceptLoop();
        return srv;
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            var rel = ctx.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty;
            if (string.IsNullOrEmpty(rel)) rel = "filtered-chat.html"; // default document
            var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
            var bytes = File.ReadAllBytes(full);
            ctx.Response.ContentType = rel.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "text/html; charset=utf-8" : "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Flush();
            ctx.Response.Close();
        }
        catch { try { ctx.Response.Abort(); } catch { } }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
        await Task.Delay(30);
        _cts.Dispose();
    }
}

public sealed class PumpFunCommand : AsyncCommand<PumpFunSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PumpFunSettings settings)
    {
        var rule = new Rule("[bold deepskyblue1]PumpFunTool - Modatrix Chat Filter[/]").Centered();
        AnsiConsole.Write(rule);
        if (settings.Verbose)
            AnsiConsole.MarkupLine("[grey]Verbose logging enabled[/]");

        var token = settings.Token.Trim();
        var headlessScrape = !settings.SessionMode;
        StaticFileServer? server = null;

        try
        {
            using var playwright = await Playwright.CreateAsync();

            // --- Session capture path ---
            if (settings.SessionMode)
            {
                var streamPageSession = await SetupStreamPage(playwright, token, headless: false, settings.Verbose);
                AnsiConsole.MarkupLine("[yellow]SESSION MODE[/]: waiting (up to 5 min) for chat to appear...");
                try
                {
                    var chatRootSession = streamPageSession.Locator("div:has-text('live chat'):has(div[data-message-id])").First;
                    await chatRootSession.WaitForAsync(new LocatorWaitForOptions { Timeout = 300_000 });
                    AnsiConsole.MarkupLine("[green]Chat detected[/] -> saving ./sessions.data");
                    await streamPageSession.Context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = "./sessions.data" });
                    AnsiConsole.MarkupLine("[green]Session saved. Re-run without --session.[/]");
                }
                catch (TimeoutException)
                {
                    AnsiConsole.MarkupLine("[red]Timeout waiting for chat (5m). Session NOT saved.[/]");
                }
                return 0;
            }

            // Clean old overlay artifacts
            SafeDelete("./filtered-chat.html");
            SafeDelete("./filtered-chat.fragment.html");

            // Config + filter
            var cfg = ChatFilterConfig.LoadOrDefault(settings.ConfigPath);
            if (settings.Verbose)
                AnsiConsole.MarkupLine($"[grey]Config loaded[/]: bannedKeywords={cfg.BannedKeywords?.Count ?? 0} bannedMentions={cfg.BannedMentions?.Count ?? 0}");
            var filter = new SpamFilter(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(3), cfg);

            using var view = new HtmlFileChatView("./filtered-chat.html", 28, $"Filtered Chat - {token}");
            AnsiConsole.MarkupLine("[grey]Overlay created: ./filtered-chat.html (AJAX fragment polling)\nAdd to OBS as Browser Source or use --show-viewer[/]");

            if (settings.ShowViewer)
            {
                try
                {
                    server = StaticFileServer.Start(settings.Port, Directory.GetCurrentDirectory());
                    AnsiConsole.MarkupLine($"[grey]Static server: http://localhost:{settings.Port}/filtered-chat.html[/]");
                    await SetupViewerPage(playwright, url: $"http://localhost:{settings.Port}/filtered-chat.html", verbose: settings.Verbose);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to start static server[/]: {Markup.Escape(ex.Message)}");
                    await SetupViewerPage(playwright, url: GetFileUrl(), verbose: settings.Verbose);
                }
            }

            var streamPage = await SetupStreamPage(playwright, token, headless: headlessScrape, settings.Verbose);
            await LoadChat(streamPage, settings.Verbose);
            AnsiConsole.MarkupLine("[green]Historical phase start[/] (relaxed)");

            bool historicalPhase = true;
            var historicalIds = new HashSet<string>();
            var liveIds = new HashSet<string>();
            long acceptedTotal = 0;
            long droppedTotal = 0;
            var swStats = Stopwatch.StartNew();

            while (true)
            {
                if (Console.KeyAvailable)
                {
                    Console.ReadKey(intercept: true);
                    AnsiConsole.MarkupLine("[yellow]Stopping (key press) ...[/]");
                    break;
                }

                ILocator allMessages;
                int allMessagesCount;
                int acceptedThisLoop = 0;
                int seenThisLoop = 0;

                try
                {
                    allMessages = streamPage.Locator("div[data-message-id]");
                    allMessagesCount = await allMessages.CountAsync();
                    if (allMessagesCount == 0)
                    {
                        var html = await streamPage.ContentAsync();
                        if (html.Contains("Application error: a client-side exception", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Pump.fun client-side exception banner detected");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Reloading stream page[/]: {Markup.Escape(ex.Message)}");
                    await streamPage.ReloadAsync();
                    await LoadChat(streamPage, settings.Verbose);
                    continue;
                }

                if (allMessagesCount == 0)
                {
                    await Task.Delay(300);
                    continue;
                }

                int startAt = Math.Max(0, allMessagesCount - 23);
                for (int idx = startAt; idx < allMessagesCount; idx++)
                {
                    try
                    {
                        var node = allMessages.Nth(idx);
                        var msgId = await node.GetAttributeAsync("data-message-id");
                        if (string.IsNullOrEmpty(msgId)) continue;
                        seenThisLoop++;

                        if (historicalPhase)
                        {
                            if (!historicalIds.Add(msgId)) continue;
                        }
                        else if (liveIds.Contains(msgId)) continue;

                        var rawBlock = await node.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 1500 });

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
                            if (!historicalPhase) liveIds.Add(msgId);
                            view.Add(accepted); // triggers fragment rewrite
                            acceptedThisLoop++;
                            acceptedTotal++;
                            if (settings.Verbose)
                                AnsiConsole.MarkupLine($"[green]+[/] {Markup.Escape(accepted.User)}: {Markup.Escape(accepted.Content)}");
                        }
                        else
                        {
                            droppedTotal++;
                        }
                    }
                    catch { /* ignore single message issues */ }
                }

                if (historicalPhase)
                {
                    historicalPhase = false;
                    historicalIds.Clear();
                    AnsiConsole.MarkupLine("[cyan]Historical backlog ingested → live strict filtering[/]");
                }

                if (swStats.ElapsedMilliseconds >= 2000)
                {
                    AnsiConsole.MarkupLine($"[grey]loop: seen={seenThisLoop} accepted+={acceptedThisLoop} totalAccepted={acceptedTotal} dropped={droppedTotal}[/]");
                    swStats.Restart();
                }

                await Task.Delay(150);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
            return 1;
        }
        finally
        {
            if (server != null) await server.DisposeAsync();
        }
        return 0;
    }

    private static string GetFileUrl() => $"file:///{Path.GetFullPath("./filtered-chat.html").Replace("\\", "/")}";

    private static async Task LoadChat(IPage page, bool verbose)
    {
        try
        {
            var chatArea = page.Locator("div:has-text('live chat'):has(div[data-message-id])").First;
            await chatArea.WaitForAsync();
            await DomHelpers.EnsureVideoMutedAndStoppedAsync(page.Locator("video"));
            await page.Context.StorageStateAsync(new() { Path = "./sessions.data" });
            await DomHelpers.RemoveElementAsync(page.Locator("#coin-content-container"));
            if (verbose) AnsiConsole.MarkupLine("[grey]Chat area prepared[/]");
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Timeout waiting for chat area / not logged in.");
        }
    }

    private static BrowserTypeLaunchOptions BuildLaunchOptions(bool headless, bool includeWindowSize)
    {
        var args = new List<string> { "--disable-blink-features=AutomationControlled", "--mute-audio" };
        if (headless) args.Add("--headless=new"); else if (includeWindowSize) args.Add("--window-size=600,1000");
        return new BrowserTypeLaunchOptions { Headless = headless, Channel = headless ? null : "msedge", Args = args.ToArray() };
    }

    private static async Task<IPage> SetupViewerPage(IPlaywright playwright, string url, bool verbose)
    {
        var browser = await playwright.Chromium.LaunchAsync(BuildLaunchOptions(headless: false, includeWindowSize: true));
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = ViewportSize.NoViewport,
            StorageStatePath = "./sessions.data",
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36",
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        if (verbose) AnsiConsole.MarkupLine($"[grey]Viewer page loaded -> {Markup.Escape(url)}[/]");
        return page;
    }

    private static async Task<IPage> SetupStreamPage(IPlaywright playwright, string tokenAddress, bool headless, bool verbose)
    {
        var browser = await playwright.Chromium.LaunchAsync(BuildLaunchOptions(headless, includeWindowSize: false));
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = headless ? null : null,
            StorageStatePath = "./sessions.data",
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36",
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"https://pump.fun/coin/{tokenAddress}");
        if (verbose) AnsiConsole.MarkupLine("[grey]Stream page opened (" + (headless ? "headless" : "interactive") + ")[/]");
        return page;
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(cfg =>
        {
            cfg.SetApplicationName("PumpFunTool");
            cfg.AddCommand<PumpFunCommand>("run")
               .WithDescription("Run the Modatrix chat filter / overlay generator.")
               .WithExample(new[] { "run", "--token", "<token>" })
               .WithExample(new[] { "run", "--token", "<token>", "--config", "chat-filter-cfg.json" })
               .WithExample(new[] { "run", "--token", "<token>", "--session" })
               .WithExample(new[] { "run", "--token", "<token>", "--show-viewer" });
        });
        return await app.RunAsync(args);
    }
}

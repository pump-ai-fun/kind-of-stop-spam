using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace kind_of_stop_spam
{
    /// <summary>
    /// Maintains a rolling list of accepted chat messages and writes them to a local HTML file
    /// that auto-refreshes every 0.5 second. Open the file in your browser to see only filtered messages.
    /// </summary>
    public sealed class HtmlFileChatView : IDisposable
    {
        private readonly object _lock = new();
        private readonly List<ChatMessage> _messages = new();
        private readonly int _capacity;
        private readonly string _filePath;
        private readonly string _title;

        public HtmlFileChatView(string filePath = "filtered.html", int capacity = 300, string? title = null)
        {
            _filePath = filePath;
            _capacity = Math.Max(10, capacity);
            _title = title ?? "Filtered Chat";

            var dir = Path.GetDirectoryName(Path.GetFullPath(_filePath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Initial write so the page exists immediately
            WriteHtmlLocked();
        }

        /// <summary>
        /// Add a message to the rolling buffer and rewrite the HTML view.
        /// </summary>
        public void Add(ChatMessage msg)
        {
            lock (_lock)
            {
                _messages.Add(msg);
                if (_messages.Count > _capacity)
                {
                    // Drop oldest beyond capacity
                    int remove = _messages.Count - _capacity;
                    _messages.RemoveRange(0, remove);
                }
                WriteHtmlLocked();
            }
        }

        private static string HtmlEncode(string s) => System.Net.WebUtility.HtmlEncode(s);

        private void WriteHtmlLocked()
        {
            var sb = new StringBuilder(16 * 1024);
            sb.Append("<!doctype html>\n");
            sb.Append("<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
            sb.Append("<meta http-equiv=\"refresh\" content=\"0.5\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            sb.Append("<title>").Append(HtmlEncode(_title)).Append("</title>\n");
            sb.Append("<style>\n");
            sb.Append("  :root{color-scheme: dark light;}\n");
            sb.Append("  body{font-family: system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif; margin:0; padding:12px; background:#0b0b0c; color:#e9e9ea;}\n");
            sb.Append("  .wrap{max-width: 900px; margin: 0 auto;}\n");
            sb.Append("  h1{font-size:18px; font-weight:600; margin:0 0 8px 0; color:#9dd3ff;}\n");
            sb.Append("  .meta{color:#9aa0a6; font-size:12px; margin-bottom:12px;}\n");
            sb.Append("  ul{list-style:none; padding:0; margin:0;}\n");
            sb.Append("  li{padding:10px 12px; margin:8px 0; background:#131417; border:1px solid #23252b; border-radius:8px;}\n");
            sb.Append("  .time{color:#a1a1a6; font-size:12px; margin-right:8px;}\n");
            sb.Append("  .user{font-weight:600; color:#b6f09c;}\n");
            sb.Append("  .content{margin-top:6px; white-space:pre-wrap; word-wrap:break-word;}\n");
            sb.Append("  .toggle{user-select:none; cursor:pointer;}\n");
            sb.Append("  .toggle input{vertical-align:middle; margin-right:6px;}\n");
            sb.Append("  .autoscroll-ctrl{position:fixed; right:12px; bottom:12px; background:#131417cc; border:1px solid #23252b; border-radius:8px; padding:6px 10px; box-shadow:0 2px 8px rgba(0,0,0,.3); z-index:9999;}\n");
            sb.Append("</style>\n</head>\n");
            // Add message count to body as a data attribute for JS to decide whether to auto-scroll
            sb.Append("<body data-msg-count=\"").Append(_messages.Count.ToString()).Append("\">\n<div class=\"wrap\">\n");
            sb.Append("<h1>").Append(HtmlEncode(_title)).Append("</h1>\n");
            sb.Append("<div class=\"meta\">Auto-refreshing every 0.5s | ");
            sb.Append("Messages: ").Append(_messages.Count.ToString());
            sb.Append("</div>\n");
            sb.Append("<ul>\n");

            // Render messages in arrival order
            foreach (var m in _messages)
            {
                var whenLocal = m.ReceivedAt.ToLocalTime().ToString("HH:mm:ss");
                var timeDisplay = !string.IsNullOrWhiteSpace(m.RawTime) ? m.RawTime : whenLocal;
                sb.Append("  <li>\n");
                sb.Append("    <div><span class=\"time\">[").Append(HtmlEncode(timeDisplay ?? whenLocal)).Append("]</span>");
                sb.Append("<span class=\"user\">").Append(HtmlEncode(m.User)).Append("</span></div>\n");
                // content with basic linkification for http(s) urls
                var content = Linkify(HtmlEncode(m.Content));
                sb.Append("    <div class=\"content\">").Append(content).Append("</div>\n");
                sb.Append("  </li>\n");
            }

            sb.Append("</ul>\n");
            // Bottom toggle control (fixed to viewport bottom-right so it's always reachable)
            sb.Append("<div class=\"autoscroll-ctrl\"><label class=\"toggle\"><input type=\"checkbox\" id=\"autoScrollToggle\"> Auto-scroll</label></div>\n");
            sb.Append("<div id=\"_bottom\"></div>\n");
            sb.Append("<script>\n(function(){\n  const AS_KEY = 'filteredChat.autoScroll';\n  const checkbox = document.getElementById('autoScrollToggle');\n  // Initialize checkbox from localStorage (default ON)\n  const stored = localStorage.getItem(AS_KEY);\n  const enabled = stored === null ? true : stored === '1';\n  checkbox.checked = enabled;\n  checkbox.addEventListener('change', () => {\n    localStorage.setItem(AS_KEY, checkbox.checked ? '1' : '0');\n  });\n  if (enabled) {\n    // Scroll to bottom on each refresh when enabled\n    const bottom = document.getElementById('_bottom');\n    // Defer to ensure layout is ready\n    setTimeout(() => {\n      if (bottom && bottom.scrollIntoView) {\n        bottom.scrollIntoView({ block: 'end' });\n      } else {\n        window.scrollTo(0, document.body.scrollHeight);\n      }\n    }, 0);\n  }\n})();\n</script>\n");
            sb.Append("</div>\n</body>\n</html>");

            try
            {
                File.WriteAllText(_filePath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Ignore transient write errors (e.g., file temporarily locked by viewer). A later refresh will succeed.
            }
        }

        private static string Linkify(string input)
        {
            // Very light linkify: replace http(s)://... sequences with <a> links. No regex dependencies here.
            // This is intentionally simple and safe because input is already HTML-encoded.
            var tokens = input.Split(' ');
            for (int i = 0; i < tokens.Length; i++)
            {
                var t = tokens[i];
                if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var url = t; // already encoded
                    tokens[i] = $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{url}</a>";
                }
            }
            return string.Join(' ', tokens);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                WriteHtmlLocked();
            }
        }
    }
}

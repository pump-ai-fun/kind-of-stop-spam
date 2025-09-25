using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace kind_of_stop_spam
{
    /// <summary>
    /// Maintains a rolling list of accepted chat messages and writes them to a local HTML file
    /// that auto-refreshes every 0.5 second. Open the file in your browser to see only filtered messages.
    /// Single-file model with atomic writes. The page temporarily pauses refresh while interacting
    /// with the Auto-scroll control to ensure it can be toggled.
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
            sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta id=\"autoRefresh\" http-equiv=\"refresh\" content=\"0.5\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<title>").Append(HtmlEncode(_title)).Append("</title>\n<style>\n");
            sb.Append("  :root{color-scheme: dark light;}\nhtml,body{min-width:0;}\nbody{font-family:system-ui,-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;margin:0;padding:12px;background:#0b0b0c;color:#e9e9ea;}\n");
            sb.Append("  .wrap{max-width:900px;margin:0 auto;}\nh1{font-size:18px;font-weight:600;margin:0 0 8px;color:#9dd3ff;}\n.meta{color:#9aa0a6;font-size:12px;margin-bottom:12px;}\n");
            sb.Append("  ul{list-style:none;padding:0;margin:0;}\nli.msg{position:relative;padding:10px 12px;margin:8px 0;background:#131417;border:1px solid #23252b;border-radius:8px;transition:background .25s ease;}\n");
            sb.Append("  .shake{animation:shake 0.2s ease-in-out 0s 10,glowPulse 0.4s ease-in-out 0s 5;}\n");
            sb.Append("  @keyframes shake{0%,100%{transform:translateX(0);}25%{transform:translateX(-12px);}50%{transform:translateX(12px);}75%{transform:translateX(-8px);}}\n");
            sb.Append("  @keyframes glowPulse{0%,100%{box-shadow:0 0 0 0 rgba(255,255,255,0);}50%{box-shadow:0 0 14px 3px rgba(255,255,255,0.30);}}\n");
            sb.Append("  .time{color:#a1a1a6;font-size:12px;margin-right:8px;}\n.user{font-weight:600;color:#b6f09c;}\n.content{margin-top:6px;white-space:pre-wrap;word-wrap:break-word;overflow-wrap:anywhere;word-break:break-word;}\n");
            sb.Append("  .toggle{user-select:none;cursor:pointer;}\n.toggle input{vertical-align:middle;margin-right:6px;}\n.autoscroll-ctrl{position:fixed;right:12px;bottom:12px;background:#131417cc;border:1px solid #23252b;border-radius:8px;padding:6px 10px;box-shadow:0 2px 8px rgba(0,0,0,.3);z-index:9999;}\n");
            sb.Append("  .reply{margin-top:6px;padding:6px 8px;background:#0f1114;border-left:3px solid #3a70d6;color:#aeb4bb;font-size:12px;border-radius:6px;overflow-wrap:anywhere;word-break:break-word;}\n.reply .reply-label{color:#8ab4f8;font-weight:600;margin-right:4px;}\n</style>\n</head>\n<body data-msg-count=\"").Append(_messages.Count.ToString()).Append("\">\n<div class=\"wrap\">\n");
            sb.Append("<h1>").Append(HtmlEncode(_title)).Append("</h1>\n<div class=\"meta\">Auto-refreshing every 0.5s | Messages: ").Append(_messages.Count.ToString()).Append("</div>\n<ul>\n");

            foreach (var m in _messages)
            {
                var whenLocal = m.ReceivedAt.ToLocalTime().ToString("HH:mm:ss");
                var timeDisplay = !string.IsNullOrWhiteSpace(m.RawTime) ? m.RawTime : whenLocal;
                string style = string.Empty;
                if (!string.IsNullOrWhiteSpace(m.HighlightColor) && !string.IsNullOrWhiteSpace(m.HighlightColor2))
                    style = $" style=\"background:linear-gradient(135deg,{HtmlEncode(m.HighlightColor!)},{HtmlEncode(m.HighlightColor2!)});\"";
                else if (!string.IsNullOrWhiteSpace(m.HighlightColor))
                    style = $" style=\"background:{HtmlEncode(m.HighlightColor!)};\"";
                var shakeCls = m.Shake ? " shake" : string.Empty;
                sb.Append("  <li class=\"msg").Append(shakeCls).Append("\"").Append(style).Append(">\n");
                sb.Append("    <div><span class=\"time\">[").Append(HtmlEncode(timeDisplay ?? whenLocal)).Append("]</span><span class=\"user\">").Append(HtmlEncode(m.User)).Append("</span></div>\n");

                if (!string.IsNullOrWhiteSpace(m.ReplyTo))
                {
                    var reply = HtmlEncode(m.ReplyTo!);
                    if (reply.Length > 200) reply = reply.Substring(0, 200) + "…";
                    var replyContent = Linkify(reply);
                    sb.Append("    <div class=\"reply\"><span class=\"reply-label\">Replying to</span> <span class=\"reply-text\">").Append(replyContent).Append("</span></div>\n");
                }

                var content = Linkify(HtmlEncode(m.Content));
                sb.Append("    <div class=\"content\">").Append(content).Append("</div>\n  </li>\n");
            }

            sb.Append("</ul>\n<div class=\"autoscroll-ctrl\"><label class=\"toggle\"><input type=\"checkbox\" id=\"autoScrollToggle\"> Auto-scroll</label></div>\n<div id=\"_bottom\"></div>\n");
            sb.Append("<script>\n(function(){const AS_KEY='filteredChat.autoScroll';const cb=document.getElementById('autoScrollToggle');const rid='autoRefresh';function setRef(on){const m=document.getElementById(rid);if(on){if(!m){const n=document.createElement('meta');n.id=rid;n.httpEquiv='refresh';n.content='0.5';document.head.appendChild(n);}}else if(m){m.remove();}}let t=null;function re(){if(t)clearTimeout(t);t=setTimeout(()=>setRef(true),1200);}const ctrl=document.querySelector('.autoscroll-ctrl');if(ctrl){['mouseenter','focusin','pointerdown','touchstart'].forEach(e=>ctrl.addEventListener(e,()=>setRef(false)));['mouseleave','focusout'].forEach(e=>ctrl.addEventListener(e,re));}const stored=localStorage.getItem(AS_KEY);const en=stored===null?true:stored==='1';cb.checked=en;cb.addEventListener('change',()=>localStorage.setItem(AS_KEY,cb.checked?'1':'0'));if(en){const b=document.getElementById('_bottom');setTimeout(()=>{if(b&&b.scrollIntoView)b.scrollIntoView({block:'end'});else window.scrollTo(0,document.body.scrollHeight);},0);}})();\n</script>\n");
            sb.Append("</div>\n</body>\n</html>");

            WriteTextAtomic(_filePath, sb.ToString(), Encoding.UTF8);
        }

        private static void WriteTextAtomic(string path, string content, Encoding enc)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, content, enc);
                if (File.Exists(path))
                {
                    var backup = path + ".bak";
                    try { File.Replace(tmp, path, backup, ignoreMetadataErrors: true); }
                    catch { File.Move(tmp, path, overwrite: true); }
                    try { if (File.Exists(backup)) File.Delete(backup); } catch {}
                }
                else
                {
                    File.Move(tmp, path, overwrite: true);
                }
            }
            catch
            {
                // Ignore transient write errors
            }
        }

        private static string Linkify(string input)
        {
            var tokens = input.Split(' ');
            for (int i = 0; i < tokens.Length; i++)
            {
                var t = tokens[i];
                if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var url = t;
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

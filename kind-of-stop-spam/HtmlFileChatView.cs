using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace kind_of_stop_spam
{
    /// <summary>
    /// Maintains a rolling list of accepted chat messages and writes them to a local HTML file.
    /// Supports effect classes for animated commands.
    /// </summary>
    public sealed class HtmlFileChatView : IDisposable
    {
        private readonly object _lock = new();
        private readonly List<ChatMessage> _messages = new();
        private readonly int _capacity;
        private readonly string _filePath;
        private readonly string _title;

        public HtmlFileChatView(string filePath = "filtered.html", int capacity = 50, string? title = null)
        {
            _filePath = filePath;
            _capacity = Math.Max(10, capacity);
            _title = title ?? "Filtered Chat";
            var dir = Path.GetDirectoryName(Path.GetFullPath(_filePath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            WriteHtmlLocked();
        }

        public void Add(ChatMessage msg)
        {
            lock (_lock)
            {
                _messages.Add(msg);
                if (_messages.Count > _capacity)
                {
                    int remove = _messages.Count - _capacity;
                    _messages.RemoveRange(0, remove);
                }
                WriteHtmlLocked();
            }
        }

        private static string HtmlEncode(string s) => System.Net.WebUtility.HtmlEncode(s);

        private static string WrapCharsIfNeeded(ChatMessage m, string encoded)
        {
            if (m.Effects.Count == 0) return encoded;
            bool charWrap = m.Effects.Any(e => e is "wave" or "scramble" or "explode" or "matrix" || e == "type");
            if (!charWrap) return encoded;
            var sb = new StringBuilder(encoded.Length * 2);
            int idx = 0;
            foreach (var ch in encoded)
            {
                if (ch == '<') { sb.Append(ch); continue; }
                sb.Append('<').Append("span class=\"ch\" data-i=\"").Append(idx++).Append("\">").Append(ch).Append("</span>");
            }
            return sb.ToString();
        }

        private void WriteHtmlLocked()
        {
            var ordered = _messages.OrderBy(m => m.ReceivedAt).ToList();
            var sb = new StringBuilder(48 * 1024);
            sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta id=\"autoRefresh\" http-equiv=\"refresh\" content=\"1\">\n<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n<title>")
              .Append(HtmlEncode(_title)).Append("</title>\n<style>\n");
            sb.Append("  :root{color-scheme: dark light;}\nhtml,body{min-width:0;}\nbody{font-family:system-ui,-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;margin:0;padding:12px;background:#0b0b0c;color:#e9e9ea;}\n");
            sb.Append("  .wrap{max-width:900px;margin:0 auto;}\nh1{font-size:18px;font-weight:600;margin:0 0 8px;color:#9dd3ff;}\n.meta{color:#9aa0a6;font-size:12px;margin-bottom:12px;}\n");
            sb.Append("  ul{list-style:none;padding:0;margin:0;}\nli.msg{position:relative;padding:10px 12px;margin:8px 0;background:#131417;border:1px solid #23252b;border-radius:8px;transition:background .25s ease;}\n");
            sb.Append("  .icon{display:inline-block;min-width:20px;margin-right:6px;font-size:16px;filter:drop-shadow(0 0 4px rgba(0,0,0,.6));}\n");
            sb.Append("  .content .ch{display:inline-block;will-change:transform,opacity;}\n.effect-active{pointer-events:none;}\n");
            sb.Append("  .wiggle{animation:wiggle 1.4s ease-in-out infinite;}@keyframes wiggle{0%,100%{transform:translateX(0);}25%{transform:translateX(-8px);}50%{transform:translateX(8px);}75%{transform:translateX(-5px);}}\n");
            sb.Append("  .glow{box-shadow:none!important;} .glow .content{animation:textGlow 1s ease-in-out infinite;}@keyframes textGlow{0%,100%{text-shadow:0 0 4px rgba(0,255,255,.45),0 0 10px rgba(0,255,255,.25);}50%{text-shadow:0 0 8px rgba(0,255,255,.95),0 0 22px rgba(0,255,255,.85),0 0 38px rgba(0,255,255,.75),0 0 54px rgba(0,255,255,.55);}}\n");
            sb.Append("  .wave .ch{animation:wave 2s ease-in-out infinite;}@keyframes wave{0%,100%{transform:translateY(0);}20%{transform:translateY(-14px);}40%{transform:translateY(0);}60%{transform:translateY(12px);}80%{transform:translateY(0);}}\n");
            sb.Append("  .scramble .ch{animation:scramble .7s steps(12) 1;}@keyframes scramble{0%{opacity:.2;transform:translateY(-18px) scale(.5);}60%{opacity:.9;transform:translateY(5px) scale(1.1);}100%{opacity:1;transform:translateY(0) scale(1);} }\n");
            sb.Append("  .type .ch{opacity:0;animation:type .8s linear forwards;}@keyframes type{0%{opacity:0;}40%{opacity:0;}100%{opacity:1;}}\n");
            sb.Append("  .glitch{position:relative;} .glitch:before,.glitch:after{content:attr(data-text);position:absolute;left:0;top:0;width:100%;overflow:hidden;mix-blend-mode:screen;animation:glitch .6s steps(6) 1;} .glitch:before{color:#f0f;clip-path:inset(0 0 50% 0);} .glitch:after{color:#0ff;clip-path:inset(50% 0 0 0);} @keyframes glitch{0%{transform:translate(0);}20%{transform:translate(-3px,-2px);}40%{transform:translate(3px,2px);}60%{transform:translate(-2px,2px);}80%{transform:translate(2px,-2px);}100%{transform:translate(0);} }\n");
            sb.Append("  .explode .ch{animation:explode .7s ease-out forwards;}@keyframes explode{0%{opacity:1;transform:translate(0,0) scale(1);}100%{opacity:0;transform:translate(var(--dx),var(--dy)) scale(0.3);}}\n");
            sb.Append("  .matrix{color:#8dff8d;} .matrix .ch{animation:matrix .9s linear infinite;}@keyframes matrix{0%{opacity:0;transform:translateY(-22px);}15%{opacity:1;}85%{opacity:.2;}100%{opacity:0;transform:translateY(24px);} }\n");
            sb.Append("  .fade{animation:fade .8s ease-in-out;}@keyframes fade{0%{opacity:1;}50%{opacity:0;}100%{opacity:1;}}\n");
            sb.Append("  .slide{animation:slide .7s ease;}@keyframes slide{0%{transform:translateX(-150%);}55%{transform:translateX(12%);}80%{transform:translateX(-5%);}100%{transform:translateX(0);} }\n");
            sb.Append("  .shake{animation:shake .45s cubic-bezier(.36,.07,.19,.97) 0s 1 both,flash .45s ease-in-out 0s 1;}@keyframes shake{0%{transform:translate(0,0) rotate(0);}10%{transform:translate(-14px,-8px) rotate(-4deg);}20%{transform:translate(16px,10px) rotate(4deg);}30%{transform:translate(-18px,12px) rotate(-5deg);}40%{transform:translate(18px,-12px) rotate(5deg);}50%{transform:translate(-14px,10px) rotate(-4deg);}60%{transform:translate(14px,-10px) rotate(4deg);}70%{transform:translate(-10px,6px) rotate(-3deg);}80%{transform:translate(10px,-6px) rotate(3deg);}90%{transform:translate(-6px,4px) rotate(-2deg);}100%{transform:translate(0,0) rotate(0);}}@keyframes flash{0%,100%{box-shadow:0 0 0 0 rgba(255,255,255,0);}50%{box-shadow:0 0 24px 8px rgba(255,255,255,.55);}}\n");
            sb.Append("  .time{color:#a1a1a6;font-size:12px;margin-right:8px;} .user{font-weight:600;color:#b6f09c;} .content{margin-top:6px;white-space:pre-wrap;word-wrap:break-word;overflow-wrap:anywhere;word-break:break-word;}\n");
            sb.Append("  .toggle{user-select:none;cursor:pointer;margin-right:18px;} .toggle input{vertical-align:middle;margin-right:6px;} .controls{display:flex;flex-wrap:wrap;gap:22px;align-items:center;margin-top:10px;font-size:12px;color:#9aa0a6;}\n");
            sb.Append("  .reply{margin-top:6px;padding:6px 8px;background:#0f1114;border-left:3px solid #3a70d6;color:#aeb4bb;font-size:12px;border-radius:6px;overflow-wrap:anywhere;word-break:break-word;} .reply .reply-label{color:#8ab4f8;font-weight:600;margin-right:4px;}");
            // Override glow so it affects only the inner text, not the whole message box
            sb.Append("  .glow{box-shadow:none!important;} .glow .content{animation:textGlow .8s ease-in-out infinite;}@keyframes textGlow{0%,100%{text-shadow:0 0 2px rgba(0,255,255,.25),0 0 6px rgba(0,255,255,.20);}50%{text-shadow:0 0 4px rgba(0,255,255,.95),0 0 12px rgba(0,255,255,.80),0 0 22px rgba(0,255,255,.65);}}");
            // Added final overrides for user-requested tuning
            sb.Append("  /* overrides */\\n  .wiggle{animation:wiggle 1.4s ease-in-out infinite !important;}\\n  .wave .ch{animation:wave 2s ease-in-out infinite !important;}\\n  @keyframes wave{0%,100%{transform:translateY(0);}20%{transform:translateY(-14px);}40%{transform:translateY(0);}60%{transform:translateY(12px);}80%{transform:translateY(0);}}\\n  .glow .content{animation:textGlow 1s ease-in-out infinite !important;}\\n  @keyframes textGlow{0%,100%{text-shadow:0 0 4px rgba(0,255,255,.45),0 0 10px rgba(0,255,255,.25);}50%{text-shadow:0 0 8px rgba(0,255,255,.95),0 0 22px rgba(0,255,255,.85),0 0 38px rgba(0,255,255,.75),0 0 54px rgba(0,255,255,.55);}}\\n");
            sb.Append("</style>\\n</head>\\n<body data-msg-count=\"")
              .Append(ordered.Count.ToString()).Append("\">\n<div class=\"wrap\">\n");
            sb.Append("<h1>").Append(HtmlEncode(_title)).Append("</h1>\n<div class=\"meta\">Auto-refreshing (toggle below) | Messages: ")
              .Append(ordered.Count.ToString()).Append("</div>\n<ul>\n");

            foreach (var m in ordered)
            {
                var whenLocal = m.ReceivedAt.ToLocalTime().ToString("HH:mm:ss");
                var timeDisplay = !string.IsNullOrWhiteSpace(m.RawTime) ? m.RawTime : whenLocal;
                string style = string.Empty;
                if (!string.IsNullOrWhiteSpace(m.HighlightColor) && !string.IsNullOrWhiteSpace(m.HighlightColor2))
                    style = $" style=\"background:linear-gradient(135deg,{HtmlEncode(m.HighlightColor!)},{HtmlEncode(m.HighlightColor2!)});\"";
                else if (!string.IsNullOrWhiteSpace(m.HighlightColor))
                    style = $" style=\"background:{HtmlEncode(m.HighlightColor!)};\"";

                string effectClasses = string.Join(' ', m.Effects.Where(e => !string.IsNullOrWhiteSpace(e)));
                var liClass = string.IsNullOrWhiteSpace(effectClasses) ? "msg" : $"msg {effectClasses}";

                sb.Append("  <li class=\"").Append(liClass).Append("\"").Append(style).Append(">\n");
                sb.Append("    <div><span class=\"time\">[").Append(HtmlEncode(timeDisplay ?? whenLocal)).Append("]</span>");
                if (!string.IsNullOrWhiteSpace(m.Icon))
                    sb.Append("<span class=\"icon\">").Append(HtmlEncode(m.Icon)).Append("</span>");
                sb.Append("<span class=\"user\">").Append(HtmlEncode(m.User)).Append("</span></div>\n");

                if (!string.IsNullOrWhiteSpace(m.ReplyTo))
                {
                    var reply = HtmlEncode(m.ReplyTo!);
                    if (reply.Length > 200) reply = reply.Substring(0, 200) + "…";
                    var replyContent = Linkify(reply);
                    sb.Append("    <div class=\"reply\"><span class=\"reply-label\">Replying to</span> <span class=\"reply-text\">")
                      .Append(replyContent).Append("</span></div>\n");
                }

                var rawContent = HtmlEncode(m.Content);
                var finalContent = WrapCharsIfNeeded(m, rawContent);
                sb.Append("    <div class=\"content\" data-text=\"").Append(rawContent).Append("\">").Append(finalContent).Append("</div>\n  </li>\n");
            }

            sb.Append("</ul>\n<div id=\"_bottom\"></div>\n<div class=\"controls\">\n  <label class=\"toggle\"><input type=\"checkbox\" id=\"autoScrollToggle\" checked> Auto-scroll</label>\n  <label class=\"toggle\"><input type=\"checkbox\" id=\"autoRefreshToggle\" checked> Auto-refresh (1s)</label>\n</div>\n");

            var script = @"<script>
(function(){
  const SC_KEY='filteredChat.autoScroll';
  const RF_KEY='filteredChat.autoRefresh';
  const autoScrollCb=document.getElementById('autoScrollToggle');
  const autoRefreshCb=document.getElementById('autoRefreshToggle');
  const metaId='autoRefresh';
  function setMeta(on){let m=document.getElementById(metaId); if(on){ if(!m){ m=document.createElement('meta'); m.id=metaId; m.httpEquiv='refresh'; m.content='1'; document.head.appendChild(m);} } else if(m){ m.remove(); }}
  function scrollBottom(){ if(!autoScrollCb.checked) return; const b=document.getElementById('_bottom'); if(b&&b.scrollIntoView) b.scrollIntoView({block:'end'}); else window.scrollTo(0,document.body.scrollHeight); }
  // Load stored states
  const scStored=localStorage.getItem(SC_KEY); autoScrollCb.checked = scStored===null?true:scStored==='1';
  const rfStored=localStorage.getItem(RF_KEY); autoRefreshCb.checked = rfStored===null?true:rfStored==='1'; setMeta(autoRefreshCb.checked);
  autoScrollCb.addEventListener('change',()=>{localStorage.setItem(SC_KEY, autoScrollCb.checked?'1':'0'); if(autoScrollCb.checked) scrollBottom();});
  autoRefreshCb.addEventListener('change',()=>{localStorage.setItem(RF_KEY, autoRefreshCb.checked?'1':'0'); setMeta(autoRefreshCb.checked);});
  // Character effect setup (stagger + explode vectors)
  document.querySelectorAll('.wave .ch, .scramble .ch, .matrix .ch, .explode .ch, .type .ch').forEach((el,i)=>{el.style.animationDelay=(i*0.03)+'s'; if(el.closest('.explode')){const ang=(i*137)%360; const r=55+(i%7)*5; const rad=ang*Math.PI/180; el.style.setProperty('--dx',Math.cos(rad)*r+'px'); el.style.setProperty('--dy',Math.sin(rad)*r+'px');}});
  // Randomize phase so loops look mid-motion
  document.querySelectorAll('.wiggle,.glow,.wave,.matrix').forEach(el=>{el.style.animationDelay=('-'+(Math.random()*1).toFixed(2)+'s');});
  function scrambleChars(root){const ch=[...root.querySelectorAll('.scramble .ch')]; if(!ch.length) return; const original=ch.map(x=>x.textContent); const glyphs='ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#$%&*'; let n=0; const id=setInterval(()=>{n++; ch.forEach(c=>{c.textContent=glyphs[Math.floor(Math.random()*glyphs.length)];}); if(n>10){ clearInterval(id); ch.forEach((c,i)=>c.textContent=original[i]); }},40);} scrambleChars(document);
  window.applyChatEffect=function(command, element){ if(!element) return; const map={wiggle:'wiggle',glow:'glow',wave:'wave',scramble:'scramble',type:'type',glitch:'glitch',explode:'explode',matrix:'matrix',fade:'fade',slide:'slide',shake:'shake'}; const cls=map[command?.toLowerCase()]; if(!cls) return; element.classList.remove(cls); void element.offsetWidth; element.classList.add(cls,'effect-active'); const once=()=>{element.classList.remove('effect-active'); element.removeEventListener('animationend',once);}; element.addEventListener('animationend',once); if(['wave','scramble','matrix','explode','type'].includes(cls)){ if(!element.querySelector('.ch')){ const txt=element.textContent||''; element.textContent=''; [...txt].forEach((c,i)=>{ const span=document.createElement('span'); span.className='ch'; span.dataset.i=i; span.textContent=c; element.appendChild(span);}); } element.querySelectorAll('.ch').forEach((el,i)=>{el.style.animationDelay=(i*0.03)+'s'; if(cls==='explode'){const ang=(i*137)%360; const r=55+(i%7)*5; const rad=ang*Math.PI/180; el.style.setProperty('--dx',Math.cos(rad)*r+'px'); el.style.setProperty('--dy',Math.sin(rad)*r+'px');}}); if(cls==='scramble') scrambleChars(element); } if(autoScrollCb.checked) scrollBottom(); };
  // Initial scroll
  scrollBottom();
})();
</script>";
            sb.Append(script);
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
            catch { }
        }

        private static string Linkify(string input)
        {
            var tokens = input.Split(' ');
            for (int i = 0; i < tokens.Length; i++)
            {
                var t = tokens[i];
                if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var url = t; tokens[i] = $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{url}</a>";
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

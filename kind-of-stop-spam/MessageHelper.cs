using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace kind_of_stop_spam
{
    /// <summary>
    /// Represents a single chat message.
    /// </summary>
    public sealed class ChatMessage
    {
        public string User { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public DateTimeOffset ReceivedAt { get; init; }
        public string? RawTime { get; init; }
        public string? ReplyTo { get; init; }
        public string? HighlightColor { get; init; }
        public string? HighlightColor2 { get; init; }
        public bool Shake { get; init; }  // true when !shake command present (animation for ~2s)
    }

    /// <summary>
    /// Filters chat messages with per-user rate limiting, content deduplication and optional keyword/mention bans.
    /// Extracts up to two hex colors from content for highlight / gradient UI (not part of dedup key).
    /// </summary>
    public sealed class SpamFilter
    {
        private readonly TimeSpan _perUserWindow;
        private readonly TimeSpan _dedupTtl;
        private readonly object _lock = new();
        private readonly ChatFilterConfig? _config;

        private readonly Dictionary<string, long> _userLastKept = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _contentExpiry = new(StringComparer.Ordinal);
        private readonly Queue<(string key, long expiry)> _expiryQueue = new();

        private static readonly Regex _multiWhitespace = new(@"\s+", RegexOptions.Compiled);
        // Keep name to avoid hot-reload rename issues
        private static readonly Regex _colorCmdRegex = new(@"(?<![0-9A-Fa-f])#([0-9A-Fa-f]{3,8})", RegexOptions.Compiled);
        private static readonly Regex _shakeCmdRegex = new(@"(?<!\S)!shake(?!\S)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Retain previously existing field (not used now) for hot-reload compatibility
        private static readonly HashSet<string> _allowedColorNames = new(StringComparer.OrdinalIgnoreCase);

        public SpamFilter(TimeSpan? perUserWindow = null, TimeSpan? dedupTtl = null, ChatFilterConfig? config = null)
        {
            _perUserWindow = perUserWindow ?? TimeSpan.FromSeconds(2);
            _dedupTtl = dedupTtl ?? TimeSpan.FromMinutes(3);
            _config = config;
        }

        public bool TryAcceptRaw(string raw, out ChatMessage kept) => TryAcceptRaw(raw, replyTo: null, out kept);

        public bool TryAcceptRaw(string raw, string? replyTo, out ChatMessage kept)
        {
            var now = DateTimeOffset.UtcNow;
            kept = new ChatMessage { User = string.Empty, Content = string.Empty, ReceivedAt = now };

            string user = string.Empty, content = string.Empty, timeStr = string.Empty;
            {
                var parts = raw.Split(new[] { "\n\n" }, StringSplitOptions.None);
                if (parts.Length >= 1) user = parts[0].Trim();
                if (parts.Length >= 2) content = parts[1].Trim();
                if (parts.Length >= 3) timeStr = parts[2].Trim();
            }

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(content))
                return false;

            // Extract colors then shake command
            var stripped = StripAndExtractColors(content, out var color1, out var color2);
            stripped = RemoveShakeCommand(stripped, out bool shake);

            var normalized = NormalizeContent(stripped);
            if (_config != null && _config.IsBanned(normalized)) return false;

            string contentKey = normalized;
            long nowTicks = now.UtcTicks;
            long contentExpiryTicks = now.Add(_dedupTtl).UtcTicks;

            lock (_lock)
            {
                while (_expiryQueue.Count > 0 && _expiryQueue.Peek().expiry < nowTicks)
                {
                    var (k, exp) = _expiryQueue.Dequeue();
                    if (_contentExpiry.TryGetValue(k, out var still) && still == exp)
                        _contentExpiry.Remove(k);
                }
                if (_contentExpiry.ContainsKey(contentKey)) return false;
                if (_userLastKept.TryGetValue(user, out var last) && (nowTicks - last < _perUserWindow.Ticks)) return false;
                _userLastKept[user] = nowTicks;
                _contentExpiry[contentKey] = contentExpiryTicks;
                _expiryQueue.Enqueue((contentKey, contentExpiryTicks));
            }

            kept = new ChatMessage
            {
                User = user,
                Content = stripped,
                ReceivedAt = now,
                RawTime = timeStr,
                ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo,
                HighlightColor = color1,
                HighlightColor2 = color2,
                Shake = shake
            };
            return true;
        }

        private static string RemoveShakeCommand(string content, out bool shake)
        {
            shake = false;
            var m = _shakeCmdRegex.Match(content);
            if (!m.Success) return content;
            shake = true;
            var removed = _shakeCmdRegex.Replace(content, " ");
            removed = _multiWhitespace.Replace(removed, " ").Trim();
            return removed;
        }

        private static string StripAndExtractColors(string content, out string? c1, out string? c2)
        {
            c1 = c2 = null;
            if (string.IsNullOrWhiteSpace(content)) return content;
            var matches = _colorCmdRegex.Matches(content);
            if (matches.Count == 0) return content;

            foreach (Match m in matches)
            {
                if (!m.Success) continue;
                var raw = m.Groups[1].Value; // hex digits
                if (!IsValidHex(raw)) continue;
                var norm = NormalizeHex(raw);
                if (c1 == null) c1 = norm; else if (c2 == null && norm != c1) { c2 = norm; break; }
            }
            if (c1 == null && c2 == null) return content;

            // Remove first occurrences of the colors we actually used.
            string result = content;
            if (c1 != null)
            {
                var pattern1 = "#" + Regex.Escape(TrimLeadingHash(c1));
                result = Regex.Replace(result, pattern1, " ", RegexOptions.IgnoreCase);
            }
            if (c2 != null)
            {
                var pattern2 = "#" + Regex.Escape(TrimLeadingHash(c2));
                result = Regex.Replace(result, pattern2, " ", RegexOptions.IgnoreCase);
            }
            result = _multiWhitespace.Replace(result, " ").Trim();
            return result;
        }

        private static bool IsValidHex(string hexDigits)
        {
            // Accept 3/4/6/8 (alpha channels trimmed later)
            return hexDigits.Length is 3 or 4 or 6 or 8;
        }

        private static string NormalizeHex(string hexDigits)
        {
            // Expand short forms, drop alpha if present
            if (hexDigits.Length == 3)
            {
                var sb = new StringBuilder(7);
                sb.Append('#');
                for (int i = 0; i < 3; i++){ var c = hexDigits[i]; sb.Append(c).Append(c);} // #abc -> #aabbcc
                return sb.ToString().ToLowerInvariant();
            }
            if (hexDigits.Length == 4) // rgba short -> ignore alpha
            {
                var sb = new StringBuilder(7);
                sb.Append('#');
                for (int i = 0; i < 3; i++){ var c = hexDigits[i]; sb.Append(c).Append(c);} // #abcf -> #aabbcc
                return sb.ToString().ToLowerInvariant();
            }
            if (hexDigits.Length == 8) // rrggbbaa -> drop aa
            {
                hexDigits = hexDigits.Substring(0,6);
            }
            return ("#" + hexDigits).ToLowerInvariant();
        }

        private static string TrimLeadingHash(string hex) => hex.StartsWith('#') ? hex[1..] : hex;

        private static string NormalizeContent(string s)
        {
            s = s.Trim();
            s = _multiWhitespace.Replace(s, " ");
            s = s.ToLowerInvariant();
            return s;
        }
    }
}

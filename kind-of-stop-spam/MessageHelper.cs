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
        public string? RawTime { get; init; } // e.g., "19:10" from source
        public string? ReplyTo { get; init; } // Optional: text snippet or username being replied to
        public string? HighlightColor { get; init; } // Optional: user-selected color via !color command
    }

    /// <summary>
    /// Filters chat messages with per-user rate limiting, content deduplication and optional keyword/mention bans.
    /// </summary>
    public sealed class SpamFilter
    {
        private readonly TimeSpan _perUserWindow;
        private readonly TimeSpan _dedupTtl;
        private readonly object _lock = new();
        private readonly ChatFilterConfig? _config;

        // user -> last kept tick
        private readonly Dictionary<string, long> _userLastKept = new(StringComparer.Ordinal);
        // contentKey -> expiry ticks
        private readonly Dictionary<string, long> _contentExpiry = new(StringComparer.Ordinal);
        // FIFO of expirations so we can evict in O(k)
        private readonly Queue<(string key, long expiry)> _expiryQueue = new();

        // perf: reuse regex
        private static readonly Regex _multiWhitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex _colorCmdRegex = new(@"#([0-9A-Fa-f]{3,8})", RegexOptions.Compiled);
        private static readonly HashSet<string> _allowedColorNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "red","blue","green","orange","purple","cyan","magenta","yellow","lime","teal","pink","violet","indigo","black","white","gray","grey","silver","gold","brown","maroon","navy","olive","aqua","fuchsia","coral","crimson","tomato","salmon","khaki","plum","orchid","turquoise","skyblue","deeppink","dodgerblue","rebeccapurple"
        };

        public SpamFilter(TimeSpan? perUserWindow = null, TimeSpan? dedupTtl = null, ChatFilterConfig? config = null)
        {
            _perUserWindow = perUserWindow ?? TimeSpan.FromSeconds(2);   // 1 msg / 2s per user
            _dedupTtl = dedupTtl ?? TimeSpan.FromMinutes(3);             // consider same text spam for 3m
            _config = config;
        }

        /// <summary>
        /// Try to accept a raw block in the format:
        /// user + "\n\n" + content + "\n\n" + time (e.g., "19:10").
        /// Returns true and the cleaned ChatMessage if kept; false if filtered out.
        /// Uses arrival time for rate limiting precision.
        /// </summary>
        public bool TryAcceptRaw(string raw, out ChatMessage kept)
        {
            // Back-compat overload: no reply info
            return TryAcceptRaw(raw, replyTo: null, out kept);
        }

        /// <summary>
        /// Overload that accepts an optional replyTo snippet/username for the message.
        /// </summary>
        public bool TryAcceptRaw(string raw, string? replyTo, out ChatMessage kept)
        {
            var now = DateTimeOffset.UtcNow;
            kept = new ChatMessage { User = string.Empty, Content = string.Empty, ReceivedAt = now };

            // Parse: username \n\n content \n\n time
            // Be forgiving with stray whitespace
            string user = string.Empty, content = string.Empty, timeStr = string.Empty;
            {
                // Split on double newline; avoid StringSplitOptions.RemoveEmptyEntries so content like "\n\n" isn't lost
                var parts = raw.Split(new[] { "\n\n" }, StringSplitOptions.None);
                if (parts.Length >= 1) user = parts[0].Trim();
                if (parts.Length >= 2) content = parts[1].Trim();
                if (parts.Length >= 3) timeStr = parts[2].Trim();
            }

            var originalContent = content;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(originalContent))
                return false; // malformed

            // Extract optional color command and strip it from displayed content
            string? highlightColor = null;
            content = StripAndExtractColor(originalContent, out highlightColor);

            // Normalize content for dedup and keyword checks (use stripped content)
            var normalized = NormalizeContent(content);

            // Config-based bans (keywords or mentions); if matched, drop
            if (_config != null && _config.IsBanned(normalized))
                return false;

            // Build a stable key (content only; duplicates across users are filtered)
            string contentKey = normalized;

            long nowTicks = now.UtcTicks;
            long contentExpiryTicks = now.Add(_dedupTtl).UtcTicks;

            lock (_lock)
            {
                // Evict expired content keys (amortized)
                while (_expiryQueue.Count > 0 && _expiryQueue.Peek().expiry < nowTicks)
                {
                    var (k, exp) = _expiryQueue.Dequeue();
                    // Only remove if this expiry matches current dictionary value (avoid out-of-order duplicates)
                    if (_contentExpiry.TryGetValue(k, out var still) && still == exp)
                        _contentExpiry.Remove(k);
                }

                // Dedup on content text
                if (_contentExpiry.ContainsKey(contentKey))
                    return false; // spam: duplicate text recently seen

                // Per-user rate limit
                if (_userLastKept.TryGetValue(user, out var lastKeptTicks))
                {
                    if (nowTicks - lastKeptTicks < _perUserWindow.Ticks)
                        return false; // spam: too soon for this user
                }

                // Accept:
                _userLastKept[user] = nowTicks;
                _contentExpiry[contentKey] = contentExpiryTicks;
                _expiryQueue.Enqueue((contentKey, contentExpiryTicks));
            }

            kept = new ChatMessage
            {
                User = user,
                Content = content,           // command stripped from display content
                ReceivedAt = now,
                RawTime = timeStr,
                ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo,
                HighlightColor = highlightColor
            };
            return true;
        }

        // --- helpers ---

        private static string StripAndExtractColor(string content, out string? color)
        {
            color = null;
            // Find first color command token
            var match = _colorCmdRegex.Match(content);
            if (!match.Success)
                return content;

            var token = match.Groups[1].Value; // either #hex or name
            if (!string.IsNullOrEmpty(token))//IsAllowedColor(token))
            {
                color = token.StartsWith('#') ? token : token.ToLowerInvariant();
                // Remove the matched token including leading '!'

                var stripped = _colorCmdRegex.Replace(content, " ", 1);
                // Normalize leftover whitespace
                stripped = _multiWhitespace.Replace(stripped, " ").Trim();
                return stripped;
            }
            return content;
        }

        private static bool IsAllowedColor(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (token.StartsWith('#'))
            {
                var hex = token.AsSpan(1);
                if (hex.Length is 3 or 6)
                {
                    for (int i = 0; i < hex.Length; i++)
                    {
                        char c = hex[i];
                        bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                        if (!ok) return false;
                    }
                    return true;
                }
                return false;
            }
            return _allowedColorNames.Contains(token);
        }

        private static string NormalizeContent(string s)
        {
            s = s.Trim();
            s = _multiWhitespace.Replace(s, " ");
            s = s.ToLowerInvariant();
            return s;
        }
    }
}

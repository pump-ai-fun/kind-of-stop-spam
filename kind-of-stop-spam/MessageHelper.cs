using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace kind_of_stop_spam
{
    /// <summary>
    /// Immutable representation of a filtered chat message.
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
        public bool Shake { get; init; }           // Retained for backward compatibility with legacy single-effect usage
        public string? Icon { get; init; }
        public IReadOnlyList<string> Effects { get; init; } = Array.Empty<string>(); // Normalized effect command tokens
    }

    /// <summary>
    /// Core spam / content filter. Two operating modes:
    /// Historical (relaxed: only banned-word filtering) and Live (strict: banned + rate limit + dedup).
    /// </summary>
    public sealed class SpamFilter
    {
        public enum Mode { Historical, Live }

        private readonly TimeSpan _perUserWindow;
        private readonly TimeSpan _dedupTtl;
        private readonly object _lock = new();
        private readonly ChatFilterConfig? _config;

        private readonly Dictionary<string, long> _userLastKept = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _contentExpiry = new(StringComparer.Ordinal);
        private readonly Queue<(string key, long expiry)> _expiryQueue = new();

        // Regex helpers
        private static readonly Regex _multiWhitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex _colorCmdRegex = new(@"(?<![0-9A-Fa-f])#([0-9A-Fa-f]{3,8})", RegexOptions.Compiled);
        private static readonly Regex _shakeCmdRegex = new(@"(?<!\S)!shake(?!\S)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _effectTokenRegex = new(@"(?<!\S)!(wiggle|glow|wave|scramble|type|glitch|explode|matrix|fade|slide)(?!\S)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public SpamFilter(TimeSpan? perUserWindow = null, TimeSpan? dedupTtl = null, ChatFilterConfig? config = null)
        {
            _perUserWindow = perUserWindow ?? TimeSpan.FromSeconds(2);
            _dedupTtl = dedupTtl ?? TimeSpan.FromMinutes(3);
            _config = config;
        }

        /// <summary>
        /// Accept convenience overload defaulting to Live mode.
        /// </summary>
        public bool TryAcceptRaw(string raw, out ChatMessage kept) => TryAcceptRaw(raw, null, Mode.Live, out kept);
        public bool TryAcceptRaw(string raw, string? replyTo, out ChatMessage kept) => TryAcceptRaw(raw, replyTo, Mode.Live, out kept);

        /// <summary>
        /// Attempt to parse and accept a raw block. Returns false if filtered out.
        /// </summary>
        public bool TryAcceptRaw(string raw, string? replyTo, Mode mode, out ChatMessage kept)
        {
            var now = DateTimeOffset.UtcNow;
            kept = new ChatMessage { User = string.Empty, Content = string.Empty, ReceivedAt = now };

            // Parse the three-part block: user, content, time (split on double newlines)
            string user = string.Empty, content = string.Empty, timeStr = string.Empty;
            var parts = raw.Split(new[] { "\n\n" }, StringSplitOptions.None);
            if (parts.Length >= 1) user = parts[0].Trim();
            if (parts.Length >= 2) content = parts[1].Trim();
            if (parts.Length >= 3) timeStr = parts[2].Trim();
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(content)) return false;

            // 1. Extract colors, 2. Extract shake, 3. Extract additional effects, 4. Normalize
            var stripped = StripAndExtractColors(content, out var c1, out var c2);
            stripped = RemoveShakeCommand(stripped, out var shakeFlag);
            var effects = ExtractEffects(ref stripped, shakeFlag);
            var normalized = NormalizeContent(stripped);

            // Banned content check (applies in both modes)
            if (_config != null && _config.IsBanned(normalized)) return false;

            // Live-only: rate limit + dedup state management
            if (mode == Mode.Live)
            {
                var nowTicks = now.UtcTicks;
                var expiry = now.Add(_dedupTtl).UtcTicks;
                lock (_lock)
                {
                    // Expire stale dedup entries
                    while (_expiryQueue.Count > 0 && _expiryQueue.Peek().expiry < nowTicks)
                    {
                        var (k, exp) = _expiryQueue.Dequeue();
                        if (_contentExpiry.TryGetValue(k, out var still) && still == exp)
                            _contentExpiry.Remove(k);
                    }

                    if (_contentExpiry.ContainsKey(normalized)) return false;            // duplicate content
                    if (_userLastKept.TryGetValue(user, out var last) && (nowTicks - last < _perUserWindow.Ticks)) return false; // per-user rate limit

                    _userLastKept[user] = nowTicks;
                    _contentExpiry[normalized] = expiry;
                    _expiryQueue.Enqueue((normalized, expiry));
                }
            }

            kept = new ChatMessage
            {
                User = user,
                Content = stripped,
                ReceivedAt = now,
                RawTime = timeStr,
                ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo,
                HighlightColor = c1,
                HighlightColor2 = c2,
                Shake = shakeFlag,
                Icon = _config?.TryGetWalletIcon(user),
                Effects = effects
            };
            return true;
        }

        private static IReadOnlyList<string> ExtractEffects(ref string content, bool alreadyHasShake)
        {
            var list = new List<string>();
            if (alreadyHasShake) list.Add("shake");
            if (string.IsNullOrWhiteSpace(content)) return list;

            var matches = _effectTokenRegex.Matches(content);
            if (matches.Count == 0) return list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in matches)
            {
                if (!m.Success) continue;
                var raw = m.Groups[1].Value.ToLowerInvariant();
                if (seen.Add(raw)) list.Add(raw);
            }

            content = _effectTokenRegex.Replace(content, " ");
            content = _multiWhitespace.Replace(content, " ").Trim();
            return list;
        }

        private static string RemoveShakeCommand(string content, out bool shake)
        {
            shake = false;
            var m = _shakeCmdRegex.Match(content);
            if (!m.Success) return content;
            shake = true;
            var removed = _shakeCmdRegex.Replace(content, " ");
            return _multiWhitespace.Replace(removed, " ").Trim();
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
                var raw = m.Groups[1].Value;
                if (!IsValidHex(raw)) continue;
                var norm = NormalizeHex(raw);
                if (c1 == null) c1 = norm;
                else if (c2 == null && norm != c1) { c2 = norm; break; }
            }

            if (c1 == null && c2 == null) return content;
            var result = content;
            if (c1 != null) { var p1 = "#" + Regex.Escape(TrimLeadingHash(c1)); result = Regex.Replace(result, p1, " ", RegexOptions.IgnoreCase); }
            if (c2 != null) { var p2 = "#" + Regex.Escape(TrimLeadingHash(c2)); result = Regex.Replace(result, p2, " ", RegexOptions.IgnoreCase); }
            return _multiWhitespace.Replace(result, " ").Trim();
        }

        private static bool IsValidHex(string hex) => hex.Length is 3 or 4 or 6 or 8;

        private static string NormalizeHex(string hex)
        {
            if (hex.Length == 3 || hex.Length == 4)
            {
                var sb = new StringBuilder(7);
                sb.Append('#');
                for (int i = 0; i < 3; i++)
                {
                    var c = hex[i];
                    sb.Append(c).Append(c);
                }
                return sb.ToString().ToLowerInvariant();
            }
            if (hex.Length == 8) hex = hex[..6]; // drop alpha if present
            return ("#" + hex).ToLowerInvariant();
        }

        private static string TrimLeadingHash(string h) => h.StartsWith('#') ? h[1..] : h;
        private static string NormalizeContent(string s) => _multiWhitespace.Replace(s.Trim(), " ").ToLowerInvariant();
    }
}

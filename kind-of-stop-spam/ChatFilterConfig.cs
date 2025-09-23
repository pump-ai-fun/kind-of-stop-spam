using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace kind_of_stop_spam
{
    /// <summary>
    /// Configuration for content-based filtering.
    /// Supports substring-based bans for keywords and mentions (case-insensitive).
    /// </summary>
    public sealed class ChatFilterConfig
    {
        public List<string>? BannedKeywords { get; set; }
        public List<string>? BannedMentions { get; set; }

        // Internal lower-cased copies for fast checks
        private HashSet<string> _keywords = new(StringComparer.Ordinal);
        private HashSet<string> _mentions = new(StringComparer.Ordinal);

        /// <summary>
        /// Normalize the lists to lower-case and trim whitespace.
        /// Call after setting properties or after deserialization.
        /// </summary>
        public void Normalize()
        {
            _keywords = new HashSet<string>((BannedKeywords ?? new List<string>())
                .Select(s => (s ?? string.Empty).Trim().ToLowerInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);
            _mentions = new HashSet<string>((BannedMentions ?? new List<string>())
                .Select(s => (s ?? string.Empty).Trim().ToLowerInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);
        }

        /// <summary>
        /// Returns true if the normalized content contains any banned keyword or mention.
        /// </summary>
        public bool IsBanned(string contentLower)
        {
            if (_keywords.Count == 0 && _mentions.Count == 0)
                return false;

            foreach (var k in _keywords)
            {
                if (contentLower.Contains(k))
                    return true;
            }
            foreach (var m in _mentions)
            {
                if (contentLower.Contains(m))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Load JSON from disk or return defaults if not found or invalid.
        /// </summary>
        public static ChatFilterConfig LoadOrDefault(string? path = null)
        {
            var cfg = new ChatFilterConfig();
            path ??= Path.Combine(Environment.CurrentDirectory, "chat-filter-cfg.json");
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var tmp = JsonSerializer.Deserialize<ChatFilterConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
                    if (tmp != null)
                        cfg = tmp;
                }
            }
            catch
            {
                // If malformed, fall back to defaults silently
            }
            cfg.Normalize();
            return cfg;
        }
    }
}

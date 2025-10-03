using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Modatrix
{
    /// <summary>
    /// Configuration container for filtering (keywords, mentions, wallet icon mapping).
    /// Banned keyword and mention checks are substring + case-insensitive.
    /// </summary>
    public sealed class ChatFilterConfig
    {
        public List<string>? BannedKeywords { get; set; }
        public List<string>? BannedMentions { get; set; }
        public Dictionary<string,string>? WalletIcons { get; set; } // e.g. { "@example": "??" }

        // Lower-cased normalized copies used at runtime
        private HashSet<string> _keywords = new(StringComparer.Ordinal);
        private HashSet<string> _mentions = new(StringComparer.Ordinal);
        private Dictionary<string,string> _walletIcons = new(StringComparer.Ordinal);

        /// <summary>
        /// Normalize configured values (trim, lowercase, ensure @ prefix for wallet tags).
        /// </summary>
        public void Normalize()
        {
            _keywords = new HashSet<string>((BannedKeywords ?? new())
                .Select(s => (s ?? string.Empty).Trim().ToLowerInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);

            _mentions = new HashSet<string>((BannedMentions ?? new())
                .Select(s => (s ?? string.Empty).Trim().ToLowerInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);

            _walletIcons = new Dictionary<string,string>(StringComparer.Ordinal);
            if (WalletIcons != null)
            {
                foreach (var kv in WalletIcons)
                {
                    var key = (kv.Key ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (!key.StartsWith('@')) key = "@" + key; // enforce prefix
                    key = key.ToLowerInvariant();
                    _walletIcons[key] = kv.Value ?? string.Empty; // last wins
                }
            }
        }

        /// <summary>
        /// Return true if the (already lowercased) content contains any banned token.
        /// </summary>
        public bool IsBanned(string contentLower)
        {
            if (_keywords.Count == 0 && _mentions.Count == 0) return false;
            foreach (var k in _keywords)
                if (contentLower.Contains(k)) return true;
            foreach (var m in _mentions)
                if (contentLower.Contains(m)) return true;
            return false;
        }

        /// <summary>
        /// Try map a user tag to an icon (accepts with or without leading '@').
        /// </summary>
        public string? TryGetWalletIcon(string userTag)
        {
            if (_walletIcons.Count == 0 || string.IsNullOrWhiteSpace(userTag)) return null;
            var key = userTag.Trim();
            if (!key.StartsWith('@')) key = "@" + key;
            key = key.ToLowerInvariant();
            return _walletIcons.TryGetValue(key, out var icon) && !string.IsNullOrEmpty(icon) ? icon : null;
        }

        /// <summary>
        /// Load configuration from a JSON file or return default (empty) config.
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
                    if (tmp != null) cfg = tmp;
                }
            }
            catch
            {
                // Ignore malformed file; proceed with defaults
            }
            cfg.Normalize();
            return cfg;
        }
    }
}

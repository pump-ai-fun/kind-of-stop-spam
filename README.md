# Modatrix (the start of a Local Chat Moderation)

![Demo GIF](./banner.jpg)

Spammers are out of control, and this project is built for the community, by the community, to take back control of our chats.
It started as a "kind of stop spam" approach, and it evolved to a foundation of a fully local chat moderation system.
Total vibe coding.

> Modatrix CA on Pump.Fun is "H27BM5y9rjrFbnq3zBkfKGjdM36cHb5cPfJvQS2Mpump"

Tame your livestream chat when Pump.fun energy goes orbital. This tool opens the coin page in a real browser, watches the live chat, filters out the spammy noise, and mirrors only the good stuff into a friendly HTML page you can drop into OBS or a second monitor. Think of it as noise‑cancelling headphones for memes, while still vibing with the Pump.fun community.

The filter scans incoming chat logs, removes repetitive spam, and outputs a cleaned HTML shell that incrementally pulls a fragment of just new messages (no more full page reload flicker). You can use it as a browser source overlay in OBS (or any streaming software). Your audience sees genuine conversation—noise minimized.

| ✨ Information | 🧾 Animation |
|---------|---------|
| 🚫 Filters spam, duplicate messages, and repetitive noise. <br> 👤 Keeps only relevant, user-friendly chat messages. <br> 📂 Outputs to a local HTML file for easy OBS overlay. <br> ⚡ Lightweight, fast, and streaming-friendly. <br> 🤝 Community-driven solution for chat moderation. <br><br> 🔸 Per-user rate limiting (default: 1 message per 2s) to chill rapid-fire floods. <br> 🔸 Content deduplication (default: 3-minute TTL) across all users to stop copy/paste storms. <br> 🔸 Keyword / mention bans via simple JSON (substring + case-insensitive). <br> 🔸 Fragment polling (AJAX) prevents flicker, preserves animations. | ![Demo GIF](./animated-char.gif)

## How it helps streamers
- Keep the chat on-screen readable and hype, not hijacked by copy-paste brigades.
- Nuke obvious promo mentions while wholesome apes, devs, and whales keep vibing.
- You control the vibe: update the JSON to match your show’s boundaries.

## Requirements
- Windows (Edge/Chromium available via the `msedge` channel)
- .NET 8 SDK

## Quick start
1. Clone and build:
   ```
   dotnet build
   ```
2. (Optional) Create a filter config at repo root (chat-filter-cfg.json):
   ```json
   {
     "BannedKeywords": ["rug", "scam"],
     "BannedMentions": ["@rugdefs", "@spammer"]
   }
   ```
3. Capture a session (once) so you can run headless:
   ```
   dotnet run --project ./kind-of-stop-spam -- run --token <token_address> --session
   ```
   Log in manually; tool waits up to 5 min, saves `./sessions.data` and exits.
4. Run headless filtering mode:
   ```
   dotnet run --project ./kind-of-stop-spam -- run --token <token_address>
   ```
5. Add `filtered-chat.html` as a Browser Source in OBS.

Example token:
```
2McSmYfSEKUMQEq4JZbb9wq2SeyLrxkd9831EB9Vpump
```

### Updated CLI (Spectre.Console)
```
run --token <token> [--config ./chat-filter-cfg.json] [--session] [--verbose]
```
- `--session` : interactive login & session capture (non-headless)
- `--verbose` : echo each accepted message to console

## Architecture (Fragment + Polling)
Previous meta-refresh approach caused flicker. Now:
- `HtmlFileChatView` writes a stable shell `filtered-chat.html` once (and on updates for safety) + a rolling `filtered-chat.fragment.html` containing only `<li>` message nodes.
- Client JS (in shell) polls the fragment every second, diffs new `data-k` keys, appends without reloading the page.
- Existing nodes retain animation state; one-shot / looping CSS effects remain smooth.

## Filtering Phases
| Phase | Dedup | Rate Limit | Banned Words | Purpose |
|-------|-------|------------|--------------|---------|
| Historical | No | No | Yes | Initial backlog context |
| Live | Yes | Yes | Yes | Real-time cleanliness |

## Chat Command Effects
Add one or more anywhere in a message (removed before display; multiple stack):

| Command | Effect |
|---------|--------|
| `!shake` | Quick intense shake + flash pulse |
| `!wiggle` | Slow horizontal wiggle |
| `!glow` | Neon cyan pulsing glow |
| `!wave` | Character vertical sine wave |
| `!scramble` | Glyph scramble then settle |
| `!type` | Typewriter reveal |
| `!glitch` | One-shot RGB split jitter |
| `!explode` | Characters radiate outward |
| `!matrix` | Green digital rain loop |
| `!fade` | Fade out then in |
| `!slide` | Slide in from left with overshoot |

Example:
```
We live !glow !wave #ff8800 #2200ff
```

## Color Highlights
Include up to two hex colors (`#abc / #aabbcc`) to style message background (solid or gradient). Removed from final text.

## Replies
Reply chains detected and a snippet (trimmed) is shown above the message content.

## Config (`chat-filter-cfg.json`)
```json
{
  "BannedKeywords": ["scam"],
  "BannedMentions": ["@annoyinguser"],
  "WalletIcons": { "@coolwhale": "🐳", "@hq3xqa": "🔥" }
}
```

## Build & Run Summary
```
dotnet build
dotnet run --project kind-of-stop-spam -- run --token <token> [--config path] [--session] [--verbose]
```

## Generated Artifacts
| File | Purpose |
|------|---------|
| `filtered-chat.html` | Static shell overlay (polls fragment) |
| `filtered-chat.fragment.html` | Latest message `<li>` nodes only |
| `sessions.data` | Playwright storage state (login reuse) |

## Overlay Controls
- Auto-scroll toggle keeps viewport pinned to newest message.
- Auto-refresh toggle controls fragment polling (1s cadence).

## Performance Notes
- Only newest N DOM messages scanned per interval.
- Atomic fragment write prevents partially-read HTML.
- Character-wrapped effects only for commands that need per-letter animation.

## Extending Effects
1. Add token to `_effectTokenRegex` in `MessageHelper.cs`.
2. Define CSS keyframes / class in `HtmlFileChatView`.
3. (Optional) Add per-character wrapping if needed.

## Error Handling
- Automatic page reload on Pump.fun client-side exception banner.
- Per-message try/catch isolation.

## Disclaimer
Local-only mirror. DOM changes upstream may require selector adjustments.

---
PRs / suggestions welcome. Enjoy cleaner, expressive chat overlays. 🚀

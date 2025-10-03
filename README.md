# Modatrix PumpFun Chat Filter / Overlay

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

## 1. Capture Session (one time)
You need an authenticated storage state (`sessions.data`).
```
dotnet run --project modatrix-chat -- run --token <tokenAddressPumpSuffix> --session
```
A browser opens; log in and wait until chat loads. When you see the tool message about session saved, close it. `sessions.data` appears in the working directory.

## 2. Normal Headless Run (generates + updates overlay)
```
dotnet run --project modatrix-chat -- run --token <tokenAddressPumpSuffix>
```
Artifacts generated / updated:
- `filtered-chat.html` (viewer shell)
- `filtered-chat.fragment.html` (polled fragment – prevents flicker)

Add `filtered-chat.html` as an OBS Browser Source (local file) OR use the static server via `--show-viewer`.

## 3. Interactive Viewer (local static server)
```
dotnet run --project modatrix-chat -- run --token <token> --show-viewer
```
Starts a tiny `HttpListener` server (default `http://localhost:17999/filtered-chat.html`) and opens a Chromium window. Point OBS to that URL if preferred.

---
## CLI Reference
Command name: `PumpFunTool run`

Required:
- `--token <TOKEN>`  Pump.fun token address (20–50 base58 chars + `pump` suffix). Example: `5Yabc...xyzpump`

Optional:
- `--config <PATH>`  Path to chat filter config JSON (default `./chat-filter-cfg.json`)
- `--session`        Session capture mode (interactive login then exit)
- `--verbose`        Verbose diagnostics (log each accepted message line-by-line)
- `--show-viewer`    Launch embedded viewer + start local static server
- `--port <PORT>`    Port for static server (default 17999; only meaningful with `--show-viewer`)

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
dotnet run --project modatrix-chat -- run --token <token>
```
Exit any running session by pressing any key in the console window.

---
## Config File (`chat-filter-cfg.json`)
Shape (all properties optional):
```json
{
  "bannedKeywords": ["scam", "rug"],
  "bannedMentions": ["@baduser"],
  "walletIcons": {"@friendly": "🤝", "@devteam": "🛠️"}
}
```
Notes:
- Matching is case‑insensitive substring.
- Mentions match as substrings; `@` prefix auto-normalized.
- `walletIcons` maps a (normalized) user tag to an emoji / glyph shown before the username.

---
## Effects (Animated Message Classes)
Internally some messages may include effect class names: `wave`, `scramble`, `explode`, `matrix`, `type`, `wiggle`, `glow`, `shake`, `slide`, `fade`, `glitch`. These trigger per‑character / element animations in the overlay. (Not a public user command API yet.)

---
## Generated Files
- `filtered-chat.html`  Main overlay page (includes JS that polls fragment)
- `filtered-chat.fragment.html`  List `<li>` items only; updated each accepted message
- `sessions.data`  Persisted authenticated storage state (required for non-session runs)

---
## Troubleshooting
- Missing session: Run once with `--session` to create `sessions.data`.
- Token format error: Ensure base58 + `pump` suffix (regex validated).
- No messages / timeout: Login likely not captured; re-run with `--session`.
- Port conflict: Supply a different `--port` when using `--show-viewer`.
- OBS not updating: Ensure Auto-refresh checkbox (in viewer) is checked OR OBS cache disabled.

---
## License
MIT (see repository). Contributions welcome.

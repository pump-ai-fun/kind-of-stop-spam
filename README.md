# Modatrix (the start of a Local Chat Moderation)

![Demo GIF](./banner.jpg)

Spammers are out of control, and this project is built for the community, by the community, to take back control of our chats.
It started as a "kind of stop spam" approach, and it evolved to a foundation of a fully local chat moderation system.
Total vibe coding.

> Modatrix CA on Pump.Fun is "H27BM5y9rjrFbnq3zBkfKGjdM36cHb5cPfJvQS2Mpump"

Tame your livestream chat when Pump.fun energy goes orbital. This tool opens the coin page in a real browser, watches the live chat, filters out the spammy noise, and mirrors only the good stuff into a friendly, auto-refreshing HTML page you can drop into OBS or a second monitor. Think of it as noise-cancelling headphones for memes, while still vibing with the Pump.fun community (we love you, you glorious degen astronauts).

The filter scans incoming chat logs, removes repetitive spam, and outputs a cleaned HTML file that you can use as a browser source overlay in OBS (or any streaming software). This way, your audience sees only the genuine conversations without the noise.

| ✨ Information | 🧾 Animation |
|---------|---------|
| 🚫 Filters spam, duplicate messages, and repetitive noise. <br> 👤 Keeps only relevant, user-friendly chat messages. <br> 📂 Outputs to a local HTML file for easy OBS overlay. <br> ⚡ Lightweight, fast, and streaming-friendly. <br> 🤝 Community-driven solution for chat moderation. <br><br> 🔸 Per-user rate limiting (default: 1 message per 2s) to chill rapid-fire floods. <br> 🔸 Content deduplication (default: 3-minute TTL) across all users to stop copy/paste storms. <br> 🔸 Keyword and mention bans via a simple JSON file (substring and case-insensitive). <br> 🔸 Clean, local HTML mirror connected to OBS (as you can see in this stream). | ![Demo GIF](./animated-char.gif)

## How it helps streamers
- Keep the chat on-screen readable and hype, not hijacked by copy-paste brigades.
- Nuke obvious promo mentions (hi @rugdefs, we see you) while wholesome apes, devs, and whales keep high-fiving.
- You control the vibe: update the JSON to match your show’s boundaries.

## Requirements
- Windows (Edge/Chromium available via the `msedge` channel)
- .NET 8 SDK

## Quick start
1) Clone the repo and restore/build:
   - dotnet build

2) (Optional) Add a filter config at the repo root:
   - chat-filter-cfg.json
     {
       "BannedKeywords": [ "rug", "scam" ],
       "BannedMentions": [ "@rugdefs", "@spammer" ]
     }

3) Run the tool (live mode):
   - dotnet run --project ./kind-of-stop-spam -- <token_address> [path-to-config-json]

Example:
- dotnet run --project ./kind-of-stop-spam -- 2McSmYfSEKUMQEq4JZbb9wq2SeyLrxkd9831EB9Vpump ./chat-filter-cfg.json

The app will:
- Open a local page: filtered-chat.html (auto-refresh, auto-scroll toggle bottom-right)
- Open the Pump.fun coin page, strip distractions, and mirror accepted messages.

Tip: Add filtered-chat.html as a Browser Source in OBS for a tidy on-stream chat.

## Configure filtering
- File: chat-filter-cfg.json (path passed as second argument or defaults to repo root)
- Shape:
  {
    "BannedKeywords": [ "fukk" ],
    "BannedMentions": [ "@badguy", "@spammer" ]
  }

Matching is substring-based and case-insensitive after whitespace is collapsed. Keep it simple and focused.

## Tests
- dotnet test (runs unit tests and a data-file pass that simulates a spicy chat log)

## Design notes
- HtmlFileChatView writes a self-contained page refreshed every 0.5s.
- SpamFilter handles rate limiting + dedup + config-based bans.
- DomHelpers mutes/halts page media and prunes non-chat chrome for performance.

## Caveats
- No cloud, no persistence—this is a local mirror for live use.
- Substring bans are simple by design; if you want exact-words, regex, or user bans, open an issue or PR.

## Shout-out
The Pump.fun community is a rocket—this keeps chat readable while you moon. Good vibes, good memes, and only the best chaos make the cut. 🚀🧪

# PumpFunTool (Filtered Chat Overlay)

A Playwright-based tool that opens a Pump.fun coin page, extracts live chat messages, filters them (banned words, rate limits, deduplication), and renders a local auto-updating HTML overlay containing only accepted messages.

## Usage
```
PumpFunTool <token_address> [path-to-config-json]
```
Default config file (if not supplied): `chat-filter-cfg.json`.

Example token format (validated): `2McSmYfSEKUMQEq4JZbb9wq2SeyLrxkd9831EB9Vpump`

## Features
- Live extraction of Pump.fun chat via Playwright (Edge/Chromium)
- Initial relaxed "historical" pass (last N messages) then strict live filtering
- Per-user rate limiting & duplicate suppression (live phase)
- Configurable banned keywords & mentions via JSON
- Wallet tag → icon mapping (emotes) from config
- Automatic HTML overlay (`filtered-chat.html`) with:
  - Auto-refresh (toggleable, 1s interval) & auto-scroll toggle
  - Gradient / solid highlight background via inline hex codes (#abc, #aabbcc)
  - Reply detection + inline display of replied snippet
  - Message animation commands (see below)

## Chat Command Effects
Add one or more commands (space separated) anywhere in a message. They are removed from the displayed text and converted into CSS/JS driven effects. Multiple may stack.

| Command | Effect |
|---------|--------|
| `!shake` | Quick intense shake + flash pulse (legacy) |
| `!wiggle` | Slow horizontal wiggle (worm-like) |
| `!glow` | Neon cyan pulsing text glow (strong multi-layer text-shadow) |
| `!wave` | Characters move in a looping vertical wave (per-character stagger) |
| `!scramble` | Characters rapidly jumble through random glyphs then settle |
| `!type` | Text reappears with a type/reveal effect |
| `!glitch` | One-shot RGB split + jitter glitch burst |
| `!explode` | Characters radiate outward and fade (one-shot) |
| `!matrix` | Continuous green digital rain fall effect per character |
| `!fade` | Fade out then back in |
| `!slide` | Slide in from left with overshoot settle |

Example:  
```
Hyped for launch! !glow !wave
```

### Color Highlights
Include up to two hex colors in the message body (e.g. `#ff8800 #2200ff`) to produce a gradient background. A single color gives a solid highlight. Colors are stripped from final text.

### Replies
If Pump.fun formats a message as a reply, the referenced snippet is captured and shown in a smaller reply panel above the message content.

## Filtering Phases
| Phase | Dedup | Rate Limit | Banned Words | Purpose |
|-------|-------|------------|--------------|---------|
| Historical | No | No | Yes | Show recent backlog for context |
| Live | Yes | Yes | Yes | Maintain clean real-time feed |

## Config (`chat-filter-cfg.json`)
```json
{
  "BannedKeywords": ["scam"],
  "BannedMentions": ["@annoyinguser"],
  "WalletIcons": { "@coolwhale": "🐳", "@hq3xqa": "🔥" }
}
```
All matching is case-insensitive; input normalized internally.

## Build & Run
```
dotnet build
dotnet run --project kind-of-stop-spam <token> [config]
```

## Generated Artifacts
| File | Purpose |
|------|---------|
| `filtered-chat.html` | Live overlay page | 
| `sessions.data` | Playwright storage state (login/session reuse) |

## Overlay Controls
Bottom toolbar:
- Auto-scroll checkbox keeps the viewport pinned to the latest message.
- Auto-refresh checkbox adds/removes the meta refresh tag (1s reload cadence). Disable it for manual observation or debugging animations.

## Performance Notes
- Only the newest N messages (constant in `Program.cs`) are scanned each poll.
- HTML is atomically rewritten each accepted batch.
- Short animation durations ensure visibility between refreshes; toggle auto-refresh off for longer effect inspection.

## Extending Effects
1. Add new command to effect extraction regex in `SpamFilter` (MessageHelper.cs).  
2. Add CSS keyframes + class in `HtmlFileChatView`.  
3. Update `applyChatEffect` JS map if you want runtime retriggering.  
4. (Optional) Wrap characters if per-letter animation is required.

## Error Handling
- Page reload on detected Pump.fun client-side exception banner.
- Safe try/catch around DOM operations (skips transient failures).

## Disclaimer
For educational / personal overlay usage. Pump.fun DOM changes may break selectors; adjust locators if that happens.

---
PRs / suggestions welcome. Enjoy cleaner and more expressive chat overlays. 🚀

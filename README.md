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

using Microsoft.Playwright;

namespace kind_of_stop_spam
{
    /// <summary>
    /// DOM helper utilities for Playwright interactions.
    /// </summary>
    public static class DomHelpers
    {
        /// <summary>
        /// Remove the first matching element if present.
        /// </summary>
        public static async Task RemoveElementAsync(ILocator locator)
        {
            if (await locator.CountAsync() == 0) return;
            await locator.First.EvaluateAsync(@"(el) => { el.remove(); }");
        }

        /// <summary>
        /// Hide the first matching element if present.
        /// </summary>
        public static async Task HideElementAsync(ILocator locator)
        {
            if (await locator.CountAsync() == 0) return;
            await locator.First.EvaluateAsync(@"(el) => { el.style.setProperty('display', 'none', 'important'); }");
        }

        /// <summary>
        /// Best-effort attempt to mute and stop a video element and remove iframes.
        /// </summary>
        public static async Task EnsureVideoMutedAndStoppedAsync(ILocator video)
        {
            // Make sure we have at least one attached element
            if (await video.CountAsync() == 0) return;
            await video.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = 3000
            });

            // Remove iframes that may autoplay content
            await video.Page.EvaluateAsync(@"() => { document.querySelectorAll('iframe').forEach(f => f.remove()); }");

            await video.First.EvaluateAsync(@"(el) => {
            if (!(el instanceof HTMLVideoElement)) return;

            // Hard mute
            el.muted = true;
            el.volume = 0;

            // Stop playback
            try { el.pause(); } catch {}

            // Don’t auto-play again
            el.autoplay = false;
            el.removeAttribute('autoplay');
            el.loop = false;
            el.removeAttribute('loop');

            // Reset to start (optional)
            try { el.currentTime = 0; } catch {}

            // If it’s a live stream (MediaStream), stop all tracks
            try {
                if (el.srcObject) {
                    const tracks = el.srcObject.getTracks?.() || [];
                    tracks.forEach(t => { try { t.stop(); } catch {} });
                    el.srcObject = null;
                }
            } catch {}

            // Make future .play() attempts auto-mute again
            if (!el.__patchedPlay) {
                el.__patchedPlay = true;
                const orig = el.play.bind(el);
                el.play = (...args) => {
                    try { el.muted = true; el.volume = 0; el.autoplay = false; el.removeAttribute('autoplay'); } catch {}
                    return orig(...args).catch(() => Promise.resolve());
                };
            }
        }");
        }
    }
}

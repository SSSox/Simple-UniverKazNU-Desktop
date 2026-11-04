using System.Collections.Generic;

namespace Univer.Desktop.Core
{
    // Chromium drops the release of a press it saw handled, so a press is kept only until another key goes down (Alt+Left
    // that closed the viewer must not go Back on its release). A release with no press is a PDF-guest shortcut.
    internal sealed class KeyReleases
    {
        // Covers an ordinary hold; auto-repeat renews the press.
        public const int Lifetime = 2000;
        private readonly Dictionary<int, int> pressed = new Dictionary<int, int>();
        public void Pressed(int keyCode, int now) { pressed[keyCode] = now; }
        // Any key-down seen before the renderer; a different key ends the earlier press.
        public void KeyDown(int keyCode)
        {
            if (pressed.Count == 0) return;
            int at; var held = pressed.TryGetValue(keyCode, out at);
            pressed.Clear();
            if (held) pressed[keyCode] = at;
        }
        // A key-down that reached the page (Chromium 109's PDF guest bypasses the pre-renderer hook) ends every handled press.
        public void PageKeyDown() { pressed.Clear(); }
        // True when this release ends a key-down that was already dispatched.
        public bool Released(int keyCode, int now)
        {
            int at;
            if (!pressed.TryGetValue(keyCode, out at)) return false;
            pressed.Remove(keyCode);
            return unchecked(now - at) <= Lifetime;
        }
    }
}

using System.Collections.Generic;

namespace Univer.Desktop.Core
{
    // The page's script contexts as DevTools reports them (origin, frame), to tell whether a message's context id is
    // Univer's own main document. DevTools raises its events on its own threads.
    internal sealed class ScriptContexts
    {
        private readonly object sync = new object();
        private readonly Dictionary<int, string> origins = new Dictionary<int, string>(), frames = new Dictionary<int, string>();
        private string mainFrame;
        public void MainFrame(string frameId) { lock (sync) mainFrame = frameId; }
        // frameId is null for a context that is not its frame's default one (an extension or isolated world).
        public void Created(int id, string origin, string frameId)
        {
            lock (sync) { origins[id] = origin; if (frameId != null) frames[id] = frameId; }
        }
        public void Destroyed(int id) { lock (sync) { origins.Remove(id); frames.Remove(id); } }
        public void Cleared() { lock (sync) { origins.Clear(); frames.Clear(); } }
        // trusted: the context's origin is Univer's; main: it is the default context of the main frame.
        public void Check(int id, out bool trusted, out bool main)
        {
            lock (sync)
            {
                string origin, frameId;
                trusted = origins.TryGetValue(id, out origin) && SitePolicy.Trusted(origin);
                main = frames.TryGetValue(id, out frameId) && frameId == mainFrame;
            }
        }
    }
}

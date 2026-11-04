using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    internal enum BrowserProfileFlavor { Legacy, Modern }
    internal sealed class Credentials
    {
        public string Login { get; set; }
        public string Password { get; set; }
    }
    internal sealed class Preferences
    {
        public string Language { get; set; } = "ru";
        public bool AutoLoginPaused { get; set; }
        public bool Readable { get; set; } = true;
        public bool Sidebar { get; set; } = true;
        public string Profile { get; set; } = Guid.NewGuid().ToString("N");
        // Optional presentation state; older preference files simply omit these values.
        public int Zoom { get; set; } = 100;
        public string Window { get; set; } = "";
        public bool Tray { get; set; } = true;
        // Interface motion is on unless the student turns it off here; Windows' animation setting does not apply.
        public bool Animations { get; set; } = true;
        // Optional, local to this Windows user's app data; old installs show the notice once.
        public bool LegacyWarningDismissed { get; set; }
        // "system" follows Windows' app mode; "light" and "dark" fix the theme. Older preference files have none.
        public string Theme { get; set; } = ThemeChoice.System;
        // Sections and useful links pinned to the home screen; older preference files have none.
        public List<PinnedLink> Pins { get; set; } = new List<PinnedLink>();
        public List<string> TileOrder { get; set; } = new List<string>();
        public Preferences Clone()
        {
            return new Preferences { Language = Language, AutoLoginPaused = AutoLoginPaused, Readable = Readable, Sidebar = Sidebar,
                Profile = Profile, Zoom = Zoom, Window = Window, Tray = Tray, Animations = Animations, LegacyWarningDismissed = LegacyWarningDismissed, Theme = Theme,
                Pins = (Pins ?? new List<PinnedLink>()).Select(p => p?.Clone()).ToList(), TileOrder = (TileOrder ?? new List<string>()).ToList() };
        }
    }
    internal sealed class StoredState
    {
        public Preferences Preferences { get; set; }
        public Credentials Credentials { get; set; }
        // The settings file could not be read; defaults are in use and only a readable saved sign-in keeps signing in.
        public bool Failed { get; set; }
        // A saved sign-in exists but cannot be decrypted for this Windows user; the settings themselves are fine.
        public bool CredentialsUnreadable { get; set; }
    }
    internal sealed class Storage
    {
        private readonly string root;
        private readonly BrowserProfileFlavor browserFlavor;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly AsyncOperationGate writes = new AsyncOperationGate();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Univer.Desktop.credentials.v1");
        // Browser profiles are named by a GUID without dashes; no other folder name is ever created, used or deleted.
        private static readonly Regex ProfileName = new Regex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant);
        public static bool IsProfileName(string name) { return name != null && ProfileName.IsMatch(name); }
        public Storage(string path, BrowserProfileFlavor browserFlavor = BrowserProfileFlavor.Legacy)
        { root = path; this.browserFlavor = browserFlavor; Directory.CreateDirectory(root); }
        public string Root { get { return root; } }
        public Preferences LoadPreferences()
        {
            var path = Path.Combine(root, "preferences.json");
            var value = File.Exists(path) ? json.Deserialize<Preferences>(File.ReadAllText(path)) : new Preferences();
            if (value == null || !IsProfileName(value.Profile)) throw new IOException("Invalid preferences");
            if (value.Language != "ru" && value.Language != "kz" && value.Language != "en") value.Language = "ru";
            if (value.Zoom < 50 || value.Zoom > 200) value.Zoom = 100;
            value.Theme = ThemeChoice.Normalize(value.Theme);
            if (value.Window == null || !Regex.IsMatch(value.Window, @"^-?\d{1,5},-?\d{1,5},\d{3,5},\d{3,5},[01]$")) value.Window = "";
            value.Pins = Shortcuts.Normalize(value.Pins);
            value.TileOrder = Shortcuts.NormalizeOrder(value.TileOrder);
            return value;
        }
        // Loaded independently, so an unreadable DPAPI record (for example after a Windows password reset) keeps valid settings.
        public StoredState Load()
        {
            var state = new StoredState();
            try { state.Preferences = LoadPreferences(); }
            catch (Exception) { state.Preferences = new Preferences { AutoLoginPaused = true }; state.Failed = true; }
            try { state.Credentials = LoadCredentials(); }
            catch (Exception) { state.CredentialsUnreadable = true; }
            // Damaged settings cannot say the student signed out; the saved sign-in still opens the session.
            if (state.Failed && state.Credentials != null) state.Preferences.AutoLoginPaused = false;
            return state;
        }
        public void SavePreferences(Preferences preferences) { AtomicFile.Write(Path.Combine(root, "preferences.json"), Encoding.UTF8.GetBytes(json.Serialize(preferences))); }
        public Credentials LoadCredentials()
        {
            var path = Path.Combine(root, "credentials.dpapi");
            if (!File.Exists(path)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            try { return json.Deserialize<Credentials>(Encoding.UTF8.GetString(bytes)); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        public void SaveCredentials(Credentials credentials)
        {
            var bytes = Encoding.UTF8.GetBytes(json.Serialize(credentials));
            try { AtomicFile.Write(Path.Combine(root, "credentials.dpapi"), ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        public void RemoveCredentials() { File.Delete(Path.Combine(root, "credentials.dpapi")); }
        public Task SavePreferencesAsync(Preferences preferences)
        {
            // Capture before waiting: a later UI edit cannot change an already queued write.
            var snapshot = preferences.Clone();
            return writes.RunAsync(() => Task.Run(() => SavePreferences(snapshot)));
        }
        public Task SaveCredentialsAsync(Credentials credentials)
        {
            var snapshot = new Credentials { Login = credentials.Login, Password = credentials.Password };
            return writes.RunAsync(() => Task.Run(() => SaveCredentials(snapshot)));
        }
        public Task RemoveCredentialsAsync() { return writes.RunAsync(() => Task.Run((Action)RemoveCredentials)); }
        // The site's notifications of the signed-in account: titles and page messages, never a form value or a secret.
        // An unreadable file starts an empty list.
        public List<SiteNotice> LoadNotices()
        {
            var path = Path.Combine(root, "notices.json");
            try { return File.Exists(path) ? Notices.Normalize(json.Deserialize<List<SiteNotice>>(File.ReadAllText(path)), Notices.Now()) : new List<SiteNotice>(); }
            catch (Exception) { return new List<SiteNotice>(); }
        }
        public Task SaveNoticesAsync(IEnumerable<SiteNotice> notices)
        {
            var snapshot = notices.Select(n => n.Copy()).ToList();
            var path = Path.Combine(root, "notices.json");
            return writes.RunAsync(() => Task.Run(() =>
            {
                if (snapshot.Count == 0) File.Delete(path);
                else AtomicFile.Write(path, Encoding.UTF8.GetBytes(json.Serialize(snapshot)));
            }));
        }
        public Task CleanOldProfilesAsync(string active) { return writes.RunAsync(() => Task.Run(() => CleanOldProfiles(active))); }
        public string BrowserRoot { get { return Path.Combine(root, browserFlavor == BrowserProfileFlavor.Modern ? "Browser-Modern" : "Browser"); } }
        public string ProfilePath(string profile)
        {
            if (!IsProfileName(profile)) throw new ArgumentException("Invalid profile");
            return Path.Combine(BrowserRoot, profile);
        }
        // Only obsolete session profiles within this application's own Browser directory.
        public void CleanOldProfiles(string active)
        {
            if (!Directory.Exists(BrowserRoot) || !ProfileCleanup.IsPlainPath(BrowserRoot)) return;
            var prefix = Path.GetFullPath(BrowserRoot) + Path.DirectorySeparatorChar;
            foreach (var path in Directory.GetDirectories(BrowserRoot))
            {
                var name = Path.GetFileName(path);
                if (name == active || !IsProfileName(name)) continue;
                if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ProfileCleanup.IsPlainTree(path)) continue;
                try { Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}

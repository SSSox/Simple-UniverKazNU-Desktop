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
        // The plain DPAPI file holds only the login: the password is sealed under the app PIN (Core/AppLock.cs) and is
        // known once the PIN is entered. Older files have no such mark.
        public bool Sealed { get; set; }
        // The account can sign in: its password is known. A sealed account before the PIN has a login only.
        public bool Usable() { return !string.IsNullOrEmpty(Login) && Password != null; }
        // Automatic sign-in runs for a saved account whose password is known and that the student has not paused. A
        // password still sealed under the PIN is a wait: nothing is tried, so no attempt of the budget is spent.
        public static bool SignInActive(Credentials credentials, Preferences prefs) { return credentials != null && credentials.Usable() && !prefs.AutoLoginPaused; }
        public Credentials LoginOnly() { return new Credentials { Login = Login, Sealed = true }; }
    }
    internal sealed class Preferences
    {
        public string Language { get; set; } = "ru";
        public bool AutoLoginPaused { get; set; }
        // A logout or an account change has begun, and the browser profiles it leaves may still hold the session. Saved
        // with the pause and cleared with the new profile names; a start that finds it finishes the change
        // (Storage.FinishSessionClear). Older preference files have none.
        public bool SessionClearPending { get; set; }
        public bool KeptCopiesClearPending { get; set; }
        public bool NoticesClearPending { get; set; }
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
        public List<string> HiddenTiles { get; set; } = new List<string>();
        // What the last update check found, when it ran (UTC ticks), and the version the student chose not to see again.
        public string UpdateVersion { get; set; } = "";
        public string UpdateSkipped { get; set; } = "";
        public long UpdateChecked { get; set; }
        // Web links open in the app's own browser window unless the student asks for the system browser. That window keeps
        // its cookies in a profile of its own (WebProfile), apart from the Univer session.
        public bool ExternalLinks { get; set; }
        public string WebProfile { get; set; } = Guid.NewGuid().ToString("N");
        // The one-time suggestion to protect the app with a PIN has been shown.
        public bool PinOffered { get; set; }
        // A tray reminder before each class of the kept week (Core/ClassReminders.cs), this many minutes ahead.
        public bool Reminders { get; set; } = true;
        public int ReminderLead { get; set; } = ClassReminders.DefaultLead;
        // A tray notification when «Текущая аттестация» shows changed scores (Core/GradeWatch.cs), read this many minutes apart.
        public bool Grades { get; set; } = true;
        public int GradeInterval { get; set; } = GradeNotice.DefaultInterval;
        // The order of «Загрузки» (DownloadOrder.Name) and the section the app opens with (StartPages); older files have none.
        public string DownloadsOrder { get; set; } = "newest";
        public string StartPage { get; set; } = StartPages.Home;
        public Preferences Clone()
        {
            return new Preferences { Language = Language, AutoLoginPaused = AutoLoginPaused, SessionClearPending = SessionClearPending, KeptCopiesClearPending = KeptCopiesClearPending, NoticesClearPending = NoticesClearPending, Readable = Readable, Sidebar = Sidebar,
                Profile = Profile, Zoom = Zoom, Window = Window, Tray = Tray, Animations = Animations, LegacyWarningDismissed = LegacyWarningDismissed, Theme = Theme,
                UpdateVersion = UpdateVersion, UpdateSkipped = UpdateSkipped, UpdateChecked = UpdateChecked,
                ExternalLinks = ExternalLinks, WebProfile = WebProfile, PinOffered = PinOffered, Reminders = Reminders, ReminderLead = ReminderLead, Grades = Grades, GradeInterval = GradeInterval,
                DownloadsOrder = DownloadsOrder, StartPage = StartPage,
                Pins = (Pins ?? new List<PinnedLink>()).Select(p => p?.Clone()).ToList(), TileOrder = (TileOrder ?? new List<string>()).ToList(), HiddenTiles = (HiddenTiles ?? new List<string>()).ToList() };
        }
    }
    internal sealed class StoredState
    {
        public Preferences Preferences { get; set; }
        public Credentials Credentials { get; set; }
        // The settings file could not be read; defaults are in use and automatic sign-in waits for explicit re-entry.
        public bool Failed { get; set; }
        // A saved sign-in exists but cannot be decrypted for this Windows user; the settings themselves are fine.
        public bool CredentialsUnreadable { get; set; }
        // The password was sealed under a PIN whose record is now missing: the account and its session have to end.
        public bool SealedOrphan { get; set; }
        // credentials.dpapi is on disk: it holds the plain password, or is left beside a sealed login (its removal failed
        // or was cut short, maybe with an older account's password). With a PIN, the next unlock seals what is known and
        // removes it.
        public bool PlainFile { get; set; }
    }
    // A credential file is still locked. The marker stays, and the next read tries again. Callers that treat every
    // IOException as a broken settings file must be able to tell this one apart from a failed preferences write.
    internal sealed class PendingCredentialRemovalException : IOException
    {
        public PendingCredentialRemovalException(Exception inner) : base("Credential removal is pending", inner) { }
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
            var value = File.Exists(path) ? json.Deserialize<Preferences>(ReadShared(path)) : new Preferences();
            if (value == null || !IsProfileName(value.Profile)) throw new IOException("Invalid preferences");
            if (value.Language != "ru" && value.Language != "kz" && value.Language != "en") value.Language = "ru";
            if (value.Zoom < 50 || value.Zoom > 200) value.Zoom = 100;
            value.Theme = ThemeChoice.Normalize(value.Theme);
            if (value.Window == null || !Regex.IsMatch(value.Window, @"^-?\d{1,5},-?\d{1,5},\d{3,5},\d{3,5},[01]$")) value.Window = "";
            value.Pins = Shortcuts.Normalize(value.Pins);
            value.TileOrder = Shortcuts.NormalizeOrder(value.TileOrder);
            value.HiddenTiles = Shortcuts.NormalizeHidden(value.HiddenTiles);
            if (!Updates.IsVersion(value.UpdateVersion)) value.UpdateVersion = "";
            if (!Updates.IsVersion(value.UpdateSkipped)) value.UpdateSkipped = "";
            if (value.UpdateChecked < 0) value.UpdateChecked = 0;
            value.GradeInterval = GradeNotice.Interval(value.GradeInterval);
            value.DownloadsOrder = DownloadOrder.Name(DownloadOrder.Parse(value.DownloadsOrder));
            value.StartPage = StartPages.Normalize(value.StartPage);
            if (!IsProfileName(value.WebProfile)) value.WebProfile = Guid.NewGuid().ToString("N");
            return value;
        }
        // A reader never stands in the way of a save: File.Replace must be able to delete the file it replaces.
        private static string ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
        }
        // Loaded independently, so an unreadable DPAPI record (for example after a Windows password reset) keeps valid settings.
        public StoredState Load(Preferences preloaded = null)
        {
            var state = new StoredState();
            if (preloaded != null) state.Preferences = preloaded.Clone();
            else
            {
                try { state.Preferences = LoadPreferences(); }
                catch (Exception) { state.Preferences = new Preferences { AutoLoginPaused = true }; state.Failed = true; }
            }
            try { state.Credentials = LoadCredentials(); }
            catch (Exception) { state.CredentialsUnreadable = true; }
            state.PlainFile = PlainFileLeft();
            // A password sealed under the PIN whose record is gone (not merely unreadable) cannot come back: the account
            // ends here, and the start replaces the browser profiles so the deleted PIN does not open the old session.
            var sealedLogin = state.Credentials != null && state.Credentials.Sealed && state.Credentials.Password == null;
            if ((sealedLogin || PinMarked) && !LockExists)
            {
                state.SealedOrphan = true;
                if (state.Credentials != null) { state.Credentials = null; state.CredentialsUnreadable = true; }
            }
            // An unreadable or missing settings file cannot revoke a previous explicit logout. Keep the protected
            // record available for an explicit re-entry, without silently using it to start a new session.
            if (state.Credentials != null && !File.Exists(Path.Combine(root, "preferences.json"))) state.Preferences.AutoLoginPaused = true;
            return state;
        }
        public void SavePreferences(Preferences preferences)
        {
            // A settings snapshot taken before a notice clear must not erase that clear while notices.json is still there.
            // The clear itself deletes the file first, and only then may a save turn the mark off.
            KeepPendingNoticeClear(preferences);
            AtomicFile.Write(Path.Combine(root, "preferences.json"), Encoding.UTF8.GetBytes(json.Serialize(preferences)), durable: true);
        }
        private void KeepPendingNoticeClear(Preferences preferences)
        {
            if (preferences.NoticesClearPending || !File.Exists(Path.Combine(root, "notices.json"))) return;
            try
            {
                var path = Path.Combine(root, "preferences.json");
                if (!File.Exists(path)) return;
                var saved = json.Deserialize<Preferences>(ReadShared(path));
                if (saved != null && saved.NoticesClearPending) preferences.NoticesClearPending = true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        // The previous run began a logout or an account change and ended before the browser profiles were replaced, so
        // they may still hold that session. This start leaves them behind: the settings name new, empty profiles (the old
        // ones are deleted as obsolete profiles), the stored notifications of that account go, and automatic sign-in
        // stays paused. A start also retries any pending kept copy deletes. A review run changes nothing on disk.
        public bool FinishSessionClear(Preferences preferences, bool save = true)
        {
            var credentialsPending = CredentialsRemovalPending;
            var sessionPending = preferences.SessionClearPending;
            if (!sessionPending && !preferences.KeptCopiesClearPending && !preferences.NoticesClearPending)
            {
                if (save && credentialsPending) FinishCredentialRemoval();
                return credentialsPending;
            }
            if (sessionPending)
            {
                preferences.Profile = Guid.NewGuid().ToString("N"); preferences.WebProfile = Guid.NewGuid().ToString("N");
                preferences.AutoLoginPaused = true; preferences.SessionClearPending = false;
                preferences.NoticesClearPending = preferences.KeptCopiesClearPending = true;
            }
            if (!save) return true;
            if (sessionPending) TryRemoveSessionFile(GradeFile.Remove);
            var noticesRemoved = !preferences.NoticesClearPending || TryRemoveSessionFile(path => File.Delete(Path.Combine(path, "notices.json")));
            // Attempt every delete, even if another copy is locked.
            var removed = !preferences.KeptCopiesClearPending ||
                (TryRemoveSessionFile(ScheduleFile.Remove) & TryRemoveSessionFile(ExamFile.Remove) & TryRemoveSessionFile(LastGradesFile.Remove));
            if (sessionPending || (preferences.NoticesClearPending && noticesRemoved) || (preferences.KeptCopiesClearPending && removed))
            {
                var updated = preferences.Clone();
                updated.NoticesClearPending = !noticesRemoved;
                updated.KeptCopiesClearPending = !removed;
                SavePreferences(updated);
                preferences.NoticesClearPending = updated.NoticesClearPending;
                preferences.KeptCopiesClearPending = updated.KeptCopiesClearPending;
            }
            // Even a locked credential file must leave the ended session's browser profiles behind.
            if (credentialsPending) FinishCredentialRemoval();
            return true;
        }

        private bool TryRemoveSessionFile(Action<string> remove)
        {
            try { remove(root); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        // A plain account lives in credentials.dpapi. A login whose password is sealed under the PIN lives in account.dpapi,
        // which older builds (and the installed app, sharing this folder) do not read: they would send its missing password
        // to the site. Each kind is written before the other file goes, so a crash (or a file another process holds) can
        // leave both: the newer one is the account, the plain one on a tie. A plain file left beside a sealed login still
        // holds a password, so its removal must succeed and the caller tries again (PlainFileLeft); the sealed login left
        // beside a plain save holds no secret, and its removal is best effort.
        public Credentials LoadCredentials()
        {
            if (CredentialsRemovalPending) return null;
            var plain = Path.Combine(root, "credentials.dpapi");
            var sealedLogin = Path.Combine(root, "account.dpapi");
            if (!File.Exists(plain)) return ReadCredentials(sealedLogin);
            if (File.Exists(sealedLogin) && File.GetLastWriteTimeUtc(sealedLogin) > File.GetLastWriteTimeUtc(plain)) return ReadCredentials(sealedLogin);
            return ReadCredentials(plain);
        }
        private Credentials ReadCredentials(string path)
        {
            if (!File.Exists(path)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            try { return json.Deserialize<Credentials>(Encoding.UTF8.GetString(bytes)); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        public void SaveCredentials(Credentials credentials)
        {
            FinishCredentialRemoval();
            var sealedLogin = credentials.Sealed && credentials.Password == null;
            var bytes = Encoding.UTF8.GetBytes(json.Serialize(credentials));
            try { AtomicFile.Write(Path.Combine(root, sealedLogin ? "account.dpapi" : "credentials.dpapi"), ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser), durable: true); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
            if (sealedLogin) { File.Delete(Path.Combine(root, "credentials.dpapi")); return; }
            try { File.Delete(Path.Combine(root, "account.dpapi")); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public bool PlainFileLeft() { return File.Exists(Path.Combine(root, "credentials.dpapi")); }
        public bool CredentialsRemovalPending { get { return File.Exists(Path.Combine(root, "credentials.remove")); } }
        public void RemoveCredentials(bool removeLock = false)
        {
            // The marker reaches disk before either password copy is touched. A failure keeps both reads and PIN
            // unlocking blocked until a later attempt finishes the removal. A forgotten PIN removes the whole lock,
            // including an unreadable record; keep that intent across a restart too.
            if (removeLock || !CredentialsRemovalPending)
                AtomicFile.Write(Path.Combine(root, "credentials.remove"), new[] { removeLock ? (byte)1 : (byte)0 }, durable: true);
            FinishCredentialRemoval();
        }
        // Retries a removal the last run (or this one) could not finish. A locked file still throws, so the marker stays.
        // Startup calls this even when preferences.json itself could not be read.
        internal void FinishPendingCredentialRemoval() { FinishCredentialRemoval(); }
        private void FinishCredentialRemoval()
        {
            if (!CredentialsRemovalPending) return;
            // Only a locked file is pending. A preferences write that fails in the same startup must stay a storage failure.
            try { CompleteCredentialRemoval(); }
            catch (IOException ex) { throw new PendingCredentialRemovalException(ex); }
            catch (UnauthorizedAccessException ex) { throw new PendingCredentialRemovalException(ex); }
        }
        private void CompleteCredentialRemoval()
        {
            var markerPath = Path.Combine(root, "credentials.remove");
            // Captured before anything is deleted: a lock written after this is a new PIN, not the account being removed.
            var markerWritten = File.GetLastWriteTimeUtc(markerPath);
            var marker = File.ReadAllBytes(markerPath);
            File.Delete(Path.Combine(root, "credentials.dpapi"));
            File.Delete(Path.Combine(root, "account.dpapi"));
            var removeLock = marker.Length > 0 && marker[0] == 1;
            if (removeLock)
            {
                if (!LockWrittenAfter(markerWritten)) RemoveLock();
            }
            else if (!LockWrittenAfter(markerWritten))
            {
                LockRecord record = null;
                var undecryptable = false;
                try { record = ReadLock(); }
                // A Windows password reset or a damaged record can never yield the sealed password. Leaving the marker
                // would make every later read throw. A locked file (IOException, UnauthorizedAccessException) stays pending.
                catch (CryptographicException) { undecryptable = true; }
                catch (InvalidDataException) { undecryptable = true; }
                catch (ArgumentException) { undecryptable = true; }
                catch (InvalidOperationException) { undecryptable = true; }
                if (undecryptable)
                {
                    if (!LockWrittenAfter(markerWritten)) RemoveLock();
                }
                else if (record != null && record.Account != null && !LockWrittenAfter(markerWritten))
                {
                    record.Account = null;
                    WriteLock(record);
                }
            }
            File.Delete(markerPath);
        }
        private bool LockWrittenAfter(DateTime markerWritten)
        {
            var path = Path.Combine(root, "lock.dpapi");
            return File.Exists(path) && File.GetLastWriteTimeUtc(path) > markerWritten;
        }
        // The app PIN's check and guess counter (Core/AppLock.cs), protected like the credentials under an entropy of its own.
        private static readonly byte[] LockEntropy = Encoding.UTF8.GetBytes("Univer.Desktop.lock.v1");
        public bool LockExists { get { return File.Exists(Path.Combine(root, "lock.dpapi")); } }
        // lock.pin marks that a PIN is set (it holds nothing secret). It is written after lock.dpapi and deleted before it,
        // so the app itself never leaves the mark without the record: a mark without lock.dpapi means the PIN file was
        // deleted from outside, and the start ends the session (Storage.Load, SealedOrphan). Older installs get the mark
        // with the next write of the record (any PIN entered).
        public bool PinMarked { get { return File.Exists(Path.Combine(root, "lock.pin")); } }
        public ILockStore LockStore { get { return new FileLockStore(this); } }
        public LockRecord LoadLock()
        {
            // An interrupted removal is tried again here. If the file is still locked, this throws and the PIN stays closed.
            FinishCredentialRemoval();
            return ReadLock();
        }
        private LockRecord ReadLock()
        {
            var path = Path.Combine(root, "lock.dpapi");
            if (!File.Exists(path)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), LockEntropy, DataProtectionScope.CurrentUser);
            try { return json.Deserialize<LockRecord>(Encoding.UTF8.GetString(bytes)) ?? throw new InvalidDataException("Empty lock record"); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        public void SaveLock(LockRecord record)
        {
            // Finish first, through WriteLock below, so this new record is not what a later credential save erases.
            FinishCredentialRemoval();
            WriteLock(record);
        }
        private void WriteLock(LockRecord record)
        {
            Directory.CreateDirectory(root);
            var bytes = Encoding.UTF8.GetBytes(json.Serialize(record));
            try { AtomicFile.Write(Path.Combine(root, "lock.dpapi"), ProtectedData.Protect(bytes, LockEntropy, DataProtectionScope.CurrentUser), durable: true); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
            // The mark is best effort: the record is already saved, and a missing mark only leaves the deleted-file check off.
            try { if (!PinMarked) AtomicFile.Write(Path.Combine(root, "lock.pin"), new byte[0], durable: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // The mark goes first: a failure there keeps the record, and the PIN stays as it was.
        public void RemoveLock() { if (PinMarked) File.Delete(Path.Combine(root, "lock.pin")); File.Delete(Path.Combine(root, "lock.dpapi")); }
        private sealed class FileLockStore : ILockStore
        {
            private readonly Storage storage;
            public FileLockStore(Storage storage) { this.storage = storage; }
            public LockRecord Load() { return storage.LoadLock(); }
            public void Save(LockRecord record) { storage.SaveLock(record); }
            public void Remove() { storage.RemoveLock(); }
        }
        public Task SavePreferencesAsync(Preferences preferences)
        {
            // Capture before waiting: a later UI edit cannot change an already queued write.
            var snapshot = preferences.Clone();
            return writes.RunAsync(() => Task.Run(() => SavePreferences(snapshot)));
        }
        public Task SaveCredentialsAsync(Credentials credentials)
        {
            var snapshot = new Credentials { Login = credentials.Login, Password = credentials.Password, Sealed = credentials.Sealed };
            return writes.RunAsync(() => Task.Run(() => SaveCredentials(snapshot)));
        }
        public Task RemoveCredentialsAsync(bool removeLock = false) { return writes.RunAsync(() => Task.Run(() => RemoveCredentials(removeLock))); }
        // The site's notifications of the signed-in account: titles and page messages, never a form value or a secret.
        // An unreadable, unbound or different account's file starts an empty list.
        private sealed class NoticeCache
        {
            public string Account { get; set; }
            public List<SiteNotice> Notices { get; set; }
        }
        public List<SiteNotice> LoadNotices(string account, bool clearPending = false)
        {
            if (clearPending || string.IsNullOrEmpty(account)) return new List<SiteNotice>();
            var path = Path.Combine(root, "notices.json");
            try
            {
                var cache = File.Exists(path) ? json.Deserialize<NoticeCache>(ReadShared(path)) : null;
                return cache != null && cache.Account == account ? Notices.Normalize(cache.Notices, Notices.Now()) : new List<SiteNotice>();
            }
            catch (Exception) { return new List<SiteNotice>(); }
        }
        public Task SaveNoticesAsync(string account, IEnumerable<SiteNotice> notices)
        {
            var snapshot = notices.Select(n => n.Copy()).ToList();
            var path = Path.Combine(root, "notices.json");
            return writes.RunAsync(() => Task.Run(() =>
            {
                if (snapshot.Count == 0 || string.IsNullOrEmpty(account)) File.Delete(path);
                else AtomicFile.Write(path, Encoding.UTF8.GetBytes(json.Serialize(new NoticeCache { Account = account, Notices = snapshot })));
            }));
        }
        // The app's copy of this week's classes (Core/ScheduleCopy.cs); another account's, damaged or missing is none.
        public ScheduleWeek LoadSchedule(string account) { return ScheduleFile.Read(root, account); }
        public Task SaveScheduleAsync(ScheduleWeek week) { return writes.RunAsync(() => Task.Run(() => ScheduleFile.Write(root, week))); }
        public Task RemoveScheduleAsync() { return writes.RunAsync(() => Task.Run(() => ScheduleFile.Remove(root))); }
        // The exams and midterms kept for the exam reminders (Core/ExamCopy.cs), by the same rules.
        public ExamPlan LoadExams(string account) { return ExamFile.Read(root, account); }
        public Task SaveExamsAsync(ExamPlan plan) { return writes.RunAsync(() => Task.Run(() => ExamFile.Write(root, plan))); }
        public Task RemoveExamsAsync() { return writes.RunAsync(() => Task.Run(() => ExamFile.Remove(root))); }
        // The course totals last seen, for the offline screen (Core/LastGrades.cs), by the same rules.
        public LastGrades LoadLastGrades(string account) { return LastGradesFile.Read(root, account); }
        public Task SaveLastGradesAsync(LastGrades grades) { return writes.RunAsync(() => Task.Run(() => LastGradesFile.Write(root, grades))); }
        public Task RemoveLastGradesAsync() { return writes.RunAsync(() => Task.Run(() => LastGradesFile.Remove(root))); }
        public GradeSheet LoadGrades(string account) { return GradeFile.Read(root, account); }
        public Task SaveGradesAsync(GradeSheet sheet) { return writes.RunAsync(() => Task.Run(() => GradeFile.Write(root, sheet))); }
        public Task RemoveGradesAsync() { return writes.RunAsync(() => Task.Run(() => GradeFile.Remove(root))); }
        public Task CleanOldProfilesAsync(string active) { return writes.RunAsync(() => Task.Run(() => CleanOldProfiles(active))); }
        // The start's housekeeping, after the window is shown: in line with the settings writes, and only while the saved
        // settings still name the profiles it was given. A logout or account change saves the new names before it opens
        // their profiles, so a cleanup queued before it can never delete a profile in use.
        public Task CleanAtStartAsync(string profile, string webProfile)
        {
            return writes.RunAsync(() => Task.Run(() =>
            {
                var path = Path.Combine(root, "preferences.json");
                var saved = File.Exists(path) ? LoadPreferences() : null;
                if (saved == null || saved.Profile == profile) CleanOldProfiles(profile);
                if (saved != null && saved.WebProfile == webProfile) CleanOldWebProfiles(webProfile);
            }));
        }
        public string BrowserRoot { get { return Path.Combine(root, browserFlavor == BrowserProfileFlavor.Modern ? "Browser-Modern" : "Browser"); } }
        public string ProfilePath(string profile)
        {
            if (!IsProfileName(profile)) throw new ArgumentException("Invalid profile");
            return Path.Combine(BrowserRoot, profile);
        }
        // The in-app browser's profile for other sites: web-{name} beside the Univer profiles, which their cleanup never takes.
        public const string WebPrefix = "web-";
        public string WebProfilePath(string profile)
        {
            if (!IsProfileName(profile)) throw new ArgumentException("Invalid profile");
            return Path.Combine(BrowserRoot, WebPrefix + profile);
        }
        // Whether preferences.json already holds this profile name: only then may other web profiles be cleaned at start-up.
        public bool WebProfileSaved(string profile)
        {
            try { var path = Path.Combine(root, "preferences.json"); return IsProfileName(profile) && File.Exists(path) && ReadShared(path).Contains(profile); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        public Task CleanOldWebProfilesAsync(string active) { return writes.RunAsync(() => Task.Run(() => CleanOldWebProfiles(active))); }
        // Only obsolete in-app browser profiles, by the same rules as the Univer ones.
        public void CleanOldWebProfiles(string active)
        {
            if (!Directory.Exists(BrowserRoot) || !ProfileCleanup.IsPlainPath(BrowserRoot)) return;
            var prefix = Path.GetFullPath(BrowserRoot) + Path.DirectorySeparatorChar;
            foreach (var path in Directory.GetDirectories(BrowserRoot))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(WebPrefix, StringComparison.Ordinal) || name == WebPrefix + active || !IsProfileName(name.Substring(WebPrefix.Length))) continue;
                if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ProfileCleanup.IsPlainTree(path)) continue;
                try { Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
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

using System;
using System.Collections.Generic;

namespace Univer.Desktop.Core
{
    internal enum MessageTone { Info, Working, Success, Warning, Error }
    internal enum MessageAction { None, Retry, Settings, Downloads, Notices }
    internal sealed class UiMessage
    {
        public string Key { get; private set; }
        public MessageTone Tone { get; private set; }
        public MessageAction Action { get; private set; }
        public bool CanDismiss { get; private set; }
        public int Priority { get; private set; }
        public UiMessage(string key, MessageTone tone, MessageAction action, int priority, bool dismiss = false)
        { Key = key; Tone = tone; Action = action; Priority = priority; CanDismiss = dismiss; }
    }
    internal sealed class UiStatus
    {
        private readonly Dictionary<string, UiMessage> slots = new Dictionary<string, UiMessage>();
        public string PageKey { get; private set; } = "loading";
        public UiMessage Current
        {
            get
            {
                UiMessage result = null;
                foreach (var value in slots.Values) if (result == null || value.Priority > result.Priority) result = value;
                return result;
            }
        }
        public void Update(string key)
        {
            if (key == "ready" || key == "loading" || key == "scriptWaiting" || key == "signingIn" || key == "confirming")
            {
                PageKey = key == "ready" ? "ready" : "loading";
                if (key == "loading") slots.Remove("page");
                UiMessage message;
                if (key == "ready" && slots.TryGetValue("automation", out message) && message.Tone == MessageTone.Working) slots.Remove("automation");
                return;
            }
            var spec = Describe(key);
            slots[Channel(key)] = spec;
        }
        // The grade summary's answers belong to the page that showed the sheet.
        public void BeginNavigation() { slots.Remove("page"); slots.Remove("presentation"); slots.Remove("report"); PageKey = "loading"; }
        public void Retry() { slots.Remove("automation"); slots.Remove("page"); slots.Remove("presentation"); }
        public void ClearStorageError() { slots.Remove("storage"); }
        public void FinishSettings() { slots.Remove("settings"); }
        public void ClearNotices() { slots.Remove("notices"); }
        public void Dismiss()
        {
            var current = Current;
            if (current != null && current.CanDismiss) slots.Remove(Channel(current.Key));
        }
        public void NewSession() { slots.Clear(); PageKey = "loading"; }
        // Takes back a message whose wait is over, and only that one: a later message in the same channel stays.
        public bool Withdraw(string key)
        {
            UiMessage held;
            var channel = Channel(key);
            if (!slots.TryGetValue(channel, out held) || held.Key != key) return false;
            slots.Remove(channel); return true;
        }
        // True while the message is held in any channel, even under a higher-priority one.
        public bool Shows(string key) { foreach (var value in slots.Values) if (value.Key == key) return true; return false; }
        public static UiMessage Describe(string key)
        {
            if (key == "storageError") return new UiMessage(key, MessageTone.Error, MessageAction.Settings, 100);
            if (key == "networkError" || key == "httpError" || key == "certificateError") return new UiMessage(key, MessageTone.Error, MessageAction.Retry, 80);
            // Opening an outside link is not a page failure: Retry (which reloads the site) would not help.
            if (key == "externalError" || key == "externalBlocked") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 72, true);
            // A teacher's link from UMKD went to the browser: the click gets an answer, since the app itself shows nothing new.
            if (key == "externalOpened") return new UiMessage(key, MessageTone.Success, MessageAction.None, 52, true);
            if (key == "automationFailed" || key == "accountMismatch" || key == "elementsMissing" || key == "credentialsUnreadable") return new UiMessage(key, MessageTone.Warning, MessageAction.Settings, 90);
            // Automatic sign-in goes on by itself: the user is told, nothing is asked.
            if (key == "sessionRestoring") return new UiMessage(key, MessageTone.Working, MessageAction.None, 56);
            if (key == "sessionReset") return new UiMessage(key, MessageTone.Info, MessageAction.None, 46, true);
            if (key == "unexpectedError") return new UiMessage(key, MessageTone.Error, MessageAction.Retry, 80, true);
            if (key == "presentationSlow") return new UiMessage(key, MessageTone.Warning, MessageAction.Retry, 75, true);
            if (key == "noticeFailed" || key == "agreementFailed" || key == "languageFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.Retry, 85);
            if (key == "downloadSigningIn") return new UiMessage(key, MessageTone.Working, MessageAction.None, 61, true);
            if (key == "downloading") return new UiMessage(key, MessageTone.Working, MessageAction.Downloads, 60, true);
            if (key == "downloadComplete") return new UiMessage(key, MessageTone.Success, MessageAction.Downloads, 65, true);
            // The answer to a click the user just made: above a slow-page notice, below errors.
            if (key == "downloadDuplicate") return new UiMessage(key, MessageTone.Info, MessageAction.Downloads, 76, true);
            if (key == "downloadLogin") return new UiMessage(key, MessageTone.Warning, MessageAction.Settings, 70, true);
            if (key == "downloadFailed" || key == "downloadOpenFailed" || key == "offOrigin" || key == "downloadTooLarge") return new UiMessage(key, MessageTone.Warning, MessageAction.Downloads, 70, true);
            if (key == "downloadBusy") return new UiMessage(key, MessageTone.Info, MessageAction.None, 74, true);
            if (key == "downloadUnsupported" || key == "downloadFromForm") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 70, true);
            if (key == "downloadCanceled") return new UiMessage(key, MessageTone.Info, MessageAction.Downloads, 62, true);
            // «Скачать все» in a UMKD section: its progress like a download's, one summary at the end.
            if (key == "downloadAllProgress" || key == "downloadAllStopping") return new UiMessage(key, MessageTone.Working, MessageAction.Downloads, 60, true);
            if (key == "downloadAllDone") return new UiMessage(key, MessageTone.Success, MessageAction.Downloads, 65, true);
            if (key == "downloadAllStopped") return new UiMessage(key, MessageTone.Info, MessageAction.Downloads, 62, true);
            if (key == "downloadAllBusy") return new UiMessage(key, MessageTone.Info, MessageAction.None, 74, true);
            // Deleting a downloaded file: the answer to the user's own click, a refusal says why and keeps the row.
            if (key == "fileDeleted") return new UiMessage(key, MessageTone.Success, MessageAction.None, 62, true);
            if (key == "deleteChanged" || key == "deleteNoBin" || key == "deleteNotFile" || key == "deleteFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 70, true);
            // A file that left its place was taken off the list when the student tried to open it.
            if (key == "downloadGone") return new UiMessage(key, MessageTone.Info, MessageAction.None, 62, true);
            // The grade summary's files: a copied picture is a quiet success, a file that could not be made a warning (a saved
            // one is told as downloadComplete, with Open and the downloads list).
            if (key == "reportCopied") return new UiMessage(key, MessageTone.Success, MessageAction.None, 52, true);
            if (key == "reportFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 70, true);
            if (key == "reportCopyFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 70, true);
            // The schedule export: a file without the neighbouring week is saved but told as a warning, with the downloads list.
            if (key == "schedulePart") return new UiMessage(key, MessageTone.Warning, MessageAction.Downloads, 70, true);
            if (key == "scheduleFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 70, true);
            if (key == "signOnOpening") return new UiMessage(key, MessageTone.Working, MessageAction.None, 58);
            if (key == "signOnOpened") return new UiMessage(key, MessageTone.Success, MessageAction.None, 52, true);
            if (key == "signOnLogin" || key == "signOnFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 68, true);
            if (key == "trayHint") return new UiMessage(key, MessageTone.Info, MessageAction.None, 20, true);
            if (key == "pinFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 48, true);
            // New notifications from the site: below every problem and running download, above quiet hints.
            if (key == "noticesNew") return new UiMessage(key, MessageTone.Info, MessageAction.Notices, 47, true);
            if (key == "manualSignIn") return new UiMessage(key, MessageTone.Info, MessageAction.Settings, 30, true);
            if (key == "saving") return new UiMessage(key, MessageTone.Working, MessageAction.None, 95);
            if (key == "settingsSaved") return new UiMessage(key, MessageTone.Success, MessageAction.None, 50, true);
            if (key == "lockOn" || key == "lockChanged" || key == "lockOff") return new UiMessage(key, MessageTone.Success, MessageAction.None, 50, true);
            if (key == "lockFailed" || key == "autostartFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 70, true);
            // The account ended after ten wrong PINs (or an unreadable PIN): sign in again from Settings.
            if (key == "lockWiped" || key == "lockUnavailable") return new UiMessage(key, MessageTone.Warning, MessageAction.Settings, 88, true);
            var working = key == "signingIn" || key == "confirming";
            return new UiMessage(key, working ? MessageTone.Working : MessageTone.Info, MessageAction.None, working ? 55 : 45, !working);
        }
        private static string Channel(string key)
        {
            if (key == "storageError") return "storage";
            // An unexpected UI error is recovered like a failed page: Retry reloads it and a new page clears it.
            if (key == "networkError" || key == "httpError" || key == "certificateError" || key == "unexpectedError") return "page";
            if (key == "presentationSlow") return "presentation";
            // A refused page-made file keeps a running or finished download's own message underneath.
            if (key == "downloadUnsupported" || key == "downloadFromForm") return "rejectedFile";
            // Its own channel: a refused repeat must not displace the progress of the download already running.
            if (key == "downloadBusy" || key == "downloadAllBusy") return "downloadBusy";
            if (key.StartsWith("download", StringComparison.Ordinal) || key == "offOrigin") return "download";
            if (key.StartsWith("external", StringComparison.Ordinal)) return "external";
            if (key.StartsWith("signOn", StringComparison.Ordinal)) return "signon";
            // The grade summary's answers stay apart from page automation, which would replace them on its next tick.
            if (key.StartsWith("report", StringComparison.Ordinal)) return "report";
            if (key == "trayHint") return "tray";
            if (key == "pinFailed") return "pins";
            if (key == "noticesNew") return "notices";
            if (key == "manualSignIn" || key == "credentialsUnreadable" || key == "sessionReset") return "account";
            if (key == "saving" || key == "settingsSaved" || key == "lockOn" || key == "lockChanged" || key == "lockOff" || key == "lockFailed" || key == "autostartFailed") return "settings";
            if (key == "lockWiped" || key == "lockUnavailable") return "account";
            return "automation";
        }
    }
}

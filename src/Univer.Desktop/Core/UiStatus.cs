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
        public void BeginNavigation() { slots.Remove("page"); slots.Remove("presentation"); PageKey = "loading"; }
        public void Retry() { slots.Remove("automation"); slots.Remove("page"); slots.Remove("presentation"); }
        public void ClearStorageError() { slots.Remove("storage"); }
        public void ClearAccountMessage() { slots.Remove("account"); }
        public void FinishSettings() { slots.Remove("settings"); }
        public void ClearNotices() { slots.Remove("notices"); }
        public void Dismiss()
        {
            var current = Current;
            if (current != null && current.CanDismiss) slots.Remove(Channel(current.Key));
        }
        public void NewSession() { slots.Clear(); PageKey = "loading"; }
        // True while the message is held in any channel, even under a higher-priority one.
        public bool Shows(string key) { foreach (var value in slots.Values) if (value.Key == key) return true; return false; }
        public static UiMessage Describe(string key)
        {
            if (key == "storageError") return new UiMessage(key, MessageTone.Error, MessageAction.Settings, 100);
            if (key == "networkError" || key == "httpError" || key == "certificateError") return new UiMessage(key, MessageTone.Error, MessageAction.Retry, 80);
            // Opening an outside link is not a page failure: Retry (which reloads the site) would not help.
            if (key == "externalError") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 72, true);
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
            if (key == "signOnOpening") return new UiMessage(key, MessageTone.Working, MessageAction.None, 58);
            if (key == "signOnOpened") return new UiMessage(key, MessageTone.Success, MessageAction.None, 52, true);
            if (key == "signOnLogin" || key == "signOnFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 68, true);
            if (key == "trayHint") return new UiMessage(key, MessageTone.Info, MessageAction.None, 20, true);
            if (key == "pinFailed") return new UiMessage(key, MessageTone.Warning, MessageAction.None, 48, true);
            // New notifications from the site: below every problem and running download, above quiet hints.
            if (key == "noticesNew") return new UiMessage(key, MessageTone.Info, MessageAction.Notices, 47, true);
            if (key == "paused") return new UiMessage(key, MessageTone.Info, MessageAction.Settings, 40);
            if (key == "manualSignIn") return new UiMessage(key, MessageTone.Info, MessageAction.Settings, 30, true);
            if (key == "saving") return new UiMessage(key, MessageTone.Working, MessageAction.None, 95);
            if (key == "settingsSaved") return new UiMessage(key, MessageTone.Success, MessageAction.None, 50, true);
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
            if (key == "downloadBusy") return "downloadBusy";
            if (key.StartsWith("download", StringComparison.Ordinal) || key == "offOrigin") return "download";
            if (key == "externalError") return "external";
            if (key.StartsWith("signOn", StringComparison.Ordinal)) return "signon";
            if (key == "trayHint") return "tray";
            if (key == "pinFailed") return "pins";
            if (key == "noticesNew") return "notices";
            if (key == "paused" || key == "manualSignIn" || key == "credentialsUnreadable" || key == "sessionReset") return "account";
            if (key == "saving" || key == "settingsSaved") return "settings";
            return "automation";
        }
    }
}

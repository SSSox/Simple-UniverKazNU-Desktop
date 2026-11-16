using System;
using System.Security.Cryptography;

namespace Univer.Desktop.Core
{
    // The page-to-app channel. The DevTools binding is taken off window at document start, before any page script runs,
    // and only the app's own scripts (inside that closure) send through it, each message after this run's random key.
    // A page that calls a binding it found elsewhere (a frame, a fallback load of the bundle) sends no key and is ignored.
    internal static class NativeChannel
    {
        public const string Binding = "__univerNativeNotify";
        public static string NewKey()
        {
            var bytes = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return "k" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant() + ":";
        }
        // The expression handed to the app's closure as __univerNative: a sender that adds the key, or null. It is strict
        // (a function the page swapped in cannot reach it through .caller) and calls no global the page can replace;
        // the app's modules pass it strings only.
        public static string Script(string key)
        {
            return "(function(raw,key){'use strict';try{delete window." + Binding + "}catch(_){}return typeof raw==='function'?function(m){if(typeof m==='string')raw(key+m)}:null})(window." + Binding + ",'" + key + "')";
        }
        // The message without the key, or null when it lacks this run's key.
        public static string Unwrap(string key, string message)
        { return message != null && !string.IsNullOrEmpty(key) && message.StartsWith(key, StringComparison.Ordinal) ? message.Substring(key.Length) : null; }
    }
}

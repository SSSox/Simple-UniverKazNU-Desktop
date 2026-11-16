using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Univer.Desktop.Core
{
    // The app PIN, as in the Android app: six digits, the easy ones refused, waits after wrong guesses and the tenth wrong
    // guess ending the saved account. The PIN closes the app's window and opens the saved login and password: they are
    // sealed with a random data key, and the data key with a key derived from the PIN, all inside the DPAPI record. A
    // program running as this Windows user can still read the record and try all million PINs offline (there is no
    // hardware key as on the phone), and the browser profile keeps the site's session without the PIN.
    internal enum PinWeakness { None, Malformed, Repeated, Sequence, Common }
    // Read on Chromium's threads: while the lock shows, the pages' keyboard handlers swallow every key.
    internal static class LockGate { public static volatile bool Engaged; }
    // Which runs go without the PIN: only a throwaway profile under %TEMP%\Univer.Desktop.QA\ (self-test and the isolated
    // --qa-* checks) and the QA build's account review; the self-test's own lock step turns the lock back on. A command-line
    // flag alone never opens the real profile without the PIN.
    internal static class LockScope
    {
        public static bool Allowed(bool lockQa, bool accountReview, string profileRoot, string tempRoot)
        {
            return lockQa || (!accountReview && !Isolated(profileRoot, tempRoot));
        }
        public static bool Isolated(string profileRoot, string tempRoot)
        {
            if (string.IsNullOrEmpty(profileRoot) || string.IsNullOrEmpty(tempRoot)) return false;
            try
            {
                var qa = Path.Combine(Path.GetFullPath(tempRoot), "Univer.Desktop.QA") + Path.DirectorySeparatorChar;
                return Path.GetFullPath(profileRoot).StartsWith(qa, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
    }
    internal static class PinPolicy
    {
        public const int Length = 6;
        private static readonly string[] Common = { "112233", "112211", "123321", "159357", "147258", "258369", "100000", "101010", "202020", "696969", "121212", "123123" };
        public static bool WellFormed(string pin)
        {
            if (pin == null || pin.Length != Length) return false;
            foreach (var c in pin) if (c < '0' || c > '9') return false;
            return true;
        }
        public static PinWeakness Weakness(string pin)
        {
            if (!WellFormed(pin)) return PinWeakness.Malformed;
            var same = true; var up = true; var down = true;
            for (var i = 1; i < pin.Length; i++)
            {
                same &= pin[i] == pin[0]; up &= pin[i] - pin[i - 1] == 1; down &= pin[i] - pin[i - 1] == -1;
            }
            if (same) return PinWeakness.Repeated;
            if (up || down) return PinWeakness.Sequence;
            if (Array.IndexOf(Common, pin) >= 0 || pin.Substring(0, 2) + pin.Substring(0, 2) + pin.Substring(0, 2) == pin || pin.Substring(0, 3) + pin.Substring(0, 3) == pin) return PinWeakness.Common;
            return PinWeakness.None;
        }
    }
    // The first four wrong PINs cost nothing; then each waits longer, and the tenth ends the saved account.
    internal static class PinLockout
    {
        public const int WipeAt = 10;
        public static long DelayAfter(int failures)
        {
            switch (failures)
            {
                case 5: return 30000;
                case 6: return 60000;
                case 7: return 5 * 60000;
                case 8: return 15 * 60000;
                case 9: return 30 * 60000;
                default: return 0;
            }
        }
        public static int AttemptsLeft(int failures) { return Math.Max(0, WipeAt - failures); }
        public static bool ShouldWipe(int failures) { return failures >= WipeAt; }
        // What is left of a wait. A clock moved back gives the whole wait again instead of ending it.
        public static long Remaining(long durationMs, long startTicks, long nowTicks)
        {
            if (durationMs <= 0) return 0;
            if (nowTicks < startTicks) return durationMs;
            var passed = (nowTicks - startTicks) / TimeSpan.TicksPerMillisecond;
            return Math.Max(0, Math.Min(durationMs, durationMs - passed));
        }
    }
    internal static class PinHash
    {
        // As on Android: PBKDF2-HMAC-SHA256, 210 000 rounds, a 16-byte salt and a 32-byte result.
        public const int Iterations = 210000, SaltSize = 16, KeySize = 32;
        public static byte[] Derive(string pin, byte[] salt, int iterations)
        {
            var password = Encoding.ASCII.GetBytes(pin ?? "");
            try { return Pbkdf2Sha256(password, salt, iterations, KeySize); }
            finally { Array.Clear(password, 0, password.Length); }
        }
        // RFC 8018 PBKDF2 over HMAC-SHA256: .NET Framework 4.5's Rfc2898DeriveBytes only offers SHA-1.
        public static byte[] Pbkdf2Sha256(byte[] password, byte[] salt, int iterations, int length)
        {
            if (iterations < 1 || length < 1) throw new ArgumentOutOfRangeException(iterations < 1 ? "iterations" : "length");
            var result = new byte[length];
            var input = new byte[salt.Length + 4];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            using (var hmac = new HMACSHA256(password))
            {
                for (int block = 1, offset = 0; offset < length; block++)
                {
                    input[salt.Length] = (byte)(block >> 24); input[salt.Length + 1] = (byte)(block >> 16);
                    input[salt.Length + 2] = (byte)(block >> 8); input[salt.Length + 3] = (byte)block;
                    var u = hmac.ComputeHash(input); var t = (byte[])u.Clone();
                    for (var i = 1; i < iterations; i++)
                    {
                        u = hmac.ComputeHash(u);
                        for (var j = 0; j < t.Length; j++) t[j] ^= u[j];
                    }
                    var take = Math.Min(t.Length, length - offset);
                    Buffer.BlockCopy(t, 0, result, offset, take); offset += take;
                    Array.Clear(t, 0, t.Length); Array.Clear(u, 0, u.Length);
                }
            }
            return result;
        }
        // Every byte is compared, so the time taken says nothing about how much of a guess was right.
        public static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            var difference = 0;
            for (var i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
    }
    // AES-256-CBC, then HMAC-SHA256 over the purpose, the IV and the ciphertext (.NET Framework 4.5 has no AES-GCM). The
    // encryption and MAC keys are derived apart from the given key, and the MAC is checked before anything is decrypted.
    internal static class PinSeal
    {
        public const int KeySize = 32;
        private const int IvSize = 16, MacSize = 32;
        public static byte[] RandomKey()
        {
            var key = new byte[KeySize];
            using (var random = new RNGCryptoServiceProvider()) random.GetBytes(key);
            return key;
        }
        public static byte[] Seal(byte[] key, byte[] plaintext, string purpose)
        {
            var enc = SubKey(key, "enc"); var mac = SubKey(key, "mac");
            try
            {
                using (var aes = new AesCryptoServiceProvider { KeySize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7, Key = enc })
                {
                    aes.GenerateIV();
                    byte[] cipher;
                    using (var encryptor = aes.CreateEncryptor()) cipher = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
                    var result = new byte[IvSize + cipher.Length + MacSize];
                    Buffer.BlockCopy(aes.IV, 0, result, 0, IvSize); Buffer.BlockCopy(cipher, 0, result, IvSize, cipher.Length);
                    var tag = Tag(mac, purpose, result, IvSize + cipher.Length);
                    Buffer.BlockCopy(tag, 0, result, IvSize + cipher.Length, MacSize);
                    return result;
                }
            }
            finally { Array.Clear(enc, 0, enc.Length); Array.Clear(mac, 0, mac.Length); }
        }
        // null when the key, the purpose or the data is not the one sealed.
        public static byte[] Open(byte[] key, byte[] sealedData, string purpose)
        {
            if (key == null || sealedData == null || sealedData.Length < IvSize + 16 + MacSize || (sealedData.Length - IvSize - MacSize) % 16 != 0) return null;
            var enc = SubKey(key, "enc"); var mac = SubKey(key, "mac");
            try
            {
                var body = sealedData.Length - MacSize;
                var tag = new byte[MacSize]; Buffer.BlockCopy(sealedData, body, tag, 0, MacSize);
                if (!PinHash.Same(Tag(mac, purpose, sealedData, body), tag)) return null;
                var iv = new byte[IvSize]; Buffer.BlockCopy(sealedData, 0, iv, 0, IvSize);
                using (var aes = new AesCryptoServiceProvider { KeySize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7, Key = enc, IV = iv })
                using (var decryptor = aes.CreateDecryptor())
                    return decryptor.TransformFinalBlock(sealedData, IvSize, body - IvSize);
            }
            finally { Array.Clear(enc, 0, enc.Length); Array.Clear(mac, 0, mac.Length); }
        }
        private static byte[] SubKey(byte[] key, string label)
        {
            using (var hmac = new HMACSHA256(key)) return hmac.ComputeHash(Encoding.ASCII.GetBytes("univer.seal." + label));
        }
        private static byte[] Tag(byte[] mac, string purpose, byte[] data, int count)
        {
            var label = Encoding.ASCII.GetBytes(purpose + "\0");
            var input = new byte[label.Length + count];
            Buffer.BlockCopy(label, 0, input, 0, label.Length); Buffer.BlockCopy(data, 0, input, label.Length, count);
            using (var hmac = new HMACSHA256(mac)) return hmac.ComputeHash(input);
        }
    }
    // The stored PIN check, the guess counter and the sealed account, kept together in one DPAPI record so none can be
    // replaced alone. A record with Key opens a data key with the PIN; an older one holds only the PIN's Hash.
    internal sealed class LockRecord
    {
        public string Salt { get; set; }
        public string Hash { get; set; }
        // The data key sealed with the key derived from the PIN; a wrong PIN fails its MAC.
        public string Key { get; set; }
        // The saved login and password sealed with the data key, or null when no account is saved.
        public string Account { get; set; }
        public int Iterations { get; set; }
        public int Failures { get; set; }
        public long WaitStart { get; set; }
        public long WaitMs { get; set; }
    }
    internal interface ILockStore
    {
        // null when no PIN is set; throws when a record exists but cannot be read.
        LockRecord Load();
        void Save(LockRecord record);
        void Remove();
    }
    internal enum PinOutcome { Success, Wrong, Wait, Wiped, Failed, Unavailable }
    internal sealed class PinResult
    {
        public PinResult(PinOutcome outcome, int attemptsLeft = 0, long waitMs = 0) { Outcome = outcome; AttemptsLeft = attemptsLeft; WaitMs = waitMs; }
        public PinOutcome Outcome { get; private set; }
        public int AttemptsLeft { get; private set; }
        public long WaitMs { get; private set; }
        // After a right PIN: the sealed account (null when none is saved), and whether the record is the older kind that
        // holds only the PIN's hash, so the account is still in the plain DPAPI file until it is sealed.
        public Credentials Account { get; set; }
        public bool Legacy { get; set; }
    }
    // Guesses run one at a time, off the UI thread. The counter is written before the PIN is checked, so closing the app
    // cannot take a guess back; a counter that cannot be written means no check at all.
    internal sealed class PinGuard
    {
        private readonly ILockStore store;
        private readonly Func<DateTime> clock;
        private readonly int iterations;
        private readonly object gate = new object();
        public PinGuard(ILockStore store, Func<DateTime> clock = null, int iterations = PinHash.Iterations)
        { this.store = store; this.clock = clock ?? (() => DateTime.UtcNow); this.iterations = iterations; }
        // The wait still to run, or 0; an unreadable record has none.
        public long RemainingWait()
        {
            lock (gate)
            {
                try { var record = store.Load(); return record == null ? 0 : PinLockout.Remaining(record.WaitMs, record.WaitStart, clock().Ticks); }
                catch (Exception) { return 0; }
            }
        }
        // The tenth wrong guess was counted but the account was not ended yet (the app closed first).
        public bool Exhausted()
        {
            lock (gate)
            {
                try { var record = store.Load(); return record != null && PinLockout.ShouldWipe(record.Failures); }
                catch (Exception) { return false; }
            }
        }
        public PinResult Guess(string pin)
        {
            lock (gate)
            {
                LockRecord record;
                try { record = store.Load(); }
                catch (Exception) { return new PinResult(PinOutcome.Unavailable); }
                if (record == null) return new PinResult(PinOutcome.Unavailable);
                byte[] salt, hash = null, wrapped = null, account = null;
                try
                {
                    salt = Convert.FromBase64String(record.Salt ?? "");
                    if (record.Key != null) { wrapped = Convert.FromBase64String(record.Key); if (record.Account != null) account = Convert.FromBase64String(record.Account); }
                    else hash = Convert.FromBase64String(record.Hash ?? "");
                }
                catch (FormatException) { return new PinResult(PinOutcome.Unavailable); }
                if (salt.Length == 0 || record.Iterations < 1 || (wrapped == null && hash.Length != PinHash.KeySize)) return new PinResult(PinOutcome.Unavailable);
                if (PinLockout.ShouldWipe(record.Failures)) return new PinResult(PinOutcome.Wiped);
                var wait = PinLockout.Remaining(record.WaitMs, record.WaitStart, clock().Ticks);
                if (wait > 0) return new PinResult(PinOutcome.Wait, PinLockout.AttemptsLeft(record.Failures), wait);
                record.Failures++;
                try { store.Save(record); }
                catch (Exception) { return new PinResult(PinOutcome.Failed, PinLockout.AttemptsLeft(record.Failures - 1)); }
                var key = PinPolicy.WellFormed(pin) ? PinHash.Derive(pin, salt, record.Iterations) : new byte[0];
                // The newer record has no hash to compare: the right PIN is the one whose key opens the data key.
                byte[] opened = null;
                bool right;
                if (wrapped != null) { opened = key.Length == 0 ? null : PinSeal.Open(key, wrapped, PurposeKey); right = opened != null && opened.Length == PinSeal.KeySize; }
                else right = PinHash.Same(key, hash);
                Array.Clear(key, 0, key.Length);
                if (right)
                {
                    Credentials saved = null;
                    if (account != null && (saved = OpenAccount(opened, account)) == null)
                    {
                        // The PIN opened the data key, but the account under it is damaged: nothing usable is left.
                        Array.Clear(opened, 0, opened.Length);
                        return new PinResult(PinOutcome.Unavailable);
                    }
                    record.Failures = 0; record.WaitStart = 0; record.WaitMs = 0;
                    // The guess was counted before the check; a counter left raised would end the account on a later
                    // tenth count, so the reset is tried again when another program briefly holds the file.
                    for (var attempt = 0; attempt < 4; attempt++)
                    {
                        try { store.Save(record); break; }
                        catch (Exception) { if (attempt < 3) System.Threading.Thread.Sleep(100 * (attempt + 1)); }
                    }
                    Remember(opened);
                    return new PinResult(PinOutcome.Success) { Account = saved, Legacy = wrapped == null };
                }
                if (PinLockout.ShouldWipe(record.Failures)) return new PinResult(PinOutcome.Wiped);
                var delay = PinLockout.DelayAfter(record.Failures);
                if (delay > 0)
                {
                    record.WaitStart = clock().Ticks; record.WaitMs = delay;
                    try { store.Save(record); } catch (Exception) { }
                }
                return new PinResult(PinOutcome.Wrong, PinLockout.AttemptsLeft(record.Failures), delay);
            }
        }
        // A new PIN with fresh counters, a new data key and the given account sealed under it (null: none saved). Also how
        // the PIN is changed: the account comes from memory, where an unlocked app holds it. Refuses a malformed or easy
        // PIN, and an account whose password is not known (still sealed), which would be lost.
        public void Enable(string pin) { Enable(pin, null); }
        public void Enable(string pin, Credentials account)
        {
            if (PinPolicy.Weakness(pin) != PinWeakness.None) throw new ArgumentException("Weak PIN");
            if (account != null && !account.Usable()) throw new ArgumentException("Account still sealed");
            var salt = new byte[PinHash.SaltSize];
            using (var random = new RNGCryptoServiceProvider()) random.GetBytes(salt);
            var kek = PinHash.Derive(pin, salt, iterations); var data = PinSeal.RandomKey();
            try
            {
                var record = new LockRecord { Salt = Convert.ToBase64String(salt), Key = Convert.ToBase64String(PinSeal.Seal(kek, data, PurposeKey)), Iterations = iterations,
                    Account = account == null ? null : Convert.ToBase64String(SealAccount(data, account)) };
                lock (gate) { store.Save(record); Remember((byte[])data.Clone()); }
            }
            finally { Array.Clear(kek, 0, kek.Length); Array.Clear(data, 0, data.Length); }
        }
        // Replaces the sealed account (null removes it), keeping the PIN and its counter. False when there is no newer
        // record or it was not opened in this run, so the caller keeps the account in the plain DPAPI file instead.
        public bool StoreAccount(Credentials account)
        {
            if (account != null && !account.Usable()) return false;
            lock (gate)
            {
                if (dataKey == null) return false;
                var record = store.Load();
                if (record == null || record.Key == null) return false;
                record.Account = account == null ? null : Convert.ToBase64String(SealAccount(dataKey, account));
                store.Save(record);
                return true;
            }
        }
        // Turning the PIN off takes the current one, counted like any other guess. The account goes back to the plain DPAPI
        // file first (restore writes it); when that fails, the PIN and the sealed account stay.
        public PinResult Disable(string pin) { return Disable(pin, null); }
        public PinResult Disable(string pin, Action<Credentials> restore)
        {
            lock (gate)
            {
                var result = Guess(pin);
                if (result.Outcome != PinOutcome.Success) return result;
                try { restore?.Invoke(result.Account); }
                catch (Exception) { return new PinResult(PinOutcome.Failed, PinLockout.AttemptsLeft(0)); }
                store.Remove(); Remember(null);
                return result;
            }
        }
        // After the account was ended (ten wrong guesses or a forgotten PIN), or the saved account was removed.
        public void Forget() { lock (gate) { store.Remove(); Remember(null); } }
        // Whether this run opened the data key (a right PIN on a newer record, or a PIN set or changed now).
        public bool Opened { get { lock (gate) return dataKey != null; } }

        private const string PurposeKey = "univer.lock.key.v2", PurposeAccount = "univer.lock.account.v2";
        private byte[] dataKey;
        private void Remember(byte[] key) { if (dataKey != null) Array.Clear(dataKey, 0, dataKey.Length); dataKey = key; }
        private static byte[] SealAccount(byte[] key, Credentials account)
        {
            var plain = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(account.Login ?? "")) + "\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(account.Password ?? "")));
            try { return PinSeal.Seal(key, plain, PurposeAccount); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
        private static Credentials OpenAccount(byte[] key, byte[] sealedAccount)
        {
            var plain = PinSeal.Open(key, sealedAccount, PurposeAccount);
            if (plain == null) return null;
            try
            {
                var parts = Encoding.UTF8.GetString(plain).Split('\n');
                if (parts.Length != 2) return null;
                return new Credentials { Login = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])), Password = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])) };
            }
            catch (FormatException) { return null; }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
    }
}

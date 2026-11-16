using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // The app PIN: which codes are refused, the waits and the tenth-guess end, the KDF and the stored record.
    public class AppLockTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        private DateTime now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        private const string Pin = "135792", Other = "246801";

        private sealed class MemoryStore : ILockStore
        {
            public LockRecord Record; public bool FailSaves, FailLoads; public int Saves;
            // The next saves that would clear a raised counter fail, as when another program holds the file for a moment.
            public int RejectResets;
            public LockRecord Load()
            {
                if (FailLoads) throw new CryptographicException("unreadable");
                return Record == null ? null : Copy(Record);
            }
            public void Save(LockRecord record)
            {
                if (FailSaves) throw new IOException("disk full");
                if (RejectResets > 0 && record.Failures == 0 && Record != null && Record.Failures > 0) { RejectResets--; throw new IOException("in use"); }
                Saves++;
                Record = Copy(record);
            }
            public void Remove() { Record = null; }
            private static LockRecord Copy(LockRecord r) { return new LockRecord { Salt = r.Salt, Hash = r.Hash, Key = r.Key, Account = r.Account, Iterations = r.Iterations, Failures = r.Failures, WaitStart = r.WaitStart, WaitMs = r.WaitMs }; }
        }
        private PinGuard Guard(MemoryStore store) { return new PinGuard(store, () => now, 5); }

        [Fact]
        public void OnlyAThrowawayQaProfileOrTheReviewGoesWithoutThePin()
        {
            const string temp = @"C:\Users\A\AppData\Local\Temp\";
            const string real = @"C:\Users\A\AppData\Local\Univer.Desktop";
            Assert.False(LockScope.Allowed(false, false, temp + @"Univer.Desktop.QA\0f3c\", temp));
            Assert.True(LockScope.Allowed(true, false, temp + @"Univer.Desktop.QA\0f3c", temp));
            Assert.False(LockScope.Allowed(false, true, real, temp));
            // The real profile, look-alike folders and a path that climbs back out of the QA folder all keep the lock.
            foreach (var root in new[] { real, temp + "Univer.Desktop.QA", temp + "Univer.Desktop.QAX\\p", temp + @"Univer.Desktop.QA\..\..\Univer.Desktop", null, "" })
                Assert.True(LockScope.Allowed(false, false, root, temp), root);
            Assert.True(LockScope.Allowed(false, false, temp + @"Univer.Desktop.QA\p", null));
        }

        [Fact]
        public void TheRightPinClearsTheCounterEvenWhenTheFileIsBrieflyHeld()
        {
            var store = new MemoryStore(); var guard = Guard(store);
            guard.Enable(Pin);
            store.Record.Failures = 9; store.RejectResets = 2;
            Assert.Equal(PinOutcome.Success, guard.Guess(Pin).Outcome);
            Assert.Equal(0, store.Record.Failures);
            Assert.False(guard.Exhausted());
        }

        [Theory]
        [InlineData("135792", PinWeakness.None)]
        [InlineData("908172", PinWeakness.None)]
        [InlineData("111111", PinWeakness.Repeated)]
        [InlineData("123456", PinWeakness.Sequence)]
        [InlineData("987654", PinWeakness.Sequence)]
        [InlineData("121212", PinWeakness.Common)]
        [InlineData("454545", PinWeakness.Common)]
        [InlineData("381381", PinWeakness.Common)]
        [InlineData("159357", PinWeakness.Common)]
        [InlineData("12345", PinWeakness.Malformed)]
        [InlineData("1234567", PinWeakness.Malformed)]
        [InlineData("12a456", PinWeakness.Malformed)]
        [InlineData("١٢٣٤٥٦", PinWeakness.Malformed)]
        [InlineData(null, PinWeakness.Malformed)]
        internal void EasyAndMalformedPinsAreRefused(string pin, PinWeakness expected)
        {
            Assert.Equal(expected, PinPolicy.Weakness(pin));
        }

        [Fact]
        public void WaitsGrowAndTheTenthWrongGuessEndsTheAccount()
        {
            Assert.Equal(new long[] { 0, 0, 0, 0, 30000, 60000, 300000, 900000, 1800000, 0 }, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }.Select(PinLockout.DelayAfter));
            Assert.Equal(1, PinLockout.AttemptsLeft(9)); Assert.False(PinLockout.ShouldWipe(9)); Assert.True(PinLockout.ShouldWipe(10));
            var start = now.Ticks;
            Assert.Equal(30000, PinLockout.Remaining(30000, start, start));
            Assert.Equal(20000, PinLockout.Remaining(30000, start, start + TimeSpan.FromSeconds(10).Ticks));
            Assert.Equal(0, PinLockout.Remaining(30000, start, start + TimeSpan.FromMinutes(5).Ticks));
            Assert.Equal(30000, PinLockout.Remaining(30000, start, start - TimeSpan.FromHours(1).Ticks));
        }

        [Fact]
        public void KdfMatchesTheFrameworksPbkdf2Sha256()
        {
            var salt = Encoding.ASCII.GetBytes("NaCl salt 16 by");
            foreach (var iterations in new[] { 1, 2, 1000 })
                foreach (var length in new[] { 16, 32, 40, 64 })
                {
                    var password = Encoding.ASCII.GetBytes("135792");
                    using (var framework = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                        Assert.Equal(framework.GetBytes(length), PinHash.Pbkdf2Sha256(password, salt, iterations, length));
                }
            Assert.True(PinHash.Same(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }));
            Assert.False(PinHash.Same(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 }));
            Assert.False(PinHash.Same(new byte[] { 1, 2, 3 }, new byte[] { 1, 2 }));
        }

        [Fact]
        public void RightPinOpensAndClearsTheCounter()
        {
            var store = new MemoryStore(); var guard = Guard(store);
            guard.Enable(Pin);
            Assert.Equal(PinOutcome.Wrong, guard.Guess(Other).Outcome);
            Assert.Equal(1, store.Record.Failures);
            Assert.Equal(PinOutcome.Success, guard.Guess(Pin).Outcome);
            Assert.Equal(0, store.Record.Failures);
            Assert.Throws<ArgumentException>(() => guard.Enable("111111"));
        }

        [Fact]
        public void TheCounterIsWrittenBeforeThePinIsChecked()
        {
            var store = new MemoryStore(); var guard = Guard(store);
            guard.Enable(Pin);
            store.FailSaves = true;
            // The right PIN is not accepted when its guess could not be counted.
            var result = guard.Guess(Pin);
            Assert.Equal(PinOutcome.Failed, result.Outcome);
            store.FailSaves = false;
            Assert.Equal(0, store.Record.Failures);
            Assert.Equal(PinOutcome.Success, guard.Guess(Pin).Outcome);
        }

        [Fact]
        public void FifthWrongGuessWaitsAndTheWaitBlocksEvenTheRightPin()
        {
            var store = new MemoryStore(); var guard = Guard(store);
            guard.Enable(Pin);
            for (var i = 1; i <= 4; i++) { var wrong = guard.Guess(Other); Assert.Equal(PinOutcome.Wrong, wrong.Outcome); Assert.Equal(0, wrong.WaitMs); Assert.Equal(10 - i, wrong.AttemptsLeft); }
            var fifth = guard.Guess(Other);
            Assert.Equal(PinOutcome.Wrong, fifth.Outcome); Assert.Equal(30000, fifth.WaitMs); Assert.Equal(5, fifth.AttemptsLeft);
            Assert.Equal(PinOutcome.Wait, guard.Guess(Pin).Outcome);
            Assert.Equal(5, store.Record.Failures);
            now = now.AddSeconds(20); Assert.Equal(10000, guard.RemainingWait());
            now = now.AddHours(-2); Assert.Equal(30000, guard.RemainingWait());
            now = now.AddHours(2).AddSeconds(11);
            Assert.Equal(PinOutcome.Success, guard.Guess(Pin).Outcome);
        }

        [Fact]
        public void TenthWrongGuessEndsTheAccountAndAStartAfterItFinishesTheJob()
        {
            var store = new MemoryStore(); var guard = Guard(store);
            guard.Enable(Pin);
            store.Record.Failures = 9;
            Assert.Equal(PinOutcome.Wiped, guard.Guess(Other).Outcome);
            Assert.True(guard.Exhausted());
            // Even the right PIN cannot undo a counted tenth guess.
            Assert.Equal(PinOutcome.Wiped, guard.Guess(Pin).Outcome);
            guard.Forget(); Assert.Null(store.Record); Assert.False(guard.Exhausted());
        }

        [Fact]
        public void TurningOffTakesTheCurrentPinAndCountsWrongOnes()
        {
            var store = new MemoryStore(); var guard = Guard(store);
            guard.Enable(Pin);
            Assert.Equal(PinOutcome.Wrong, guard.Disable(Other).Outcome); Assert.NotNull(store.Record); Assert.Equal(1, store.Record.Failures);
            Assert.Equal(PinOutcome.Success, guard.Disable(Pin).Outcome); Assert.Null(store.Record);
        }

        [Fact]
        public void AnUnreadableRecordIsNeverAWrongGuess()
        {
            var store = new MemoryStore(); var guard = Guard(store);
            guard.Enable(Pin); store.FailLoads = true;
            Assert.Equal(PinOutcome.Unavailable, guard.Guess(Pin).Outcome);
            store.FailLoads = false; Assert.Equal(0, store.Record.Failures);
            store.Record.Key = "not base64!"; Assert.Equal(PinOutcome.Unavailable, guard.Guess(Pin).Outcome);
        }

        [Fact]
        public void StoredRecordIsProtectedAndReadBack()
        {
            var storage = new Storage(folder);
            Assert.False(storage.LockExists); Assert.Null(storage.LoadLock());
            var guard = new PinGuard(storage.LockStore, () => now, 5);
            guard.Enable(Pin);
            Assert.True(storage.LockExists);
            var raw = File.ReadAllBytes(Path.Combine(folder, "lock.dpapi"));
            Assert.DoesNotContain("Iterations", Encoding.UTF8.GetString(raw));
            Assert.Equal(PinOutcome.Wrong, guard.Guess(Other).Outcome);
            Assert.Equal(1, storage.LoadLock().Failures);
            Assert.Equal(PinOutcome.Success, guard.Guess(Pin).Outcome);
            File.WriteAllBytes(Path.Combine(folder, "lock.dpapi"), new byte[] { 1, 2, 3 });
            Assert.Equal(PinOutcome.Unavailable, guard.Guess(Pin).Outcome);
            storage.RemoveLock(); Assert.False(storage.LockExists);
        }

        [Fact]
        public void TheRealRoundCountStaysUsable()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            PinHash.Derive(Pin, new byte[PinHash.SaltSize], PinHash.Iterations);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), "PBKDF2 took " + watch.Elapsed);
        }

        private static readonly Credentials Account = new Credentials { Login = "QA student", Password = "synthetic pass é" };
        private static void Same(Credentials expected, Credentials actual) { Assert.NotNull(actual); Assert.Equal(expected.Login, actual.Login); Assert.Equal(expected.Password, actual.Password); }

        [Fact]
        public void ThePinSealsTheAccountAndOnlyTheRightPinOpensIt()
        {
            var store = new MemoryStore();
            Guard(store).Enable(Pin, Account);
            Assert.Null(store.Record.Hash); Assert.NotNull(store.Record.Key); Assert.NotNull(store.Record.Account);
            // A new run (a fresh guard) knows nothing until the PIN is entered; a wrong one is counted and opens nothing.
            var guard = Guard(store);
            Assert.False(guard.StoreAccount(Account));
            var wrong = guard.Guess(Other);
            Assert.Equal(PinOutcome.Wrong, wrong.Outcome); Assert.Null(wrong.Account); Assert.False(guard.Opened);
            Assert.Equal(1, store.Record.Failures); Assert.NotNull(store.Record.Account);
            var right = guard.Guess(Pin);
            Assert.Equal(PinOutcome.Success, right.Outcome); Assert.False(right.Legacy); Same(Account, right.Account);
            Assert.True(guard.Opened); Assert.Equal(0, store.Record.Failures);
        }

        [Fact]
        public void TheRecordHoldsNeitherThePasswordNorThePinKey()
        {
            var storage = new Storage(folder);
            new PinGuard(storage.LockStore, () => now, 5).Enable(Pin, Account);
            var record = storage.LoadLock();
            var key = Convert.ToBase64String(PinHash.Derive(Pin, Convert.FromBase64String(record.Salt), record.Iterations));
            var fields = new[] { record.Salt, record.Hash, record.Key, record.Account };
            Assert.DoesNotContain(key, fields);
            var unprotected = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(folder, "lock.dpapi")), Encoding.UTF8.GetBytes("Univer.Desktop.lock.v1"), DataProtectionScope.CurrentUser));
            Assert.DoesNotContain("synthetic", unprotected); Assert.DoesNotContain("QA student", unprotected); Assert.DoesNotContain(key, unprotected);
            Assert.Throws<ArgumentException>(() => new PinGuard(storage.LockStore, () => now, 5).Enable(Pin, new Credentials { Login = "QA student", Sealed = true }));
        }

        [Fact]
        public void ANewPinAndANewPasswordKeepWhatIsSealed()
        {
            var store = new MemoryStore();
            Guard(store).Enable(Pin, Account);
            var guard = Guard(store);
            Same(Account, guard.Guess(Pin).Account);
            var changed = new Credentials { Login = "QA student", Password = "another synthetic" };
            Assert.True(guard.StoreAccount(changed));
            Same(changed, Guard(store).Guess(Pin).Account);
            guard.Enable(Other, changed);
            Assert.Equal(PinOutcome.Wrong, Guard(store).Guess(Pin).Outcome);
            Same(changed, Guard(store).Guess(Other).Account);
            Assert.True(guard.StoreAccount(null));
            var none = Guard(store).Guess(Other);
            Assert.Equal(PinOutcome.Success, none.Outcome); Assert.Null(none.Account);
        }

        [Fact]
        public void TurningThePinOffWritesTheAccountBackFirst()
        {
            var store = new MemoryStore(); var guard = Guard(store);
            guard.Enable(Pin, Account);
            Assert.Equal(PinOutcome.Failed, guard.Disable(Pin, account => { throw new IOException("disk full"); }).Outcome);
            Assert.NotNull(store.Record); Assert.NotNull(store.Record.Account);
            Credentials restored = null;
            Assert.Equal(PinOutcome.Success, guard.Disable(Pin, account => restored = account).Outcome);
            Assert.Null(store.Record); Same(Account, restored); Assert.False(guard.Opened);
        }

        [Fact]
        public void AnOlderHashOnlyRecordOpensAndIsSealedAfterwards()
        {
            var salt = new byte[PinHash.SaltSize]; salt[0] = 7;
            var store = new MemoryStore { Record = new LockRecord { Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(PinHash.Derive(Pin, salt, 5)), Iterations = 5 } };
            var guard = Guard(store);
            Assert.Equal(PinOutcome.Wrong, guard.Guess(Other).Outcome);
            var right = guard.Guess(Pin);
            Assert.Equal(PinOutcome.Success, right.Outcome); Assert.True(right.Legacy); Assert.Null(right.Account);
            Assert.False(guard.StoreAccount(Account));
            guard.Enable(Pin, Account);
            Assert.Null(store.Record.Hash);
            Same(Account, Guard(store).Guess(Pin).Account);
        }

        [Fact]
        public void ASealIsRefusedForAnotherKeyPurposeOrAChangedByte()
        {
            var key = PinSeal.RandomKey(); var data = Encoding.UTF8.GetBytes("synthetic");
            var sealedData = PinSeal.Seal(key, data, "a");
            Assert.Equal(data, PinSeal.Open(key, sealedData, "a"));
            Assert.Null(PinSeal.Open(key, sealedData, "b"));
            Assert.Null(PinSeal.Open(PinSeal.RandomKey(), sealedData, "a"));
            foreach (var i in new[] { 0, 20, sealedData.Length - 1 })
            {
                var changed = (byte[])sealedData.Clone(); changed[i] ^= 1;
                Assert.Null(PinSeal.Open(key, changed, "a"));
            }
            Assert.Null(PinSeal.Open(key, new byte[10], "a"));
        }

        [Fact]
        public void WhenBothAccountFilesAreLeftTheNewerOneIsRead()
        {
            var storage = new Storage(folder);
            var plain = Path.Combine(folder, "credentials.dpapi");
            var sealedLogin = Path.Combine(folder, "account.dpapi");
            // A plain save cannot remove the sealed login another process holds open: it still succeeds and is read.
            storage.SaveCredentials(Account.LoginOnly());
            using (new FileStream(sealedLogin, FileMode.Open, FileAccess.Read, FileShare.None))
                storage.SaveCredentials(Account);
            Assert.True(File.Exists(sealedLogin));
            File.SetLastWriteTimeUtc(sealedLogin, DateTime.UtcNow.AddMinutes(-1));
            Assert.True(storage.LoadCredentials().Usable());
            // A sealed save whose plain password cannot go fails (the caller tries again), yet its newer sealed login
            // wins over the older plain password left behind, and the leftover is reported.
            storage.SaveCredentials(new Credentials { Login = "Old student", Password = "old" });
            using (new FileStream(plain, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<IOException>(() => storage.SaveCredentials(Account.LoginOnly()));
            Assert.True(storage.PlainFileLeft());
            // The start sees the leftover beside the newer sealed login, so the next unlock removes it.
            new PinGuard(storage.LockStore, () => now, 5).Enable(Pin, Account);
            var started = storage.Load();
            Assert.True(started.PlainFile); Assert.False(started.Credentials.Usable());
            File.SetLastWriteTimeUtc(plain, DateTime.UtcNow.AddMinutes(-1));
            var loaded = storage.LoadCredentials();
            Assert.Equal("QA student", loaded.Login); Assert.False(loaded.Usable());
            // On a tie the plain file is read.
            var when = DateTime.UtcNow.AddMinutes(-2);
            File.SetLastWriteTimeUtc(plain, when); File.SetLastWriteTimeUtc(sealedLogin, when);
            Assert.Equal("Old student", storage.LoadCredentials().Login);
            // The retry removes it.
            storage.SaveCredentials(Account.LoginOnly());
            Assert.False(storage.PlainFileLeft()); Assert.False(storage.LoadCredentials().Usable()); Assert.False(storage.Load().PlainFile);
        }

        [Fact]
        public void ASealedLoginWhosePinRecordIsGoneEndsTheAccount()
        {
            var storage = new Storage(folder);
            storage.SavePreferences(new Preferences());
            storage.SaveCredentials(Account.LoginOnly());
            // Builds without the PIN read credentials.dpapi: a sealed login is never there for them to submit.
            Assert.False(File.Exists(Path.Combine(folder, "credentials.dpapi"))); Assert.True(File.Exists(Path.Combine(folder, "account.dpapi")));
            var orphan = storage.Load();
            Assert.Null(orphan.Credentials); Assert.True(orphan.SealedOrphan); Assert.True(orphan.CredentialsUnreadable);
            new PinGuard(storage.LockStore, () => now, 5).Enable(Pin, Account);
            var locked = storage.Load();
            Assert.False(locked.SealedOrphan); Assert.Equal("QA student", locked.Credentials.Login); Assert.False(locked.Credentials.Usable());
            // A plain account from before the PIN is untouched.
            // A plain save (PIN off) is written before the sealed login goes.
            storage.SaveCredentials(Account);
            Assert.False(File.Exists(Path.Combine(folder, "account.dpapi")));
            Assert.True(storage.Load().Credentials.Usable());
            storage.RemoveLock();
            var plain = storage.Load();
            Assert.False(plain.SealedOrphan); Assert.True(plain.Credentials.Usable());
            // A PIN without a saved account (manual sign-in): the mark next to the record tells a deleted PIN file.
            storage.RemoveCredentials();
            new PinGuard(storage.LockStore, () => now, 5).Enable(Pin);
            Assert.True(storage.PinMarked); Assert.False(storage.Load().SealedOrphan);
            File.Delete(Path.Combine(folder, "lock.dpapi"));
            var manual = storage.Load();
            Assert.True(manual.SealedOrphan); Assert.False(manual.CredentialsUnreadable); Assert.Null(manual.Credentials);
            // The app's own removal (PIN off, account ended) takes the mark first, so it never looks like a deletion.
            new PinGuard(storage.LockStore, () => now, 5).Enable(Pin); storage.RemoveLock();
            Assert.False(storage.PinMarked); Assert.False(storage.Load().SealedOrphan);
            // A mark that cannot be written (a folder in its place) never fails a guess: the right PIN still opens.
            Directory.CreateDirectory(Path.Combine(folder, "lock.pin"));
            var blocked = new PinGuard(storage.LockStore, () => now, 5); blocked.Enable(Pin, Account);
            var opened = new PinGuard(storage.LockStore, () => now, 5).Guess(Pin);
            Assert.Equal(PinOutcome.Success, opened.Outcome); Assert.Equal(0, storage.LoadLock().Failures); Same(Account, opened.Account); storage.RemoveLock(); Assert.False(storage.LockExists);
        }

        [Fact]
        public void ASealedPasswordIsAWaitNotASignIn()
        {
            var prefs = new Preferences();
            Assert.False(Credentials.SignInActive(Account.LoginOnly(), prefs));
            Assert.True(Credentials.SignInActive(Account, prefs));
            Assert.False(Credentials.SignInActive(null, prefs));
            prefs.AutoLoginPaused = true;
            Assert.False(Credentials.SignInActive(Account, prefs));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

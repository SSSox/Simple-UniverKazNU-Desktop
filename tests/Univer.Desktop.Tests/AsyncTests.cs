using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public sealed class AsyncTests
    {
        [Fact]
        public async Task GateSerializesOperationsAndReleasesAfterFailure()
        {
            var gate = new AsyncOperationGate(); var hold = new TaskCompletionSource<bool>(); var entered = new TaskCompletionSource<bool>(); var secondEntered = false;
            var first = gate.RunAsync(async () => { entered.SetResult(true); await hold.Task; throw new InvalidOperationException(); });
            await entered.Task;
            var second = gate.RunAsync(() => { secondEntered = true; return Task.FromResult(true); });
            Assert.False(secondEntered); hold.SetResult(true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => first); await second; Assert.True(secondEntered);
        }
        [Fact]
        public async Task DeadlinePropagatesOperationFailure()
        { await Assert.ThrowsAsync<InvalidOperationException>(() => TaskDeadline.AwaitAsync(Task.Run(() => { throw new InvalidOperationException(); }), TimeSpan.FromSeconds(2), CancellationToken.None)); }
        [Fact]
        public async Task DeadlineStopsWaitingForHungOperation()
        { await Assert.ThrowsAsync<TimeoutException>(() => TaskDeadline.AwaitAsync(new TaskCompletionSource<bool>().Task, TimeSpan.FromMilliseconds(10), CancellationToken.None)); }
        [Fact]
        public async Task CancellationInterruptsDeadline()
        { using (var stop = new CancellationTokenSource()) { stop.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => TaskDeadline.AwaitAsync(new TaskCompletionSource<bool>().Task, TimeSpan.FromSeconds(2), stop.Token)); } }
        // A page read left unanswered when the page is replaced must not hold the next page's tick until CEF's deadline.
        [Fact]
        public async Task ReadStopsWaitingWhenNavigationBeginsAndIgnoresItsLateFailure()
        {
            var navigation = new NavigationSignal(); var hung = new TaskCompletionSource<string>();
            var read = TaskDeadline.UntilAsync(hung.Task, navigation.Next, null);
            Assert.False(read.IsCompleted);
            navigation.Raise();
            Assert.Same(read, await Task.WhenAny(read, Task.Delay(2000)));
            Assert.Null(await read);
            hung.SetException(new TaskCanceledException());
        }
        [Fact]
        public async Task ReadKeepsItsAnswerAndALaterNavigationWaitsAgain()
        {
            var navigation = new NavigationSignal();
            Assert.Equal("ok", await TaskDeadline.UntilAsync(Task.FromResult("ok"), navigation.Next, null));
            navigation.Raise();
            var pending = TaskDeadline.UntilAsync(new TaskCompletionSource<string>().Task, navigation.Next, "fallback");
            await Task.Delay(50); Assert.False(pending.IsCompleted);
            navigation.Raise(); Assert.Equal("fallback", await pending);
        }
        // A tick requested during a tick (a viewer opened, settings replaced the preferences) runs next with its own arguments.
        [Fact]
        public async Task RequestsDuringARunFoldIntoOneMoreRunWithTheLatestArguments()
        {
            var runs = new List<string>(); var hold = new TaskCompletionSource<bool>(); var coalescer = new Coalescer<string>();
            Func<string, Task> run = async value => { runs.Add(value); if (value == "main") await hold.Task; };
            var first = coalescer.RunAsync("main", run, value => value != "closed");
            await coalescer.RunAsync("viewer-busy", run, _ => true); await coalescer.RunAsync("viewer-dom", run, _ => true);
            Assert.Equal(new[] { "main" }, runs);
            hold.SetResult(true); await first;
            Assert.Equal(new[] { "main", "viewer-dom" }, runs);
            var again = new TaskCompletionSource<bool>(); runs.Clear();
            var second = coalescer.RunAsync("main", async value => { runs.Add(value); if (value == "main") await again.Task; }, value => value != "closed");
            await coalescer.RunAsync("closed", run, _ => true); again.SetResult(true); await second;
            Assert.Equal(new[] { "main" }, runs);
        }
        // Opening a document hides the main browser: a tick stored for it before that navigation must not run afterwards.
        [Fact]
        public async Task ANavigationForgetsTheRequestStoredBeforeItAndAFailedRunFreesTheNext()
        {
            var runs = new List<string>(); var hold = new TaskCompletionSource<bool>(); var coalescer = new Coalescer<string>();
            Func<string, Task> run = async value => { runs.Add(value); if (value == "main") await hold.Task; };
            var first = coalescer.RunAsync("main", run, _ => true);
            await coalescer.RunAsync("main-poll", run, _ => true);
            coalescer.Drop();
            hold.SetResult(true); await first;
            Assert.Equal(new[] { "main" }, runs);
            hold = new TaskCompletionSource<bool>(); runs.Clear();
            var second = coalescer.RunAsync("main", run, _ => true);
            coalescer.Drop(); await coalescer.RunAsync("viewer-dom", run, _ => true);
            Assert.Equal(new[] { "main" }, runs);
            hold.SetResult(true); await second;
            Assert.Equal(new[] { "main", "viewer-dom" }, runs);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coalescer.RunAsync("broken", _ => { throw new InvalidOperationException(); }, _ => true));
            runs.Clear(); await coalescer.RunAsync("after", run, _ => true);
            Assert.Equal(new[] { "after" }, runs);
        }
        [Fact]
        public void NavigationResumesWaitersOutsideTheHandlerThatRaisedIt()
        {
            var navigation = new NavigationSignal(); var handler = Thread.CurrentThread.ManagedThreadId; var resumed = new ManualResetEventSlim(); var on = 0;
            navigation.Next.ContinueWith(_ => { on = Thread.CurrentThread.ManagedThreadId; resumed.Set(); }, TaskContinuationOptions.ExecuteSynchronously);
            navigation.Raise();
            Assert.True(resumed.Wait(2000)); Assert.NotEqual(handler, on);
        }
    }
}

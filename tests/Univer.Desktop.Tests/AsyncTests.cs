using System;
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
    }
}

using System.Buffers.Binary;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Category", "OfflineFixture")]
[Trait("AuditPlan", "010Barrier")]
public sealed class WDriverRequestBarrierRelayTests
{
    [Fact]
    public async Task AllocateOnlyDoesNotCompleteExecuteBarrier()
    {
        using var observer = new WDriverRequestBarrierObserver();
        var generation = await observer.ArmExecuteAsync(CancellationToken.None);
        await Forward(observer, true, Frame(3));
        await Forward(observer, false, Frame(0));
        using var stop = new CancellationTokenSource();
        var waiting = observer.WaitExecuteForwardedAsync(generation, stop.Token);
        Assert.False(waiting.IsCompleted);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal([WRequestStage.Allocate], observer.Stages);
    }

    [Fact]
    public async Task AllocateResponseThenDirectExecuteCompletesOnlyAfterFullForward()
    {
        using var observer = new WDriverRequestBarrierObserver();
        var generation = await observer.ArmExecuteAsync(CancellationToken.None);
        await Forward(observer, true, Frame(3));
        await Forward(observer, false, Frame(0));
        var frame = Frame(5, flag: 1, bodyLength: 17);
        var waiting = observer.WaitExecuteForwardedAsync(generation, CancellationToken.None);
        var finalWrite = NewSignal(); var release = NewSignal(); var written = 0;
        var sink = new List<byte>();
        var pending = Forward(observer, true, frame, maxRead: 3, writer: async (bytes, token) =>
        {
            var count = Math.Min(2, bytes.Length);
            if (written + count == frame.Length) { finalWrite.TrySetResult(); await release.Task.WaitAsync(token); }
            sink.AddRange(bytes.Span[..count].ToArray()); written += count; return count;
        });
        await finalWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(waiting.IsCompleted);
        release.TrySetResult(); await pending; await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        observer.AssertExecutePending(generation, NewSignal().Task);
        Assert.Equal(frame, sink);
        Assert.Equal([WRequestStage.Allocate, WRequestStage.DirectExecute], observer.Stages);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(6, 0)]
    [InlineData(13, 0)]
    public async Task NextRequestWaitsForPreviousResponseForwardAccounting(short opcode, byte flag)
    {
        using var observer = new WDriverRequestBarrierObserver();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var generation = await observer.ArmExecuteAsync(deadline.Token);
        await Forward(observer, true, Frame(3));
        var barrier = observer.WaitExecuteForwardedAsync(generation, deadline.Token);
        var responseBytes = Frame(0, bodyLength: 4);
        var delivered = NewSignal(); var finishAccounting = NewSignal(); var written = 0;
        var response = Forward(observer, false, responseBytes, writer: async (bytes, token) =>
        {
            written += bytes.Length; // The client can consume these bytes before this await resumes.
            if (written == responseBytes.Length)
            {
                delivered.TrySetResult();
                await finishAccounting.Task.WaitAsync(token);
            }
            return bytes.Length;
        }, cancellationToken: deadline.Token);
        Task<byte[]>? next = null;
        try
        {
            await delivered.Task.WaitAsync(deadline.Token);
            var before = observer.ClientBytesForwarded;
            var request = Frame(opcode, flag, bodyLength: 4);
            next = Forward(observer, true, request, cancellationToken: deadline.Token);
            Assert.False(next.IsCompleted);
            Assert.False(barrier.IsCompleted);
            Assert.Equal(before, observer.ClientBytesForwarded);
            Assert.Equal([WRequestStage.Allocate], observer.Stages);
            finishAccounting.TrySetResult();
            await response.WaitAsync(deadline.Token);
            Assert.Equal(request, await next.WaitAsync(deadline.Token));
            await barrier.WaitAsync(deadline.Token);
            observer.AssertExecutePending(generation, NewSignal().Task);
            Assert.Equal(before + request.Length, observer.ClientBytesForwarded);
            Assert.Equal([WRequestStage.Allocate, opcode == 5 ? WRequestStage.DirectExecute : WRequestStage.PreparedExecute], observer.Stages);
        }
        finally
        {
            finishAccounting.TrySetResult();
            observer.Dispose();
            var tasks = new List<Task> { response, barrier };
            if (next is not null) tasks.Add(next);
            foreach (var task in tasks) _ = await Record.ExceptionAsync(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PrepareOnlyDoesNotCompleteExecuteBarrier(byte flag)
    {
        using var observer = new WDriverRequestBarrierObserver();
        var generation = await observer.ArmExecuteAsync(CancellationToken.None);
        await Forward(observer, true, Frame(5, flag)); await Forward(observer, false, Frame(0));
        Assert.False(observer.WaitExecuteForwardedAsync(generation, CancellationToken.None).IsCompleted);
        Assert.Equal(flag == 0 ? WRequestStage.Prepare : WRequestStage.Other, observer.Stages.Single());
    }

    [Theory]
    [InlineData(6)]
    [InlineData(13)]
    public async Task PreparedExecuteOpcodesCompleteExecuteBarrier(short opcode)
    {
        using var observer = new WDriverRequestBarrierObserver();
        var generation = await observer.ArmExecuteAsync(CancellationToken.None);
        await Forward(observer, true, Frame(5)); await Forward(observer, false, Frame(0));
        Assert.False(observer.WaitExecuteForwardedAsync(generation, CancellationToken.None).IsCompleted);
        await Forward(observer, true, Frame(opcode, bodyLength: 4));
        await observer.WaitExecuteForwardedAsync(generation, CancellationToken.None);
        observer.AssertExecutePending(generation, NewSignal().Task);
        Assert.Equal([WRequestStage.Prepare, WRequestStage.PreparedExecute], observer.Stages);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65536)]
    public async Task FragmentedAndCoalescedFramesPreserveBytesAndBoundaries(int maxRead)
    {
        using var observer = new WDriverRequestBarrierObserver();
        var generation = await observer.ArmExecuteAsync(CancellationToken.None);
        // A complete heartbeat followed by an execution frame in the same stream.
        var bytes = Frame(269, bodyLength: 4).Concat(Frame(5, 1, bodyLength: 27000)).ToArray();
        var result = await Forward(observer, true, bytes, maxRead, maxWrite: 3);
        Assert.Equal(bytes, result);
        Assert.Equal(bytes.Length, observer.ClientBytesForwarded);
        await observer.WaitExecuteForwardedAsync(generation, CancellationToken.None);
        Assert.Equal([WRequestStage.DirectExecute], observer.Stages);
    }

    [Fact]
    public async Task CrcTrailerIsCountedOnceAndTruncationCannotCompleteBarrier()
    {
        using (var observer = new WDriverRequestBarrierObserver())
        {
            var generation = await observer.ArmExecuteAsync(CancellationToken.None);
            var bytes = Frame(5, 1, bodyLength: 4);
            Assert.Equal(bytes, await Forward(observer, true, bytes));
            await observer.WaitExecuteForwardedAsync(generation, CancellationToken.None);
        }
        foreach (var bytes in new[] { Frame(5, 1, 4)[..^1], Frame(5, 1)[..^1], HeaderLength(-1), HeaderLength(int.MaxValue), HeaderLength(64 * 1024 * 1024) })
        {
            using var observer = new WDriverRequestBarrierObserver();
            var generation = await observer.ArmExecuteAsync(CancellationToken.None);
            var waiting = observer.WaitExecuteForwardedAsync(generation, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidDataException>(() => Forward(observer, true, bytes));
            await Assert.ThrowsAsync<InvalidDataException>(() => waiting);
        }
    }

    [Fact]
    public async Task EarlyOrOpcodeZeroResponseInvalidatesPendingObservation()
    {
        foreach (var opcode in new short[] { 0, 987 })
        {
            using var observer = new WDriverRequestBarrierObserver();
            var generation = await observer.ArmExecuteAsync(CancellationToken.None);
            var finalWrite = NewSignal(); var release = NewSignal();
            var sending = Forward(observer, true, Frame(5, 1, 1), writer: async (bytes, token) =>
            {
                if (bytes.Length == 1) { finalWrite.TrySetResult(); await release.Task.WaitAsync(token); }
                return bytes.Length;
            });
            await finalWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Forward(observer, false, Frame(opcode)); // Response is allowed before the final send continuation.
            release.TrySetResult(); await sending;
            await observer.WaitExecuteForwardedAsync(generation, CancellationToken.None);
            Assert.Throws<InvalidOperationException>(() => observer.AssertExecutePending(generation, NewSignal().Task));
        }
        using (var observer = new WDriverRequestBarrierObserver())
        {
            var generation = await observer.ArmExecuteAsync(CancellationToken.None);
            await Forward(observer, true, Frame(6));
            var firstByte = NewSignal(); var release = NewSignal(); var offset = 0; var response = Frame(269);
            var pumping = observer.ForwardFramesAsync(false, async (buffer, token) =>
            {
                if (offset == 0) { buffer.Span[0] = response[0]; offset++; return 1; }
                if (offset == 1) { firstByte.TrySetResult(); await release.Task.WaitAsync(token); }
                if (offset == response.Length) return 0;
                response.AsMemory(offset).CopyTo(buffer); var count = response.Length - offset; offset += count; return count;
            }, (bytes, _) => ValueTask.FromResult(bytes.Length));
            await firstByte.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<InvalidOperationException>(() => observer.AssertExecutePending(generation, NewSignal().Task));
            release.TrySetResult(); await pumping;
            observer.AssertExecutePending(generation, NewSignal().Task); // Only a *complete* heartbeat can be ignored.
            Assert.Throws<InvalidOperationException>(() => observer.AssertExecutePending(generation + 1, NewSignal().Task));
            Assert.Throws<InvalidOperationException>(() => observer.AssertExecutePending(generation, Task.CompletedTask));
        }
    }

    [Fact]
    public async Task CancellationAndDisposalReleaseEveryBarrierWaiter()
    {
        foreach (var allocate in new[] { false, true })
        {
            using var observer = new WDriverRequestBarrierObserver();
            var generation = await observer.ArmExecuteAsync(CancellationToken.None);
            if (allocate) await Forward(observer, true, Frame(3));
            var waiting = observer.WaitExecuteForwardedAsync(generation, CancellationToken.None);
            var idle = observer.ArmExecuteAsync(new CancellationToken(true));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => idle);
            observer.Dispose();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        using (var observer = new WDriverRequestBarrierObserver())
        {
            var generation = await observer.ArmExecuteAsync(CancellationToken.None);
            var waiting = observer.WaitExecuteForwardedAsync(generation, CancellationToken.None);
            await Assert.ThrowsAsync<IOException>(() => Forward(observer, true, Frame(6), writer: (_, _) => throw new IOException("Synthetic forwarding fault")));
            await Assert.ThrowsAsync<IOException>(() => waiting);
            Assert.Throws<InvalidOperationException>(() => observer.AssertExecutePending(generation, NewSignal().Task));
        }
        foreach (var dispose in new[] { false, true })
        {
            using var observer = new WDriverRequestBarrierObserver();
            using var stop = new CancellationTokenSource();
            await Forward(observer, true, Frame(3));
            var next = Forward(observer, true, Frame(6), cancellationToken: stop.Token);
            Assert.False(next.IsCompleted);
            if (dispose)
            {
                observer.Dispose();
                await Assert.ThrowsAsync<ObjectDisposedException>(() => next.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            else
            {
                await stop.CancelAsync();
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(stop.Token, error.CancellationToken);
            }
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static byte[] HeaderLength(int length)
    { var header = Frame(5, 1); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6, 4), length); return header; }
    private static byte[] Frame(short opcode, byte flag = 0, int bodyLength = 0)
    {
        var bytes = new byte[64 + bodyLength];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4, 2), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6, 4), bodyLength);
        bytes[21] = flag;
        bytes.AsSpan(64).Fill(0x5A);
        return bytes;
    }
    private static async Task<byte[]> Forward(WDriverRequestBarrierObserver observer, bool fromClient, byte[] bytes,
        int maxRead = int.MaxValue, int maxWrite = int.MaxValue,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<int>>? writer = null,
        CancellationToken cancellationToken = default)
    {
        var offset = 0; var sink = new List<byte>();
        await observer.ForwardFramesAsync(fromClient, (buffer, _) =>
        {
            var count = Math.Min(Math.Min(buffer.Length, maxRead), bytes.Length - offset);
            bytes.AsMemory(offset, count).CopyTo(buffer); offset += count; return ValueTask.FromResult(count);
        }, writer ?? ((data, _) =>
        {
            var count = Math.Min(maxWrite, data.Length); sink.AddRange(data.Span[..count].ToArray()); return ValueTask.FromResult(count);
        }), cancellationToken);
        return sink.ToArray();
    }
}

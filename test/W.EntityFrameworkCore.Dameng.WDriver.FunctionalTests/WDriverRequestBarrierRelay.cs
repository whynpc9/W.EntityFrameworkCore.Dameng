using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

// Test-only plaintext relay. Protocol evidence is pinned to public W source
// aef142f05af67d2ce300f0a9f3a9fbd198f304cc; no body decoding or packet logging.
internal sealed class WDriverRequestBarrierRelay : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Socket> _sockets = [];
    private readonly string _host;
    private readonly int _port;
    private readonly bool _resetOnAccept;
    private readonly Task _accepting;
    private readonly List<Task> _pumps = [];
    private readonly List<WDriverRequestBarrierObserver> _observers = [];
    private readonly bool _observeFrames;
    private WDriverRequestBarrierObserver? _observer;
    private long _forwarded;
    private long _lastClientForward;
    private long _lastServerForward;
    private long _lastClientForwardTimestamp;
    private long _clientBytesBeforeReset;

    public WDriverRequestBarrierRelay(string connectionString, bool resetOnAccept, bool listen = true, bool observeFrames = false)
    {
        var builder = new DmConnectionStringBuilder(connectionString);
        var server = Convert.ToString(builder["server"], CultureInfo.InvariantCulture)!;
        _port = builder.TryGetValue("port", out var port) && port is not null && !string.IsNullOrEmpty(port.ToString())
            ? Convert.ToInt32(port, CultureInfo.InvariantCulture)
            : 5236;
        var separator = server.LastIndexOf(':');
        if (separator > 0 && !server.StartsWith('[') && server.IndexOf(':') == separator)
        {
            _port = int.Parse(server[(separator + 1)..], CultureInfo.InvariantCulture);
            server = server[..separator];
        }

        _host = server.Trim('[', ']');
        _resetOnAccept = resetOnAccept;
        _observeFrames = observeFrames;
        _listener.Start();
        builder.Remove("server");
        builder["server"] = IPAddress.Loopback.ToString();
        builder.Remove("port");
        builder["port"] = ((IPEndPoint)_listener.LocalEndpoint).Port;
        ConnectionString = builder.ConnectionString;

        // Without a listener the reserved loopback port refuses the connection.
        if (!listen)
        {
            _listener.Stop();
        }

        _accepting = listen ? AcceptAsync() : Task.CompletedTask;
    }

    public string ConnectionString { get; }

    public WDriverRequestBarrierObserver Observer
        => _observer ?? throw new InvalidOperationException("No observed session has connected.");

    /// <summary>Gets the client bytes a reset-on-accept relay received before resetting.</summary>
    public long ClientBytesBeforeReset
        => Interlocked.Read(ref _clientBytesBeforeReset);

    /// <summary>
    /// Resets every relayed connection once the latest forwarded data came from the client and
    /// the server has not answered it for <paramref name="outstandingFor" />.
    /// </summary>
    public async Task ResetWhileARequestIsOutstandingAsync(TimeSpan outstandingFor, TimeSpan timeout)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            lock (_sockets)
            {
                if (_lastClientForward > _lastServerForward
                    && Stopwatch.GetElapsedTime(_lastClientForwardTimestamp) >= outstandingFor)
                {
                    ResetAll();
                    return;
                }
            }

            if (Stopwatch.GetElapsedTime(started) > timeout)
            {
                throw new TimeoutException("No forwarded client request stayed unanswered at the relay.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    public void ResetAll()
    {
        lock (_sockets)
        {
            foreach (var socket in _sockets)
            {
                Reset(socket);
            }

            _sockets.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;
        ResetAll();
        await Task.WhenAll(_pumps);
        foreach (var observer in _observers) observer.Dispose();
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptSocketAsync(_stop.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            if (_resetOnAccept)
            {
                // Reset only after the client's first (login) bytes arrived, so the failure is after send.
                try
                {
                    using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    receiveTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                    var received = await client.ReceiveAsync(new byte[1024], SocketFlags.None, receiveTimeout.Token);
                    Interlocked.Add(ref _clientBytesBeforeReset, received);
                }
                catch (Exception error) when (error is OperationCanceledException or SocketException)
                {
                }

                Reset(client);
                continue;
            }

            var upstream = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await upstream.ConnectAsync(_host, _port, _stop.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException)
            {
                Reset(client);
                upstream.Dispose();
                continue;
            }

            lock (_sockets)
            {
                _sockets.Add(client);
                _sockets.Add(upstream);
            }

            if (_observeFrames)
            {
                var observer = new WDriverRequestBarrierObserver();
                _observers.Add(observer);
                _observer = observer;
                _pumps.Add(PumpObservedAsync(client, upstream, fromClient: true, observer));
                _pumps.Add(PumpObservedAsync(upstream, client, fromClient: false, observer));
            }
            else
            {
                _pumps.Add(PumpAsync(client, upstream, fromClient: true));
                _pumps.Add(PumpAsync(upstream, client, fromClient: false));
            }
        }
    }

    private async Task PumpObservedAsync(Socket source, Socket target, bool fromClient, WDriverRequestBarrierObserver observer)
    {
        try
        {
            await observer.ForwardFramesAsync(fromClient,
                (buffer, token) => source.ReceiveAsync(buffer, SocketFlags.None, token),
                (buffer, token) => target.SendAsync(buffer, SocketFlags.None, token), _stop.Token);
            observer.EndSession();
            target.Shutdown(SocketShutdown.Send);
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException or OperationCanceledException)
        {
            observer.EndSession();
        }
        catch (Exception error)
        {
            observer.Fail(error);
            Reset(source);
            Reset(target);
        }
    }

    private async Task PumpAsync(Socket source, Socket target, bool fromClient)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            int read;
            while ((read = await source.ReceiveAsync(buffer, SocketFlags.None, _stop.Token)) > 0)
            {
                // SendAsync may accept only part of the segment; forward every received byte.
                for (var sent = 0; sent < read;)
                {
                    sent += await target.SendAsync(buffer.AsMemory(sent, read - sent), SocketFlags.None, _stop.Token);
                }

                lock (_sockets)
                {
                    _forwarded++;
                    if (fromClient)
                    {
                        _lastClientForward = _forwarded;
                        _lastClientForwardTimestamp = Stopwatch.GetTimestamp();
                    }
                    else
                    {
                        _lastServerForward = _forwarded;
                    }
                }
            }

            target.Shutdown(SocketShutdown.Send);
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally { Array.Clear(buffer); }
    }

    // A zero linger time makes Close send RST instead of a graceful FIN.
    private static void Reset(Socket socket)
    {
        try
        {
            socket.LingerState = new LingerOption(true, 0);
            socket.Close();
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException)
        {
        }
    }
}


internal enum WRequestStage { Other, Allocate, Prepare, DirectExecute, PreparedExecute }

/// <summary>One exclusively used session; only a complete forwarded Execute satisfies the barrier.</summary>
internal sealed class WDriverRequestBarrierObserver : IDisposable
{
    private readonly object _sync = new();
    private TaskCompletionSource _changed = NewSignal();
    private Observation? _observation;
    private Exchange? _pending;
    private bool _clientFrameActive;
    private bool _serverFrameActive;
    private bool _ended;
    private Exception? _failure;
    private long _generation;
    private long _clientBytes;
    private readonly List<WRequestStage> _stages = [];
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public long ClientBytesForwarded => Interlocked.Read(ref _clientBytes);
    public WRequestStage[] Stages { get { lock (_sync) return [.. _stages]; } }

    public async Task<long> ArmExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            Task changed;
            lock (_sync)
            {
                ThrowIfUnavailable();
                if (_pending is null && !_clientFrameActive && !_serverFrameActive)
                {
                    _observation?.Forwarded.TrySetCanceled(CancellationToken.None);
                    _observation = new Observation(++_generation);
                    return _generation;
                }
                changed = _changed.Task;
            }
            await changed.WaitAsync(cancellationToken);
        }
    }

    public Task WaitExecuteForwardedAsync(long generation, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ThrowIfUnavailable();
            return GetObservation(generation).Forwarded.Task.WaitAsync(cancellationToken);
        }
    }

    public void AssertExecutePending(long generation, Task operation)
    {
        lock (_sync)
        {
            ThrowIfUnavailable();
            var observation = GetObservation(generation);
            if (!observation.CompleteForward || observation.ResponseStarted || _serverFrameActive || operation.IsCompleted)
                throw new InvalidOperationException("Execute pending-result evidence is unavailable for this generation.");
        }
    }

    public async Task ForwardFramesAsync(bool fromClient,
        Func<Memory<byte>, CancellationToken, ValueTask<int>> read,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<int>> write,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[64];
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var offset = 0;
                while (offset < header.Length)
                {
                    var count = await read(header.AsMemory(offset), cancellationToken);
                    if (count == 0)
                    {
                        if (offset != 0) throw new InvalidDataException("Truncated fixed frame header.");
                        return;
                    }
                    if (count < 0 || count > header.Length - offset) throw new InvalidDataException("Invalid read count.");
                    if (offset == 0) FrameStarted(fromClient);
                    offset += count;
                }
                var opcode = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(4, 2));
                var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(6, 4));
                // bodyLength already includes the CRC trailer; never add another four bytes.
                if (length < 0 || length > 64 * 1024 * 1024 - 64)
                    throw new InvalidDataException("Frame length exceeds the pinned protocol bound.");
                _ = checked(64 + length);
                var heartbeat = opcode == 269;
                Exchange? exchange = null;
                if (fromClient)
                {
                    if (!heartbeat) exchange = await BeginRequestAsync(Classify(opcode, header[21]), cancellationToken).ConfigureAwait(false);
                }
                else if (!heartbeat) ResponseStarted();

                await SendAllAsync(header, fromClient, write, cancellationToken);
                for (var remaining = length; remaining > 0;)
                {
                    var count = await read(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
                    if (count <= 0) throw new InvalidDataException("Truncated frame body.");
                    if (count > remaining || count > buffer.Length) throw new InvalidDataException("Invalid body read count.");
                    await SendAllAsync(buffer.AsMemory(0, count), fromClient, write, cancellationToken);
                    remaining -= count;
                }
                FrameCompleted(fromClient, heartbeat, exchange);
                Array.Clear(header);
            }
        }
        catch (Exception error)
        {
            Fail(error);
            throw;
        }
        finally
        {
            Array.Clear(header);
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private async Task SendAllAsync(ReadOnlyMemory<byte> bytes, bool fromClient,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<int>> write, CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < bytes.Length;)
        {
            var count = await write(bytes[offset..], cancellationToken);
            if (count <= 0 || count > bytes.Length - offset) throw new IOException("Invalid forwarding write count.");
            if (fromClient) Interlocked.Add(ref _clientBytes, count);
            offset += count;
        }
    }

    private void FrameStarted(bool fromClient)
    {
        lock (_sync)
        {
            if (fromClient) _clientFrameActive = true;
            else _serverFrameActive = true; // Unclassified partial header must invalidate pending-result evidence.
            Pulse();
        }
    }

    private async Task<Exchange> BeginRequestAsync(WRequestStage stage, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task changed;
            lock (_sync)
            {
                ThrowIfUnavailable();
                if (_pending is null)
                {
                    var target = _observation is { Selected: false } && stage is WRequestStage.DirectExecute or WRequestStage.PreparedExecute
                        ? _observation : null;
                    if (target is not null) target.Selected = true;
                    _pending = new Exchange(stage, target);
                    return _pending;
                }

                // A client can receive the last response byte and send its next request before
                // the server pump resumes to account for that byte. Let that pump finish first.
                changed = _changed.Task;
            }
            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void ResponseStarted()
    {
        lock (_sync)
        {
            if (_pending is null) throw new InvalidOperationException("Non-heartbeat response has no pending exchange.");
            // The first non-heartbeat response belongs to the current exchange, including opcode 0/unknown.
            if (_pending.Target is { } target) target.ResponseStarted = true;
        }
    }

    private void FrameCompleted(bool fromClient, bool heartbeat, Exchange? exchange)
    {
        lock (_sync)
        {
            if (fromClient)
            {
                _clientFrameActive = false;
                if (exchange is not null)
                {
                    exchange.CompleteForward = true;
                    _stages.Add(exchange.Stage);
                    if (exchange.Target is { } target)
                    {
                        target.CompleteForward = true;
                        target.Forwarded.TrySetResult();
                    }
                }
            }
            else
            {
                _serverFrameActive = false;
                if (!heartbeat && _pending is { } pending) pending.ResponseComplete = true;
            }
            if (_pending is { CompleteForward: true, ResponseComplete: true }) _pending = null;
            Pulse();
        }
    }

    private static WRequestStage Classify(short opcode, byte executeFlag) => opcode switch
    {
        3 => WRequestStage.Allocate,
        5 when executeFlag == 0 => WRequestStage.Prepare,
        5 when executeFlag == 1 => WRequestStage.DirectExecute,
        6 or 13 => WRequestStage.PreparedExecute,
        _ => WRequestStage.Other
    };

    public void Fail(Exception error)
    {
        lock (_sync)
        {
            _failure ??= error;
            _observation?.Forwarded.TrySetException(error);
            Pulse();
        }
    }
    public void EndSession()
    {
        lock (_sync)
        {
            _ended = true;
            _observation?.Forwarded.TrySetCanceled(CancellationToken.None);
            Pulse();
        }
    }
    public void Dispose() => EndSession();
    private Observation GetObservation(long generation)
        => _observation is { } observation && observation.Generation == generation
            ? observation : throw new InvalidOperationException("Stale Execute observation generation.");
    private void ThrowIfUnavailable()
    {
        if (_failure is not null) throw new InvalidOperationException("Frame forwarding failed.", _failure);
        ObjectDisposedException.ThrowIf(_ended, this);
    }
    private void Pulse() { var changed = _changed; _changed = NewSignal(); changed.TrySetResult(); }
    private sealed class Observation(long generation)
    {
        public long Generation { get; } = generation;
        public bool Selected { get; set; }
        public bool CompleteForward { get; set; }
        public bool ResponseStarted { get; set; }
        public TaskCompletionSource Forwarded { get; } = NewSignal();
    }
    private sealed class Exchange(WRequestStage stage, Observation? target)
    {
        public WRequestStage Stage { get; } = stage;
        public Observation? Target { get; } = target;
        public bool CompleteForward { get; set; }
        public bool ResponseComplete { get; set; }
    }
}

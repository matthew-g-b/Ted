namespace TED.Protocol;

using System.IO;

/// <summary>One read-only ISO-TP transaction at a time on 11-bit CAN identifiers.</summary>
public sealed class IsoTpClient : IDisposable
{
    private readonly SlcanConnection _link;
    private readonly SemaphoreSlim _singleRequest = new(1, 1);
    private readonly object _sync = new();
    private Pending? _pending;
    private bool _disposed;
    public event Action<ushort>? FlowControlSent;

    private sealed class Pending
    {
        public readonly ushort TxId;
        public readonly ushort RxId;
        public readonly TaskCompletionSource<byte[]> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<byte> Bytes = new();
        public int ExpectedSize = -1;
        public int Sequence = 1;

        public Pending(ushort txId, ushort rxId) { TxId = txId; RxId = rxId; }
    }

    public IsoTpClient(SlcanConnection link)
    {
        _link = link;
        _link.FrameReceived += OnFrame;
    }

    public async Task<byte[]> RequestAsync(
        ushort txId, ushort rxId, byte[] request,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (request.Length is < 1 or > 7)
            throw new ArgumentException("Only single-frame diagnostic requests are supported.");

        await _singleRequest.WaitAsync(cancellationToken);
        Pending? transaction = null;
        try
        {
            transaction = new Pending(txId, rxId);
            lock (_sync) _pending = transaction;

            byte[] frame = new byte[8];
            frame[0] = (byte)request.Length;
            request.CopyTo(frame, 1);
            _link.SendFrame(txId, frame);
            return await transaction.Completion.Task.WaitAsync(timeout, cancellationToken);
        }
        finally
        {
            lock (_sync)
                if (ReferenceEquals(_pending, transaction)) _pending = null;
            _singleRequest.Release();
        }
    }

    private void OnFrame(CanFrame f)
    {
        Pending? p;
        bool sendFlowControl = false;
        lock (_sync)
        {
            p = _pending;
            if (p is null || f.Id != p.RxId || f.Data.Length == 0
                || p.Completion.Task.IsCompleted) return;

            int type = f.Data[0] >> 4;
            if (type == 0) // Single Frame
            {
                int length = f.Data[0] & 0xF;
                if (length == 0 || length > f.Data.Length - 1) return;
                p.Completion.TrySetResult(f.Data.AsSpan(1, length).ToArray());
            }
            else if (type == 1) // First Frame: 12-bit total payload length
            {
                if (f.Data.Length < 8 || p.ExpectedSize >= 0) return;
                int size = ((f.Data[0] & 0xF) << 8) | f.Data[1];
                if (size <= 6 || size > 4095)
                {
                    p.Completion.TrySetException(new InvalidDataException("Invalid ISO-TP length."));
                    return;
                }
                p.ExpectedSize = size;
                p.Bytes.AddRange(f.Data.AsSpan(2).ToArray());
                sendFlowControl = true;
            }
            else if (type == 2 && p.ExpectedSize >= 0) // Consecutive Frame
            {
                int sequence = f.Data[0] & 0xF;
                if (sequence != (p.Sequence & 0xF))
                {
                    p.Completion.TrySetException(new InvalidDataException("ISO-TP sequence mismatch."));
                    return;
                }
                p.Sequence++;
                int left = p.ExpectedSize - p.Bytes.Count;
                int take = Math.Min(left, f.Data.Length - 1);
                if (take <= 0) return;
                p.Bytes.AddRange(f.Data.AsSpan(1, take).ToArray());
                if (p.Bytes.Count == p.ExpectedSize)
                    p.Completion.TrySetResult(p.Bytes.ToArray());
            }
        }

        if (sendFlowControl)
        {
            try
            {
                // Continue To Send; block size = 0; STmin = 0. 11-bit response on p.RxId.
                // The flow-control frame travels on the request CAN ID.
                _link.SendFrame(p!.TxId, new byte[] { 0x30, 0, 0, 0, 0, 0, 0, 0 });
                FlowControlSent?.Invoke(p.TxId);
            }
            catch (Exception e) { p!.Completion.TrySetException(e); }
        }
    }

    public void CancelPending()
    {
        lock (_sync)
        {
            _pending?.Completion.TrySetCanceled();
            _pending = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _link.FrameReceived -= OnFrame;
        CancelPending();
        _singleRequest.Dispose();
    }
}

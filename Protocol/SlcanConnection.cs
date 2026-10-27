using System.IO.Ports;
using System.Text;
using System.IO;

namespace TED.Protocol;

/// <summary>
/// ASCII SLCAN on the CANable's USB virtual COM port. Events fire on the serial receive thread.
/// Matches normaldotcom/canable2-fw, including the unterminated V response.
/// </summary>
public sealed class SlcanConnection : IDisposable
{
    private readonly SerialPort _port;
    private readonly StringBuilder _buffer = new();
    private readonly object _rxLock = new();
    private readonly object _txLock = new();
    private bool _disposed;

    public event Action<string>? LineReceived;
    public event Action<CanFrame>? FrameReceived;

    public SlcanConnection(string portName)
    {
        _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            Encoding = Encoding.ASCII,
            Handshake = Handshake.None,
            ReadBufferSize = 65536,
            WriteTimeout = 1000,
            NewLine = "\r"
        };
        _port.DataReceived += OnDataReceived;
    }

    public void Open() => _port.Open();

    public void Command(string command)
    {
        if (string.IsNullOrEmpty(command) || command.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("Send one SLCAN command without a newline.", nameof(command));
        lock (_txLock)
        {
            if (!_port.IsOpen) throw new InvalidOperationException("COM port not open.");
            _port.Write(command + "\r");
        }
    }

    public void SendFrame(ushort id, ReadOnlySpan<byte> payload)
    {
        if (id > 0x7FF || payload.Length > 8)
            throw new ArgumentOutOfRangeException(nameof(payload), "Requires 11-bit ID and <=8 data bytes.");
        Command($"t{id:X3}{payload.Length:X1}{Convert.ToHexString(payload)}");
    }

    // This firmware sends 'V' response as raw text WITHOUT a trailing CR.
    // Call once after the version query, while CAN is still closed.
    public void FlushUnterminatedVersion()
    {
        string? line = null;
        lock (_rxLock)
        {
            if (_buffer.Length != 0)
            {
                line = _buffer.ToString();
                _buffer.Clear();
            }
        }
        if (line != null) Deliver(line);
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            string text = _port.ReadExisting();
            List<string> complete = new();
            lock (_rxLock)
            {
                foreach (char ch in text)
                {
                    if (ch is '\r' or '\n' or '\a')
                    {
                        if (_buffer.Length != 0)
                        {
                            complete.Add(_buffer.ToString());
                            _buffer.Clear();
                        }
                        if (ch == '\a') complete.Add("[CANable BEL/error]");
                    }
                    else if (!char.IsControl(ch))
                    {
                        _buffer.Append(ch);
                        if (_buffer.Length > 4096)
                        {
                            _buffer.Clear();
                            complete.Add("[Oversize serial reply discarded]");
                        }
                    }
                }
            }
            foreach (string line in complete) Deliver(line);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            LineReceived?.Invoke("[Serial receive error: " + ex.Message + "]");
        }
    }

    private void Deliver(string line)
    {
        LineReceived?.Invoke(line);
        // ISO-TP still reacts synchronously on the serial worker thread.
        if (CanFrame.TryParse(line, out var frame) && frame != null)
            FrameReceived?.Invoke(frame);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _port.DataReceived -= OnDataReceived;
        lock (_txLock)
        {
            if (_port.IsOpen)
            {
                try { _port.Write("C\r"); } catch { /* disconnected */ }
                _port.Close();
            }
        }
        _port.Dispose();
    }
}

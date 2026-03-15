using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// Minimal UDP/OSC receiver. Runs a background thread, parses incoming OSC
/// messages, and queues them for consumption on the Unity main thread.
/// Only int ('i') and float ('f') argument types are decoded.
/// </summary>
public class OSCServer : IDisposable {

    public class Message {
        public string Address;
        public object[] Args;
    }

    private UdpClient _udp;
    private Thread _thread;
    private volatile bool _running;

    private readonly Queue<Message> _queue = new Queue<Message>();
    private readonly object _lock = new object();

    public OSCServer(int port) {
        _udp = new UdpClient(port);
        _running = true;
        _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "OSCServer" };
        _thread.Start();
    }

    /// <summary>Dequeue one message if available. Returns false when queue is empty.</summary>
    public bool TryDequeue(out Message msg) {
        lock (_lock) {
            if (_queue.Count == 0) { msg = null; return false; }
            msg = _queue.Dequeue();
            return true;
        }
    }

    // ── background thread ──────────────────────────────────────────────

    private void ReceiveLoop() {
        while (_running) {
            try {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _udp.Receive(ref ep);
                var msg = ParseOSC(data);
                if (msg != null) {
                    lock (_lock) { _queue.Enqueue(msg); }
                }
            } catch (SocketException) {
                break; // socket was closed
            } catch {
                // skip malformed packets
            }
        }
    }

    // ── OSC parser ─────────────────────────────────────────────────────

    private static Message ParseOSC(byte[] data) {
        int offset = 0;

        string addr = ReadString(data, ref offset);
        if (string.IsNullOrEmpty(addr) || addr[0] != '/') return null;

        if (offset >= data.Length) return null;
        string typeTag = ReadString(data, ref offset);
        if (typeTag.Length < 1 || typeTag[0] != ',') return null;

        string types = typeTag.Substring(1);
        var args = new object[types.Length];

        for (int i = 0; i < types.Length; i++) {
            switch (types[i]) {
                case 'i': args[i] = ReadInt32(data, ref offset); break;
                case 'f': args[i] = ReadFloat(data, ref offset); break;
                default:  Skip4(data, ref offset); args[i] = null; break;
            }
        }

        return new Message { Address = addr, Args = args };
    }

    private static string ReadString(byte[] d, ref int offset) {
        int start = offset;
        while (offset < d.Length && d[offset] != 0) offset++;
        string s = Encoding.ASCII.GetString(d, start, offset - start);
        offset++; // consume null terminator
        offset = (offset + 3) & ~3; // pad to 4-byte boundary
        return s;
    }

    private static int ReadInt32(byte[] d, ref int offset) {
        if (offset + 4 > d.Length) return 0;
        // OSC is big-endian
        int v = (d[offset] << 24) | (d[offset + 1] << 16) | (d[offset + 2] << 8) | d[offset + 3];
        offset += 4;
        return v;
    }

    private static float ReadFloat(byte[] d, ref int offset) {
        if (offset + 4 > d.Length) return 0f;
        // Reverse bytes for little-endian host
        byte[] b = { d[offset + 3], d[offset + 2], d[offset + 1], d[offset] };
        offset += 4;
        return BitConverter.ToSingle(b, 0);
    }

    private static void Skip4(byte[] d, ref int offset) {
        offset = Math.Min(offset + 4, d.Length);
    }

    // ── IDisposable ────────────────────────────────────────────────────

    public void Dispose() {
        _running = false;
        try { _udp?.Close(); } catch { }
        _udp = null;
    }
}

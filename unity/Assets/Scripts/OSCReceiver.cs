using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// Single UDP socket on port 7002 — BOTH sends OSC position to backend AND receives gains.
///
/// Why one socket matters (Android conntrack):
///   Quest binds to port 7002.
///   SendOSC() sends:  quest:7002 → pc:7000
///   Android records:  REPLY tuple = pc:7000 → quest:7002
///   Backend replies:  pc:7000  → quest:7002  ← matches, Android allows it in
///
///   Requires a proper router (not Windows Mobile Hotspot which blocks host→client).
/// </summary>
public class OSCReceiver : MonoBehaviour {
    [Header("OSC Settings")]
    public int listenPort = 7002;

    [Header("Backend")]
    public string backendIP   = "10.10.10.5";
    public int    backendPort = 7000;

    private UdpClient udpClient;
    private Thread    receiveThread;
    private volatile bool running;
    private SpeakerManager speakerManager;
    private readonly object _sendLock = new object();

    private readonly ConcurrentQueue<(int sourceId, float[] gains)> gainQueue =
        new ConcurrentQueue<(int, float[])>();

    // ── diagnostics ──────────────────────────────────────────────────────────
    public float  LastPacketTime { get; private set; } = -999f;
    public int    TotalPackets   { get; private set; } = 0;
    public int    RawPackets     { get; private set; } = 0;
    public string BindError      { get; private set; } = "";
    public static int TotalOSCSent = 0;

    // ── lifecycle ────────────────────────────────────────────────────────────

    void Start() {
        var sm = FindObjectOfType<SpeakerManager>();
        if (sm != null) {
            speakerManager = sm;
            if (!string.IsNullOrEmpty(sm.backendIP))
                backendIP = sm.backendIP;
        }
        StartListening();
    }

    void StartListening() {
        try {
            udpClient = new UdpClient();
            udpClient.Client.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));
            udpClient.Client.ReceiveTimeout = 500;

            running = true;
            receiveThread = new Thread(ReceiveLoop) {
                IsBackground = true, Name = "OSCReceiveThread"
            };
            receiveThread.Start();
            BindError = "";
            Debug.Log($"[OSCReceiver] UDP :{listenPort}  backend={backendIP}:{backendPort}");

            StartCoroutine(SendHelloAfterBind());
        } catch (Exception e) {
            BindError = e.Message;
            Debug.LogError($"[OSCReceiver] Bind failed :{listenPort}: {e.Message}");
        }
    }

    void Update() {
        RawPackets = Interlocked.CompareExchange(ref _rawPackets, 0, 0);
        while (gainQueue.TryDequeue(out var item)) {
            speakerManager?.ApplyBackendGains(item.sourceId, item.gains);
            LastPacketTime = Time.time;
            TotalPackets++;
        }
    }

    void OnDestroy() {
        running = false;
        try { udpClient?.Close(); } catch { }
    }

    // ── send hello on startup ────────────────────────────────────────────────

    System.Collections.IEnumerator SendHelloAfterBind() {
        yield return null;
        for (int i = 0; i < 3; i++) {
            SendOSC("/spatial/hello", 0);
            yield return new WaitForSeconds(0.3f);
        }
    }

    // ── public send API ──────────────────────────────────────────────────────

    public void SendOSC(string address, params object[] args) {
        if (udpClient == null || !string.IsNullOrEmpty(BindError)) return;
        try {
            byte[] data = BuildOSCMessage(address, args);
            lock (_sendLock) {
                udpClient.Send(data, data.Length, backendIP, backendPort);
            }
            TotalOSCSent++;
        } catch (Exception e) {
            Debug.LogWarning($"[OSCReceiver] Send failed: {e.Message}");
        }
    }

    // ── receive loop ─────────────────────────────────────────────────────────

    private int _rawPackets = 0;

    void ReceiveLoop() {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        while (running) {
            try {
                byte[] data = udpClient.Receive(ref ep);
                Interlocked.Increment(ref _rawPackets);
                ParseOSC(data);
            } catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) {
                // expected — keep looping
            } catch (SocketException ex) {
                if (running) BindError = "SocketErr: " + ex.Message;
                break;
            } catch (Exception e) {
                if (running) Debug.LogWarning($"[OSCReceiver] rx: {e.Message}");
            }
        }
    }

    // ── OSC parser ────────────────────────────────────────────────────────────

    void ParseOSC(byte[] data) {
        int offset = 0;
        string address = ReadOSCString(data, ref offset);
        if (address != "/spatial/speaker_gains") return;

        string typeTags = ReadOSCString(data, ref offset);
        if (string.IsNullOrEmpty(typeTags) || !typeTags.StartsWith(",") || typeTags.Length < 2)
            return;
        typeTags = typeTags.Substring(1);

        if (typeTags[0] != 'i' || offset + 4 > data.Length) return;
        int sourceId = ReadInt32BE(data, ref offset);

        int numGains = typeTags.Length - 1;
        float[] gains = new float[numGains];
        for (int i = 0; i < numGains; i++) {
            if (offset + 4 > data.Length) break;
            if (typeTags[i + 1] == 'f') gains[i] = ReadFloat32BE(data, ref offset);
            else offset += 4;
        }
        gainQueue.Enqueue((sourceId, gains));
    }

    // ── OSC builder ───────────────────────────────────────────────────────────

    static byte[] BuildOSCMessage(string address, object[] args) {
        var stream = new System.IO.MemoryStream();
        WriteOSCString(stream, address);
        string typetag = ",";
        foreach (object a in args) {
            if (a is int)                    typetag += "i";
            else if (a is float || a is double) typetag += "f";
        }
        WriteOSCString(stream, typetag);
        foreach (object a in args) {
            byte[] b;
            if (a is int iv) {
                b = BitConverter.GetBytes(iv);
                if (BitConverter.IsLittleEndian) Array.Reverse(b);
                stream.Write(b, 0, 4);
            } else if (a is float fv) {
                b = BitConverter.GetBytes(fv);
                if (BitConverter.IsLittleEndian) Array.Reverse(b);
                stream.Write(b, 0, 4);
            } else if (a is double dv) {
                b = BitConverter.GetBytes((float)dv);
                if (BitConverter.IsLittleEndian) Array.Reverse(b);
                stream.Write(b, 0, 4);
            }
        }
        return stream.ToArray();
    }

    static void WriteOSCString(System.IO.MemoryStream s, string str) {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(str);
        s.Write(bytes, 0, bytes.Length);
        int pad = (4 - ((bytes.Length + 1) % 4)) % 4;
        s.WriteByte(0);
        for (int i = 0; i < pad; i++) s.WriteByte(0);
    }

    static string ReadOSCString(byte[] data, ref int offset) {
        int start = offset;
        while (offset < data.Length && data[offset] != 0) offset++;
        string s = System.Text.Encoding.ASCII.GetString(data, start, offset - start);
        offset++;
        offset = (offset + 3) & ~3;
        return s;
    }

    static int ReadInt32BE(byte[] data, ref int offset) {
        int v = (data[offset] << 24) | (data[offset+1] << 16) |
                (data[offset+2] << 8)  |  data[offset+3];
        offset += 4;
        return v;
    }

    static float ReadFloat32BE(byte[] data, ref int offset) {
        byte[] b = { data[offset+3], data[offset+2], data[offset+1], data[offset] };
        offset += 4;
        return BitConverter.ToSingle(b, 0);
    }
}

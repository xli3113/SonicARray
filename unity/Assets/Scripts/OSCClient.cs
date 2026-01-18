using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

public class OSCClient {
    private UdpClient udpClient;
    private IPEndPoint endPoint;
    private bool isConnected = false;
    
    public OSCClient(string ipAddress, int port) {
        try {
            endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
            udpClient = new UdpClient();
            isConnected = true;
        } catch (Exception e) {
            UnityEngine.Debug.LogError($"OSC Client initialization failed: {e.Message}");
            isConnected = false;
        }
    }
    
    public void Send(string address, params object[] values) {
        if (!isConnected || udpClient == null) return;
        
        try {
            byte[] message = BuildOSCMessage(address, values);
            udpClient.Send(message, message.Length, endPoint);
        } catch (Exception e) {
            UnityEngine.Debug.LogError($"OSC Send failed: {e.Message}");
        }
    }
    
    private byte[] BuildOSCMessage(string address, object[] values) {
        System.IO.MemoryStream stream = new System.IO.MemoryStream();
        
        // Write address
        WriteOSCString(stream, address);
        
        // Write type tag
        string typeTag = ",";
        foreach (object val in values) {
            if (val is int) typeTag += "i";
            else if (val is float) typeTag += "f";
            else if (val is double) typeTag += "f";
            else if (val is string) typeTag += "s";
            else if (val is bool) typeTag += val.ToString().ToLower() == "true" ? "T" : "F";
        }
        WriteOSCString(stream, typeTag);
        
        // Write arguments
        foreach (object val in values) {
            if (val is int) {
                int intVal = (int)val;
                byte[] bytes = BitConverter.GetBytes(intVal);
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                stream.Write(bytes, 0, 4);
            } else if (val is float) {
                float floatVal = (float)val;
                byte[] bytes = BitConverter.GetBytes(floatVal);
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                stream.Write(bytes, 0, 4);
            } else if (val is double) {
                float floatVal = (float)(double)val;
                byte[] bytes = BitConverter.GetBytes(floatVal);
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                stream.Write(bytes, 0, 4);
            } else if (val is string) {
                WriteOSCString(stream, (string)val);
            }
        }
        
        return stream.ToArray();
    }
    
    private void WriteOSCString(System.IO.MemoryStream stream, string str) {
        byte[] bytes = Encoding.ASCII.GetBytes(str);
        stream.Write(bytes, 0, bytes.Length);
        
        // Pad to 4-byte boundary
        int padding = (4 - (bytes.Length % 4)) % 4;
        for (int i = 0; i < padding; ++i) {
            stream.WriteByte(0);
        }
    }
    
    public void Close() {
        if (udpClient != null) {
            udpClient.Close();
            udpClient = null;
        }
        isConnected = false;
    }
}

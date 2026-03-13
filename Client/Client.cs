using Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client
{
    class Client
    {
        private bool tcpActive;
        private bool udpActive;
        private TcpClient tcpClient;
        private UdpClient udpClient;
        private NetworkStream stream;
        private Thread tcpListenerThread;
        private Thread udpListenerThread;
        private IPEndPoint ipEndPoint;

        public bool TcpConnected
        {
            get { return tcpClient.Connected; }
        }

        public bool UdpConnected
        {
            get { return udpClient.Client.Connected; }
        }

        public delegate void PacketReceivedHandler(object sender, PacketReceivedEventArgs data);
        public event PacketReceivedHandler PacketReceived;

        protected void OnPacketReceived(object sender, PacketReceivedEventArgs data)
        {
            if (PacketReceived != null)
            {
                PacketReceived(this, data);
            }
        }

        public Client()
        {
            tcpClient = new TcpClient();
            udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = 20000;
            tcpListenerThread = new Thread(TcpListener);
            udpListenerThread = new Thread(UdpListener);
        }

        public void Disconnect()
        {
            tcpActive = false;
            udpActive = false;
        }

        // Reads exactly 'count' bytes from the stream into 'buffer', blocking until done.
        // Returns false if the connection was closed before all bytes were read.
        private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0) return false;
                offset += read;
            }
            return true;
        }

        private void TcpListener()
        {
            tcpActive = true;
            byte[] lengthBuffer = new byte[4];

            try
            {
                while (tcpActive && TcpConnected)
                {
                    // Read 4-byte length prefix (blocking).
                    if (!ReadExact(stream, lengthBuffer, 4)) break;

                    int packetLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (packetLength <= 0 || packetLength > 65536) break;

                    // Read exactly packetLength bytes.
                    byte[] packetBuffer = new byte[packetLength];
                    if (!ReadExact(stream, packetBuffer, packetLength)) break;

                    Packet packet = Packet.Populate(Encoding.ASCII.GetString(packetBuffer));
                    PacketReceived(this, new PacketReceivedEventArgs(ProtocolType.Tcp, packet));
                }
            }
            catch (Exception)
            {
                // Connection closed or reset -- exit cleanly.
            }

            tcpClient.Close();
        }

        private void UdpListener()
        {
            udpActive = true;

            while (udpActive)
            {
                if (UdpConnected)
                {
                    try
                    {
                        // Receive returns a fresh array each call -- no shared buffer needed.
                        byte[] receiveBytes = udpClient.Receive(ref ipEndPoint);
                        Packet packet = Packet.Populate(Encoding.ASCII.GetString(receiveBytes));
                        PacketReceived(this, new PacketReceivedEventArgs(ProtocolType.Udp, packet));
                    }
                    catch (SocketException)
                    {
                        // Receive timeout -- loop back and retry.
                        continue;
                    }
                }

                Thread.Sleep(10);
            }

            udpClient.Close();
        }

        public void Connect(IPAddress ipAddress, int tcpPort, int udpPort)
        {
            ipEndPoint = new IPEndPoint(ipAddress, udpPort);
            tcpClient.Connect(ipAddress, tcpPort);
            udpClient.Connect(ipEndPoint);
            stream = tcpClient.GetStream();
            tcpListenerThread.Start();
            udpListenerThread.Start();
        }

        public void Send(ProtocolType protocolType, Packet packet)
        {
            switch (protocolType)
            {
                case ProtocolType.Tcp:
                    // Length-prefix framing: [4-byte length][packet bytes]
                    byte[] data = packet.ToBytes();
                    byte[] lengthPrefix = BitConverter.GetBytes(data.Length);
                    stream.Write(lengthPrefix, 0, 4);
                    stream.Write(data, 0, data.Length);
                    break;
                case ProtocolType.Udp:
                    udpClient.Send(packet.ToBytes(), packet.Length);
                    break;
            }
        }
    }
}

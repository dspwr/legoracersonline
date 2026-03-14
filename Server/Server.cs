using Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Server
{
    class Server
    {
        private bool started;
        private TcpListener tcpServer;
        private UdpClient udpServer;
        private List<ServerParticipant> participants;
        private Thread tcpListenerThread;
        private Thread udpListenerThread;

        // Lock protecting the participants list -- accessed from TCP and UDP threads.
        private readonly object _participantsLock = new object();

        // Signals both listener threads to stop cleanly (avoids Thread.Abort).
        private volatile bool _stopRequested;

        public bool Started
        {
            get { return started; }
        }

        public bool RaceActive { get; set; }

        public List<ServerParticipant> Participants
        {
            get { return participants; }
        }

        public delegate void ServerUpdatedHandler(object sender, ServerUpdatedEventArgs data);
        public event ServerUpdatedHandler ServerUpdated;

        protected void OnServerUpdated(object sender, ServerUpdatedEventArgs data)
        {
            if (ServerUpdated != null)
            {
                ServerUpdated(this, data);
            }
        }

        public Server()
        {
            tcpServer = new TcpListener(new IPEndPoint(IPAddress.Any, 3031));
            udpServer = new UdpClient();
            udpServer.Client.Bind(new IPEndPoint(IPAddress.Any, 3030));

            participants = new List<ServerParticipant>();

            started = false;

            tcpListenerThread = new Thread(TcpListener);
            udpListenerThread = new Thread(UdpListener);
        }

        public void Start()
        {
            tcpServer.Start();

            if (!tcpListenerThread.IsAlive)
            {
                tcpListenerThread.Start();
            }

            if (!udpListenerThread.IsAlive)
            {
                udpListenerThread.Start();
            }

            started = true;
        }

        public void Stop()
        {
            _stopRequested = true;
            started = false;

            // Closing the sockets unblocks any pending Receive/Accept calls in the listener threads.
            tcpServer.Stop();
            udpServer.Close();
        }

        private void TcpListener()
        {
            try
            {
                while (!_stopRequested)
                {
                    if (started)
                    {
                        List<ServerParticipant> snapshot;

                        lock (_participantsLock)
                        {
                            if (tcpServer.Pending())
                            {
                                participants.Add(new ServerParticipant(tcpServer.AcceptTcpClient()));
                            }

                            snapshot = participants.ToList();
                        }

                        foreach (ServerParticipant participant in snapshot)
                        {
                            if (participant.RemoveFromServer)
                                continue;

                            NetworkStream stream = participant.Client.GetStream();

                            // Accumulate available bytes into the participant's pending buffer.
                            if (stream.DataAvailable)
                            {
                                byte[] temp = new byte[4096];
                                int bytesRead = stream.Read(temp, 0, temp.Length);

                                if (bytesRead > 0)
                                {
                                    int oldLength = participant.PendingData.Length;
                                    byte[] newBuffer = new byte[oldLength + bytesRead];
                                    Buffer.BlockCopy(participant.PendingData, 0, newBuffer, 0, oldLength);
                                    Buffer.BlockCopy(temp, 0, newBuffer, oldLength, bytesRead);
                                    participant.PendingData = newBuffer;
                                }
                            }

                            // Parse all complete packets from the buffer.
                            // Protocol: [4-byte little-endian length][packet bytes]
                            while (participant.PendingData.Length >= 4)
                            {
                                int packetLength = BitConverter.ToInt32(participant.PendingData, 0);

                                if (packetLength <= 0 || packetLength > 65536)
                                {
                                    // Corrupt framing -- drop this client.
                                    participant.RemoveFromServer = true;
                                    break;
                                }

                                if (participant.PendingData.Length < 4 + packetLength)
                                    break; // Not enough data yet, wait for more.

                                byte[] packetData = new byte[packetLength];
                                Buffer.BlockCopy(participant.PendingData, 4, packetData, 0, packetLength);

                                byte[] remaining = new byte[participant.PendingData.Length - 4 - packetLength];
                                Buffer.BlockCopy(participant.PendingData, 4 + packetLength, remaining, 0, remaining.Length);
                                participant.PendingData = remaining;

                                Packet packet = Packet.Populate(Encoding.ASCII.GetString(packetData));
                                HandleTcpPacket(participant, stream, packet);
                            }
                        }

                        lock (_participantsLock)
                        {
                            participants.RemoveAll(p => p.RemoveFromServer);
                        }
                    }

                    Thread.Sleep(1); // Prevent busy-wait.
                }
            }
            catch (Exception exc)
            {
                if (!_stopRequested)
                    ErrorHandler.ShowDialog("TCP Packet could not be read", "The receiving or reading of a TCP Packet caused an error.", exc);
            }
        }

        private void HandleTcpPacket(ServerParticipant participant, NetworkStream stream, Packet packet)
        {
            if (packet.PacketType == PacketType.Connect)
            {
                List<ServerParticipant> current;
                lock (_participantsLock)
                {
                    current = participants.ToList();
                }

                if (current.Count >= 6)
                {
                    participant.RemoveFromServer = true;
                    Send(stream, new Packet() { PacketType = PacketType.Connect, Content = "FULL" });
                }
                else if (current.Any(p => p.Nickname == packet.Content))
                {
                    participant.RemoveFromServer = true;
                    Send(stream, new Packet() { PacketType = PacketType.Connect, Content = "UNAVAILABLE" });
                }
                else
                {
                    participant.Nickname = packet.Content;

                    Send(stream, new Packet()
                    {
                        PacketType = PacketType.Connect,
                        Content = String.Join("|", current.Select(p => p.Nickname))
                    });

                    SendAll(new Packet()
                    {
                        PacketType = PacketType.Join,
                        Content = packet.Content
                    }, packet.Content);

                    List<ServerParticipant> active;
                    lock (_participantsLock)
                    {
                        active = participants.Where(p => !p.RemoveFromServer).ToList();
                    }
                    OnServerUpdated(this, new ServerUpdatedEventArgs(active));
                }
            }
            else if (packet.PacketType == PacketType.Disconnect)
            {
                if (participant.Nickname == packet.Content)
                {
                    SendAll(new Packet()
                    {
                        PacketType = PacketType.Disconnect,
                        Content = participant.Nickname
                    }, participant.Nickname);

                    participant.RemoveFromServer = true;

                    List<ServerParticipant> active;
                    lock (_participantsLock)
                    {
                        active = participants.Where(p => !p.RemoveFromServer).ToList();
                    }
                    OnServerUpdated(this, new ServerUpdatedEventArgs(active));
                }
            }
            else if (packet.PacketType == PacketType.PowerUp)
            {
                List<ServerParticipant> receivers;
                lock (_participantsLock)
                {
                    receivers = participants
                        .Where(p => !p.RemoveFromServer && p.Nickname != participant.Nickname)
                        .ToList();
                }

                foreach (ServerParticipant receiver in receivers)
                {
                    NetworkStream receiverStream = receiver.Client.GetStream();
                    Send(receiverStream, packet);
                }
            }
        }

        private void UdpListener()
        {
            while (!_stopRequested)
            {
                // Use a local endpoint variable -- the shared ipEndPoint field was a bug:
                // Receive() overwrites it, so a concurrent Send() could reply to the wrong address.
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                try
                {
                    byte[] receiveBytes = udpServer.Receive(ref remoteEndPoint);
                    string data = Encoding.ASCII.GetString(receiveBytes);

                    Packet packet = Packet.Populate(data);

                    if (packet.PacketType == PacketType.Coordinates)
                    {
                        string[] contentParts = packet.Content.Split('|');

                        ServerParticipant foundPlayer;
                        lock (_participantsLock)
                        {
                            foundPlayer = participants.FirstOrDefault(p => p.Nickname == contentParts[0]);
                        }

                        if (foundPlayer != null)
                        {
                            if (!Settings.FreezeAllPlayersEnabled)
                            {
                                foundPlayer.X = float.Parse(contentParts[1], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.Y = float.Parse(contentParts[2], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.Z = float.Parse(contentParts[3], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.SpeedX = float.Parse(contentParts[4], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.SpeedY = float.Parse(contentParts[5], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.SpeedZ = float.Parse(contentParts[6], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.VectorX1 = float.Parse(contentParts[7], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.VectorY1 = float.Parse(contentParts[8], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.VectorZ1 = float.Parse(contentParts[9], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.VectorX2 = float.Parse(contentParts[10], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.VectorY2 = float.Parse(contentParts[11], System.Globalization.CultureInfo.InvariantCulture);
                                foundPlayer.VectorZ2 = float.Parse(contentParts[12], System.Globalization.CultureInfo.InvariantCulture);

                                foundPlayer.LastActivity = DateTime.Now;
                            }

                            string stringToSend = PacketType.Coordinates + "|" + Settings.Serialize();

                            List<ServerParticipant> snapshot;
                            lock (_participantsLock)
                            {
                                snapshot = participants.ToList();
                            }

                            foreach (ServerParticipant player in snapshot)
                            {
                                stringToSend += "|" + player.Nickname + ";" + player.X.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.Z.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.SpeedX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.SpeedY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.SpeedZ.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.VectorX1.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.VectorY1.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.VectorZ1.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.VectorX2.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.VectorY2.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.VectorZ2.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + player.PowerUpType + ";" + player.PowerUpWhiteBricks;
                            }

                            byte[] sendBytes = Encoding.ASCII.GetBytes(stringToSend);
                            udpServer.Send(sendBytes, sendBytes.Length, remoteEndPoint);
                        }
                    }
                }
                catch (SocketException exc)
                {
                    if (_stopRequested)
                        break; // Socket was intentionally closed by Stop().

                    if (exc.ErrorCode == 10054)
                    {
                        // Participant closed the client -- no action required.
                    }
                }
                catch (Exception exc)
                {
                    if (!_stopRequested)
                        Console.WriteLine(exc.Message);
                }
            }
        }

        // Sends a length-prefixed TCP packet.
        private void Send(NetworkStream stream, Packet packet)
        {
            byte[] data = packet.ToBytes();
            byte[] lengthPrefix = BitConverter.GetBytes(data.Length);
            stream.Write(lengthPrefix, 0, 4);
            stream.Write(data, 0, data.Length);
        }

        public void SendAll(Packet packet)
        {
            List<ServerParticipant> snapshot;
            lock (_participantsLock)
            {
                snapshot = participants.ToList();
            }

            foreach (ServerParticipant receiver in snapshot)
            {
                NetworkStream receiverStream = receiver.Client.GetStream();
                Send(receiverStream, packet);
            }
        }

        public void SendAll(Packet packet, string except)
        {
            List<ServerParticipant> snapshot;
            lock (_participantsLock)
            {
                snapshot = participants.Where(p => p.Nickname != except).ToList();
            }

            foreach (ServerParticipant receiver in snapshot)
            {
                NetworkStream receiverStream = receiver.Client.GetStream();
                Send(receiverStream, packet);
            }
        }

        public void DisconnectParticipant(ServerParticipant participant)
        {
            SendAll(new Packet()
            {
                PacketType = PacketType.Disconnect,
                Content = participant.Nickname
            });

            participant.RemoveFromServer = true;

            List<ServerParticipant> active;
            lock (_participantsLock)
            {
                active = participants.Where(p => !p.RemoveFromServer).ToList();
            }
            OnServerUpdated(this, new ServerUpdatedEventArgs(active));
        }

        public void DisconnectAll()
        {
            List<ServerParticipant> snapshot;
            lock (_participantsLock)
            {
                snapshot = participants.ToList();
            }

            foreach (ServerParticipant participant in snapshot)
            {
                SendAll(new Packet()
                {
                    PacketType = PacketType.Disconnect,
                    Content = participant.Nickname
                });
            }

            lock (_participantsLock)
            {
                participants.ForEach(p => { p.RemoveFromServer = true; });
            }
        }
    }
}

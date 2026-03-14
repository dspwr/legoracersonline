<<<<<<< HEAD
﻿using Library;
using System;
=======
using Library;
>>>>>>> e81dd5f93ca141b4c2f375355bf8e029364bbeef
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

        public bool Started
        {
            get { return started; }
        }

        public List<ServerParticipant> Participants
        {
            get
            {
                return participants;
            }
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

            ipEndPoint = new IPEndPoint(IPAddress.Any, 3030);

            udpServer.Client.Bind(ipEndPoint);

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
            if (tcpListenerThread.IsAlive)
            {
                tcpListenerThread.Abort();
            }

            if (udpListenerThread.IsAlive)
            {
                udpListenerThread.Abort();
            }

            tcpServer.Stop();
            udpServer.Close();

            started = false;
        }

        private void TcpListener()
        {
            try
            {
                while (true)
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
                                int i;

                                while (stream.DataAvailable && (i = stream.Read(bytes, 0, bytes.Length)) != 0)
                                {
                                    data = Encoding.ASCII.GetString(bytes, 0, i);

                    Thread.Sleep(1); // Prevent busy-wait.
                }
            }
            catch (Exception exc)
            {
                if (exc.GetType() != typeof(ThreadAbortException))
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

                                            Send(stream, new Packet()
                                            {
                                                PacketType = PacketType.Connect,
                                                Content = "FULL"
                                            });
                                        }
                                        else if (participants.Where(p => p.Nickname == packet.Content).Count() > 0)
                                        {
                                            participant.RemoveFromServer = true;

                                            Send(stream, new Packet()
                                                {
                                                    PacketType = PacketType.Connect,
                                                    Content = "UNAVAILABLE"
                                                });
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

                                            Send(receiverStream, packet);
                                        }
                                    }
                                }
                            }
                        }

                        participants.RemoveAll(p => p.RemoveFromServer);
                    }
                }
            }
            catch (Exception exc)
            {
                if (exc.GetType() != typeof(ThreadAbortException))
                    ErrorHandler.ShowDialog("TCP Packet could not be read", "The receiving or reading of a TCP Packet caused an error.", exc);
            }
        }

        private void UdpListener()
        {
            string data = "";

            while (true)
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
                    if (exc.ErrorCode == 10054)
                    {
                        // Participant closed the client -- no action required.
                    }
                }
                catch (Exception exc)
                {
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

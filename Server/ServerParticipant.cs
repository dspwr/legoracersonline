using Library;
using System;
using System.Net.Sockets;

namespace Server
{
    class ServerParticipant : Participant
    {
        public TcpClient Client { get; private set; }
        public bool RemoveFromServer { get; set; }

        /// <summary>
        /// Accumulation buffer for partial TCP packets (length-prefix framing).
        /// </summary>
        public byte[] PendingData { get; set; } = new byte[0];

        /// <summary>
        /// Gets or sets the DateTime the Participant contacted the Server the last time.
        /// </summary>
        public DateTime LastActivity { get; set; }

        public ServerParticipant(TcpClient client)
        {
            this.Client = client;
        }
    }
}
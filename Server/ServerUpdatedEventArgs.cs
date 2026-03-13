using System;
using System.Collections.Generic;

namespace Server
{
    class ServerUpdatedEventArgs : EventArgs
    {
        public List<ServerParticipant> Participants { get; internal set; }

        public ServerUpdatedEventArgs(List<ServerParticipant> participants)
        {
            Participants = participants;
        }
    }
}
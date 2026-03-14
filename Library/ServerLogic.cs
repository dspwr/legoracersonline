using System;

namespace Library
{
    public interface IServerPacketBroadcaster
    {
        void SendToAll(Packet packet);
    }

    /// <summary>
    /// Pure-logic helpers extracted from Server for unit-testability.
    /// </summary>
    public static class ServerLogic
    {
        /// <summary>
        /// Returns true when a new Connect request should be rejected because a race is already in progress.
        /// </summary>
        public static bool ShouldRejectNewConnection(bool raceActive)
        {
            return raceActive;
        }

        /// <summary>
        /// Builds the packet sent to a client whose connection is rejected due to an active race.
        /// </summary>
        public static Packet BuildConnectionRejectionPacket()
        {
            return new Packet() { PacketType = PacketType.ConnectionRejected, Content = "GameInProgress" };
        }

        /// <summary>
        /// Builds the Race packet broadcast to all clients when a race starts.
        /// Content format: <c>TrackId|Laps|ParticipantsCount</c>.
        /// </summary>
        public static Packet BuildRacePacket(int trackId, int laps, int participantsCount)
        {
            return new Packet()
            {
                PacketType = PacketType.Race,
                Content = trackId + "|" + laps + "|" + participantsCount
            };
        }

        /// <summary>
        /// Moves server state to InGame and broadcasts a race start packet.
        /// </summary>
        public static bool StartRace(
            int trackId,
            int laps,
            int participantsCount,
            IServerPacketBroadcaster broadcaster)
        {
            if (broadcaster == null)
                throw new ArgumentNullException("broadcaster");

            broadcaster.SendToAll(BuildRacePacket(trackId, laps, participantsCount));
            return true;
        }
    }
}

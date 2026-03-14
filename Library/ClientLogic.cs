using System;
using System.Collections.Generic;
using System.Linq;

namespace Library
{
    /// <summary>
    /// Pure-logic helpers extracted from ClientForm for unit-testability.
    /// </summary>
    public static class ClientLogic
    {
        public interface IGameRaceStarter
        {
            void SetupAndStartRace(int trackId, int laps, int networkOpponents);
        }

        /// <summary>
        /// Returns the number of remote (non-local) players — the slot count that
        /// would be occupied by AI/network opponents in the game.
        /// </summary>
        public static int CalculateAIDriversAmount(IEnumerable<Participant> participants, string localNickname)
        {
            return participants.Count(p => p.Nickname != localNickname);
        }

        /// <summary>
        /// Parses a PowerUp packet content string with the format
        /// <c>Nickname|BrickType|WhiteBricks</c> where BrickType and WhiteBricks
        /// are integers.
        /// </summary>
        /// <exception cref="FormatException">Thrown when the content does not have
        /// exactly three pipe-separated tokens or the numeric fields are invalid.</exception>
        public static PowerUpInfo ParsePowerUpContent(string content)
        {
            if (content == null) throw new ArgumentNullException("content");
            string[] parts = content.Split('|');
            if (parts.Length != 3)
                throw new FormatException("PowerUp content must have exactly 3 pipe-separated fields.");

            return new PowerUpInfo(
                nickname:    parts[0],
                brickType:   int.Parse(parts[1]),
                whiteBricks: int.Parse(parts[2]));
        }
        /// <summary>
        /// Parses a Race packet content string with the format
        /// <c>TrackId|Laps|ParticipantsCount</c> where all values are integers.
        /// </summary>
        /// <exception cref="FormatException">Thrown when the content does not have
        /// exactly three pipe-separated tokens or the numeric fields are invalid.</exception>
        public static RaceInfo ParseRaceContent(string content)
        {
            if (content == null) throw new ArgumentNullException("content");
            string[] parts = content.Split('|');
            if (parts.Length != 3)
                throw new FormatException("Race content must have exactly 3 pipe-separated fields.");

            return new RaceInfo(
                trackId: int.Parse(parts[0]),
                laps: int.Parse(parts[1]),
                participantsCount: int.Parse(parts[2]));
        }

        /// <summary>
        /// Applies a Race packet to a game race starter implementation.
        /// </summary>
        public static void ApplyRacePacket(string content, IGameRaceStarter raceStarter)
        {
            if (raceStarter == null) throw new ArgumentNullException("raceStarter");

            RaceInfo raceInfo = ParseRaceContent(content);
            int networkOpponents = raceInfo.ParticipantsCount - 1;
            if (networkOpponents < 0)
            {
                networkOpponents = 0;
            }

            raceStarter.SetupAndStartRace(raceInfo.TrackId, raceInfo.Laps, networkOpponents);
        }
    }

    public struct RaceInfo
    {
        public int TrackId { get; private set; }
        public int Laps { get; private set; }
        public int ParticipantsCount { get; private set; }

        public RaceInfo(int trackId, int laps, int participantsCount) : this()
        {
            TrackId = trackId;
            Laps = laps;
            ParticipantsCount = participantsCount;
        }
    }

    public struct PowerUpInfo
    {
        public string Nickname   { get; private set; }
        public int    BrickType  { get; private set; }
        public int    WhiteBricks { get; private set; }

        public PowerUpInfo(string nickname, int brickType, int whiteBricks) : this()
        {
            Nickname    = nickname;
            BrickType   = brickType;
            WhiteBricks = whiteBricks;
        }
    }
}

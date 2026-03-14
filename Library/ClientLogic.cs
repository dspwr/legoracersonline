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

using System;
using System.Collections.Generic;
using System.Linq;

namespace Library
{
    /// <summary>
    /// Encapsulates the zombie-player cleanup logic so it can be unit-tested
    /// without a running server or UI.
    /// </summary>
    public static class ZombieCleanup
    {
        /// <summary>
        /// Identifies participants that have been silent longer than
        /// <paramref name="timeout"/> and invokes <paramref name="onDisconnect"/>
        /// for each of them.  Does nothing when <paramref name="raceActive"/> is
        /// <c>false</c> (lobby state).
        /// </summary>
        /// <param name="participants">Live participant list.</param>
        /// <param name="raceActive">Whether a race is currently in progress.</param>
        /// <param name="timeout">Inactivity threshold after which a player is considered a zombie.</param>
        /// <param name="onDisconnect">Action called for every zombie (e.g. DisconnectParticipant).</param>
        /// <returns>The list of zombie participants that were passed to <paramref name="onDisconnect"/>.</returns>
        public static IReadOnlyList<Participant> PerformCleanup(
            IEnumerable<Participant> participants,
            bool raceActive,
            TimeSpan timeout,
            Action<Participant> onDisconnect)
        {
            if (!raceActive)
                return new List<Participant>();

            DateTime now = DateTime.Now;
            List<Participant> zombies = participants
                .Where(p => (now - p.LastActivity) > timeout)
                .ToList();

            foreach (Participant zombie in zombies)
                onDisconnect(zombie);

            return zombies;
        }
    }
}

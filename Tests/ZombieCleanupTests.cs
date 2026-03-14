using System;
using System.Collections.Generic;
using NUnit.Framework;
using Library;

namespace Tests
{
    /// <summary>
    /// Tests for zombie-player cleanup logic (Task 3).
    /// </summary>
    [TestFixture]
    public class ZombieCleanupTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        // ── Helper ────────────────────────────────────────────────────────────

        private static Participant MakeParticipant(string nickname, double secondsAgo)
        {
            return new Participant
            {
                Nickname     = nickname,
                LastActivity = DateTime.Now.AddSeconds(-secondsAgo)
            };
        }

        // ── raceActive = true ─────────────────────────────────────────────────

        [Test]
        public void PerformCleanup_RaceActive_RemovesOnlyTimedOutPlayer()
        {
            // Arrange
            var active = MakeParticipant("Alice", 2);   // 2 s ago  — OK
            var zombie = MakeParticipant("Bob",   45);  // 45 s ago — timed out
            var participants = new List<Participant> { active, zombie };

            var disconnected = new List<Participant>();

            // Act
            ZombieCleanup.PerformCleanup(
                participants,
                raceActive:   true,
                timeout:      Timeout,
                onDisconnect: p => disconnected.Add(p));

            // Assert
            Assert.AreEqual(1, disconnected.Count,               "exactly one zombie");
            Assert.AreEqual("Bob", disconnected[0].Nickname,     "correct zombie disconnected");
        }

        [Test]
        public void PerformCleanup_RaceActive_ActivePlayerIsNotDisconnected()
        {
            var active = MakeParticipant("Alice", 2);
            var participants = new List<Participant> { active };

            var disconnected = new List<Participant>();
            ZombieCleanup.PerformCleanup(participants, true, Timeout, p => disconnected.Add(p));

            Assert.AreEqual(0, disconnected.Count, "active player must not be removed");
        }

        [Test]
        public void PerformCleanup_RaceActive_AllZombies_RemovesAll()
        {
            var p1 = MakeParticipant("X", 60);
            var p2 = MakeParticipant("Y", 90);
            var participants = new List<Participant> { p1, p2 };

            var disconnected = new List<Participant>();
            ZombieCleanup.PerformCleanup(participants, true, Timeout, p => disconnected.Add(p));

            Assert.AreEqual(2, disconnected.Count);
        }

        // ── raceActive = false (lobby) ─────────────────────────────────────────

        [Test]
        public void PerformCleanup_RaceNotActive_DoesNotRemoveAnyPlayer()
        {
            // Arrange — player well over timeout, but race is not active (lobby).
            var zombie = MakeParticipant("Bob", 45);
            var participants = new List<Participant> { zombie };

            var disconnected = new List<Participant>();

            // Act
            ZombieCleanup.PerformCleanup(
                participants,
                raceActive:   false,
                timeout:      Timeout,
                onDisconnect: p => disconnected.Add(p));

            // Assert
            Assert.AreEqual(0, disconnected.Count,
                "cleanup must not kick players during lobby (raceActive=false)");
        }

        [Test]
        public void PerformCleanup_RaceNotActive_ReturnsEmptyList()
        {
            var zombie = MakeParticipant("Bob", 45);
            var result = ZombieCleanup.PerformCleanup(
                new List<Participant> { zombie },
                raceActive:   false,
                timeout:      Timeout,
                onDisconnect: _ => { });

            Assert.AreEqual(0, result.Count);
        }

        // ── edge cases ────────────────────────────────────────────────────────

        [Test]
        public void PerformCleanup_EmptyList_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                ZombieCleanup.PerformCleanup(
                    new List<Participant>(),
                    raceActive:   true,
                    timeout:      Timeout,
                    onDisconnect: _ => { }));
        }

        [Test]
        public void PerformCleanup_PlayerJustBelowTimeout_IsNotRemoved()
        {
            // A player whose inactivity is slightly below the timeout threshold
            // should NOT be removed (strict greater-than comparison).
            var borderline = new Participant
            {
                Nickname     = "Edge",
                // 500 ms below the threshold ensures the comparison sees < timeout.
                LastActivity = DateTime.Now - Timeout + TimeSpan.FromMilliseconds(500)
            };

            var disconnected = new List<Participant>();
            ZombieCleanup.PerformCleanup(
                new List<Participant> { borderline },
                raceActive:   true,
                timeout:      Timeout,
                onDisconnect: p => disconnected.Add(p));

            Assert.AreEqual(0, disconnected.Count,
                "player just below timeout threshold must not be evicted");
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Library;

namespace Tests
{
    /// <summary>
    /// Tests for the race-start synchronization mechanism (Stage 6/7).
    /// All tests run without a live game process — server network I/O is verified
    /// via the pure-logic ServerLogic helpers extracted for testability.
    /// </summary>
    [TestFixture]
    public class RaceStartTests
    {
        private sealed class FakeBroadcaster : IServerPacketBroadcaster
        {
            public List<Packet> SentPackets { get; private set; }

            public FakeBroadcaster()
            {
                SentPackets = new List<Packet>();
            }

            public void SendToAll(Packet packet)
            {
                SentPackets.Add(packet);
            }
        }

        private sealed class FakeRaceStarter : ClientLogic.IGameRaceStarter
        {
            public int Calls { get; private set; }
            public int LastTrackId { get; private set; }
            public int LastLaps { get; private set; }
            public int LastNetworkOpponents { get; private set; }

            public void SetupAndStartRace(int trackId, int laps, int networkOpponents)
            {
                Calls++;
                LastTrackId = trackId;
                LastLaps = laps;
                LastNetworkOpponents = networkOpponents;
            }
        }

        // Test 1: Server start race transition + broadcast payload.

        [Test]
        public void StartRace_SetsInGameAndBroadcastsRacePacketToAll()
        {
            FakeBroadcaster broadcaster = new FakeBroadcaster();
            bool inGame = false;

            inGame = ServerLogic.StartRace(
                trackId: 5,
                laps: 3,
                participantsCount: 4,
                broadcaster: broadcaster);

            Assert.IsTrue(inGame, "server should transition to InGame");
            Assert.AreEqual(1, broadcaster.SentPackets.Count, "exactly one race packet should be broadcast");
            Assert.AreEqual(PacketType.Race, broadcaster.SentPackets[0].PacketType);
            Assert.AreEqual("5|3|4", broadcaster.SentPackets[0].Content);
        }

        // Test 2: Client maps race packet to game API call.

        [Test]
        public void ApplyRacePacket_MapsTrackAndPlayersToGameApi()
        {
            FakeRaceStarter raceStarter = new FakeRaceStarter();

            ClientLogic.ApplyRacePacket("5|3|3", raceStarter);

            Assert.AreEqual(1, raceStarter.Calls);
            Assert.AreEqual(5, raceStarter.LastTrackId);
            Assert.AreEqual(3, raceStarter.LastLaps);
            Assert.AreEqual(2, raceStarter.LastNetworkOpponents, "network opponents = participants - local player");
        }

        [Test]
        public void ParseRaceContent_TooFewFields_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                ClientLogic.ParseRaceContent("5"));
        }

        [Test]
        public void ParseRaceContent_NullContent_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ClientLogic.ParseRaceContent(null));
        }

            // Test 3: Security - join/connect rejection during InGame.

        [Test]
            public void ShouldRejectNewConnection_WhenInGame_ReturnsTrue()
        {
            Assert.IsTrue(ServerLogic.ShouldRejectNewConnection(raceActive: true),
                "new connections must be rejected when a race is active");
        }

        [Test]
        public void ShouldRejectNewConnection_WhenLobby_ReturnsFalse()
        {
            Assert.IsFalse(ServerLogic.ShouldRejectNewConnection(raceActive: false),
                "new connections must be allowed when in lobby");
        }

        [Test]
        public void BuildConnectionRejectionPacket_HasCorrectTypeAndContent()
        {
            // Arrange + Act
            Packet rejection = ServerLogic.BuildConnectionRejectionPacket();

            // Assert
            Assert.AreEqual(PacketType.ConnectionRejected, rejection.PacketType,
                "rejection packet must be PacketType.ConnectionRejected");
            Assert.AreEqual("GameInProgress", rejection.Content,
                "rejection content must be 'GameInProgress'");
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Library;

namespace Tests
{
    /// <summary>
    /// Tests for client-side logic helpers (Task 2).
    /// </summary>
    [TestFixture]
    public class ClientLogicTests
    {
        // ── CalculateAIDriversAmount ──────────────────────────────────────────

        [Test]
        public void CalculateAIDriversAmount_WithFourPlayersAndOneLocal_ReturnsThree()
        {
            // Arrange
            var participants = new List<Participant>
            {
                new Participant { Nickname = "LocalPlayer" },
                new Participant { Nickname = "Player2"     },
                new Participant { Nickname = "Player3"     },
                new Participant { Nickname = "Player4"     },
            };

            // Act
            int result = ClientLogic.CalculateAIDriversAmount(participants, "LocalPlayer");

            // Assert
            Assert.AreEqual(3, result);
        }

        [Test]
        public void CalculateAIDriversAmount_AllRemote_ReturnsAll()
        {
            var participants = new List<Participant>
            {
                new Participant { Nickname = "A" },
                new Participant { Nickname = "B" },
            };
            Assert.AreEqual(2, ClientLogic.CalculateAIDriversAmount(participants, "X"));
        }

        [Test]
        public void CalculateAIDriversAmount_EmptyList_ReturnsZero()
        {
            Assert.AreEqual(0, ClientLogic.CalculateAIDriversAmount(new List<Participant>(), "X"));
        }

        [Test]
        public void CalculateAIDriversAmount_OnlyLocalPlayer_ReturnsZero()
        {
            var participants = new List<Participant>
            {
                new Participant { Nickname = "LocalPlayer" }
            };
            Assert.AreEqual(0, ClientLogic.CalculateAIDriversAmount(participants, "LocalPlayer"));
        }

        // ── ParsePowerUpContent ───────────────────────────────────────────────

        [Test]
        public void ParsePowerUpContent_ValidInput_ReturnsCorrectFields()
        {
            // Format: Nickname|BrickType|WhiteBricks
            // BrickType 1 == Red, WhiteBricks 2
            PowerUpInfo info = ClientLogic.ParsePowerUpContent("Player2|1|2");

            Assert.AreEqual("Player2", info.Nickname);
            Assert.AreEqual(1,         info.BrickType);
            Assert.AreEqual(2,         info.WhiteBricks);
        }

        [Test]
        public void ParsePowerUpContent_ZeroWhiteBricks_Parses()
        {
            PowerUpInfo info = ClientLogic.ParsePowerUpContent("Alpha|3|0");
            Assert.AreEqual("Alpha", info.Nickname);
            Assert.AreEqual(3,       info.BrickType);
            Assert.AreEqual(0,       info.WhiteBricks);
        }

        [Test]
        public void ParsePowerUpContent_TooFewFields_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                ClientLogic.ParsePowerUpContent("Player2|1"));
        }

        [Test]
        public void ParsePowerUpContent_NullContent_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ClientLogic.ParsePowerUpContent(null));
        }
    }
}

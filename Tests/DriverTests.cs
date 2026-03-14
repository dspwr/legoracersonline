using NUnit.Framework;
using LEGORacersAPI;

namespace Tests
{
    /// <summary>
    /// Tests for Driver state management (Task 1).
    /// </summary>
    [TestFixture]
    public class DriverTests
    {
        // A minimal testable subclass: overrides StartPollingThread so no background
        // thread is started (which would try to access a null MemoryManager).
        private class TestDriver : Driver
        {
            protected override void StartPollingThread() { /* no-op for testing */ }
            public void InitializeForTest() { Initialize(); }
        }

        [SetUp]
        public void ResetCounter()
        {
            // Each test starts from a clean counter state.
            Driver.ResetDriverCounter();
        }

        [Test]
        public void ResetDriverCounter_SetsCurrentDriverNumberToZero()
        {
            // Arrange — dirty the counter first.
            var d = new TestDriver();
            d.InitializeForTest(); // counter becomes 1

            // Act
            Driver.ResetDriverCounter();

            // Assert
            Assert.AreEqual(0, Driver.CurrentDriverNumber);
        }

        [Test]
        public void Initialize_IncrementsCounterStartingFromZero()
        {
            // Arrange — counter is already 0 from SetUp.

            // Act — initialise three drivers sequentially.
            var d1 = new TestDriver();
            d1.InitializeForTest();
            Assert.AreEqual(1, Driver.CurrentDriverNumber, "after first  Init");

            var d2 = new TestDriver();
            d2.InitializeForTest();
            Assert.AreEqual(2, Driver.CurrentDriverNumber, "after second Init");

            var d3 = new TestDriver();
            d3.InitializeForTest();
            Assert.AreEqual(3, Driver.CurrentDriverNumber, "after third  Init");
        }

        [Test]
        public void ResetDriverCounter_AfterMultipleInits_ResetsToZero()
        {
            var d1 = new TestDriver();
            var d2 = new TestDriver();
            d1.InitializeForTest();
            d2.InitializeForTest();

            Driver.ResetDriverCounter();

            Assert.AreEqual(0, Driver.CurrentDriverNumber);

            // A new init should restart from 0.
            var d3 = new TestDriver();
            d3.InitializeForTest();
            Assert.AreEqual(1, Driver.CurrentDriverNumber);
        }
    }
}

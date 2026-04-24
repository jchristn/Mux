namespace Test.Xunit.Commands
{
    using global::Xunit;
    using Mux.Cli.Commands;

    /// <summary>
    /// Unit tests for queued interactive prompt management.
    /// </summary>
    public class QueuedMessageManagerTests
    {
        /// <summary>
        /// Verifies that queued prompts dequeue in FIFO order.
        /// </summary>
        [Fact]
        public void Enqueue_Dequeue_IsFifo()
        {
            QueuedMessageManager manager = new QueuedMessageManager();
            manager.Enqueue("first");
            manager.Enqueue("second");

            Assert.True(manager.TryDequeue(out QueuedMessageEntry first));
            Assert.True(manager.TryDequeue(out QueuedMessageEntry second));

            Assert.Equal("first", first.Text);
            Assert.Equal("second", second.Text);
        }

        /// <summary>
        /// Verifies that taking the last queued prompt removes the newest entry.
        /// </summary>
        [Fact]
        public void TryTakeLast_RemovesNewestEntry()
        {
            QueuedMessageManager manager = new QueuedMessageManager();
            manager.Enqueue("first");
            manager.Enqueue("second");

            Assert.True(manager.TryTakeLast(out QueuedMessageEntry last));
            Assert.Equal("second", last.Text);
            Assert.Single(manager.Snapshot());
            Assert.Equal("first", manager.Snapshot()[0].Text);
        }

        /// <summary>
        /// Verifies that replacing the last queued entry preserves queue size and updates the text.
        /// </summary>
        [Fact]
        public void TryReplaceLast_SwapsNewestEntryText()
        {
            QueuedMessageManager manager = new QueuedMessageManager();
            manager.Enqueue("first");
            manager.Enqueue("second");

            Assert.True(manager.TryReplaceLast("draft", out QueuedMessageEntry previous));

            Assert.Equal("second", previous.Text);
            Assert.Equal(2, manager.Count);
            Assert.Equal("draft", manager.Snapshot()[1].Text);
        }

        /// <summary>
        /// Verifies that clearing removes all queued entries.
        /// </summary>
        [Fact]
        public void Clear_RemovesAllEntries()
        {
            QueuedMessageManager manager = new QueuedMessageManager();
            manager.Enqueue("first");
            manager.Enqueue("second");

            manager.Clear();

            Assert.Equal(0, manager.Count);
            Assert.Empty(manager.Snapshot());
        }
    }
}

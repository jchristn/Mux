namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Maintains FIFO queued prompts for the interactive session.
    /// </summary>
    public class QueuedMessageManager
    {
        #region Private-Members

        private readonly List<QueuedMessageEntry> _Entries = new List<QueuedMessageEntry>();
        private int _NextSequenceNumber = 1;

        #endregion

        #region Public-Members

        /// <summary>
        /// Number of queued prompts.
        /// </summary>
        public int Count => _Entries.Count;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Adds a new prompt to the end of the queue.
        /// </summary>
        /// <param name="text">The prompt text to queue.</param>
        /// <returns>The created queue entry.</returns>
        public QueuedMessageEntry Enqueue(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            QueuedMessageEntry entry = new QueuedMessageEntry
            {
                SequenceNumber = _NextSequenceNumber++,
                EnqueuedAtUtc = DateTime.UtcNow,
                Text = text
            };

            _Entries.Add(entry);
            return entry;
        }

        /// <summary>
        /// Removes and returns the next queued prompt in FIFO order.
        /// </summary>
        /// <param name="entry">The dequeued entry when present.</param>
        /// <returns>True if an entry was dequeued.</returns>
        public bool TryDequeue(out QueuedMessageEntry entry)
        {
            if (_Entries.Count > 0)
            {
                entry = _Entries[0];
                _Entries.RemoveAt(0);
                return true;
            }

            entry = new QueuedMessageEntry();
            return false;
        }

        /// <summary>
        /// Removes and returns the newest queued prompt.
        /// </summary>
        /// <param name="entry">The removed entry when present.</param>
        /// <returns>True if an entry was removed.</returns>
        public bool TryTakeLast(out QueuedMessageEntry entry)
        {
            if (_Entries.Count > 0)
            {
                int index = _Entries.Count - 1;
                entry = _Entries[index];
                _Entries.RemoveAt(index);
                return true;
            }

            entry = new QueuedMessageEntry();
            return false;
        }

        /// <summary>
        /// Replaces the newest queued prompt text and returns the previous entry.
        /// </summary>
        /// <param name="replacementText">The new text to store in the newest entry.</param>
        /// <param name="previousEntry">The previous entry contents before replacement.</param>
        /// <returns>True if the newest entry was replaced.</returns>
        public bool TryReplaceLast(string replacementText, out QueuedMessageEntry previousEntry)
        {
            if (replacementText == null) throw new ArgumentNullException(nameof(replacementText));

            if (_Entries.Count > 0)
            {
                int index = _Entries.Count - 1;
                previousEntry = new QueuedMessageEntry
                {
                    Id = _Entries[index].Id,
                    SequenceNumber = _Entries[index].SequenceNumber,
                    EnqueuedAtUtc = _Entries[index].EnqueuedAtUtc,
                    Text = _Entries[index].Text
                };

                _Entries[index].Text = replacementText;
                return true;
            }

            previousEntry = new QueuedMessageEntry();
            return false;
        }

        /// <summary>
        /// Returns a copy of the current queued entries in execution order.
        /// </summary>
        /// <returns>A list copy of queued entries.</returns>
        public List<QueuedMessageEntry> Snapshot()
        {
            List<QueuedMessageEntry> result = new List<QueuedMessageEntry>();
            foreach (QueuedMessageEntry entry in _Entries)
            {
                result.Add(new QueuedMessageEntry
                {
                    Id = entry.Id,
                    SequenceNumber = entry.SequenceNumber,
                    EnqueuedAtUtc = entry.EnqueuedAtUtc,
                    Text = entry.Text
                });
            }

            return result;
        }

        /// <summary>
        /// Removes all queued prompts.
        /// </summary>
        public void Clear()
        {
            _Entries.Clear();
        }

        #endregion
    }
}

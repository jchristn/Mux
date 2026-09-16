namespace Mux.Core.Runs
{
    using System.Collections.Generic;
    using System.Threading.Channels;

    /// <summary>
    /// A single subscriber's view of a <see cref="RunHandle"/>'s event stream: a point-in-time replay of the
    /// canonical envelope frames already emitted (so a late subscriber catches up), followed by a live channel
    /// of subsequent frames. The replay and the channel are captured atomically when the subscription is
    /// created, so no frame is missed or duplicated across the boundary. Each frame is a serialized canonical
    /// event envelope (identical to the <c>mux print --output-format jsonl</c> contract). Dispose via
    /// <see cref="RunHandle.Unsubscribe"/> when done.
    /// </summary>
    public sealed class RunSubscription
    {
        #region Public-Members

        /// <summary>
        /// The serialized envelope frames already emitted at the moment of subscription, in order. Send these
        /// before reading <see cref="Reader"/> to observe the full stream from the beginning.
        /// </summary>
        public IReadOnlyList<string> Replay { get; }

        /// <summary>
        /// The live reader for frames emitted after the subscription was created. Completes when the run ends.
        /// </summary>
        public ChannelReader<string> Reader { get; }

        #endregion

        #region Internal-Members

        internal Channel<string> Channel { get; }

        #endregion

        #region Constructors-and-Factories

        internal RunSubscription(IReadOnlyList<string> replay, Channel<string> channel)
        {
            Replay = replay;
            Channel = channel;
            Reader = channel.Reader;
        }

        #endregion
    }
}

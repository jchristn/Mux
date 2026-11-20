namespace Mux.Core.Cloud
{
    using System.Threading.Channels;

    /// <summary>
    /// The cloud-to-machine side of one relayed WebSocket channel: queued text frames and the close the cloud sent.
    /// <para>Thread safety: safe for one writer (the tunnel receive loop) and one reader (the forwarder).</para>
    /// </summary>
    public sealed class CloudWsChannel
    {
        #region Private-Members

        private readonly Channel<string> _Inbound = Channel.CreateBounded<string>(new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
        private volatile int _CloseCode = 1000;
        private volatile string _CloseReason = string.Empty;

        #endregion

        #region Public-Members

        /// <summary>Channel identifier.</summary>
        public string Id { get; }

        /// <summary>Frames from the cloud client, in order. Completes when the cloud closes the channel.</summary>
        public ChannelReader<string> Reader => _Inbound.Reader;

        /// <summary>Close code the cloud sent (default 1000).</summary>
        public int CloseCode => _CloseCode;

        /// <summary>Close reason the cloud sent.</summary>
        public string CloseReason => _CloseReason;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="id">Channel identifier.</param>
        public CloudWsChannel(string id)
        {
            Id = id ?? string.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>Queue a frame from the cloud.</summary>
        /// <param name="text">Frame text.</param>
        /// <returns>True when queued.</returns>
        public bool Enqueue(string text)
        {
            return _Inbound.Writer.TryWrite(text ?? string.Empty);
        }

        /// <summary>Record the cloud's close and complete the queue.</summary>
        /// <param name="code">Close code.</param>
        /// <param name="reason">Close reason.</param>
        public void Close(int code, string reason)
        {
            _CloseCode = code < 1000 || code > 4999 ? 1000 : code;
            _CloseReason = reason ?? string.Empty;
            _Inbound.Writer.TryComplete();
        }

        #endregion
    }
}

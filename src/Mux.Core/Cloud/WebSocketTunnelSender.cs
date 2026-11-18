namespace Mux.Core.Cloud
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using MuxCloud.Protocol;

    /// <summary>
    /// <see cref="ICloudTunnelSender"/> over a <see cref="WebSocket"/>, serialized by a single send lock.
    /// <para>Thread safety: safe to call concurrently.</para>
    /// </summary>
    public sealed class WebSocketTunnelSender : ICloudTunnelSender, IDisposable
    {
        #region Private-Members

        private readonly WebSocket _Socket;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="socket">Open socket.</param>
        /// <exception cref="ArgumentNullException">socket is null.</exception>
        public WebSocketTunnelSender(WebSocket socket)
        {
            _Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> SendAsync(TunnelEnvelope envelope, CancellationToken token)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (_Disposed || _Socket.State != WebSocketState.Open) return false;
            byte[] bytes = Encoding.UTF8.GetBytes(TunnelProtocol.Serialize(envelope));
            try
            {
                await _Lock.WaitAsync(token).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            try
            {
                if (_Socket.State != WebSocketState.Open) return false;
                await _Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                return true;
            }
            catch (Exception e) when (e is WebSocketException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                return false;
            }
            finally
            {
                try { _Lock.Release(); } catch (ObjectDisposedException) { }
            }
        }

        /// <summary>Release the send lock.</summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Lock.Dispose();
        }

        #endregion
    }
}

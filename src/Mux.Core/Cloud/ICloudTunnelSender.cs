namespace Mux.Core.Cloud
{
    using System.Threading;
    using System.Threading.Tasks;
    using MuxCloud.Protocol;

    /// <summary>
    /// Sends frames on the tunnel. Implementations serialize concurrent sends so frames never interleave.
    /// </summary>
    public interface ICloudTunnelSender
    {
        /// <summary>
        /// Send one envelope.
        /// </summary>
        /// <param name="envelope">Envelope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when sent; false when the tunnel is closed.</returns>
        Task<bool> SendAsync(TunnelEnvelope envelope, CancellationToken token);
    }
}

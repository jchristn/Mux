namespace Test.Shared.Llm
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Test <see cref="HttpMessageHandler"/> that blocks until the request is cancelled, used to verify
    /// that user cancellation is propagated rather than converted into a connection error.
    /// </summary>
    public sealed class BlockingUntilCancelledHandler : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Expected cancellation before completion.");
        }
    }
}

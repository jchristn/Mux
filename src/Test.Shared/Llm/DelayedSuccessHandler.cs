namespace Test.Shared.Llm
{
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Test <see cref="HttpMessageHandler"/> that returns a fixed successful response after a delay.
    /// Used to exercise streaming behavior without a real server.
    /// </summary>
    public sealed class DelayedSuccessHandler : HttpMessageHandler
    {
        private readonly int _DelayMs;
        private readonly string _Content;
        private readonly string _MediaType;

        /// <summary>
        /// Initializes a new instance of the <see cref="DelayedSuccessHandler"/> class.
        /// </summary>
        /// <param name="delayMs">The delay before responding, in milliseconds.</param>
        /// <param name="content">The response body content.</param>
        /// <param name="mediaType">The response media type.</param>
        public DelayedSuccessHandler(int delayMs, string content, string mediaType)
        {
            _DelayMs = delayMs;
            _Content = content;
            _MediaType = mediaType;
        }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_DelayMs, cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_Content, Encoding.UTF8, _MediaType)
            };
        }
    }
}

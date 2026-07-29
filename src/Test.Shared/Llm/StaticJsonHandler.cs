namespace Test.Shared.Llm
{
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Test <see cref="HttpMessageHandler"/> that returns a fixed JSON response for every request.
    /// </summary>
    public sealed class StaticJsonHandler : HttpMessageHandler
    {
        private readonly string _ResponseJson;

        /// <summary>
        /// Initializes a new instance of the <see cref="StaticJsonHandler"/> class.
        /// </summary>
        /// <param name="responseJson">The JSON body to return for every request.</param>
        public StaticJsonHandler(string responseJson)
        {
            _ResponseJson = responseJson;
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_ResponseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}

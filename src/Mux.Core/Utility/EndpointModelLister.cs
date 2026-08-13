namespace Mux.Core.Utility
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Live-enumerates the models a configured endpoint's backend advertises. Ollama endpoints are queried
    /// through their native tags endpoint (<see cref="OllamaModelLister"/>); OpenAI, vLLM, and other
    /// OpenAI-compatible endpoints are queried through the OpenAI models endpoint
    /// (<see cref="OpenAiModelLister"/>). Backend failures are captured on the returned record rather than
    /// thrown, so enumerating a set of endpoints never aborts on the first unreachable one.
    /// </summary>
    public static class EndpointModelLister
    {
        /// <summary>
        /// Discovers the models advertised by <paramref name="endpoint"/>'s backend.
        /// </summary>
        /// <param name="endpoint">The endpoint to enumerate.</param>
        /// <param name="ignoreCertErrors">True to bypass TLS certificate validation for the request.</param>
        /// <param name="cancellationToken">A token to cancel the request.</param>
        /// <returns>A result carrying either the discovered model ids or a classified failure. Never null.</returns>
        public static async Task<EndpointModelListResult> ListModelsAsync(
            EndpointConfig endpoint,
            bool ignoreCertErrors,
            CancellationToken cancellationToken)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            try
            {
                List<string> models;
                if (endpoint.AdapterType == AdapterTypeEnum.Ollama)
                {
                    models = await OllamaModelLister
                        .ListModelsAsync(endpoint.BaseUrl, ignoreCertErrors, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    models = await OpenAiModelLister
                        .ListModelsAsync(endpoint.BaseUrl, endpoint.Headers, ignoreCertErrors, cancellationToken)
                        .ConfigureAwait(false);
                }

                return EndpointModelListResult.Ok(models);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                (string code, string message) = Classify(ex);
                return EndpointModelListResult.Failed(code, message);
            }
        }

        private static (string Code, string Message) Classify(Exception ex)
        {
            switch (ex)
            {
                case ArgumentException:
                    return ("invalid_argument", ex.Message);
                case TaskCanceledException:
                case TimeoutException:
                    return ("query_timeout", "The backend did not respond before the timeout elapsed.");
                case HttpRequestException http when http.StatusCode.HasValue:
                    return ClassifyStatus(http.StatusCode.Value, http.Message);
                case HttpRequestException http:
                    return ("backend_unreachable", http.Message);
                default:
                    return ("query_error", ex.Message);
            }
        }

        private static (string Code, string Message) ClassifyStatus(HttpStatusCode statusCode, string message)
        {
            return (int)statusCode switch
            {
                401 or 403 => ("auth_error", message),
                404 => ("models_endpoint_not_found", message),
                429 => ("rate_limited", message),
                >= 500 => ("backend_error", message),
                _ => ("http_error", message)
            };
        }
    }

    /// <summary>
    /// The outcome of a live model enumeration against a single endpoint.
    /// </summary>
    public sealed class EndpointModelListResult
    {
        private EndpointModelListResult(bool success, List<string> models, string errorCode, string errorMessage)
        {
            Success = success;
            Models = models;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Whether the backend was queried successfully.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// The discovered model ids, sorted case-insensitively. Empty on failure.
        /// </summary>
        public List<string> Models { get; }

        /// <summary>
        /// A machine-readable error code when the query failed; empty on success.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// A human-readable error message when the query failed; empty on success.
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// Creates a successful result carrying the discovered models.
        /// </summary>
        /// <param name="models">The discovered model ids.</param>
        /// <returns>A successful result.</returns>
        public static EndpointModelListResult Ok(List<string> models)
        {
            return new EndpointModelListResult(true, models ?? new List<string>(), string.Empty, string.Empty);
        }

        /// <summary>
        /// Creates a failed result carrying a classified error.
        /// </summary>
        /// <param name="errorCode">The machine-readable error code.</param>
        /// <param name="errorMessage">The human-readable error message.</param>
        /// <returns>A failed result.</returns>
        public static EndpointModelListResult Failed(string errorCode, string errorMessage)
        {
            return new EndpointModelListResult(false, new List<string>(), errorCode, errorMessage);
        }
    }
}

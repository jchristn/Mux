namespace Mux.Core.Llm
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Utility;

    /// <summary>
    /// Provides retry logic with exponential backoff for LLM HTTP requests.
    /// </summary>
    public static class RetryHandler
    {
        #region Public-Methods

        /// <summary>
        /// Executes an asynchronous action with retry logic and exponential backoff.
        /// </summary>
        /// <typeparam name="T">The return type of the action.</typeparam>
        /// <param name="action">The asynchronous action to execute.</param>
        /// <param name="maxRetries">The maximum number of retry attempts. Defaults to 3.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <param name="onRetry">Optional callback invoked on each retry with attempt number, max retries, and error message.</param>
        /// <returns>The result of the action on success.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> action,
            int maxRetries = 3,
            CancellationToken cancellationToken = default,
            Action<int, int, string>? onRetry = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            int attempt = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    attempt++;

                    if (attempt > maxRetries || !IsRetryableException(ex))
                    {
                        throw;
                    }

                    int delaySeconds = (int)Math.Pow(2, attempt - 1); // 1s, 2s, 4s
                    int delayMs = delaySeconds * 1000;

                    string message = $"retry {attempt}/{maxRetries} after {delaySeconds}s: {ex.Message}";
                    Console.Error.WriteLine(ConsoleMessageStyler.Notification(char.ToUpperInvariant(message[0]) + message.Substring(1)));

                    if (onRetry != null)
                    {
                        onRetry(attempt, maxRetries, ex.Message);
                    }

                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Determines whether the specified exception is retryable.
        /// </summary>
        /// <param name="ex">The exception to evaluate.</param>
        /// <returns>True if the exception is retryable; otherwise false.</returns>
        public static bool IsRetryableException(Exception ex)
        {
            if (ex is HttpRequestException httpEx)
            {
                if (httpEx.StatusCode.HasValue)
                {
                    return IsRetryableStatusCode((int)httpEx.StatusCode.Value);
                }

                // No status code means connection failure — retryable
                return true;
            }

            if (ex is TaskCanceledException tce)
            {
                // Only retry if it's an HTTP timeout, not user cancellation.
                // User cancellation has a CancellationToken that is canceled.
                if (tce.CancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                // HTTP timeout — retryable
                return true;
            }

            if (ex is IOException)
            {
                // Connection issues — retryable
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified HTTP status code is retryable.
        /// </summary>
        /// <param name="statusCode">The HTTP status code to evaluate.</param>
        /// <returns>True if the status code is retryable (429, 500, 502, 503, 504); otherwise false.</returns>
        public static bool IsRetryableStatusCode(int statusCode)
        {
            switch (statusCode)
            {
                case 429: // Too Many Requests
                case 500: // Internal Server Error
                case 502: // Bad Gateway
                case 503: // Service Unavailable
                case 504: // Gateway Timeout
                    return true;
                default:
                    return false;
            }
        }

        #endregion
    }
}

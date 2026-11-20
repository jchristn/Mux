namespace Mux.Core.Cloud
{
    using System;

    /// <summary>
    /// Enrollment with Mux Cloud failed. <see cref="Exception.Message"/> is suitable to show the user.
    /// </summary>
    public class CloudEnrollmentException : Exception
    {
        /// <summary>HTTP status returned by the cloud, or 0 when the request never got a response.</summary>
        public int StatusCode { get; }

        /// <summary>Stable error code from the cloud (for example <c>Unauthorized</c>), or empty.</summary>
        public string ErrorCode { get; }

        /// <summary>Instantiate.</summary>
        /// <param name="message">User-facing message.</param>
        /// <param name="statusCode">HTTP status, or 0.</param>
        /// <param name="errorCode">Error code, or empty.</param>
        /// <param name="inner">Underlying failure, or null.</param>
        public CloudEnrollmentException(string message, int statusCode, string errorCode, Exception? inner = null) : base(message, inner)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode ?? string.Empty;
        }
    }
}

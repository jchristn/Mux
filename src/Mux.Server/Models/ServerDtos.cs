namespace Mux.Server.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Health/status response for <c>GET /v1.0/api/health</c>.
    /// </summary>
    public class HealthResponse
    {
        /// <summary>Fixed status literal.</summary>
        public string Status { get; set; } = "healthy";

        /// <summary>Product name.</summary>
        public string Product { get; set; } = "mux";

        /// <summary>Product version.</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Process id hosting the server.</summary>
        public int Pid { get; set; } = 0;

        /// <summary>UTC time the server started.</summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Server uptime, formatted d.hh:mm:ss.</summary>
        public string Uptime { get; set; } = string.Empty;

        /// <summary>UTC time this response was produced.</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// A configured endpoint, projected for the API (never carries secrets).
    /// </summary>
    public class EndpointSummary
    {
        /// <summary>Endpoint name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Adapter type (kebab-case).</summary>
        public string AdapterType { get; set; } = string.Empty;

        /// <summary>Base URL, if set.</summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>Model identifier.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Whether this endpoint is the configured default.</summary>
        public bool IsDefault { get; set; } = false;
    }

    /// <summary>
    /// A persisted session, projected for the API.
    /// </summary>
    public class SessionSummary
    {
        /// <summary>Session id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Session title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Endpoint name captured with the session.</summary>
        public string EndpointName { get; set; } = string.Empty;

        /// <summary>Model captured with the session.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>UTC creation time.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.MinValue;

        /// <summary>UTC last-updated time.</summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.MinValue;
    }

    /// <summary>
    /// Standard error envelope.
    /// </summary>
    public class ApiError
    {
        /// <summary>Short machine-readable error code.</summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>Human-readable message.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Create an error envelope.</summary>
        /// <param name="error">Error code.</param>
        /// <param name="message">Message.</param>
        public ApiError(string error, string message)
        {
            Error = error;
            Message = message;
        }
    }

    /// <summary>
    /// A list wrapper so array payloads are self-describing.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    public class ListResponse<T>
    {
        /// <summary>The items.</summary>
        public List<T> Items { get; set; } = new List<T>();

        /// <summary>Item count.</summary>
        public int Count { get; set; } = 0;

        /// <summary>Wrap a list.</summary>
        /// <param name="items">Items.</param>
        public ListResponse(List<T> items)
        {
            Items = items ?? new List<T>();
            Count = Items.Count;
        }
    }
}

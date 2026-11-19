namespace Mux.Server.Models
{
    /// <summary>
    /// The optional body of <c>POST /v1.0/api/sessions/{id}/compact</c>.
    /// </summary>
    public class SessionCompactRequest
    {
        /// <summary>The endpoint whose model writes the summary. Null uses the session's endpoint, then the default.</summary>
        public string? Endpoint { get; set; }
    }
}

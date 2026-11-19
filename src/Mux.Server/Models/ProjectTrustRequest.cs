namespace Mux.Server.Models
{
    /// <summary>
    /// The body of <c>PUT /v1.0/api/trust</c>.
    /// </summary>
    public class ProjectTrustRequest
    {
        /// <summary>A directory inside the project. Required.</summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary><c>all</c>, <c>playbooks</c>, <c>ignore</c>, or <c>reset</c> (removes the decision).</summary>
        public string Level { get; set; } = string.Empty;
    }
}

namespace Mux.Server.Models
{
    /// <summary>
    /// The trust decision for a project, which gates the project's checked-in skills that run commands.
    /// </summary>
    public class ProjectTrustDto
    {
        /// <summary>The working directory that was asked about.</summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>The project root the decision applies to.</summary>
        public string ProjectRoot { get; set; } = string.Empty;

        /// <summary><c>all</c>, <c>playbooks</c>, <c>ignore</c>, or <c>unknown</c> (no decision recorded).</summary>
        public string Level { get; set; } = "unknown";
    }
}

namespace Mux.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The on-disk shape of <c>~/.mux/trusted-projects.json</c>: the list of per-project trust decisions.
    /// </summary>
    public class ProjectTrustFile
    {
        #region Private-Members

        private List<ProjectTrustEntry> _Projects = new List<ProjectTrustEntry>();

        #endregion

        #region Public-Members

        /// <summary>
        /// The recorded decisions. Never null.
        /// </summary>
        [JsonPropertyName("projects")]
        public List<ProjectTrustEntry> Projects
        {
            get => _Projects;
            set => _Projects = value ?? new List<ProjectTrustEntry>();
        }

        #endregion
    }
}

namespace Mux.Core.Skills.Evaluation
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One skill selection evaluation case: a prompt, the files of a small project it is asked in, the skills
    /// that should handle it, and skills that must not be listed for that project.
    /// </summary>
    public sealed class SkillEvalCase
    {
        #region Private-Members

        private string _Id = string.Empty;
        private string _Prompt = string.Empty;
        private List<string> _Files = new List<string>();
        private List<string> _Expect = new List<string>();
        private List<string> _Absent = new List<string>();

        #endregion

        #region Public-Members

        /// <summary>
        /// A short unique id, such as <c>go-run-tests</c>. Null is stored as empty.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// What the user asks, in their words. Null is stored as empty.
        /// </summary>
        [JsonPropertyName("prompt")]
        public string Prompt
        {
            get => _Prompt;
            set => _Prompt = value ?? string.Empty;
        }

        /// <summary>
        /// Relative paths of files to create (empty) in the project fixture, such as <c>go.mod</c>. A path ending in
        /// <c>/</c> creates a directory. Null is stored as empty.
        /// </summary>
        [JsonPropertyName("files")]
        public List<string> Files
        {
            get => _Files;
            set => _Files = value ?? new List<string>();
        }

        /// <summary>
        /// Skills that should handle the prompt. The case passes when any of them is listed and ranks within the
        /// evaluator's top N. Null is stored as empty.
        /// </summary>
        [JsonPropertyName("expect")]
        public List<string> Expect
        {
            get => _Expect;
            set => _Expect = value ?? new List<string>();
        }

        /// <summary>
        /// Skills that must not be listed for this project (gating checks). Null is stored as empty.
        /// </summary>
        [JsonPropertyName("absent")]
        public List<string> Absent
        {
            get => _Absent;
            set => _Absent = value ?? new List<string>();
        }

        #endregion
    }
}

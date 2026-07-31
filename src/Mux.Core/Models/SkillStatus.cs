namespace Mux.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// A detached, UI-facing snapshot of one skill's state: its identity, whether it is enabled and valid,
    /// how many commands it declares, its tags, and — when invalid — a short error summary.
    /// </summary>
    public class SkillStatus
    {
        #region Private-Members

        private string _Name = string.Empty;
        private string _Title = string.Empty;
        private bool _Enabled = true;
        private bool _Valid = true;
        private int _CommandCount = 0;
        private List<string> _Tags = new List<string>();
        private string? _Error = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// The skill identifier.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? string.Empty;
        }

        /// <summary>
        /// The skill's human-readable title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the skill is enabled for the current session.
        /// </summary>
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// Whether the skill passed validation.
        /// </summary>
        public bool Valid
        {
            get => _Valid;
            set => _Valid = value;
        }

        /// <summary>
        /// The number of commands the skill declares.
        /// </summary>
        public int CommandCount
        {
            get => _CommandCount;
            set => _CommandCount = value;
        }

        /// <summary>
        /// The skill's tags. Never null.
        /// </summary>
        public List<string> Tags
        {
            get => _Tags;
            set => _Tags = value ?? new List<string>();
        }

        /// <summary>
        /// A short error summary when the skill is invalid; otherwise null.
        /// </summary>
        public string? Error
        {
            get => _Error;
            set => _Error = value;
        }

        #endregion
    }
}

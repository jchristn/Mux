namespace Mux.Cli.Commands
{
    /// <summary>
    /// Describes a keyboard shortcut shown by the interactive help command.
    /// </summary>
    internal class InputShortcut
    {
        #region Private-Members

        private string _Shortcut = string.Empty;
        private string _Description = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="InputShortcut"/> class.
        /// </summary>
        /// <param name="shortcut">The shortcut text.</param>
        /// <param name="description">The shortcut description.</param>
        public InputShortcut(string shortcut, string description)
        {
            _Shortcut = shortcut;
            _Description = description;
        }

        #endregion

        #region Public-Properties

        /// <summary>
        /// Gets or sets the shortcut text.
        /// </summary>
        public string Shortcut
        {
            get => _Shortcut;
            set => _Shortcut = value ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets the shortcut description.
        /// </summary>
        public string Description
        {
            get => _Description;
            set => _Description = value ?? string.Empty;
        }

        #endregion
    }
}

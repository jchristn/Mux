namespace Mux.Cli.Commands
{
    /// <summary>
    /// Settings for the interactive command surface. Currently carries only the shared
    /// <see cref="CommonSettings"/> options; interactive-specific options are added as the
    /// TUIKit-based interactive UI is built (M6+).
    /// </summary>
    public class InteractiveSettings : CommonSettings
    {
        /// <summary>
        /// An optional startup prompt. When supplied, the splash screen is skipped and the shell opens
        /// straight into interactive mode with this prompt submitted as the first turn.
        /// </summary>
        public string? Prompt { get; set; }
    }
}

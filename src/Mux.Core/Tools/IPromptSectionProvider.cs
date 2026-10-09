namespace Mux.Core.Tools
{
    /// <summary>
    /// Implemented by a tool provider that also contributes a section to the system prompt (for example the memory
    /// index). <see cref="ExternalToolsBinder"/> appends the section of every additional provider that implements it.
    /// </summary>
    public interface IPromptSectionProvider
    {
        /// <summary>
        /// Builds the provider's system-prompt section for a working directory.
        /// </summary>
        /// <param name="workingDirectory">The working directory the turn runs in.</param>
        /// <returns>The section text (starting with a blank line), or empty for none.</returns>
        string BuildPromptSection(string workingDirectory);
    }
}

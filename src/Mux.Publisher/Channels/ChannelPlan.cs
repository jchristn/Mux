namespace Mux.Publisher.Channels
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The output of a channel driver's planning pass: the recipe files it would write and the shell
    /// commands it would run to package, sign, and publish. Splitting planning from execution is what
    /// makes packaging logic unit testable — tests assert the rendered files and command lines without
    /// invoking any external packager. <see cref="Notes"/> carries honest status such as "submitted;
    /// pending review" for channels that end at an external gate.
    /// </summary>
    public sealed class ChannelPlan
    {
        /// <summary>The channel this plan belongs to (for example <c>inno</c>).</summary>
        public string Channel { get; }

        /// <summary>Recipe/intermediate files to write, relative to the staging directory.</summary>
        public List<GeneratedFile> Files { get; } = new List<GeneratedFile>();

        /// <summary>Ordered commands to run (publish, sign, package, push).</summary>
        public List<ShellCommand> Commands { get; } = new List<ShellCommand>();

        /// <summary>Human-readable notes, including external-gate/pending states.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>
        /// True when the channel ends at an external review gate (winget PR, Chocolatey moderation,
        /// Flathub/Snap review) that no automation can push through. The orchestrator reports these as
        /// pending rather than claiming the package is live.
        /// </summary>
        public bool EndsAtExternalGate { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelPlan"/> class.
        /// </summary>
        /// <param name="channel">The channel name.</param>
        public ChannelPlan(string channel)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        /// <summary>Adds a generated file to the plan and returns the plan for chaining.</summary>
        /// <param name="relativePath">Path relative to the staging directory.</param>
        /// <param name="content">The file content.</param>
        /// <returns>This plan.</returns>
        public ChannelPlan AddFile(string relativePath, string content)
        {
            Files.Add(new GeneratedFile(relativePath, content));
            return this;
        }

        /// <summary>Adds a command to the plan and returns the plan for chaining.</summary>
        /// <param name="command">The command to run.</param>
        /// <returns>This plan.</returns>
        public ChannelPlan AddCommand(ShellCommand command)
        {
            Commands.Add(command ?? throw new ArgumentNullException(nameof(command)));
            return this;
        }

        /// <summary>Adds a command from its parts and returns the plan for chaining.</summary>
        /// <param name="description">What the command does.</param>
        /// <param name="executable">The program to run.</param>
        /// <param name="arguments">The argument list.</param>
        /// <returns>This plan.</returns>
        public ChannelPlan AddCommand(string description, string executable, params string[] arguments)
        {
            Commands.Add(new ShellCommand(executable, new List<string>(arguments)) { Description = description });
            return this;
        }

        /// <summary>Adds a note to the plan and returns the plan for chaining.</summary>
        /// <param name="note">The note text.</param>
        /// <returns>This plan.</returns>
        public ChannelPlan AddNote(string note)
        {
            if (!string.IsNullOrWhiteSpace(note)) Notes.Add(note);
            return this;
        }
    }

    /// <summary>A file a channel driver renders (an Inno script, a Homebrew formula, a control file).</summary>
    public sealed class GeneratedFile
    {
        /// <summary>Path relative to the staging directory.</summary>
        public string RelativePath { get; }

        /// <summary>The file's textual content.</summary>
        public string Content { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GeneratedFile"/> class.
        /// </summary>
        /// <param name="relativePath">Path relative to the staging directory.</param>
        /// <param name="content">The file content.</param>
        public GeneratedFile(string relativePath, string content)
        {
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            Content = content ?? string.Empty;
        }
    }

    /// <summary>A single external command to run as part of a channel's execution.</summary>
    public sealed class ShellCommand
    {
        /// <summary>The executable to invoke (for example <c>iscc</c>, <c>codesign</c>, <c>fpm</c>).</summary>
        public string Executable { get; }

        /// <summary>The argument list, one token per element (no shell splitting).</summary>
        public List<string> Arguments { get; }

        /// <summary>Optional working directory; null means the staging directory.</summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>Human-readable description of the step.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>When true, a non-zero exit does not abort the channel (best-effort steps).</summary>
        public bool ContinueOnError { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShellCommand"/> class.
        /// </summary>
        /// <param name="executable">The executable to invoke.</param>
        /// <param name="arguments">The argument list.</param>
        public ShellCommand(string executable, List<string> arguments)
        {
            Executable = executable ?? throw new ArgumentNullException(nameof(executable));
            Arguments = arguments ?? new List<string>();
        }

        /// <summary>
        /// Renders the command as a readable, roughly shell-quoted line for logs and dry-run output.
        /// </summary>
        /// <returns>The formatted command line.</returns>
        public string ToDisplayString()
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append(Quote(Executable));
            foreach (string argument in Arguments)
            {
                builder.Append(' ');
                builder.Append(Quote(argument));
            }

            return builder.ToString();
        }

        private static string Quote(string token)
        {
            if (string.IsNullOrEmpty(token)) return "\"\"";
            bool needsQuotes = token.IndexOfAny(new[] { ' ', '\t', '"', '\'', '\\' }) >= 0;
            if (!needsQuotes) return token;
            return "\"" + token.Replace("\"", "\\\"") + "\"";
        }
    }
}

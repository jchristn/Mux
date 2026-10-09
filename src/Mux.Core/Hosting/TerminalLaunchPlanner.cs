namespace Mux.Core.Hosting
{
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Builds what a GUI launcher (the tray agent) runs to open the interactive mux terminal in a new terminal window
    /// on each platform. Pure: it only produces text and argument lists, so it can be tested without opening windows.
    /// When the CLI was not found, the window shows how to install it instead of failing silently.
    /// </summary>
    public static class TerminalLaunchPlanner
    {
        #region Public-Members

        /// <summary>The message shown in the terminal when the mux CLI cannot be found.</summary>
        public const string NotFoundMessage = "mux was not found. Install it with ./scripts/macos/install-tool.sh (or the linux or windows equivalent), put it on PATH, or set MUX_CLI to its path.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The command line that starts the CLI, quoted for a POSIX shell, or null when no CLI was found.
        /// </summary>
        /// <param name="location">The located CLI, or null.</param>
        /// <returns>The quoted command line, or null.</returns>
        public static string? PosixCommandLine(MuxCliLocation? location)
        {
            if (location == null || string.IsNullOrWhiteSpace(location.Executable))
            {
                return null;
            }

            StringBuilder builder = new StringBuilder(QuotePosix(location.Executable));
            foreach (string argument in location.LeadingArguments)
            {
                builder.Append(' ').Append(QuotePosix(argument));
            }

            return builder.ToString();
        }

        /// <summary>
        /// The macOS <c>.command</c> script Terminal runs: a login shell (so the user's PATH and dotnet are present),
        /// changes to the working directory, removes itself, and starts mux. Without a CLI it prints how to install it
        /// and leaves a shell open.
        /// </summary>
        /// <param name="location">The located CLI, or null.</param>
        /// <param name="workingDirectory">The folder the terminal starts in.</param>
        /// <returns>The script text.</returns>
        public static string BuildMacCommandFile(MuxCliLocation? location, string workingDirectory)
        {
            StringBuilder script = new StringBuilder();
            script.Append("#!/bin/zsh -l\n");
            script.Append("rm -f \"$0\"\n");
            script.Append("cd ").Append(QuotePosix(workingDirectory)).Append(" 2>/dev/null || cd \"$HOME\"\n");
            script.Append("clear\n");
            string? command = PosixCommandLine(location);
            if (command != null)
            {
                script.Append(command).Append('\n');
            }
            else
            {
                script.Append("if command -v mux >/dev/null 2>&1; then\n  mux\nelse\n  echo ").Append(QuotePosix(NotFoundMessage)).Append("\nfi\n");
            }

            script.Append("exec \"${SHELL:-/bin/zsh}\" -l\n");
            return script.ToString();
        }

        /// <summary>
        /// The command that opens a new console window on Windows running mux and keeps it open afterwards.
        /// </summary>
        /// <param name="location">The located CLI, or null (then <c>mux</c> is resolved through PATH).</param>
        /// <param name="workingDirectory">The folder the console starts in.</param>
        /// <returns>The command.</returns>
        public static TerminalCommand BuildWindowsCommand(MuxCliLocation? location, string workingDirectory)
        {
            StringBuilder inner = new StringBuilder();
            if (location != null && !string.IsNullOrWhiteSpace(location.Executable))
            {
                inner.Append(QuoteWindows(location.Executable));
                foreach (string argument in location.LeadingArguments)
                {
                    inner.Append(' ').Append(QuoteWindows(argument));
                }
            }
            else
            {
                inner.Append("mux || echo ").Append(NotFoundMessage);
            }

            // start treats its first quoted argument as the window title, so the title must be quoted explicitly.
            return new TerminalCommand
            {
                FileName = "cmd.exe",
                RawArguments = "/c start \"mux\" /d \"" + workingDirectory + "\" cmd.exe /k " + inner
            };
        }

        /// <summary>
        /// The terminal emulators to try on Linux, in order, each running mux through a login shell and keeping the
        /// window open afterwards.
        /// </summary>
        /// <param name="location">The located CLI, or null.</param>
        /// <param name="workingDirectory">The folder the terminal starts in.</param>
        /// <returns>The candidates.</returns>
        public static IReadOnlyList<TerminalCommand> BuildLinuxCommands(MuxCliLocation? location, string workingDirectory)
        {
            string run = "cd " + QuotePosix(workingDirectory) + " 2>/dev/null || cd \"$HOME\"; "
                + (PosixCommandLine(location) ?? "if command -v mux >/dev/null 2>&1; then mux; else echo " + QuotePosix(NotFoundMessage) + "; fi")
                + "; exec \"${SHELL:-/bin/sh}\" -l";
            List<string> shell = new List<string> { "/bin/sh", "-lc", run };
            List<TerminalCommand> commands = new List<TerminalCommand>
            {
                new TerminalCommand { FileName = "x-terminal-emulator", Arguments = Prefix("-e", shell) },
                new TerminalCommand { FileName = "gnome-terminal", Arguments = Prefix("--", shell) },
                new TerminalCommand { FileName = "konsole", Arguments = Prefix("-e", shell) },
                new TerminalCommand { FileName = "xfce4-terminal", Arguments = new List<string> { "-x", "/bin/sh", "-lc", run } },
                new TerminalCommand { FileName = "kitty", Arguments = new List<string>(shell) },
                new TerminalCommand { FileName = "alacritty", Arguments = Prefix("-e", shell) },
                new TerminalCommand { FileName = "xterm", Arguments = Prefix("-e", shell) }
            };
            return commands;
        }

        /// <summary>
        /// Quotes a value for a POSIX shell with single quotes.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The quoted value.</returns>
        public static string QuotePosix(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
        }

        #endregion

        #region Private-Methods

        private static string QuoteWindows(string value)
        {
            string text = value ?? string.Empty;
            return text.IndexOfAny(new[] { ' ', '\t', '&', '(', ')' }) >= 0 ? "\"" + text + "\"" : text;
        }

        private static List<string> Prefix(string first, List<string> rest)
        {
            List<string> list = new List<string> { first };
            list.AddRange(rest);
            return list;
        }

        #endregion
    }
}

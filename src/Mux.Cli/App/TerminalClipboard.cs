namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Copies text to the system clipboard from the terminal. It tries the platform's clipboard command first
    /// (<c>pbcopy</c> on macOS, <c>clip</c> on Windows, <c>wl-copy</c>, <c>xclip</c>, or <c>xsel</c> on Linux), and
    /// falls back to the OSC 52 escape sequence, which most modern terminals (and tmux, and terminals over SSH)
    /// turn into a clipboard write.
    /// </summary>
    public static class TerminalClipboard
    {
        #region Public-Methods

        /// <summary>
        /// Builds the OSC 52 sequence that asks the terminal to put the text on the clipboard.
        /// </summary>
        /// <param name="text">The text. Null is treated as empty.</param>
        /// <returns>The escape sequence (<c>ESC ] 52 ; c ; base64 BEL</c>).</returns>
        public static string BuildOsc52(string? text)
        {
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return "\u001b]52;c;" + encoded + "\u0007";
        }

        /// <summary>
        /// The clipboard commands to try, in order, for a platform.
        /// </summary>
        /// <param name="isWindows">Whether the platform is Windows.</param>
        /// <param name="isMacOS">Whether the platform is macOS.</param>
        /// <returns>Each candidate as the executable followed by its arguments.</returns>
        public static IReadOnlyList<string[]> GetCommandCandidates(bool isWindows, bool isMacOS)
        {
            List<string[]> candidates = new List<string[]>();
            if (isWindows)
            {
                candidates.Add(new[] { "clip.exe" });
            }
            else if (isMacOS)
            {
                candidates.Add(new[] { "pbcopy" });
            }
            else
            {
                candidates.Add(new[] { "wl-copy" });
                candidates.Add(new[] { "xclip", "-selection", "clipboard" });
                candidates.Add(new[] { "xsel", "--clipboard", "--input" });
            }

            return candidates;
        }

        /// <summary>
        /// Copies the text to the clipboard and describes how.
        /// </summary>
        /// <param name="text">The text to copy. Null is treated as empty.</param>
        /// <returns>A one-line status message for the user.</returns>
        public static string Copy(string? text)
        {
            string value = text ?? string.Empty;
            string count = value.Length == 1 ? "1 character" : value.Length + " characters";
            foreach (string[] candidate in GetCommandCandidates(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()))
            {
                if (TryRunCommand(candidate, value))
                {
                    return "Copied " + count + " to the clipboard (" + candidate[0] + ").";
                }
            }

            try
            {
                TextWriter output = Console.Out;
                output.Write(BuildOsc52(value));
                output.Flush();
                return "Sent " + count + " to the terminal clipboard (OSC 52); paste to check your terminal allows it.";
            }
            catch (Exception ex)
            {
                return "Copy failed: " + ex.Message;
            }
        }

        #endregion

        #region Private-Methods

        private static bool TryRunCommand(string[] command, string text)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(command[0])
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                for (int i = 1; i < command.Length; i++)
                {
                    info.ArgumentList.Add(command[i]);
                }

                // clip.exe reads the console code page; UTF-16 with a byte order mark is the form it reads reliably.
                Encoding encoding = OperatingSystem.IsWindows() ? new UnicodeEncoding(false, true) : new UTF8Encoding(false);
                info.StandardInputEncoding = encoding;
                using (Process? process = Process.Start(info))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    if (OperatingSystem.IsWindows())
                    {
                        process.StandardInput.Write('﻿');
                    }

                    process.StandardInput.Write(text);
                    process.StandardInput.Close();
                    if (!process.WaitForExit(5000))
                    {
                        try { process.Kill(true); } catch (Exception) { }
                        return false;
                    }

                    return process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion
    }
}

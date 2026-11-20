namespace Mux.Cli.Commands
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Reads a secret from the console a key at a time for prompts such as <c>mux cloud login</c>'s password. Each
    /// typed character echoes as <see cref="MaskChar"/>; Ctrl+R redraws the typed text in place and Ctrl+R again masks
    /// it, so a password can be checked before it is sent. Enter finishes; Backspace deletes the last character. The
    /// echo uses only backspaces and spaces (no escape sequences), so it works in any console.
    /// </summary>
    public static class MaskedLineReader
    {
        #region Public-Members

        /// <summary>The character echoed for each typed character while the value is hidden.</summary>
        public const char MaskChar = '*';

        /// <summary>The prompt suffix that names the reveal key, for example "Password (Ctrl+R to show): ".</summary>
        public const string RevealHint = "Ctrl+R to show";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reads one line.
        /// </summary>
        /// <param name="readKey">Returns the next key press without echoing it (normally <c>() => Console.ReadKey(true)</c>).</param>
        /// <param name="echo">Where the mask or the revealed text is drawn (normally standard output).</param>
        /// <returns>The typed value, without the Enter.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string Read(Func<ConsoleKeyInfo> readKey, TextWriter echo)
        {
            if (readKey is null) throw new ArgumentNullException(nameof(readKey));
            if (echo is null) throw new ArgumentNullException(nameof(echo));

            StringBuilder value = new StringBuilder();
            bool revealed = false;
            while (true)
            {
                ConsoleKeyInfo key = readKey();
                if (key.Key == ConsoleKey.Enter) break;

                if (key.Key == ConsoleKey.R && (key.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    // Redraw the same number of cells with the other form, so nothing is left over.
                    string erase = new string('\b', value.Length);
                    revealed = !revealed;
                    echo.Write(erase + (revealed ? value.ToString() : new string(MaskChar, value.Length)));
                    echo.Flush();
                    continue;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (value.Length > 0)
                    {
                        value.Length--;
                        echo.Write("\b \b");
                        echo.Flush();
                    }

                    continue;
                }

                if (char.IsControl(key.KeyChar)) continue;

                value.Append(key.KeyChar);
                echo.Write(revealed ? key.KeyChar : MaskChar);
                echo.Flush();
            }

            return value.ToString();
        }

        #endregion
    }
}

namespace Mux.Publisher.Publishing
{
    /// <summary>
    /// The unsigned-build notice that ships inside installers. The same steps appear word for word in the
    /// release notes (<c>.github/release-install-notes.md</c>), <c>INSTALL.md</c>, and the README's download
    /// section; keep all four in step when the OS wording changes.
    /// </summary>
    public static class UnsignedNotice
    {
        /// <summary>
        /// The text of the <c>If &lt;App&gt; won't open.txt</c> note placed beside the app in the <c>.dmg</c>.
        /// </summary>
        /// <param name="display">The product name as users see it.</param>
        /// <param name="cliName">The command name (for the PATH instructions), or null when there is none.</param>
        /// <returns>The note text, with LF line endings.</returns>
        public static string MacOsNote(string display, string? cliName)
        {
            string text =
                display + " is free, open-source software and is not code-signed (signing certificates are an ongoing cost we\n" +
                "choose not to pass on). Your Mac may warn that the developer is unidentified. That warning doesn't mean the\n" +
                "download is damaged; to confirm a file is the one published, check it against SHA256SUMS on the release page.\n" +
                "\n" +
                "IF MACOS SAYS \"" + display + "\" NOT OPENED\n" +
                "\n" +
                "macOS shows: \"" + display + "\" Not Opened. Apple could not verify \"" + display + "\" is free of malware that may harm\n" +
                "your Mac or compromise your privacy. It has Move to Trash and Done buttons. To open it anyway:\n" +
                "\n" +
                "1. Click Done. Do not click Move to Trash.\n" +
                "2. Open System Settings > Privacy & Security and scroll down to Security.\n" +
                "3. Next to \"" + display + "\" was blocked to protect your Mac, click Open Anyway. The button only appears for\n" +
                "   about an hour after the blocked attempt; if it is missing, try to open " + display + " again first.\n" +
                "4. Click Open Anyway again in the dialog that follows and enter your password. You only do this once.\n" +
                "\n" +
                "Or skip the prompt in Terminal, after dragging " + display + " to Applications:\n" +
                "\n" +
                "    xattr -dr com.apple.quarantine /Applications/" + display + ".app\n" +
                "\n" +
                "On macOS 15 and later, Control-click > Open no longer bypasses this prompt.\n";

            if (!string.IsNullOrWhiteSpace(cliName))
            {
                text +=
                    "\n" +
                    "THE " + cliName!.ToUpperInvariant() + " COMMAND\n" +
                    "\n" +
                    "A disk image can't add commands to your PATH. The .pkg installer does it for you; after a drag-and-drop\n" +
                    "install, run:\n" +
                    "\n" +
                    "    sudo mkdir -p /usr/local/bin\n" +
                    "    sudo ln -sf /Applications/" + display + ".app/Contents/MacOS/" + cliName + " /usr/local/bin/" + cliName + "\n";
            }

            return text;
        }
    }
}

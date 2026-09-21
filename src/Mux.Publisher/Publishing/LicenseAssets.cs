namespace Mux.Publisher.Publishing
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Renders the project license into the formats the interactive installers need to <b>display and demand
    /// acceptance</b>: plain text for the Inno Setup license page, RTF for the WiX license dialog, and a macOS
    /// disk-image Software License Agreement resource plist (LPic/STR#/TEXT) that gates mounting a <c>.dmg</c>
    /// behind an Agree/Disagree prompt. The license text is read from <c>LICENSE.md</c> at the repository root
    /// when present, and falls back to the canonical MIT text (so rendering is deterministic in tests and in
    /// any environment where the file is not staged).
    /// </summary>
    public static class LicenseAssets
    {
        #region Public-Members

        /// <summary>
        /// The canonical MIT license text used when <c>LICENSE.md</c> cannot be read. Kept in sync with the
        /// repository's <c>LICENSE.md</c>; the real file wins whenever it is present.
        /// </summary>
        public const string CanonicalMit =
            "MIT License\n\n" +
            "Copyright (c) 2026 Joel Christner\n\n" +
            "Permission is hereby granted, free of charge, to any person obtaining a copy " +
            "of this software and associated documentation files (the \"Software\"), to deal " +
            "in the Software without restriction, including without limitation the rights " +
            "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell " +
            "copies of the Software, and to permit persons to whom the Software is " +
            "furnished to do so, subject to the following conditions:\n\n" +
            "The above copyright notice and this permission notice shall be included in all " +
            "copies or substantial portions of the Software.\n\n" +
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR " +
            "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, " +
            "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE " +
            "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER " +
            "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, " +
            "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE " +
            "SOFTWARE.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the license text as plain text (LF-normalized), read from <c>&lt;repoRoot&gt;/LICENSE.md</c>
        /// when it exists, otherwise <see cref="CanonicalMit"/>.
        /// </summary>
        /// <param name="repoRoot">The repository root directory.</param>
        /// <returns>The license text.</returns>
        public static string ReadLicenseText(string repoRoot)
        {
            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                try
                {
                    string path = Path.Combine(repoRoot, "LICENSE.md");
                    if (File.Exists(path))
                    {
                        return Normalize(File.ReadAllText(path));
                    }
                }
                catch (Exception)
                {
                    // Fall through to the canonical text; the license page must never be the reason a build fails.
                }
            }

            return Normalize(CanonicalMit);
        }

        /// <summary>
        /// Wraps plain license text in a minimal RTF document for the WiX license dialog (which renders RTF,
        /// not plain text). RTF control characters are escaped and blank lines are preserved.
        /// </summary>
        /// <param name="plainText">The plain license text.</param>
        /// <returns>An RTF document.</returns>
        public static string ToRtf(string plainText)
        {
            StringBuilder body = new StringBuilder();
            foreach (char c in Normalize(plainText))
            {
                switch (c)
                {
                    case '\\': body.Append("\\\\"); break;
                    case '{': body.Append("\\{"); break;
                    case '}': body.Append("\\}"); break;
                    case '\n': body.Append("\\par\n"); break;
                    default:
                        if (c > 0x7F)
                        {
                            body.Append("\\'").Append(((int)c & 0xFF).ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            body.Append(c);
                        }

                        break;
                }
            }

            return "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fnil Segoe UI;}}\n\\fs20\n" + body + "\n}\n";
        }

        /// <summary>
        /// Builds the macOS disk-image Software License Agreement resource plist consumed by
        /// <c>hdiutil udifrez -xml</c>. It carries a default-English <c>LPic</c> language map, an English
        /// <c>STR#</c> button/label set, and the license itself as a <c>TEXT</c> resource — the combination
        /// that makes Disk Images show an Agree/Disagree prompt before the volume will mount.
        /// </summary>
        /// <param name="plainText">The plain license text.</param>
        /// <returns>The resource plist XML.</returns>
        public static string DmgSlaResourcesPlist(string plainText)
        {
            // LPic: defaultLanguageID=0, count=1, then { languageID=0, localResourceIndex=0, twoByteFlag=0 }.
            byte[] lpic = { 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };

            // STR#: a big-endian count followed by Pascal strings — the six labels the SLA panel draws.
            string[] labels =
            {
                "English",
                "Agree",
                "Disagree",
                "Print",
                "Save...",
                "This is a legal agreement between you and the licensor. To continue click Agree, or click Disagree to cancel."
            };
            byte[] str = BuildStringList(labels);

            // TEXT: the license body with classic Mac (CR) line endings, as the SLA panel expects.
            byte[] text = Latin1(Normalize(plainText).Replace("\n", "\r"));

            StringBuilder builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            builder.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
            builder.Append("<plist version=\"1.0\">\n<dict>\n");
            AppendResource(builder, "LPic", "", lpic);
            AppendResource(builder, "STR#", "English", str);
            AppendResource(builder, "TEXT", "English SLA", text);
            builder.Append("</dict>\n</plist>\n");
            return builder.ToString();
        }

        #endregion

        #region Private-Methods

        private static void AppendResource(StringBuilder builder, string type, string name, byte[] data)
        {
            builder.Append("  <key>").Append(type).Append("</key>\n  <array>\n    <dict>\n");
            builder.Append("      <key>Attributes</key><string>0x0000</string>\n");
            builder.Append("      <key>Data</key><data>").Append(Convert.ToBase64String(data)).Append("</data>\n");
            builder.Append("      <key>ID</key><string>5000</string>\n");
            builder.Append("      <key>Name</key><string>").Append(name).Append("</string>\n");
            builder.Append("    </dict>\n  </array>\n");
        }

        private static byte[] BuildStringList(string[] items)
        {
            List<byte> bytes = new List<byte>();
            bytes.Add((byte)((items.Length >> 8) & 0xFF));
            bytes.Add((byte)(items.Length & 0xFF));
            foreach (string item in items)
            {
                byte[] ascii = Latin1(item);
                int length = ascii.Length > 255 ? 255 : ascii.Length;
                bytes.Add((byte)length);
                for (int i = 0; i < length; i++)
                {
                    bytes.Add(ascii[i]);
                }
            }

            return bytes.ToArray();
        }

        private static byte[] Latin1(string value)
        {
            byte[] bytes = new byte[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bytes[i] = c > 0xFF ? (byte)'?' : (byte)c;
            }

            return bytes;
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        }

        #endregion
    }
}

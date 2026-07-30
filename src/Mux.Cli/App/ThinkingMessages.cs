namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// A library of light, non-offensive "thinking" phrases shown beneath a submitted prompt while the
    /// model works. The phrases are loaded from an embedded resource; a small built-in set is used if the
    /// resource is unavailable. <see cref="Next"/> returns a randomly chosen phrase, and
    /// <see cref="Spinner"/> supplies the animation frames.
    /// </summary>
    public static class ThinkingMessages
    {
        #region Private-Members

        private const string ResourceSuffix = "thinking-messages.txt";

        private static readonly string[] _Fallback =
        {
            "Thinking…",
            "Working on it…",
            "Just a moment…",
            "Considering…",
            "Putting it together…",
            "Cogitating…",
            "Almost there…",
        };

        private static readonly IReadOnlyList<string> _All = LoadMessages();

        // A subtle braille spinner that reads as a gentle pulse rather than a spinning wheel.
        private static readonly string[] _Spinner = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        #endregion

        #region Public-Members

        /// <summary>
        /// The full set of loaded thinking phrases.
        /// </summary>
        public static IReadOnlyList<string> All
        {
            get => _All;
        }

        /// <summary>
        /// The spinner animation frames.
        /// </summary>
        public static IReadOnlyList<string> Spinner
        {
            get => _Spinner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns a randomly chosen thinking phrase.
        /// </summary>
        /// <returns>A phrase from <see cref="All"/>.</returns>
        public static string Next()
        {
            if (_All.Count == 0)
            {
                return "Thinking…";
            }

            return _All[Random.Shared.Next(_All.Count)];
        }

        /// <summary>
        /// Returns the spinner frame for the given tick.
        /// </summary>
        /// <param name="tick">A monotonically increasing counter.</param>
        /// <returns>The spinner glyph for this tick.</returns>
        public static string SpinnerFrame(int tick)
        {
            int index = ((tick % _Spinner.Length) + _Spinner.Length) % _Spinner.Length;
            return _Spinner[index];
        }

        #endregion

        #region Private-Methods

        private static IReadOnlyList<string> LoadMessages()
        {
            try
            {
                Assembly assembly = typeof(ThinkingMessages).Assembly;
                string? resourceName = null;
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        resourceName = name;
                        break;
                    }
                }

                if (resourceName == null)
                {
                    return _Fallback;
                }

                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    return _Fallback;
                }

                using StreamReader reader = new StreamReader(stream);
                List<string> messages = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0 && seen.Add(trimmed))
                    {
                        messages.Add(trimmed);
                    }
                }

                return messages.Count > 0 ? messages : _Fallback;
            }
            catch (Exception)
            {
                return _Fallback;
            }
        }

        #endregion
    }
}

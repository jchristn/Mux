namespace Mux.Core.Agent
{
    using System;
    using System.Text;

    /// <summary>
    /// Detects a model stuck in a repetition loop by looking at the tail of the text it is streaming. A tail
    /// that is one short unit repeated over and over (<c>"* \n* \n* \n"</c>, the same sentence again and
    /// again), or a long run with no letter or digit at all, means the model will not end its turn on its
    /// own; the agent loop stops the stream instead of letting it run to the token limit.
    ///
    /// <para>Thread safety: the type is stateless and its members are safe to call concurrently.</para>
    /// </summary>
    public static class RepetitionDetector
    {
        /// <summary>
        /// How many trailing characters the periodic check examines. The tail must repeat one unit for this
        /// whole length.
        /// </summary>
        public const int PeriodicWindow = 2000;

        /// <summary>
        /// The longest repeating unit, in characters, the periodic check looks for. With
        /// <see cref="PeriodicWindow"/> this means at least 31 repetitions of the unit.
        /// </summary>
        public const int MaxUnitLength = 64;

        /// <summary>
        /// How many trailing characters must contain no letter or digit for the text to count as degenerate.
        /// </summary>
        public const int SymbolWindow = 1000;

        /// <summary>
        /// Checks whether the tail of <paramref name="text"/> is a repetition loop.
        /// </summary>
        /// <param name="text">The assistant text streamed so far in the current turn.</param>
        /// <param name="reason">A short description of what was detected, or empty when nothing was.</param>
        /// <returns>True when the tail is degenerate.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
        public static bool IsDegenerate(StringBuilder text, out string reason)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            reason = string.Empty;
            if (text.Length < SymbolWindow)
            {
                return false;
            }

            // Copy the tail once: indexing a large StringBuilder walks its chunks on every access.
            int tailLength = Math.Min(text.Length, Math.Max(PeriodicWindow, SymbolWindow));
            string tail = text.ToString(text.Length - tailLength, tailLength);

            if (!HasLetterOrDigit(tail, tail.Length - SymbolWindow))
            {
                reason = "the last " + SymbolWindow + " characters contain no letters or digits";
                return true;
            }

            if (tail.Length >= PeriodicWindow)
            {
                int start = tail.Length - PeriodicWindow;
                for (int unit = 1; unit <= MaxUnitLength; unit++)
                {
                    if (IsPeriodic(tail, start, unit))
                    {
                        reason = "the last " + PeriodicWindow + " characters repeat a " + unit + "-character unit ("
                            + JsonEscape(tail.Substring(tail.Length - unit)) + ") " + (PeriodicWindow / unit) + " times";
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasLetterOrDigit(string text, int start)
        {
            for (int i = start; i < text.Length; i++)
            {
                if (char.IsLetterOrDigit(text[i]))
                {
                    return true;
                }
            }

            return false;
        }

        // True when every character from start + unit to the end equals the character one unit earlier.
        private static bool IsPeriodic(string text, int start, int unit)
        {
            for (int i = start + unit; i < text.Length; i++)
            {
                if (text[i] != text[i - unit])
                {
                    return false;
                }
            }

            return true;
        }

        private static string JsonEscape(string value)
        {
            return System.Text.Json.JsonSerializer.Serialize(value);
        }
    }
}

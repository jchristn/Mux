namespace Mux.Desktop.Conversation
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Light-hearted status phrases shown while a turn is in flight before the first token arrives. Purely
    /// cosmetic; a front end rotates through them on a timer.
    /// </summary>
    public static class ThinkingQuips
    {
        private static readonly IReadOnlyList<string> Quips = new List<string>
        {
            "Thinking…",
            "Consulting the model…",
            "Warming up the tokens…",
            "Reticulating splines…",
            "Reading between the lines…",
            "Chasing down a good answer…",
            "Untangling the context…",
            "Compiling thoughts…",
            "Doing the needful…",
            "Weighing the options…",
            "Summoning the right words…",
            "Almost there…",
            "Considering the edge cases…",
            "Herding electrons…"
        };

        /// <summary>The number of available quips.</summary>
        public static int Count
        {
            get => Quips.Count;
        }

        /// <summary>
        /// Return the quip at a rotating index (wraps around). Non-negative indices only; the value is taken
        /// modulo <see cref="Count"/>.
        /// </summary>
        /// <param name="index">A monotonically increasing counter.</param>
        /// <returns>A quip string.</returns>
        public static string At(int index)
        {
            int safe = Math.Abs(index) % Quips.Count;
            return Quips[safe];
        }
    }
}

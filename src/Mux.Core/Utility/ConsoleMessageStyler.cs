namespace Mux.Core.Utility
{
    /// <summary>
    /// Applies lightweight ANSI styling to human-facing console messages.
    /// </summary>
    public static class ConsoleMessageStyler
    {
        private const string DimAnsi = "\x1b[90m";
        private const string GreenAnsi = "\x1b[32m";
        private const string RedAnsi = "\x1b[31m";
        private const string ResetAnsi = "\x1b[0m";

        /// <summary>
        /// Formats an informational or notification message in dark grey.
        /// </summary>
        /// <param name="message">The message to style.</param>
        /// <returns>The styled message.</returns>
        public static string Notification(string message)
        {
            return Apply(message, DimAnsi);
        }

        /// <summary>
        /// Formats a successful action message in green.
        /// </summary>
        /// <param name="message">The message to style.</param>
        /// <returns>The styled message.</returns>
        public static string Success(string message)
        {
            return Apply(message, GreenAnsi);
        }

        /// <summary>
        /// Formats a failure message in red.
        /// </summary>
        /// <param name="message">The message to style.</param>
        /// <returns>The styled message.</returns>
        public static string Failure(string message)
        {
            return Apply(message, RedAnsi);
        }

        private static string Apply(string message, string ansi)
        {
            return string.IsNullOrEmpty(message)
                ? string.Empty
                : ansi + message + ResetAnsi;
        }
    }
}

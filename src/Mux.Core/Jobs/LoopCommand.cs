namespace Mux.Core.Jobs
{
    using System;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// A parsed <c>/loop</c> request: <c>[interval] [--max N] &lt;prompt&gt;</c>. An interval such as <c>5m</c>,
    /// <c>90s</c>, <c>1h30m</c>, or <c>1d</c> makes a fixed-interval loop; without one the loop is self-paced.
    /// </summary>
    public sealed class LoopCommand
    {
        #region Private-Members

        private static readonly Regex _IntervalPattern = new Regex(@"^(?:(\d+)([smhd]))+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex _IntervalPart = new Regex(@"(\d+)([smhd])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        #endregion

        #region Constructors-and-Factories

        private LoopCommand(string prompt, TimeSpan? interval, int? maxIterations)
        {
            Prompt = prompt;
            Interval = interval;
            MaxIterations = maxIterations;
        }

        #endregion

        #region Public-Members

        /// <summary>The prompt to repeat.</summary>
        public string Prompt { get; }

        /// <summary>The fixed interval, or null for a self-paced loop.</summary>
        public TimeSpan? Interval { get; }

        /// <summary>The requested iteration cap, or null for the configured default.</summary>
        public int? MaxIterations { get; }

        /// <summary>The usage line shown when a request cannot be parsed.</summary>
        public const string Usage = "Usage: /loop [interval] [--max N] <prompt>   (interval like 30s, 5m, 1h30m; omit it to let the model pace the loop)";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parses the text after <c>/loop</c>.
        /// </summary>
        /// <param name="text">The arguments, for example <c>5m --max 10 check the deploy</c>.</param>
        /// <param name="command">The parsed command, or null when parsing fails.</param>
        /// <param name="error">Why parsing failed, or empty on success.</param>
        /// <returns>True when the text is a valid request.</returns>
        public static bool TryParse(string? text, out LoopCommand? command, out string error)
        {
            command = null;
            error = string.Empty;
            string rest = (text ?? string.Empty).Trim();
            TimeSpan? interval = null;
            int? max = null;

            while (rest.Length > 0)
            {
                string token = FirstToken(rest, out string remainder);
                if (interval == null && max == null && LooksLikeInterval(token))
                {
                    if (!TryParseInterval(token, out TimeSpan parsed))
                    {
                        error = "'" + token + "' is not a valid interval. " + Usage;
                        return false;
                    }

                    interval = parsed;
                    rest = remainder;
                    continue;
                }

                if (string.Equals(token, "--max", StringComparison.OrdinalIgnoreCase))
                {
                    string value = FirstToken(remainder, out string afterValue);
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedMax) || parsedMax < 1)
                    {
                        error = "--max needs a whole number of at least 1. " + Usage;
                        return false;
                    }

                    max = parsedMax;
                    rest = afterValue;
                    continue;
                }

                break;
            }

            if (string.IsNullOrWhiteSpace(rest))
            {
                error = "No prompt given. " + Usage;
                return false;
            }

            command = new LoopCommand(rest, interval, max);
            return true;
        }

        /// <summary>
        /// Parses an interval such as <c>45s</c>, <c>5m</c>, <c>2h</c>, <c>1d</c>, or a combination like <c>1h30m</c>.
        /// </summary>
        /// <param name="text">The interval text.</param>
        /// <param name="interval">The parsed interval.</param>
        /// <returns>True when the text is a positive interval.</returns>
        public static bool TryParseInterval(string? text, out TimeSpan interval)
        {
            interval = TimeSpan.Zero;
            string value = (text ?? string.Empty).Trim();
            if (!_IntervalPattern.IsMatch(value))
            {
                return false;
            }

            double seconds = 0;
            foreach (Match part in _IntervalPart.Matches(value))
            {
                if (!double.TryParse(part.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out double amount))
                {
                    return false;
                }

                switch (char.ToLowerInvariant(part.Groups[2].Value[0]))
                {
                    case 's': seconds += amount; break;
                    case 'm': seconds += amount * 60; break;
                    case 'h': seconds += amount * 3600; break;
                    default: seconds += amount * 86400; break;
                }
            }

            if (seconds <= 0 || seconds > TimeSpan.FromDays(30).TotalSeconds)
            {
                return false;
            }

            interval = TimeSpan.FromSeconds(seconds);
            return true;
        }

        /// <summary>
        /// Formats an interval compactly, for example <c>5m</c>, <c>1h30m</c>, or <c>45s</c>.
        /// </summary>
        /// <param name="interval">The interval.</param>
        /// <returns>The compact text.</returns>
        public static string FormatInterval(TimeSpan interval)
        {
            long total = (long)Math.Max(0, Math.Round(interval.TotalSeconds));
            if (total == 0)
            {
                return "0s";
            }

            StringBuilder builder = new StringBuilder();
            long days = total / 86400;
            long hours = total % 86400 / 3600;
            long minutes = total % 3600 / 60;
            long seconds = total % 60;
            if (days > 0) builder.Append(days.ToString(CultureInfo.InvariantCulture)).Append('d');
            if (hours > 0) builder.Append(hours.ToString(CultureInfo.InvariantCulture)).Append('h');
            if (minutes > 0) builder.Append(minutes.ToString(CultureInfo.InvariantCulture)).Append('m');
            if (seconds > 0) builder.Append(seconds.ToString(CultureInfo.InvariantCulture)).Append('s');
            return builder.ToString();
        }

        #endregion

        #region Private-Methods

        private static bool LooksLikeInterval(string token)
        {
            return _IntervalPattern.IsMatch(token);
        }

        private static string FirstToken(string text, out string remainder)
        {
            string trimmed = text.TrimStart();
            int space = 0;
            while (space < trimmed.Length && !char.IsWhiteSpace(trimmed[space]))
            {
                space++;
            }

            remainder = trimmed.Substring(space).TrimStart();
            return trimmed.Substring(0, space);
        }

        #endregion
    }
}

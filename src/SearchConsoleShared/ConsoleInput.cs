namespace SearchConsoleShared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Shared interactive console input helpers.
    /// </summary>
    public static class ConsoleInput
    {
        /// <summary>
        /// Reads a string from the console.
        /// </summary>
        /// <param name="prompt">Prompt text.</param>
        /// <param name="defaultValue">Optional default value.</param>
        /// <param name="allowEmpty">Whether empty input is allowed.</param>
        /// <returns>The entered string or default value.</returns>
        public static string GetString(string prompt, string? defaultValue = null, bool allowEmpty = true)
        {
            while (true)
            {
                Console.Write(string.IsNullOrWhiteSpace(defaultValue)
                    ? $"{prompt} "
                    : $"{prompt} [{defaultValue}] ");

                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    if (defaultValue is not null)
                    {
                        return defaultValue;
                    }

                    if (allowEmpty)
                    {
                        return string.Empty;
                    }

                    continue;
                }

                return input.Trim();
            }
        }

        /// <summary>
        /// Reads an integer from the console.
        /// </summary>
        /// <param name="prompt">Prompt text.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <param name="minValue">Minimum allowed value.</param>
        /// <param name="maxValue">Maximum allowed value.</param>
        /// <returns>The resolved integer value.</returns>
        public static int GetInt(string prompt, int defaultValue, int minValue, int maxValue)
        {
            while (true)
            {
                string input = GetString(prompt, defaultValue.ToString(), false);
                if (int.TryParse(input, out int value) && value >= minValue && value <= maxValue)
                {
                    return value;
                }

                Console.WriteLine($"Please enter an integer from {minValue} to {maxValue}.");
            }
        }

        /// <summary>
        /// Reads a yes/no value from the console.
        /// </summary>
        /// <param name="prompt">Prompt text.</param>
        /// <param name="defaultValue">Default boolean value.</param>
        /// <returns>The resolved boolean value.</returns>
        public static bool GetBoolean(string prompt, bool defaultValue)
        {
            while (true)
            {
                string input = GetString(prompt, defaultValue ? "y" : "n", false);
                switch (input.Trim().ToLowerInvariant())
                {
                    case "y":
                    case "yes":
                    case "true":
                    case "1":
                        return true;
                    case "n":
                    case "no":
                    case "false":
                    case "0":
                        return false;
                }

                Console.WriteLine("Please enter y or n.");
            }
        }

        /// <summary>
        /// Reads a comma-separated list from the console.
        /// </summary>
        /// <param name="prompt">Prompt text.</param>
        /// <param name="defaultValues">Optional default values.</param>
        /// <returns>The normalized list.</returns>
        public static List<string> GetCsv(string prompt, IEnumerable<string>? defaultValues = null)
        {
            string? defaultValue = defaultValues is not null && defaultValues.Any()
                ? string.Join(", ", defaultValues)
                : null;

            string input = GetString(prompt, defaultValue, true);

            return string.IsNullOrWhiteSpace(input)
                ? new List<string>()
                : input
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
    }
}

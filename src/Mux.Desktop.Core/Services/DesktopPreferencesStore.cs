namespace Mux.Desktop.Services
{
    using System;
    using System.IO;
    using System.Text.Json;

    /// <summary>
    /// Loads and saves <see cref="DesktopPreferences"/> to <c>desktop.json</c> in a config directory. Both
    /// operations are best-effort and never throw: a missing or corrupt file yields defaults, and a failed
    /// write is swallowed (returning false) so a preference change never breaks the app.
    /// </summary>
    public sealed class DesktopPreferencesStore
    {
        private const string FileName = "desktop.json";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _ConfigDirectory;

        /// <summary>
        /// Instantiate the store over a config directory.
        /// </summary>
        /// <param name="configDirectory">The directory <c>desktop.json</c> lives in. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configDirectory"/> is null.</exception>
        public DesktopPreferencesStore(string configDirectory)
        {
            _ConfigDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
        }

        /// <summary>
        /// The full path to the preferences file.
        /// </summary>
        public string FilePath => Path.Combine(_ConfigDirectory, FileName);

        /// <summary>
        /// Load the preferences, returning defaults when the file is absent or unreadable.
        /// </summary>
        /// <returns>The loaded preferences (never null).</returns>
        public DesktopPreferences Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new DesktopPreferences();
                }

                string json = File.ReadAllText(FilePath);
                DesktopPreferences? loaded = JsonSerializer.Deserialize<DesktopPreferences>(json, Options);
                return Normalize(loaded ?? new DesktopPreferences());
            }
            catch (Exception)
            {
                return new DesktopPreferences();
            }
        }

        /// <summary>
        /// Save the preferences. Best-effort.
        /// </summary>
        /// <param name="preferences">The preferences to persist. Required.</param>
        /// <returns><c>true</c> when the file was written; otherwise <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="preferences"/> is null.</exception>
        public bool Save(DesktopPreferences preferences)
        {
            if (preferences == null)
            {
                throw new ArgumentNullException(nameof(preferences));
            }

            try
            {
                Directory.CreateDirectory(_ConfigDirectory);
                string json = JsonSerializer.Serialize(Normalize(preferences), Options);
                File.WriteAllText(FilePath, json);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static DesktopPreferences Normalize(DesktopPreferences preferences)
        {
            switch ((preferences.ThemeMode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "system":
                    preferences.ThemeMode = "system";
                    break;
                case "light":
                    preferences.ThemeMode = "light";
                    break;
                default:
                    preferences.ThemeMode = "dark";
                    break;
            }

            return preferences;
        }
    }
}

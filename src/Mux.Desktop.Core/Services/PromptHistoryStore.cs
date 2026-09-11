namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    /// <summary>
    /// Persists the composer prompt history to <c>prompt-history.json</c> in a config directory. Best-effort:
    /// a missing or corrupt file loads as empty, and a failed write is swallowed.
    /// </summary>
    public sealed class PromptHistoryStore
    {
        private const string FileName = "prompt-history.json";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = false };

        private readonly string _ConfigDirectory;

        /// <summary>
        /// Instantiate the store over a config directory.
        /// </summary>
        /// <param name="configDirectory">The directory the history file lives in. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configDirectory"/> is null.</exception>
        public PromptHistoryStore(string configDirectory)
        {
            _ConfigDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
        }

        /// <summary>
        /// The full path to the history file.
        /// </summary>
        public string FilePath => Path.Combine(_ConfigDirectory, FileName);

        /// <summary>
        /// Load the history entries (oldest-to-newest), returning an empty list on any failure.
        /// </summary>
        /// <returns>The entries; never null.</returns>
        public List<string> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new List<string>();
                }

                string json = File.ReadAllText(FilePath);
                List<string>? entries = JsonSerializer.Deserialize<List<string>>(json, Options);
                return entries ?? new List<string>();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Save the history entries. Best-effort.
        /// </summary>
        /// <param name="entries">The entries to persist. Required.</param>
        /// <returns><c>true</c> when written; otherwise <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries"/> is null.</exception>
        public bool Save(IReadOnlyList<string> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            try
            {
                Directory.CreateDirectory(_ConfigDirectory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, Options));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

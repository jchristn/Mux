namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Mux.Core.Sessions;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Working-directory suggestions for starting a conversation: the directories recent sessions ran in (most
    /// recent first) plus the user's home directory. A new chat may use any existing directory; the chat route
    /// rejects one that does not exist.
    /// </summary>
    public sealed class WorkspaceRoutes
    {
        #region Private-Members

        private readonly string? _ApiKey;
        private readonly SessionStore _SessionStore;
        private int _MaxSuggestions = 50;

        #endregion

        #region Public-Members

        /// <summary>
        /// Largest number of recent directories returned (the home directory is added on top). Default 50,
        /// minimum 1, maximum 500.
        /// </summary>
        public int MaxSuggestions
        {
            get => _MaxSuggestions;
            set => _MaxSuggestions = Math.Clamp(value, 1, 500);
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="sessionStore">Session store the suggestions are drawn from.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sessionStore"/> is null.</exception>
        public WorkspaceRoutes(string? apiKey, SessionStore sessionStore)
        {
            _ApiKey = apiKey;
            _SessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/workspaces", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                IReadOnlyList<SessionSnapshot> snapshots = await _SessionStore.ListAsync(req.Http.Token).ConfigureAwait(false);
                req.Http.Response.StatusCode = 200;
                return (object)new ListResponse<WorkspaceDto>(Build(snapshots, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), _MaxSuggestions));
            }, Documentation.ApiDoc.WorkspacesGet);
        }

        /// <summary>
        /// Build the suggestion list: distinct existing session directories, most recently updated first, then the
        /// home directory when it is not already listed.
        /// </summary>
        /// <param name="snapshots">Saved sessions. Null is treated as empty.</param>
        /// <param name="homeDirectory">The user's home directory, or empty to omit it.</param>
        /// <param name="maxRecent">Largest number of recent directories.</param>
        /// <returns>Suggestions.</returns>
        public static List<WorkspaceDto> Build(IEnumerable<SessionSnapshot>? snapshots, string? homeDirectory, int maxRecent)
        {
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            Dictionary<string, WorkspaceDto> byPath = new Dictionary<string, WorkspaceDto>(comparer);
            foreach (SessionSnapshot snapshot in snapshots ?? Enumerable.Empty<SessionSnapshot>())
            {
                string dir = Normalize(snapshot?.WorkingDirectory);
                if (dir.Length == 0 || snapshot == null) continue;
                if (byPath.TryGetValue(dir, out WorkspaceDto? existing))
                {
                    existing.SessionCount++;
                    if (existing.LastUsedUtc == null || snapshot.UpdatedUtc > existing.LastUsedUtc) existing.LastUsedUtc = snapshot.UpdatedUtc;
                    continue;
                }

                if (!Directory.Exists(dir)) continue;
                byPath[dir] = new WorkspaceDto { Path = dir, Name = NameOf(dir), Source = "recent", LastUsedUtc = snapshot.UpdatedUtc, SessionCount = 1 };
            }

            List<WorkspaceDto> result = byPath.Values
                .OrderByDescending(w => w.LastUsedUtc ?? DateTime.MinValue)
                .Take(Math.Max(1, maxRecent))
                .ToList();

            string home = Normalize(homeDirectory);
            if (home.Length > 0 && Directory.Exists(home) && !result.Any(w => comparer.Equals(w.Path, home)))
            {
                result.Add(new WorkspaceDto { Path = home, Name = NameOf(home), Source = "home" });
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                string full = Path.GetFullPath(path.Trim());
                string root = Path.GetPathRoot(full) ?? string.Empty;
                return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string NameOf(string path)
        {
            string name = Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? path : name;
        }

        #endregion
    }
}

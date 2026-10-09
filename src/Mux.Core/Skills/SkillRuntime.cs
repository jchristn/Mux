namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Mux.Core.Tools;
    using Mux.Core.Utility;

    /// <summary>
    /// Owns a session's skills. It discovers user skills from the skills directory, applies the enablement
    /// overrides from the skills index, and re-scans on a periodic timer. When project skills are enabled, it
    /// also discovers the skills checked into the project that contains a working directory (under
    /// <see cref="ProjectSkillRoots"/>), applies that project's trust level, and merges them over the user
    /// skills, so one runtime can serve several working directories at once (the desktop app and the REST
    /// server share a runtime across conversations). Discovery builds fresh catalogs off to the side and
    /// swaps them in atomically, so an in-flight <c>run_skill</c> never observes a half-loaded set. The
    /// runtime itself is the <see cref="IExternalToolProvider"/> the agent loop consumes. Thread-safe.
    /// </summary>
    public sealed class SkillRuntime : IExternalToolProvider, IDisposable
    {
        #region Private-Members

        private readonly string _SkillsDirectory;
        private readonly Func<List<SkillIndexEntry>> _LoadIndex;
        private readonly Action _OnSkillsChanged;
        private readonly TimeSpan _Interval;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly object _Sync = new object();
        private readonly SkillExecutor _Executor = new SkillExecutor();
        private readonly TaskCompletionSource<bool> _FirstRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, SkillCatalogView> _ProjectViews = new Dictionary<string, SkillCatalogView>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        private readonly AppliesToMatcher _Matcher = new AppliesToMatcher();
        private ToolPresenceCache _Tools = new ToolPresenceCache();

        private IReadOnlyList<Skill> _UserSkills = new List<Skill>();
        private SkillCatalogView? _UserView;
        private List<SkillStatus> _Status = new List<SkillStatus>();
        private string _Signature = string.Empty;
        private Task? _Loop;
        private bool _Disposed;

        private bool _ProjectSkillsEnabled = false;
        private List<string> _ProjectSkillRoots = MuxSettings.DefaultProjectSkillRoots();
        private string _ListingMode = "relevant";
        private ProjectTrustStore? _TrustStore = null;
        private bool _TrustAllProjects = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillRuntime"/> class with project skills disabled.
        /// </summary>
        /// <param name="skillsDirectory">The directory whose subfolders are skills. Must not be null.</param>
        /// <param name="loadIndex">Loads the skills index (enablement overrides). Must not be null.</param>
        /// <param name="onSkillsChanged">Invoked after a refresh that changes the exposed skill set. Must not be null.</param>
        /// <param name="interval">The re-scan interval. Defaults to 30 seconds when null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public SkillRuntime(string skillsDirectory, Func<List<SkillIndexEntry>> loadIndex, Action onSkillsChanged, TimeSpan? interval = null)
        {
            _SkillsDirectory = skillsDirectory ?? throw new ArgumentNullException(nameof(skillsDirectory));
            _LoadIndex = loadIndex ?? throw new ArgumentNullException(nameof(loadIndex));
            _OnSkillsChanged = onSkillsChanged ?? throw new ArgumentNullException(nameof(onSkillsChanged));
            _Interval = interval ?? TimeSpan.FromSeconds(30);
            _Tools.TimeToLive = _Interval;
        }

        /// <summary>
        /// Creates a runtime configured from settings: the resolved skills directory, the skills index, the
        /// refresh interval, project skills and their roots, the listing mode, and the trust store at
        /// <c>trusted-projects.json</c> in the config directory. The runtime is not started.
        /// </summary>
        /// <param name="settings">The settings. Must not be null.</param>
        /// <param name="onSkillsChanged">Invoked after a refresh that changes the exposed skill set. Must not be null.</param>
        /// <returns>The configured runtime.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static SkillRuntime FromSettings(MuxSettings settings, Action onSkillsChanged)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (onSkillsChanged == null) throw new ArgumentNullException(nameof(onSkillsChanged));

            SkillRuntime runtime = new SkillRuntime(
                SettingsLoader.ResolveSkillsDirectory(settings),
                SettingsLoader.LoadSkillIndex,
                onSkillsChanged,
                TimeSpan.FromSeconds(settings.SkillRefreshIntervalSeconds))
            {
                ProjectSkillsEnabled = settings.ProjectSkillsEnabled,
                ProjectSkillRoots = settings.ProjectSkillRoots,
                ListingMode = settings.SkillListingMode,
                TrustStore = new ProjectTrustStore(SettingsLoader.GetTrustedProjectsPath())
            };
            runtime.Executor.DefaultEnvironment[ProdPatternVariable] = settings.SkillProdPattern;
            return runtime;
        }

        #endregion

        #region Public-Members

        /// <inheritdoc/>
        public string Name => "skills";

        /// <summary>
        /// The environment variable that carries <see cref="MuxSettings.SkillProdPattern"/> to skill processes.
        /// </summary>
        public const string ProdPatternVariable = "MUX_SKILL_PROD_PATTERN";

        /// <summary>
        /// The executor that runs skill commands; its <see cref="SkillExecutor.DefaultEnvironment"/> applies to every
        /// run.
        /// </summary>
        public SkillExecutor Executor => _Executor;

        /// <summary>
        /// The cache that answers whether a <c>requiresTools</c> executable is on PATH. Replaceable for tests. Never null.
        /// </summary>
        public ToolPresenceCache Tools
        {
            get { lock (_Sync) { return _Tools; } }
            set { lock (_Sync) { _Tools = value ?? new ToolPresenceCache(); } }
        }

        /// <summary>
        /// The directory this runtime scans for user skills.
        /// </summary>
        public string SkillsDirectory => _SkillsDirectory;

        /// <summary>
        /// A task that completes after the first discovery finishes.
        /// </summary>
        public Task FirstRefreshCompleted => _FirstRefresh.Task;

        /// <summary>
        /// Whether skills checked into the project containing a working directory are discovered and merged
        /// over the user skills. Defaults to <c>false</c> for a directly constructed runtime;
        /// <see cref="FromSettings"/> applies <see cref="MuxSettings.ProjectSkillsEnabled"/>.
        /// </summary>
        public bool ProjectSkillsEnabled
        {
            get { lock (_Sync) { return _ProjectSkillsEnabled; } }
            set { lock (_Sync) { _ProjectSkillsEnabled = value; _ProjectViews.Clear(); } }
        }

        /// <summary>
        /// The project skill directories, relative to the project root, in precedence order. Defaults to
        /// <c>.mux/skills</c>, <c>.claude/skills</c>, <c>.agents/skills</c>. Normalized on assignment (see
        /// <see cref="MuxSettings.NormalizeProjectSkillRoots"/>). Never null.
        /// </summary>
        public List<string> ProjectSkillRoots
        {
            get { lock (_Sync) { return new List<string>(_ProjectSkillRoots); } }
            set { lock (_Sync) { _ProjectSkillRoots = MuxSettings.NormalizeProjectSkillRoots(value); _ProjectViews.Clear(); } }
        }

        /// <summary>
        /// Which usable skills <see cref="BuildPromptSection(string?)"/> lists: <c>relevant</c> (the default),
        /// <c>all</c>, or <c>none</c>. Unrecognized values fall back to <c>relevant</c>.
        /// </summary>
        public string ListingMode
        {
            get { lock (_Sync) { return _ListingMode; } }
            set { lock (_Sync) { _ListingMode = MuxSettings.TryNormalizeSkillListingMode(value, out string normalized) ? normalized : "relevant"; } }
        }

        /// <summary>
        /// The store holding per-project trust decisions. Null means no decision is ever recorded, so project
        /// skills with commands stay blocked unless <see cref="TrustAllProjects"/> is set.
        /// </summary>
        public ProjectTrustStore? TrustStore
        {
            get { lock (_Sync) { return _TrustStore; } }
            set { lock (_Sync) { _TrustStore = value; _ProjectViews.Clear(); } }
        }

        /// <summary>
        /// When true, every project is treated as fully trusted for this runtime without consulting or
        /// changing the trust store (the <c>--trust-project-skills</c> flag). Defaults to false.
        /// </summary>
        public bool TrustAllProjects
        {
            get { lock (_Sync) { return _TrustAllProjects; } }
            set { lock (_Sync) { _TrustAllProjects = value; _ProjectViews.Clear(); } }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts the background discovery and periodic re-scan loop. Safe to call once.
        /// </summary>
        public void Start()
        {
            lock (_Sync)
            {
                if (_Loop != null || _Disposed)
                {
                    return;
                }

                _Loop = Task.Run(() => LoopAsync(_Cts.Token));
            }
        }

        /// <summary>
        /// Requests an immediate re-scan, for example after the library was edited through the manager.
        /// Cached project views are discarded so the next lookup re-reads them.
        /// </summary>
        public void RequestRefresh()
        {
            if (_Disposed)
            {
                return;
            }

            lock (_Sync)
            {
                _ProjectViews.Clear();
            }

            _ = RefreshAsync(force: true, _Cts.Token);
        }

        /// <summary>
        /// Discovers skills immediately and waits for the result, without starting the periodic loop. Use it
        /// for one-shot callers (a REST request, a single print run) that need a loaded catalog now.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that completes when discovery has finished.</returns>
        public async Task RefreshNowAsync(CancellationToken cancellationToken)
        {
            await RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns a detached status snapshot for every user skill in the library.
        /// </summary>
        /// <returns>The per-skill status list.</returns>
        public List<SkillStatus> GetStatus()
        {
            lock (_Sync)
            {
                List<SkillStatus> copy = new List<SkillStatus>(_Status.Count);
                foreach (SkillStatus status in _Status)
                {
                    copy.Add(status.Clone());
                }

                return copy;
            }
        }

        /// <summary>
        /// Returns a detached status snapshot for every skill visible from a working directory: project
        /// skills (including blocked ones) followed by the user skills they do not shadow.
        /// </summary>
        /// <param name="workingDirectory">The working directory. Null returns the user skills only.</param>
        /// <returns>The per-skill status list.</returns>
        public List<SkillStatus> GetStatus(string? workingDirectory)
        {
            SkillCatalogView? view = GetView(workingDirectory);
            return view == null ? new List<SkillStatus>() : new List<SkillStatus>(view.Catalog.GetStatus());
        }

        /// <summary>
        /// Returns the project root that a working directory resolves to: its repository root, or the
        /// directory itself outside a repository. Null when the input is null or empty.
        /// </summary>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The project root, or null.</returns>
        public static string? ResolveProjectRoot(string? workingDirectory)
        {
            return RepositoryRootLocator.FindProjectRoot(workingDirectory);
        }

        /// <summary>
        /// Returns the skills visible from a working directory, building and caching the project view on first
        /// use. A cached project view is rebuilt after the refresh interval or when the user skills change.
        /// Returns null before the first discovery has completed.
        /// </summary>
        /// <param name="workingDirectory">The working directory. Null returns the user-only view.</param>
        /// <returns>The view, or null when nothing has been discovered yet.</returns>
        public SkillCatalogView? GetView(string? workingDirectory)
        {
            SkillCatalogView? userView;
            bool projectsEnabled;
            lock (_Sync)
            {
                userView = _UserView;
                projectsEnabled = _ProjectSkillsEnabled;
            }

            if (userView == null || !projectsEnabled || string.IsNullOrWhiteSpace(workingDirectory))
            {
                return userView;
            }

            string? root = RepositoryRootLocator.FindProjectRoot(workingDirectory);
            if (root == null)
            {
                return userView;
            }

            lock (_Sync)
            {
                if (_ProjectViews.TryGetValue(root, out SkillCatalogView? cached)
                    && DateTime.UtcNow - cached.BuiltUtc < _Interval)
                {
                    return cached;
                }
            }

            SkillCatalogView built = BuildProjectView(root);
            lock (_Sync)
            {
                _ProjectViews[root] = built;
            }

            return built;
        }

        /// <summary>
        /// Looks up a usable skill by name as seen from a working directory (project skills shadow user
        /// skills).
        /// </summary>
        /// <param name="name">The skill name.</param>
        /// <param name="workingDirectory">The working directory. Null searches user skills only.</param>
        /// <param name="skill">The skill when found.</param>
        /// <returns><c>true</c> when a usable skill with the name exists.</returns>
        public bool TryGetSkill(string name, string? workingDirectory, out Skill skill)
        {
            SkillCatalogView? view = GetView(workingDirectory);
            if (view != null && !string.IsNullOrWhiteSpace(name) && view.Catalog.TryGet(name, out Skill found) && found.IsUsable)
            {
                skill = found;
                return true;
            }

            skill = new Skill();
            return false;
        }

        /// <summary>
        /// Returns the usable skills that can be invoked by name from a working directory, in catalog order.
        /// </summary>
        /// <param name="workingDirectory">The working directory. Null returns user skills only.</param>
        /// <returns>The invocable skills.</returns>
        public IReadOnlyList<Skill> GetInvocableSkills(string? workingDirectory)
        {
            List<Skill> result = new List<Skill>();
            SkillCatalogView? view = GetView(workingDirectory);
            if (view == null)
            {
                return result;
            }

            foreach (Skill skill in view.Catalog.GetEnabledValidSkills())
            {
                if (skill.Manifest.UserInvocable)
                {
                    result.Add(skill);
                }
            }

            return result;
        }

        /// <summary>
        /// Records a trust decision for the project containing a working directory and discards its cached
        /// view so the decision applies to the next lookup. Fires the skills-changed callback.
        /// </summary>
        /// <param name="workingDirectory">A directory inside the project. Must not be null or empty.</param>
        /// <param name="level">The level to record.</param>
        /// <returns>The project root the decision was recorded for.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workingDirectory"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the runtime has no trust store.</exception>
        /// <exception cref="IOException">Thrown when the trust store cannot be written.</exception>
        public string SetProjectTrust(string workingDirectory, ProjectTrustLevelEnum level)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory)) throw new ArgumentException("A working directory is required.", nameof(workingDirectory));

            string root = RepositoryRootLocator.FindProjectRoot(workingDirectory)
                ?? throw new ArgumentException("The working directory could not be resolved.", nameof(workingDirectory));

            ProjectTrustStore store = TrustStore
                ?? throw new InvalidOperationException("This skills runtime has no trust store, so project trust cannot be recorded.");

            store.SetLevel(root, level);
            lock (_Sync)
            {
                _ProjectViews.Remove(root);
            }

            NotifyChanged();
            return root;
        }

        /// <summary>
        /// Builds the system-prompt section that lists the usable user skills. Equivalent to
        /// <see cref="BuildPromptSection(string?)"/> with no working directory, so no relevance filtering
        /// applies. Returns an empty string when there are none.
        /// </summary>
        /// <returns>The prompt section (leading with a blank line), or an empty string.</returns>
        public string BuildPromptSection()
        {
            return BuildPromptSection(null);
        }

        /// <summary>
        /// Builds the system-prompt section that lists the skills the model should know about from a working
        /// directory. Skills that opted out of model invocation are never listed. In <c>relevant</c> mode,
        /// skills whose <c>appliesTo</c> globs do not match the project are left out and counted in a footer;
        /// in <c>none</c> mode the section is empty. Returns an empty string when nothing is listed.
        /// </summary>
        /// <param name="workingDirectory">The working directory. Null lists user skills without relevance filtering.</param>
        /// <returns>The prompt section (leading with a blank line), or an empty string.</returns>
        public string BuildPromptSection(string? workingDirectory)
        {
            SkillCatalogView? view = GetView(workingDirectory);
            if (view == null)
            {
                return string.Empty;
            }

            string mode = ListingMode;
            if (mode == "none")
            {
                return string.Empty;
            }

            bool filter = mode == "relevant" && !string.IsNullOrWhiteSpace(workingDirectory);
            string? relevanceRoot = filter ? RepositoryRootLocator.FindProjectRoot(workingDirectory) : null;
            ToolPresenceCache tools = Tools;
            List<Skill> listed = new List<Skill>();
            int hidden = 0;
            foreach (Skill skill in view.Catalog.GetEnabledValidSkills())
            {
                if (!skill.Manifest.ModelInvocable)
                {
                    continue;
                }

                if (filter && !view.IsRelevant(skill, relevanceRoot, _Matcher, tools))
                {
                    hidden++;
                    continue;
                }

                listed.Add(skill);
            }

            if (listed.Count == 0 && hidden == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("\n\n");
            if (listed.Count > 0)
            {
                builder.Append(PromptResolver.Shared.GetEffective("section.skills")).Append('\n');
            }

            foreach (Skill skill in listed)
            {
                builder.Append($"- {skill.Manifest.Name}: {skill.Manifest.Description}\n");
            }

            if (hidden > 0)
            {
                builder.Append(PromptResolver.Shared.GetEffective("section.skills.more").Replace("{Count}", hidden.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append('\n');
            }

            return builder.ToString().TrimEnd();
        }

        /// <inheritdoc/>
        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            SkillCatalogView? userView;
            List<SkillCatalogView> projectViews;
            lock (_Sync)
            {
                userView = _UserView;
                projectViews = new List<SkillCatalogView>(_ProjectViews.Values);
            }

            if (userView != null && userView.Catalog.GetEnabledValidSkills().Count > 0)
            {
                return userView.Provider.GetToolDefinitions();
            }

            foreach (SkillCatalogView view in projectViews)
            {
                if (view.Catalog.GetEnabledValidSkills().Count > 0)
                {
                    return view.Provider.GetToolDefinitions();
                }
            }

            return new List<ToolDefinition>();
        }

        /// <inheritdoc/>
        public bool HasTool(string toolName)
        {
            SkillCatalogView? userView;
            lock (_Sync)
            {
                userView = _UserView;
            }

            return userView != null && userView.Provider.HasTool(toolName);
        }

        /// <inheritdoc/>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            SkillCatalogView? userView;
            lock (_Sync)
            {
                userView = _UserView;
            }

            return userView == null ? ToolMutationKind.Mutating : userView.Provider.GetMutationKind(toolName);
        }

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            SkillCatalogView? view = GetView(workingDirectory);
            if (view == null)
            {
                return new ToolResult
                {
                    ToolCallId = toolName,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "skills_unavailable", message = "No skills are loaded." })
                };
            }

            return await view.Provider.ExecuteAsync(toolName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (_Sync)
            {
                if (_Disposed)
                {
                    return;
                }

                _Disposed = true;
            }

            _Cts.Cancel();
            try { _Loop?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
            _Cts.Dispose();
            _Gate.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync(CancellationToken cancellationToken)
        {
            await RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_Interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task RefreshAsync(bool force, CancellationToken cancellationToken)
        {
            try
            {
                await _Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested || _Disposed)
                {
                    return;
                }

                IReadOnlyList<Skill> skills;
                try
                {
                    SkillLoader loader = new SkillLoader(_SkillsDirectory, SkillScopeEnum.User);
                    skills = await loader.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    skills = new List<Skill>();
                }

                ApplyEnablement(skills);

                string signature = ComputeSignature(skills);
                bool userChanged = force || !string.Equals(signature, _Signature, StringComparison.Ordinal);
                bool projectChanged = RefreshProjectViews(userChanged ? skills : null);

                if (!userChanged)
                {
                    if (projectChanged)
                    {
                        NotifyChanged();
                    }

                    return;
                }

                SkillCatalog catalog = new SkillCatalog(skills);
                SkillCatalogView userView = new SkillCatalogView(catalog, new SkillToolProvider(catalog, _Executor), null, ProjectTrustLevelEnum.Unknown, signature);
                List<SkillStatus> status = new List<SkillStatus>(catalog.GetStatus());

                lock (_Sync)
                {
                    _UserSkills = skills;
                    _UserView = userView;
                    _Status = status;
                    _Signature = signature;
                    _ProjectViews.Clear();
                }

                _FirstRefresh.TrySetResult(true);
                NotifyChanged();
            }
            finally
            {
                if (!_Disposed)
                {
                    try { _Gate.Release(); } catch (Exception) { }
                }
            }
        }

        // Re-reads every cached project view. When the user skills changed (newUserSkills non-null) the cache
        // is dropped instead, because every view embeds the old user set. Returns whether any project's
        // skills changed, so the caller can tell listeners.
        private bool RefreshProjectViews(IReadOnlyList<Skill>? newUserSkills)
        {
            List<KeyValuePair<string, SkillCatalogView>> cached;
            lock (_Sync)
            {
                if (newUserSkills != null || !_ProjectSkillsEnabled || _UserView == null)
                {
                    return false;
                }

                cached = new List<KeyValuePair<string, SkillCatalogView>>(_ProjectViews);
            }

            bool changed = false;
            foreach (KeyValuePair<string, SkillCatalogView> entry in cached)
            {
                SkillCatalogView rebuilt = BuildProjectView(entry.Key);
                if (!string.Equals(rebuilt.Signature, entry.Value.Signature, StringComparison.Ordinal))
                {
                    changed = true;
                }

                lock (_Sync)
                {
                    _ProjectViews[entry.Key] = rebuilt;
                }
            }

            return changed;
        }

        private SkillCatalogView BuildProjectView(string projectRoot)
        {
            IReadOnlyList<Skill> userSkills;
            List<string> roots;
            ProjectTrustStore? store;
            bool trustAll;
            lock (_Sync)
            {
                userSkills = _UserSkills;
                roots = new List<string>(_ProjectSkillRoots);
                store = _TrustStore;
                trustAll = _TrustAllProjects;
            }

            ProjectTrustLevelEnum trust = trustAll
                ? ProjectTrustLevelEnum.All
                : (store?.GetLevel(projectRoot) ?? ProjectTrustLevelEnum.Unknown);

            List<Skill> projectSkills = new List<Skill>();
            if (trust != ProjectTrustLevelEnum.Ignore)
            {
                string userDirectory = SafeFullPath(_SkillsDirectory);
                foreach (string relative in roots)
                {
                    string directory = SafeFullPath(Path.Combine(projectRoot, relative));
                    if (directory.Length == 0
                        || !Directory.Exists(directory)
                        || string.Equals(directory, userDirectory, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                    {
                        // The user skills directory is never re-read as a project root (for example when mux
                        // runs in the home directory, where ".mux/skills" is the user library itself).
                        continue;
                    }

                    try
                    {
                        foreach (Skill skill in new SkillLoader(directory, SkillScopeEnum.Project).Discover())
                        {
                            if (skill.Manifest.Commands.Count > 0 && trust != ProjectTrustLevelEnum.All)
                            {
                                skill.CommandsBlocked = true;
                            }

                            projectSkills.Add(skill);
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // An unreadable project skill root contributes nothing.
                    }
                }
            }

            ApplyEnablement(projectSkills);

            // The user skills are shared across views, so the merge must not mutate them: ShadowsUserSkill is
            // only ever set on project skills, which belong to this view alone.
            SkillCatalog catalog = SkillCatalog.Merge(projectSkills, userSkills);
            string signature = ComputeSignature(catalog.All) + "#" + ProjectTrustStore.ToWireName(trust);
            return new SkillCatalogView(catalog, new SkillToolProvider(catalog, _Executor), projectRoot, trust, signature);
        }

        private void NotifyChanged()
        {
            try
            {
                _OnSkillsChanged();
            }
            catch (Exception)
            {
            }
        }

        private void ApplyEnablement(IReadOnlyList<Skill> skills)
        {
            List<SkillIndexEntry> index;
            try
            {
                index = _LoadIndex() ?? new List<SkillIndexEntry>();
            }
            catch (Exception)
            {
                index = new List<SkillIndexEntry>();
            }

            Dictionary<string, SkillIndexEntry> byId = new Dictionary<string, SkillIndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (SkillIndexEntry entry in index)
            {
                if (!string.IsNullOrWhiteSpace(entry.Id))
                {
                    byId[entry.Id] = entry;
                }
            }

            foreach (Skill skill in skills)
            {
                if (byId.TryGetValue(skill.Manifest.Name, out SkillIndexEntry? entry))
                {
                    skill.Manifest.Enabled = entry.Enabled;
                }
            }
        }

        private static string SafeFullPath(string path)
        {
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return string.Empty;
            }
        }

        private static string ComputeSignature(IReadOnlyList<Skill> skills)
        {
            StringBuilder builder = new StringBuilder();
            foreach (Skill skill in skills)
            {
                builder.Append(skill.Manifest.Name);
                builder.Append('|');
                builder.Append(skill.Scope == SkillScopeEnum.Project ? 'p' : 'u');
                builder.Append('|');
                builder.Append(skill.Manifest.Version);
                builder.Append('|');
                builder.Append(skill.Manifest.Enabled ? '1' : '0');
                builder.Append('|');
                builder.Append(skill.IsValid ? '1' : '0');
                builder.Append('|');
                builder.Append(skill.CommandsBlocked ? '1' : '0');
                builder.Append('|');
                builder.Append(skill.Manifest.Commands.Count);
                builder.Append('|');
                builder.Append(skill.Manifest.Description.GetHashCode());
                builder.Append(';');
            }

            return builder.ToString();
        }

        #endregion
    }
}

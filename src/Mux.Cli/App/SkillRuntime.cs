namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Mux.Core.Tools;

    /// <summary>
    /// Owns the interactive session's skills: it discovers them from the skills directory, applies the
    /// enablement overrides from the skills index, and re-scans on a periodic timer. Discovery builds a
    /// fresh catalog and tool provider off to the side and swaps them in atomically, so an in-flight
    /// <c>run_skill</c> never observes a half-loaded set. The runtime itself is the
    /// <see cref="IExternalToolProvider"/> the agent loop consumes; internal swaps are invisible to the loop.
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

        private SkillToolProvider? _Provider;
        private SkillCatalog? _Catalog;
        private List<SkillStatus> _Status = new List<SkillStatus>();
        private string _Signature = string.Empty;
        private Task? _Loop;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillRuntime"/> class.
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
        }

        #endregion

        #region Public-Members

        /// <inheritdoc/>
        public string Name => "skills";

        /// <summary>
        /// A task that completes after the first discovery finishes.
        /// </summary>
        public Task FirstRefreshCompleted => _FirstRefresh.Task;

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
        /// </summary>
        public void RequestRefresh()
        {
            if (_Disposed)
            {
                return;
            }

            _ = RefreshAsync(force: true, _Cts.Token);
        }

        /// <summary>
        /// Returns a detached status snapshot for every skill in the library.
        /// </summary>
        /// <returns>The per-skill status list.</returns>
        public List<SkillStatus> GetStatus()
        {
            lock (_Sync)
            {
                List<SkillStatus> copy = new List<SkillStatus>(_Status.Count);
                foreach (SkillStatus status in _Status)
                {
                    copy.Add(new SkillStatus
                    {
                        Name = status.Name,
                        Title = status.Title,
                        Enabled = status.Enabled,
                        Valid = status.Valid,
                        CommandCount = status.CommandCount,
                        Tags = new List<string>(status.Tags),
                        Error = status.Error
                    });
                }

                return copy;
            }
        }

        /// <summary>
        /// Builds the system-prompt section that lists the enabled, valid skills so the model knows they
        /// exist and how to call them. Returns an empty string when there are none.
        /// </summary>
        /// <returns>The prompt section (leading with a blank line), or an empty string.</returns>
        public string BuildPromptSection()
        {
            SkillCatalog? catalog;
            lock (_Sync)
            {
                catalog = _Catalog;
            }

            if (catalog == null)
            {
                return string.Empty;
            }

            IReadOnlyList<Skill> skills = catalog.GetEnabledValidSkills();
            if (skills.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("\n\nThe following skills are available. Call the `skill` tool with a skill's name to read its ");
            builder.Append("instructions, then `run_skill` to execute one of its commands:\n");
            foreach (Skill skill in skills)
            {
                builder.Append($"- {skill.Manifest.Name}: {skill.Manifest.Description}\n");
            }

            return builder.ToString().TrimEnd();
        }

        /// <inheritdoc/>
        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            SkillToolProvider? provider;
            lock (_Sync)
            {
                provider = _Provider;
            }

            return provider == null ? new List<ToolDefinition>() : provider.GetToolDefinitions();
        }

        /// <inheritdoc/>
        public bool HasTool(string toolName)
        {
            SkillToolProvider? provider;
            lock (_Sync)
            {
                provider = _Provider;
            }

            return provider != null && provider.HasTool(toolName);
        }

        /// <inheritdoc/>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            SkillToolProvider? provider;
            lock (_Sync)
            {
                provider = _Provider;
            }

            return provider == null ? ToolMutationKind.Mutating : provider.GetMutationKind(toolName);
        }

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            SkillToolProvider? provider;
            lock (_Sync)
            {
                provider = _Provider;
            }

            if (provider == null)
            {
                return new ToolResult
                {
                    ToolCallId = toolName,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "skills_unavailable", message = "No skills are loaded." })
                };
            }

            return await provider.ExecuteAsync(toolName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
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
                    SkillLoader loader = new SkillLoader(_SkillsDirectory);
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
                if (!force && string.Equals(signature, _Signature, StringComparison.Ordinal))
                {
                    return;
                }

                SkillCatalog catalog = new SkillCatalog(skills);
                SkillToolProvider provider = new SkillToolProvider(catalog, _Executor);
                List<SkillStatus> status = new List<SkillStatus>(catalog.GetStatus());

                lock (_Sync)
                {
                    _Catalog = catalog;
                    _Provider = provider;
                    _Status = status;
                    _Signature = signature;
                }

                _FirstRefresh.TrySetResult(true);

                try
                {
                    _OnSkillsChanged();
                }
                catch (Exception)
                {
                }
            }
            finally
            {
                if (!_Disposed)
                {
                    try { _Gate.Release(); } catch (Exception) { }
                }
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

        private static string ComputeSignature(IReadOnlyList<Skill> skills)
        {
            StringBuilder builder = new StringBuilder();
            foreach (Skill skill in skills)
            {
                builder.Append(skill.Manifest.Name);
                builder.Append('|');
                builder.Append(skill.Manifest.Version);
                builder.Append('|');
                builder.Append(skill.Manifest.Enabled ? '1' : '0');
                builder.Append('|');
                builder.Append(skill.IsValid ? '1' : '0');
                builder.Append('|');
                builder.Append(skill.Manifest.Commands.Count);
                builder.Append(';');
            }

            return builder.ToString();
        }

        #endregion
    }
}

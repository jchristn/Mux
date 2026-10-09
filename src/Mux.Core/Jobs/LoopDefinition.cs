namespace Mux.Core.Jobs
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One recurring prompt: its text, its pacing (a fixed interval, or self-paced through the
    /// <c>schedule_next</c> tool), its iteration cap, and its progress. Serializable so active loops persist with
    /// the session; a restored loop comes back paused.
    /// </summary>
    public sealed class LoopDefinition
    {
        #region Private-Members

        private string _Id = string.Empty;
        private string _Prompt = string.Empty;
        private int? _IntervalSeconds = null;
        private int _MaxIterations = 1;
        private int _IterationCount = 0;

        #endregion

        #region Public-Members

        /// <summary>
        /// The short loop identifier shown in <c>/loops</c> (for example <c>L1</c>).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// The prompt submitted on every iteration.
        /// </summary>
        public string Prompt
        {
            get => _Prompt;
            set => _Prompt = value ?? string.Empty;
        }

        /// <summary>
        /// The fixed interval between iteration starts, in seconds, or null for a self-paced loop.
        /// </summary>
        public int? IntervalSeconds
        {
            get => _IntervalSeconds;
            set => _IntervalSeconds = value.HasValue && value.Value > 0 ? value : null;
        }

        /// <summary>
        /// The number of iterations after which the loop completes. At least one.
        /// </summary>
        public int MaxIterations
        {
            get => _MaxIterations;
            set => _MaxIterations = Math.Max(1, value);
        }

        /// <summary>
        /// The number of iterations started so far.
        /// </summary>
        public int IterationCount
        {
            get => _IterationCount;
            set => _IterationCount = Math.Max(0, value);
        }

        /// <summary>
        /// The loop's state.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public LoopStatusEnum Status { get; set; } = LoopStatusEnum.Scheduled;

        /// <summary>
        /// When the loop was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// When the next iteration is due (UTC), or null when the loop is not scheduled.
        /// </summary>
        public DateTime? NextFireUtc { get; set; }

        /// <summary>
        /// When the most recent iteration started (UTC), or null before the first one.
        /// </summary>
        public DateTime? LastFiredUtc { get; set; }

        /// <summary>
        /// Fixed-interval fire times skipped because an iteration ran past them. Iterations never overlap or
        /// catch up in a burst.
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// The reason the model gave with its last <c>schedule_next</c> call, or null.
        /// </summary>
        public string? LastReason { get; set; }

        /// <summary>
        /// Why the loop stopped, completed, or was paused, or null while it is scheduled or running.
        /// </summary>
        public string? StatusReason { get; set; }

        /// <summary>
        /// Whether the loop is self-paced (no fixed interval).
        /// </summary>
        [JsonIgnore]
        public bool IsSelfPaced => !_IntervalSeconds.HasValue;

        /// <summary>
        /// Whether the loop can still fire (scheduled, running, or paused).
        /// </summary>
        [JsonIgnore]
        public bool IsActive => Status == LoopStatusEnum.Scheduled || Status == LoopStatusEnum.Running || Status == LoopStatusEnum.Paused;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns a copy of this loop.
        /// </summary>
        /// <returns>The copy.</returns>
        public LoopDefinition Clone()
        {
            return (LoopDefinition)MemberwiseClone();
        }

        #endregion
    }
}

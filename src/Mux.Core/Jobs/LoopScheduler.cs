namespace Mux.Core.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Owns the recurring prompts (<c>/loop</c>) of one session. A loop fires on a fixed interval, or is
    /// self-paced: each iteration ends with the model calling <c>schedule_next</c> with a delay or a stop, and an
    /// iteration that ends without that call stops the loop, so a model that never stops cannot run away. Every
    /// loop has an iteration cap. A running loop never fires again until its iteration ends, and fixed-interval
    /// fire times missed while an iteration ran are skipped rather than replayed in a burst.
    /// </summary>
    /// <remarks>
    /// The scheduler is pure state plus a clock: callers ask it which loops are due (<see cref="GetDue"/>), start
    /// one (<see cref="TryBegin"/>), run the iteration prompt as a normal turn, and report the end
    /// (<see cref="Complete"/>). That keeps the write lease, approvals, and transcripts identical to typed
    /// prompts. All members are thread-safe.
    /// </remarks>
    public sealed class LoopScheduler
    {
        #region Private-Members

        private readonly object _Sync = new object();
        private readonly TimeProvider _Clock;
        private readonly List<LoopDefinition> _Loops = new List<LoopDefinition>();
        private readonly Dictionary<string, LoopDecision> _Decisions = new Dictionary<string, LoopDecision>(StringComparer.OrdinalIgnoreCase);
        private int _MaxIterations;
        private int _MinIntervalSeconds;
        private int _NextNumber = 1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="LoopScheduler"/> class.
        /// </summary>
        /// <param name="maxIterations">The default and maximum iteration cap (clamped to 1..<see cref="MaxIterationsLimit"/>).</param>
        /// <param name="minIntervalSeconds">The shortest allowed interval and <c>schedule_next</c> delay, in seconds (clamped to 1..<see cref="MaxDelaySeconds"/>).</param>
        /// <param name="clock">The clock; defaults to the system clock. Tests pass a fake.</param>
        public LoopScheduler(int maxIterations = DefaultMaxIterations, int minIntervalSeconds = DefaultMinIntervalSeconds, TimeProvider? clock = null)
        {
            _Clock = clock ?? TimeProvider.System;
            Configure(maxIterations, minIntervalSeconds);
        }

        #endregion

        #region Public-Members

        /// <summary>The default iteration cap.</summary>
        public const int DefaultMaxIterations = 50;

        /// <summary>The highest iteration cap a setting may choose.</summary>
        public const int MaxIterationsLimit = 1000;

        /// <summary>The default shortest interval, in seconds.</summary>
        public const int DefaultMinIntervalSeconds = 30;

        /// <summary>The longest delay <c>schedule_next</c> accepts, in seconds.</summary>
        public const int MaxDelaySeconds = 3600;

        /// <summary>
        /// Raised (outside the lock) whenever a loop is created, fired, completed, paused, resumed, or stopped.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>The iteration cap applied when a loop does not ask for one, and the most any loop may ask for.</summary>
        public int MaxIterations
        {
            get { lock (_Sync) { return _MaxIterations; } }
        }

        /// <summary>The shortest allowed interval and <c>schedule_next</c> delay, in seconds.</summary>
        public int MinIntervalSeconds
        {
            get { lock (_Sync) { return _MinIntervalSeconds; } }
        }

        /// <summary>The current time according to the scheduler's clock.</summary>
        public DateTime UtcNow => _Clock.GetUtcNow().UtcDateTime;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Updates the limits (for example after a settings change). Existing loops keep their own caps.
        /// </summary>
        /// <param name="maxIterations">The new default and maximum iteration cap.</param>
        /// <param name="minIntervalSeconds">The new shortest interval, in seconds.</param>
        public void Configure(int maxIterations, int minIntervalSeconds)
        {
            lock (_Sync)
            {
                _MaxIterations = Math.Clamp(maxIterations, 1, MaxIterationsLimit);
                _MinIntervalSeconds = Math.Clamp(minIntervalSeconds, 1, MaxDelaySeconds);
            }
        }

        /// <summary>
        /// Creates a loop that fires its first iteration immediately.
        /// </summary>
        /// <param name="prompt">The prompt to repeat. Must not be blank.</param>
        /// <param name="interval">The fixed interval, or null for a self-paced loop.</param>
        /// <param name="maxIterations">The iteration cap, or null for the default. Values above the maximum are lowered to it.</param>
        /// <returns>A copy of the new loop.</returns>
        /// <exception cref="ArgumentException">Thrown when the prompt is blank.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the interval is shorter than the minimum or the cap is below one.</exception>
        public LoopDefinition Create(string prompt, TimeSpan? interval, int? maxIterations = null)
        {
            if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("A loop needs a prompt.", nameof(prompt));
            if (maxIterations.HasValue && maxIterations.Value < 1) throw new ArgumentOutOfRangeException(nameof(maxIterations), "A loop needs at least one iteration.");

            LoopDefinition copy;
            lock (_Sync)
            {
                if (interval.HasValue && interval.Value.TotalSeconds < _MinIntervalSeconds)
                {
                    throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be at least " + LoopCommand.FormatInterval(TimeSpan.FromSeconds(_MinIntervalSeconds)) + " (setting loopMinIntervalSeconds).");
                }

                DateTime now = UtcNow;
                LoopDefinition loop = new LoopDefinition
                {
                    Id = "L" + (_NextNumber++).ToString(CultureInfo.InvariantCulture),
                    Prompt = prompt.Trim(),
                    IntervalSeconds = interval.HasValue ? (int)Math.Round(interval.Value.TotalSeconds) : null,
                    MaxIterations = Math.Min(maxIterations ?? _MaxIterations, _MaxIterations),
                    Status = LoopStatusEnum.Scheduled,
                    CreatedUtc = now,
                    NextFireUtc = now
                };
                _Loops.Add(loop);
                copy = loop.Clone();
            }

            OnChanged();
            return copy;
        }

        /// <summary>
        /// Returns copies of every loop, oldest first.
        /// </summary>
        /// <returns>The loops.</returns>
        public IReadOnlyList<LoopDefinition> List()
        {
            lock (_Sync)
            {
                return _Loops.ConvertAll(l => l.Clone());
            }
        }

        /// <summary>
        /// Returns copies of the loops that can still fire (scheduled, running, or paused), oldest first. This is
        /// what persists with a session.
        /// </summary>
        /// <returns>The active loops.</returns>
        public List<LoopDefinition> Snapshot()
        {
            lock (_Sync)
            {
                return _Loops.FindAll(l => l.IsActive).ConvertAll(l => l.Clone());
            }
        }

        /// <summary>
        /// Returns a copy of one loop.
        /// </summary>
        /// <param name="id">The loop id (case-insensitive).</param>
        /// <returns>The loop, or null when no loop has that id.</returns>
        public LoopDefinition? Get(string id)
        {
            lock (_Sync)
            {
                return Find(id)?.Clone();
            }
        }

        /// <summary>
        /// Returns the scheduled loops whose fire time has arrived, earliest first. Running, paused, and finished
        /// loops are never due.
        /// </summary>
        /// <returns>The due loops (copies).</returns>
        public IReadOnlyList<LoopDefinition> GetDue()
        {
            DateTime now = UtcNow;
            lock (_Sync)
            {
                List<LoopDefinition> due = _Loops.FindAll(l => l.Status == LoopStatusEnum.Scheduled && l.NextFireUtc.HasValue && l.NextFireUtc.Value <= now);
                due.Sort((a, b) => Nullable.Compare(a.NextFireUtc, b.NextFireUtc));
                return due.ConvertAll(l => l.Clone());
            }
        }

        /// <summary>
        /// The earliest fire time among scheduled loops, or null when none is scheduled.
        /// </summary>
        /// <returns>The next fire time (UTC).</returns>
        public DateTime? GetNextFireUtc()
        {
            lock (_Sync)
            {
                DateTime? next = null;
                foreach (LoopDefinition loop in _Loops)
                {
                    if (loop.Status == LoopStatusEnum.Scheduled && loop.NextFireUtc.HasValue && (!next.HasValue || loop.NextFireUtc.Value < next.Value))
                    {
                        next = loop.NextFireUtc;
                    }
                }

                return next;
            }
        }

        /// <summary>
        /// Whether any loop is scheduled or running (paused loops do not count, since nothing will fire them).
        /// </summary>
        /// <returns>True while some loop can still fire without user action.</returns>
        public bool HasLiveLoops()
        {
            lock (_Sync)
            {
                return _Loops.Exists(l => l.Status == LoopStatusEnum.Scheduled || l.Status == LoopStatusEnum.Running);
            }
        }

        /// <summary>
        /// Starts an iteration of a due loop: marks it running and counts the iteration.
        /// </summary>
        /// <param name="id">The loop id.</param>
        /// <returns>A copy of the running loop, or null when the loop is not scheduled (for example already running).</returns>
        public LoopDefinition? TryBegin(string id)
        {
            LoopDefinition? copy;
            lock (_Sync)
            {
                LoopDefinition? loop = Find(id);
                if (loop == null || loop.Status != LoopStatusEnum.Scheduled)
                {
                    return null;
                }

                loop.Status = LoopStatusEnum.Running;
                loop.IterationCount++;
                loop.LastFiredUtc = UtcNow;
                loop.NextFireUtc = null;
                loop.StatusReason = null;
                _Decisions.Remove(loop.Id);
                copy = loop.Clone();
            }

            OnChanged();
            return copy;
        }

        /// <summary>
        /// Records the model's <c>schedule_next</c> decision for a running self-paced loop. It takes effect when
        /// the iteration ends.
        /// </summary>
        /// <param name="id">The loop id, or null for the only running self-paced loop.</param>
        /// <param name="delaySeconds">The delay before the next iteration, when not stopping.</param>
        /// <param name="stop">True to stop the loop instead.</param>
        /// <param name="reason">A one-line reason.</param>
        /// <param name="message">A confirmation, or why the decision was refused.</param>
        /// <returns>True when the decision was recorded.</returns>
        public bool TryRecordDecision(string? id, int? delaySeconds, bool stop, string? reason, out string message)
        {
            lock (_Sync)
            {
                LoopDefinition? loop;
                if (string.IsNullOrWhiteSpace(id))
                {
                    List<LoopDefinition> running = _Loops.FindAll(l => l.Status == LoopStatusEnum.Running && l.IsSelfPaced);
                    if (running.Count != 1)
                    {
                        message = running.Count == 0
                            ? "No self-paced loop iteration is running, so there is nothing to schedule."
                            : "More than one self-paced loop is running; pass loop_id.";
                        return false;
                    }

                    loop = running[0];
                }
                else
                {
                    loop = Find(id!);
                    if (loop == null)
                    {
                        message = "No loop has the id '" + id + "'.";
                        return false;
                    }

                    if (loop.Status != LoopStatusEnum.Running || !loop.IsSelfPaced)
                    {
                        message = "Loop " + loop.Id + " is not a running self-paced loop.";
                        return false;
                    }
                }

                if (!stop)
                {
                    if (!delaySeconds.HasValue)
                    {
                        message = "Pass delay_seconds, or stop: true.";
                        return false;
                    }

                    if (delaySeconds.Value < _MinIntervalSeconds || delaySeconds.Value > MaxDelaySeconds)
                    {
                        message = "delay_seconds must be between " + _MinIntervalSeconds.ToString(CultureInfo.InvariantCulture) + " and " + MaxDelaySeconds.ToString(CultureInfo.InvariantCulture) + ".";
                        return false;
                    }
                }

                string trimmedReason = (reason ?? string.Empty).Trim();
                if (trimmedReason.Length > 200)
                {
                    trimmedReason = trimmedReason.Substring(0, 200);
                }

                _Decisions[loop.Id] = new LoopDecision(stop, delaySeconds ?? 0, trimmedReason);
                message = stop
                    ? "Loop " + loop.Id + " will stop when this iteration ends."
                    : "Loop " + loop.Id + " will run again " + LoopCommand.FormatInterval(TimeSpan.FromSeconds(delaySeconds!.Value)) + " after this iteration ends.";
                return true;
            }
        }

        /// <summary>
        /// Ends a running iteration and schedules the next one: the interval for a fixed loop (skipping fire
        /// times that already passed), the recorded <c>schedule_next</c> delay for a self-paced loop, or a stop
        /// when the cap is reached, the model asked to stop, a self-paced iteration made no decision, or a pause or
        /// cancel was requested while it ran.
        /// </summary>
        /// <param name="id">The loop id.</param>
        /// <param name="failed">True when the iteration's turn failed or was cancelled; the loop then pauses.</param>
        /// <returns>A copy of the updated loop, or null when the loop is not running.</returns>
        public LoopDefinition? Complete(string id, bool failed = false)
        {
            LoopDefinition? copy;
            lock (_Sync)
            {
                LoopDefinition? loop = Find(id);
                if (loop == null || loop.Status != LoopStatusEnum.Running)
                {
                    if (loop != null)
                    {
                        _Decisions.Remove(loop.Id);
                    }

                    return loop?.Clone();
                }

                DateTime now = UtcNow;
                _Decisions.TryGetValue(loop.Id, out LoopDecision? decision);
                _Decisions.Remove(loop.Id);
                if (decision != null && decision.Reason.Length > 0)
                {
                    loop.LastReason = decision.Reason;
                }

                if (failed)
                {
                    loop.Status = LoopStatusEnum.Paused;
                    loop.StatusReason = "the iteration failed or was cancelled; resume with /loops resume " + loop.Id;
                }
                else if (loop.IterationCount >= loop.MaxIterations)
                {
                    loop.Status = LoopStatusEnum.Completed;
                    loop.StatusReason = "reached the iteration cap (" + loop.MaxIterations.ToString(CultureInfo.InvariantCulture) + ")";
                }
                else if (loop.IsSelfPaced)
                {
                    if (decision == null)
                    {
                        loop.Status = LoopStatusEnum.Stopped;
                        loop.StatusReason = "the iteration ended without calling schedule_next";
                    }
                    else if (decision.Stop)
                    {
                        loop.Status = LoopStatusEnum.Stopped;
                        loop.StatusReason = "the model stopped the loop" + (decision.Reason.Length > 0 ? ": " + decision.Reason : string.Empty);
                    }
                    else
                    {
                        loop.Status = LoopStatusEnum.Scheduled;
                        loop.NextFireUtc = now.AddSeconds(decision.DelaySeconds);
                    }
                }
                else
                {
                    TimeSpan interval = TimeSpan.FromSeconds(loop.IntervalSeconds!.Value);
                    DateTime next = (loop.LastFiredUtc ?? now) + interval;
                    while (next <= now)
                    {
                        next += interval;
                        loop.SkippedCount++;
                    }

                    loop.Status = LoopStatusEnum.Scheduled;
                    loop.NextFireUtc = next;
                }

                if (loop.Status != LoopStatusEnum.Scheduled)
                {
                    loop.NextFireUtc = null;
                }

                copy = loop.Clone();
            }

            OnChanged();
            return copy;
        }

        /// <summary>
        /// Stops a loop. A running iteration finishes, but nothing fires after it.
        /// </summary>
        /// <param name="id">The loop id.</param>
        /// <returns>True when an active loop was stopped.</returns>
        public bool Cancel(string id)
        {
            lock (_Sync)
            {
                LoopDefinition? loop = Find(id);
                if (loop == null || !loop.IsActive)
                {
                    return false;
                }

                loop.Status = LoopStatusEnum.Stopped;
                loop.StatusReason = "cancelled";
                loop.NextFireUtc = null;
                _Decisions.Remove(loop.Id);
            }

            OnChanged();
            return true;
        }

        /// <summary>
        /// Stops every active loop.
        /// </summary>
        /// <returns>The number of loops stopped.</returns>
        public int CancelAll()
        {
            int count = 0;
            foreach (LoopDefinition loop in List())
            {
                if (Cancel(loop.Id))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Pauses a scheduled loop. A running loop cannot be paused until its iteration ends.
        /// </summary>
        /// <param name="id">The loop id.</param>
        /// <returns>True when the loop was paused.</returns>
        public bool Pause(string id)
        {
            lock (_Sync)
            {
                LoopDefinition? loop = Find(id);
                if (loop == null || loop.Status != LoopStatusEnum.Scheduled)
                {
                    return false;
                }

                loop.Status = LoopStatusEnum.Paused;
                loop.StatusReason = "paused";
                loop.NextFireUtc = null;
            }

            OnChanged();
            return true;
        }

        /// <summary>
        /// Resumes a paused loop. Its next iteration fires immediately.
        /// </summary>
        /// <param name="id">The loop id.</param>
        /// <returns>True when the loop was resumed.</returns>
        public bool Resume(string id)
        {
            lock (_Sync)
            {
                LoopDefinition? loop = Find(id);
                if (loop == null || loop.Status != LoopStatusEnum.Paused)
                {
                    return false;
                }

                if (loop.IterationCount >= loop.MaxIterations)
                {
                    loop.Status = LoopStatusEnum.Completed;
                    loop.StatusReason = "reached the iteration cap (" + loop.MaxIterations.ToString(CultureInfo.InvariantCulture) + ")";
                }
                else
                {
                    loop.Status = LoopStatusEnum.Scheduled;
                    loop.StatusReason = null;
                    loop.NextFireUtc = UtcNow;
                }
            }

            OnChanged();
            return true;
        }

        /// <summary>
        /// Replaces the loops with ones persisted in a session. Active loops come back paused (a resumed session
        /// never starts running prompts on its own); finished loops are dropped.
        /// </summary>
        /// <param name="loops">The persisted loops, or null for none.</param>
        /// <returns>The number of loops restored.</returns>
        public int Restore(IEnumerable<LoopDefinition>? loops)
        {
            int count = 0;
            lock (_Sync)
            {
                _Loops.Clear();
                _Decisions.Clear();
                if (loops != null)
                {
                    foreach (LoopDefinition persisted in loops)
                    {
                        if (persisted == null || !persisted.IsActive || string.IsNullOrWhiteSpace(persisted.Prompt) || string.IsNullOrWhiteSpace(persisted.Id))
                        {
                            continue;
                        }

                        LoopDefinition loop = persisted.Clone();
                        loop.Status = LoopStatusEnum.Paused;
                        loop.StatusReason = "restored with the session; resume with /loops resume " + loop.Id;
                        loop.NextFireUtc = null;
                        loop.MaxIterations = Math.Min(loop.MaxIterations, MaxIterationsLimit);
                        _Loops.Add(loop);
                        count++;
                        if (loop.Id.Length > 1 && loop.Id[0] == 'L' && int.TryParse(loop.Id.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= _NextNumber)
                        {
                            _NextNumber = number + 1;
                        }
                    }
                }
            }

            OnChanged();
            return count;
        }

        /// <summary>
        /// Builds the prompt submitted for one iteration: the loop's prompt plus a short header, and for a
        /// self-paced loop the instruction to finish with <c>schedule_next</c>.
        /// </summary>
        /// <param name="loop">The running loop.</param>
        /// <returns>The iteration prompt.</returns>
        public string BuildIterationPrompt(LoopDefinition loop)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));

            StringBuilder builder = new StringBuilder();
            builder.Append(loop.Prompt);
            builder.Append("\n\n[mux loop ").Append(loop.Id)
                .Append(", iteration ").Append(loop.IterationCount.ToString(CultureInfo.InvariantCulture))
                .Append(" of at most ").Append(loop.MaxIterations.ToString(CultureInfo.InvariantCulture));
            if (loop.IsSelfPaced)
            {
                builder.Append(". You pace this loop. When this iteration's work is done, call schedule_next with delay_seconds (")
                    .Append(MinIntervalSeconds.ToString(CultureInfo.InvariantCulture)).Append(" to ").Append(MaxDelaySeconds.ToString(CultureInfo.InvariantCulture))
                    .Append(") and a one-line reason to run again, or with stop: true when the goal is met or nothing more can be done. If you do not call it, the loop stops.]");
            }
            else
            {
                builder.Append(", repeating every ").Append(LoopCommand.FormatInterval(TimeSpan.FromSeconds(loop.IntervalSeconds!.Value)))
                    .Append(". Report what changed since the last iteration.]");
            }

            return builder.ToString();
        }

        /// <summary>
        /// Describes a loop on one line for <c>/loops</c> and notices.
        /// </summary>
        /// <param name="loop">The loop.</param>
        /// <param name="now">The current time (UTC), used for the time until the next fire.</param>
        /// <returns>The description.</returns>
        public static string Describe(LoopDefinition loop, DateTime now)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));

            string pacing = loop.IsSelfPaced ? "self-paced" : "every " + LoopCommand.FormatInterval(TimeSpan.FromSeconds(loop.IntervalSeconds!.Value));
            string state = loop.Status switch
            {
                LoopStatusEnum.Scheduled => loop.NextFireUtc.HasValue && loop.NextFireUtc.Value > now
                    ? "next in " + LoopCommand.FormatInterval(loop.NextFireUtc.Value - now)
                    : "due now",
                LoopStatusEnum.Running => "running",
                LoopStatusEnum.Paused => "paused",
                LoopStatusEnum.Stopped => "stopped",
                _ => "completed"
            };

            string prompt = loop.Prompt.Replace('\n', ' ');
            if (prompt.Length > 60)
            {
                prompt = prompt.Substring(0, 57) + "...";
            }

            return loop.Id + "  " + pacing + "  " + loop.IterationCount.ToString(CultureInfo.InvariantCulture) + "/" + loop.MaxIterations.ToString(CultureInfo.InvariantCulture)
                + "  " + state + "  " + prompt;
        }

        #endregion

        #region Private-Methods

        private LoopDefinition? Find(string id)
        {
            return _Loops.Find(l => string.Equals(l.Id, (id ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}

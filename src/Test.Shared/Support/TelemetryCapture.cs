namespace Test.Shared.Support
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Observability;

    /// <summary>
    /// An in-memory telemetry collector for tests: an <see cref="ActivityListener"/> and a
    /// <see cref="MeterListener"/> subscribed to the <c>Mux</c> and <c>Watson</c> sources plus a test-only
    /// <c>Mux.Tests</c> source used to open a root span. Because listeners are process-wide, assertions should
    /// scope spans to the test's own trace (<see cref="SpansInTrace"/>) and match metrics by distinctive labels.
    /// </summary>
    public sealed class TelemetryCapture : IDisposable
    {
        #region Private-Members

        // Declared before _TestSource: constructing an ActivitySource consults every live listener, so the
        // name set must already exist when the source is created.
        private static readonly HashSet<string> _Sources = new HashSet<string>(StringComparer.Ordinal)
        {
            MuxTelemetryNames.ActivitySourceName,
            "Watson",
            "Mux.Tests"
        };
        private static readonly ActivitySource _TestSource = new ActivitySource("Mux.Tests");

        private readonly ActivityListener _ActivityListener;
        private readonly MeterListener _MeterListener;
        private readonly ConcurrentQueue<Activity> _Spans = new ConcurrentQueue<Activity>();
        private readonly ConcurrentQueue<CapturedMeasurement> _Measurements = new ConcurrentQueue<CapturedMeasurement>();
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Starts listening.
        /// </summary>
        public TelemetryCapture()
        {
            _ActivityListener = new ActivityListener
            {
                ShouldListenTo = (ActivitySource source) => _Sources.Contains(source.Name),
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = (Activity activity) => _Spans.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(_ActivityListener);

            _MeterListener = new MeterListener();
            _MeterListener.InstrumentPublished = (Instrument instrument, MeterListener listener) =>
            {
                if (instrument.Meter.Name == MuxTelemetryNames.MeterName || instrument.Meter.Name == "Watson")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _MeterListener.SetMeasurementEventCallback<long>((Instrument i, long v, ReadOnlySpan<KeyValuePair<string, object?>> t, object? s) => Add(i, v, t));
            _MeterListener.SetMeasurementEventCallback<int>((Instrument i, int v, ReadOnlySpan<KeyValuePair<string, object?>> t, object? s) => Add(i, v, t));
            _MeterListener.SetMeasurementEventCallback<double>((Instrument i, double v, ReadOnlySpan<KeyValuePair<string, object?>> t, object? s) => Add(i, v, t));
            _MeterListener.Start();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts a root span on the test-only source; mux spans started while it is current join its trace.
        /// </summary>
        /// <param name="name">The span name.</param>
        /// <returns>The started root activity. Never null while this capture is alive.</returns>
        public Activity StartRoot(string name)
        {
            Activity? activity = _TestSource.StartActivity(name, ActivityKind.Internal, default(ActivityContext));
            if (activity == null) throw new InvalidOperationException("test activity source is not being listened to");
            return activity;
        }

        /// <summary>
        /// Stopped spans belonging to a trace.
        /// </summary>
        /// <param name="traceId">The trace id.</param>
        /// <returns>The spans, in stop order.</returns>
        public List<Activity> SpansInTrace(ActivityTraceId traceId)
        {
            return _Spans.Where((Activity a) => a.TraceId == traceId).ToList();
        }

        /// <summary>
        /// Every stopped span with the given name.
        /// </summary>
        /// <param name="name">The span display name.</param>
        /// <returns>The spans.</returns>
        public List<Activity> SpansNamed(string name)
        {
            return _Spans.Where((Activity a) => string.Equals(a.DisplayName, name, StringComparison.Ordinal)).ToList();
        }

        /// <summary>
        /// Measurements for an instrument whose tags include every <c>key=value</c> pair.
        /// </summary>
        /// <param name="name">The instrument name.</param>
        /// <param name="tagPairs">Required tag pairs.</param>
        /// <returns>The matching measurements.</returns>
        public List<CapturedMeasurement> Measurements(string name, params string[] tagPairs)
        {
            return _Measurements.Where((CapturedMeasurement m) => m.Name == name && m.HasTags(tagPairs)).ToList();
        }

        /// <summary>
        /// Whether any measurement matches.
        /// </summary>
        /// <param name="name">The instrument name.</param>
        /// <param name="tagPairs">Required tag pairs.</param>
        /// <returns>True when at least one measurement matches.</returns>
        public bool Any(string name, params string[] tagPairs)
        {
            return Measurements(name, tagPairs).Count > 0;
        }

        /// <summary>
        /// Polls observable instruments (gauges) so their current values are captured.
        /// </summary>
        public void CollectObservables()
        {
            _MeterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// Polls until a condition holds or the timeout elapses.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="timeoutMs">The timeout in milliseconds.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>True when the condition held before the timeout.</returns>
        public static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }

            return condition();
        }

        /// <summary>
        /// Stops listening.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _MeterListener.Dispose();
            _ActivityListener.Dispose();
        }

        #endregion

        #region Private-Methods

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Dictionary<string, string?> map = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                map[tag.Key] = tag.Value?.ToString();
            }

            _Measurements.Enqueue(new CapturedMeasurement(instrument.Name, value, map));
        }

        #endregion
    }
}

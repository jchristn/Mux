namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Settings;
    using Mux.Core.Telemetry;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Read-only usage-telemetry routes plus the pricing read/write surface. Summaries, time series,
    /// breakdowns, and the paginated event history all read the shared SQLite store through a
    /// <see cref="UsageQueryService"/>; when telemetry is disabled the query endpoints return empty results
    /// with an <c>enabled: false</c> signal so the dashboard shows an explanatory empty state rather than an
    /// error. Pricing is served from <c>pricing.json</c> independently of telemetry, so it works either way.
    /// </summary>
    public sealed class UsageRoutes
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly string? _ApiKey;
        private readonly UsageQueryService? _Query;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="query">The usage query service, or null when telemetry is disabled.</param>
        public UsageRoutes(string? apiKey, UsageQueryService? query)
        {
            _ApiKey = apiKey;
            _Query = query;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/usage/summary", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                if (_Query == null)
                {
                    req.Http.Response.StatusCode = 200;
                    return (object)new UsageSummary();
                }

                UsageFilter filter = BuildFilter(req.Http);
                UsageSummary summary = await _Query.GetSummaryAsync(filter, CancellationToken.None).ConfigureAwait(false);
                req.Http.Response.StatusCode = 200;
                return (object)summary;
            });

            app.Get("/v1.0/api/usage/timeseries", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                if (_Query == null)
                {
                    req.Http.Response.StatusCode = 200;
                    return (object)new ListResponse<UsageBucket>(new List<UsageBucket>());
                }

                UsageFilter filter = BuildFilter(req.Http);
                // Derive an aligned, fixed-granularity window from the range so the series has a fixed number
                // of evenly-spaced slices (60 x 1-min for the hour, 96 x 15-min for the day, 84 x 2-hour for
                // the week, 60 x 12-hour for the month). Overrides the coarse window BuildFilter set.
                long bucketMs = ResolveTimeseriesWindow(req.Http, filter);
                List<UsageBucket> buckets = await _Query.GetTimeseriesAsync(filter, bucketMs, CancellationToken.None).ConfigureAwait(false);
                req.Http.Response.StatusCode = 200;
                return (object)new ListResponse<UsageBucket>(buckets);
            });

            app.Get("/v1.0/api/usage/breakdown", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                if (_Query == null)
                {
                    req.Http.Response.StatusCode = 200;
                    return (object)new ListResponse<UsageBreakdownRow>(new List<UsageBreakdownRow>());
                }

                string dimension = req.Http.Request.Query.Elements["dimension"] ?? "model";
                UsageFilter filter = BuildFilter(req.Http);
                List<UsageBreakdownRow> rows = await _Query.GetBreakdownAsync(dimension, filter, CancellationToken.None).ConfigureAwait(false);
                req.Http.Response.StatusCode = 200;
                return (object)new ListResponse<UsageBreakdownRow>(rows);
            });

            app.Get("/v1.0/api/usage/events", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                if (_Query == null)
                {
                    req.Http.Response.StatusCode = 200;
                    return (object)new UsageEventPage();
                }

                UsageFilter filter = BuildFilter(req.Http);
                int page = ParseInt(req.Http.Request.Query.Elements["page"], 1);
                int pageSize = ParseInt(req.Http.Request.Query.Elements["pageSize"], 25);
                UsageEventPage result = await _Query.GetEventsAsync(filter, page, pageSize, CancellationToken.None).ConfigureAwait(false);
                req.Http.Response.StatusCode = 200;
                return (object)result;
            });

            app.Delete("/v1.0/api/usage/events", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                if (_Query == null)
                {
                    req.Http.Response.StatusCode = 200;
                    return (object)new ApiError("Unavailable", "Usage telemetry is disabled.");
                }

                string? idText = req.Http.Request.Query.Elements["id"];
                if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A numeric 'id' query parameter is required.");
                }

                try
                {
                    int deleted = await _Query.DeleteEventAsync(id, CancellationToken.None).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 200;
                    return (object)new UsageDeleteResult { Deleted = deleted };
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("DeleteFailed", "Failed to delete usage event: " + ex.Message);
                }
            });

            app.Get("/v1.0/api/usage/filters", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                UsageFiltersDto dto = new UsageFiltersDto { Enabled = _Query != null };
                if (_Query != null)
                {
                    dto.Endpoints = await _Query.GetEndpointsAsync(CancellationToken.None).ConfigureAwait(false);
                    dto.Models = await _Query.GetModelsAsync(CancellationToken.None).ConfigureAwait(false);
                }

                req.Http.Response.StatusCode = 200;
                return (object)dto;
            });

            app.Get("/v1.0/api/usage/pricing", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                PricingTable table = SettingsLoader.LoadPricing();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(table).ConfigureAwait(false);
            });

            app.Put("/v1.0/api/usage/pricing", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                PricingTable? table;
                try
                {
                    table = JsonSerializer.Deserialize<PricingTable>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (Exception)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (table == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A pricing table is required.");
                }

                try
                {
                    SettingsLoader.SavePricing(table);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(SettingsLoader.LoadPricing()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("SaveFailed", "Failed to save pricing: " + ex.Message);
                }
            });
        }

        #endregion

        #region Private-Methods

        private object Unauthorized()
        {
            return new ApiError("Unauthorized", "Authentication required.");
        }

        private static UsageFilter BuildFilter(WatsonWebserver.Core.HttpContextBase ctx)
        {
            UsageFilter filter = new UsageFilter();

            long from = ParseLong(ctx.Request.Query.Elements["from"], 0);
            long to = ParseLong(ctx.Request.Query.Elements["to"], 0);

            // A `range` keyword (hour/day/week/month/all) is a convenience: when explicit from/to are not
            // supplied, derive the window from the server clock. Explicit from/to always win.
            string? range = ctx.Request.Query.Elements["range"];
            if (from == 0 && to == 0 && !string.IsNullOrWhiteSpace(range))
            {
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long span = UsageWindow.NaturalSpanMs(range);
                if (span > 0)
                {
                    filter.FromUnixMs = nowMs - span;
                    filter.ToUnixMs = nowMs;
                }
            }
            else
            {
                filter.FromUnixMs = from;
                filter.ToUnixMs = to;
            }

            filter.EndpointName = NullIfBlank(ctx.Request.Query.Elements["endpoint"]);
            filter.Model = NullIfBlank(ctx.Request.Query.Elements["model"]);

            string? callKind = ctx.Request.Query.Elements["callKind"];
            if (!string.IsNullOrWhiteSpace(callKind) && Enum.TryParse(callKind, true, out UsageCallKindEnum parsedKind))
            {
                filter.CallKind = parsedKind;
            }

            string? success = ctx.Request.Query.Elements["success"];
            if (!string.IsNullOrWhiteSpace(success) && bool.TryParse(success, out bool parsedSuccess))
            {
                filter.Success = parsedSuccess;
            }

            return filter;
        }

        /// <summary>
        /// Resolves an aligned, fixed-granularity window for the time series and returns the bucket width in
        /// milliseconds. The window is snapped to the bucket grid and sized to a fixed slice count per range:
        /// hour = 60 x 1-minute, day = 96 x 15-minute, week = 84 x 2-hour, month = 60 x 12-hour. An explicit
        /// from/to (custom range) is honored with a bucket width chosen for roughly 90 slices. The resolved
        /// window is written back onto <paramref name="filter"/> (From/To), overriding the coarse window.
        /// </summary>
        private static long ResolveTimeseriesWindow(WatsonWebserver.Core.HttpContextBase ctx, UsageFilter filter)
        {
            long explicitFrom = ParseLong(ctx.Request.Query.Elements["from"], 0);
            long explicitTo = ParseLong(ctx.Request.Query.Elements["to"], 0);

            if (explicitFrom > 0 && explicitTo > explicitFrom)
            {
                long span = explicitTo - explicitFrom;
                long width = UsageWindow.NiceBucketMs(span / 90L);
                filter.FromUnixMs = explicitFrom;
                filter.ToUnixMs = explicitTo;
                return width;
            }

            UsageWindow.Bucketing(ctx.Request.Query.Elements["range"], out long bucketMs, out int count);

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long lastStart = (now / bucketMs) * bucketMs;
            long firstStart = lastStart - ((count - 1) * bucketMs);
            filter.FromUnixMs = firstStart;
            filter.ToUnixMs = lastStart + bucketMs - 1;
            return bucketMs;
        }

        private static long ParseLong(string? value, long fallback)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : fallback;
        }

        private static int ParseInt(string? value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }

        private static string? NullIfBlank(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        #endregion
    }
}

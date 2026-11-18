namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Theming;
    using Mux.Server.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// REST routes for the shared color palettes: list every built-in and custom palette with the active
    /// choice, set the active palette, save a custom palette, and delete one. Changes land in the shared
    /// <c>settings.json</c>, so the TUI, desktop app, web dashboard, and VS Code all follow them.
    /// </summary>
    public sealed class PaletteRoutes
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string? _ApiKey;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">The API key required on requests, or null when the server runs without one.</param>
        public PaletteRoutes(string? apiKey)
        {
            _ApiKey = apiKey;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the palette routes.
        /// </summary>
        /// <param name="app">The webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/palettes", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(BuildList(SettingsLoader.LoadSettings())).ConfigureAwait(false);
            }, Documentation.ApiDoc.PalettesGet);

            app.Put("/v1.0/api/palettes/active", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                PaletteActiveRequest? body = Parse<PaletteActiveRequest>(req.Http.Request.DataAsString);
                if (body == null || string.IsNullOrWhiteSpace(body.Id))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "An 'Id' is required (a palette id or 'system').");
                }

                try
                {
                    ColorPaletteSettings.SetActive(body.Id!);
                }
                catch (ArgumentException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return (object)new ApiError("NotFound", CleanMessage(ex));
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(BuildList(SettingsLoader.LoadSettings())).ConfigureAwait(false);
            }, Documentation.ApiDoc.PalettesSetActive);

            app.Put("/v1.0/api/palettes", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                PaletteSaveRequest? body = Parse<PaletteSaveRequest>(req.Http.Request.DataAsString);
                if (body?.Palette == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A 'Palette' object is required.");
                }

                try
                {
                    ColorPaletteSettings.SaveCustom(body.Palette.ToPalette(), body.Activate);
                }
                catch (ArgumentException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", CleanMessage(ex));
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(BuildList(SettingsLoader.LoadSettings())).ConfigureAwait(false);
            }, Documentation.ApiDoc.PalettesPut);

            app.Delete("/v1.0/api/palettes", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string? id = req.Http.Request.Query.Elements["id"];
                if (string.IsNullOrWhiteSpace(id))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "An 'id' query parameter is required.");
                }

                if (ColorPaletteCatalog.IsReservedId(id))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Built-in palettes cannot be deleted.");
                }

                if (!ColorPaletteSettings.DeleteCustom(id))
                {
                    req.Http.Response.StatusCode = 404;
                    return (object)new ApiError("NotFound", "No custom palette has id '" + id + "'.");
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(BuildList(SettingsLoader.LoadSettings())).ConfigureAwait(false);
            }, Documentation.ApiDoc.PalettesDelete);
        }

        /// <summary>
        /// Builds the palette listing for the given settings.
        /// </summary>
        /// <param name="settings">The loaded settings.</param>
        /// <returns>The listing.</returns>
        public static PaletteListDto BuildList(MuxSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            PaletteListDto list = new PaletteListDto
            {
                Active = settings.ColorPalette,
                DefaultId = ColorPaletteCatalog.DefaultId,
                CursorBlink = settings.CursorBlink
            };

            foreach (ColorPalette palette in ColorPaletteCatalog.GetAll(settings.CustomPalettes))
            {
                list.Palettes.Add(PaletteDto.FromPalette(palette));
            }

            return list;
        }

        #endregion

        #region Private-Methods

        private static T? Parse<T>(string? body) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(body ?? string.Empty, _JsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string CleanMessage(ArgumentException ex)
        {
            string message = ex.Message;
            int index = message.IndexOf(" (Parameter", StringComparison.Ordinal);
            return index > 0 ? message.Substring(0, index) : message;
        }

        private static object Unauthorized(HttpContextBase context)
        {
            context.Response.StatusCode = 401;
            return new ApiError("Unauthorized", "Authentication required.");
        }

        #endregion
    }

    /// <summary>
    /// The palette listing returned by the palette routes.
    /// </summary>
    public sealed class PaletteListDto
    {
        /// <summary>
        /// The chosen palette id (<c>system</c> or a palette id), or null when none has been chosen yet.
        /// </summary>
        public string? Active { get; set; }

        /// <summary>
        /// The default palette id (<c>mux</c>).
        /// </summary>
        public string DefaultId { get; set; } = ColorPaletteCatalog.DefaultId;

        /// <summary>
        /// Whether the TUI caret blinks.
        /// </summary>
        public bool CursorBlink { get; set; } = true;

        /// <summary>
        /// Every palette, built-ins first, each fully resolved.
        /// </summary>
        public List<PaletteDto> Palettes { get; set; } = new List<PaletteDto>();
    }

    /// <summary>
    /// One palette on the wire. <see cref="Colors"/> maps each role name (<c>background</c>, <c>surface</c>,
    /// <c>surfaceAlt</c>, <c>border</c>, <c>text</c>, <c>muted</c>, <c>accent</c>, <c>accentText</c>,
    /// <c>cursor</c>, <c>selection</c>, <c>success</c>, <c>warning</c>, <c>error</c>, <c>info</c>) to a hex color.
    /// </summary>
    public sealed class PaletteDto
    {
        /// <summary>
        /// The palette id.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The display name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// True for a dark palette.
        /// </summary>
        public bool Dark { get; set; } = true;

        /// <summary>
        /// True for a palette that ships with mux (read-only).
        /// </summary>
        public bool BuiltIn { get; set; }

        /// <summary>
        /// For a custom palette, the palette that supplies colors it leaves out.
        /// </summary>
        public string? Base { get; set; }

        /// <summary>
        /// Role name to hex color.
        /// </summary>
        public Dictionary<string, string?> Colors { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Converts a palette to its wire form.
        /// </summary>
        /// <param name="palette">The palette.</param>
        /// <returns>The DTO.</returns>
        public static PaletteDto FromPalette(ColorPalette palette)
        {
            PaletteDto dto = new PaletteDto
            {
                Id = palette.Id,
                Name = palette.Name,
                Dark = palette.Dark,
                BuiltIn = palette.BuiltIn,
                Base = palette.BuiltIn ? null : palette.Base
            };

            foreach (string role in ColorPaletteColors.RoleNames)
            {
                dto.Colors[role] = palette.Colors.Get(role);
            }

            return dto;
        }

        /// <summary>
        /// Converts the wire form to a palette (unknown role names are ignored).
        /// </summary>
        /// <returns>The palette.</returns>
        public ColorPalette ToPalette()
        {
            ColorPalette palette = new ColorPalette { Id = Id ?? string.Empty, Name = Name ?? string.Empty, Dark = Dark, Base = Base };
            if (Colors != null)
            {
                foreach (string role in ColorPaletteColors.RoleNames)
                {
                    if (Colors.TryGetValue(role, out string? value) && !string.IsNullOrWhiteSpace(value))
                    {
                        palette.Colors.Set(role, value);
                    }
                }
            }

            return palette;
        }
    }

    /// <summary>
    /// Body of <c>PUT /v1.0/api/palettes/active</c>.
    /// </summary>
    public sealed class PaletteActiveRequest
    {
        /// <summary>
        /// The palette id, or <c>system</c>.
        /// </summary>
        public string? Id { get; set; }
    }

    /// <summary>
    /// Body of <c>PUT /v1.0/api/palettes</c>.
    /// </summary>
    public sealed class PaletteSaveRequest
    {
        /// <summary>
        /// The custom palette to add or replace (matched by id).
        /// </summary>
        public PaletteDto? Palette { get; set; }

        /// <summary>
        /// True to also make it the active palette.
        /// </summary>
        public bool Activate { get; set; }
    }
}

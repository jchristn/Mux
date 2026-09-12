namespace Mux.Desktop
{
    using System;
    using System.IO;
    using System.Reflection;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Media.Imaging;
    using Avalonia.Platform;

    /// <summary>
    /// Loads the embedded mux logo glyphs (black for light surfaces, white for dark surfaces) as window
    /// icons and bitmaps. Best-effort: a missing resource yields null rather than throwing.
    /// </summary>
    internal static class IconResources
    {
        /// <summary>
        /// Load the window/taskbar icon, choosing the glyph that reads against the current OS theme.
        /// </summary>
        /// <returns>The window icon, or null when the resource cannot be loaded.</returns>
        internal static WindowIcon? LoadWindowIcon()
        {
            Stream? stream = OpenLogoStream(UseWhiteGlyph());
            if (stream == null)
            {
                return null;
            }

            using (stream)
            {
                return new WindowIcon(stream);
            }
        }

        /// <summary>
        /// Load the logo as a bitmap for in-window display.
        /// </summary>
        /// <param name="white">True to load the white glyph (for dark backgrounds); false for the black glyph.</param>
        /// <returns>The logo bitmap, or null when the resource cannot be loaded.</returns>
        internal static Bitmap? LoadLogoBitmap(bool white)
        {
            Stream? stream = OpenLogoStream(white);
            if (stream == null)
            {
                return null;
            }

            using (stream)
            {
                return new Bitmap(stream);
            }
        }

        private static Stream? OpenLogoStream(bool white)
        {
            // A single grey glyph is used for the window/taskbar icon and splash on both light and dark
            // surfaces, matching the app/exe icon (icon-grey.ico). The theme parameter is retained for the
            // call sites but no longer selects a variant.
            try
            {
                return Assembly.GetExecutingAssembly().GetManifestResourceStream("Mux.Desktop.logo-grey.png");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool UseWhiteGlyph()
        {
            try
            {
                IPlatformSettings? settings = Application.Current?.PlatformSettings;
                if (settings != null)
                {
                    return settings.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
                }
            }
            catch (Exception)
            {
                // Fall through to the light-surface default.
            }

            return false;
        }
    }
}

namespace Mux.Desktop.Shell
{
    using System;
    using Avalonia.Controls;
    using Avalonia.Input;

    /// <summary>
    /// Small helpers for building native menus (the macOS menu bar). Shortcuts use Command on macOS and Control
    /// elsewhere. Stateless and safe to call from the UI thread.
    /// </summary>
    public static class NativeMenuFactory
    {
        #region Public-Methods

        /// <summary>
        /// Creates a menu item that runs an action when clicked.
        /// </summary>
        /// <param name="header">The item text. Must not be null.</param>
        /// <param name="action">The action to run. Must not be null.</param>
        /// <param name="key">An optional shortcut key, combined with Command (macOS) or Control.</param>
        /// <param name="shift">Whether the shortcut also requires Shift.</param>
        /// <returns>The menu item.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="header"/> or <paramref name="action"/> is null.</exception>
        public static NativeMenuItem Item(string header, Action action, Key? key = null, bool shift = false)
        {
            ArgumentNullException.ThrowIfNull(header);
            ArgumentNullException.ThrowIfNull(action);

            NativeMenuItem item = new NativeMenuItem { Header = header };
            if (key.HasValue)
            {
                KeyModifiers modifiers = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
                if (shift)
                {
                    modifiers |= KeyModifiers.Shift;
                }

                item.Gesture = new KeyGesture(key.Value, modifiers);
            }

            item.Click += (sender, args) => action();
            return item;
        }

        /// <summary>
        /// Creates a top-level or nested submenu holding the given items.
        /// </summary>
        /// <param name="header">The submenu text. Must not be null.</param>
        /// <param name="items">The items, in order; null entries become separators.</param>
        /// <returns>The submenu item.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="header"/> is null.</exception>
        public static NativeMenuItem Submenu(string header, params NativeMenuItemBase?[] items)
        {
            ArgumentNullException.ThrowIfNull(header);

            NativeMenu menu = new NativeMenu();
            foreach (NativeMenuItemBase? item in items ?? Array.Empty<NativeMenuItemBase?>())
            {
                menu.Items.Add(item ?? new NativeMenuItemSeparator());
            }

            return new NativeMenuItem { Header = header, Menu = menu };
        }

        #endregion
    }
}

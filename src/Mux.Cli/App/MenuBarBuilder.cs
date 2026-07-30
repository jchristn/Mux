namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using TUIKit.Widgets;

    /// <summary>
    /// Builds a TUIKit <see cref="MenuBar"/> (and its constituent <see cref="Menu"/>s) from the command
    /// catalog, grouping commands by <see cref="CommandDescriptor.Category"/> in first-seen order. Each
    /// menu item is wired directly to its command's handler, so the menu is a pure view over the catalog
    /// and cannot drift from the other command surfaces.
    /// </summary>
    public static class MenuBarBuilder
    {
        private const string DefaultCategory = "Commands";

        /// <summary>
        /// Builds the grouped menus for the catalog. Exposed for inspection/testing.
        /// </summary>
        /// <param name="catalog">The command catalog. Must not be null.</param>
        /// <returns>One <see cref="Menu"/> per category, in first-seen order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalog"/> is null.</exception>
        public static IReadOnlyList<Menu> BuildMenus(MuxCommandCatalog catalog)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));

            List<string> order = new List<string>();
            Dictionary<string, Menu> byCategory = new Dictionary<string, Menu>(StringComparer.Ordinal);

            foreach (CommandDescriptor descriptor in catalog.Commands)
            {
                string category = string.IsNullOrWhiteSpace(descriptor.Category) ? DefaultCategory : descriptor.Category;
                if (!byCategory.TryGetValue(category, out Menu? menu))
                {
                    menu = new Menu(category);
                    byCategory[category] = menu;
                    order.Add(category);
                }

                menu.Add(descriptor.Title, descriptor.Handler);
            }

            List<Menu> menus = new List<Menu>();
            foreach (string category in order)
            {
                menus.Add(byCategory[category]);
            }

            return menus;
        }

        /// <summary>
        /// Builds a <see cref="MenuBar"/> populated from the catalog.
        /// </summary>
        /// <param name="catalog">The command catalog. Must not be null.</param>
        /// <returns>A configured <see cref="MenuBar"/>.</returns>
        public static MenuBar Build(MuxCommandCatalog catalog)
        {
            MenuBar bar = new MenuBar();
            foreach (Menu menu in BuildMenus(catalog))
            {
                bar.Add(menu);
            }

            return bar;
        }
    }
}

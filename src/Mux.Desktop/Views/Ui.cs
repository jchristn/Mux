namespace Mux.Desktop.Views
{
    using Avalonia.Controls;

    /// <summary>
    /// Small UI helpers shared across the desktop views. <see cref="Tip{T}"/> attaches a descriptive
    /// hover tooltip to a control and pins it to <see cref="PlacementMode.Pointer"/> so it always renders
    /// next to the cursor (inside the application window) rather than being clipped off to a screen edge.
    /// </summary>
    public static class Ui
    {
        /// <summary>
        /// Attach a hover tooltip to a control and return the same control (fluent).
        /// </summary>
        /// <typeparam name="T">The control type.</typeparam>
        /// <param name="control">The control to annotate.</param>
        /// <param name="tip">The descriptive tooltip text.</param>
        /// <returns>The same control, so calls can be chained inline.</returns>
        public static T Tip<T>(this T control, string tip) where T : Control
        {
            ToolTip.SetTip(control, tip);
            ToolTip.SetPlacement(control, PlacementMode.Pointer);
            ToolTip.SetShowDelay(control, 400);
            return control;
        }
    }
}

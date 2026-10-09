namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Interaction;

    /// <summary>
    /// The approval card for a plan presented with <c>exit_plan</c>: the plan, then "Approve and auto-accept edits",
    /// "Approve", or "Keep planning" with optional feedback. Returns a <see cref="PlanReview"/> through
    /// <c>ShowDialog&lt;PlanReview&gt;</c>; closing the dialog keeps planning.
    /// </summary>
    public sealed class PlanReviewDialog : Window
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the dialog for a plan.
        /// </summary>
        /// <param name="proposal">The plan. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="proposal"/> is null.</exception>
        public PlanReviewDialog(PlanProposal proposal)
        {
            ArgumentNullException.ThrowIfNull(proposal);

            Title = "Review the plan";
            Icon = IconResources.LoadWindowIcon();
            Width = 640;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            DockPanel root = new DockPanel { Margin = new Thickness(20) };
            TextBlock heading = new TextBlock { Text = "The agent finished planning. Approve the plan to carry it out.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);

            StackPanel bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            TextBox feedback = new TextBox { PlaceholderText = "Feedback for the agent if you keep planning (optional)" };
            bottom.Children.Add(feedback);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button keep = new Button { Content = "Keep planning" };
            keep.Click += (sender, args) => Close(new PlanReview { Decision = PlanReviewDecisionEnum.KeepPlanning, Feedback = (feedback.Text ?? string.Empty).Trim() });
            buttons.Children.Add(keep);
            Button approve = new Button { Content = "Approve" };
            approve.Click += (sender, args) => Close(new PlanReview { Decision = PlanReviewDecisionEnum.Approve });
            buttons.Children.Add(approve);
            Button auto = new Button { Content = "Approve and auto-accept edits", Background = AppTheme.Current.AccentButton, Foreground = AppTheme.Current.AccentText };
            auto.Click += (sender, args) => Close(new PlanReview { Decision = PlanReviewDecisionEnum.ApproveAutoAccept });
            buttons.Children.Add(auto);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            root.Children.Add(new TextBox
            {
                Text = proposal.ToText(),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
                FontSize = 12
            });
            Content = root;
        }

        #endregion
    }
}

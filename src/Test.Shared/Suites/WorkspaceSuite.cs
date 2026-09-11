namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Desktop.ViewModels;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the tabbed workspace view models: the per-tab attention model
    /// (<see cref="WorkspaceTabViewModel"/>) — background unread, error, approval priority, focus clearing —
    /// and tab management (<see cref="WorkspaceViewModel"/>) — open/focus/dedup/close.
    /// </summary>
    public static class WorkspaceSuite
    {
        /// <summary>
        /// Builds the workspace suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the workspace cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "Workspace",
                "Tabbed workspace and attention model",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Workspace", "BackgroundUnread", "A background tab marks unread on content events", (CancellationToken ct) =>
                    {
                        WorkspaceTabViewModel tab = new WorkspaceTabViewModel("a", "A");
                        tab.NotifyEvent(new AssistantTextEvent { Text = "hi" });
                        MuxAssert.IsTrue(tab.HasUnread, "has unread");
                        MuxAssert.AreEqual(1, tab.UnreadCount, "unread count");
                        MuxAssert.AreEqual(TabStatus.Unread, tab.Status, "status unread");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Workspace", "ActiveTabIgnoresEvents", "The focused tab does not raise attention", (CancellationToken ct) =>
                    {
                        WorkspaceTabViewModel tab = new WorkspaceTabViewModel("a", "A");
                        tab.Activate();
                        tab.NotifyEvent(new AssistantTextEvent { Text = "hi" });
                        MuxAssert.IsFalse(tab.HasUnread, "no unread when active");
                        MuxAssert.AreEqual(TabStatus.Idle, tab.Status, "idle when active and not running");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Workspace", "ErrorAndPriority", "Approval outranks error outranks unread outranks running", (CancellationToken ct) =>
                    {
                        WorkspaceTabViewModel tab = new WorkspaceTabViewModel("a", "A");
                        tab.NotifyBusy(true);
                        MuxAssert.AreEqual(TabStatus.Running, tab.Status, "running");
                        tab.NotifyEvent(new AssistantTextEvent { Text = "x" });
                        MuxAssert.AreEqual(TabStatus.Unread, tab.Status, "unread over running");
                        tab.NotifyEvent(new ErrorEvent { Code = "e", Message = "m" });
                        MuxAssert.AreEqual(TabStatus.Error, tab.Status, "error over unread");
                        tab.NotifyApprovalPending();
                        MuxAssert.AreEqual(TabStatus.NeedsApproval, tab.Status, "approval over error");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Workspace", "ApprovalIgnoredWhenActive", "Approval pending is ignored on the focused tab", (CancellationToken ct) =>
                    {
                        WorkspaceTabViewModel tab = new WorkspaceTabViewModel("a", "A");
                        tab.Activate();
                        tab.NotifyApprovalPending();
                        MuxAssert.IsFalse(tab.NeedsApproval, "no approval flag when active");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Workspace", "ActivateClearsAttention", "Focusing a tab clears unread, error, and approval", (CancellationToken ct) =>
                    {
                        WorkspaceTabViewModel tab = new WorkspaceTabViewModel("a", "A");
                        tab.NotifyEvent(new AssistantTextEvent { Text = "x" });
                        tab.NotifyEvent(new ErrorEvent { Code = "e", Message = "m" });
                        tab.NotifyApprovalPending();
                        tab.Activate();
                        MuxAssert.IsFalse(tab.HasUnread, "unread cleared");
                        MuxAssert.IsFalse(tab.HasError, "error cleared");
                        MuxAssert.IsFalse(tab.NeedsApproval, "approval cleared");
                        MuxAssert.AreEqual(0, tab.UnreadCount, "count reset");
                        MuxAssert.AreEqual(TabStatus.Idle, tab.Status, "idle after focus");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Workspace", "NullEventGuard", "A null event is rejected", (CancellationToken ct) =>
                    {
                        WorkspaceTabViewModel tab = new WorkspaceTabViewModel("a", "A");
                        MuxAssert.Throws<ArgumentNullException>(() => tab.NotifyEvent(null!), "null event");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Workspace", "OpenFocusesAndDedups", "Open adds and focuses; opening the same id dedups", (CancellationToken ct) =>
                    {
                        WorkspaceViewModel workspace = new WorkspaceViewModel();
                        WorkspaceTabViewModel a = workspace.OpenTab("a", "A");
                        WorkspaceTabViewModel b = workspace.OpenTab("b", "B");
                        MuxAssert.AreEqual(2, workspace.Tabs.Count, "two tabs");
                        MuxAssert.IsTrue(ReferenceEquals(b, workspace.ActiveTab), "b active");
                        MuxAssert.IsFalse(a.IsActive, "a deactivated");

                        WorkspaceTabViewModel again = workspace.OpenTab("a", "A-again");
                        MuxAssert.AreEqual(2, workspace.Tabs.Count, "no duplicate");
                        MuxAssert.IsTrue(ReferenceEquals(a, again), "same instance reused");
                        MuxAssert.IsTrue(ReferenceEquals(a, workspace.ActiveTab), "a refocused");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Workspace", "CloseFallsBackThenEmpty", "Closing the active tab falls back; closing the last clears the active tab", (CancellationToken ct) =>
                    {
                        WorkspaceViewModel workspace = new WorkspaceViewModel();
                        WorkspaceTabViewModel a = workspace.OpenTab("a", "A");
                        WorkspaceTabViewModel b = workspace.OpenTab("b", "B");

                        MuxAssert.IsTrue(workspace.CloseTab(b), "closed b");
                        MuxAssert.IsTrue(ReferenceEquals(a, workspace.ActiveTab), "a now active");
                        MuxAssert.IsTrue(a.IsActive, "a activated");

                        MuxAssert.IsTrue(workspace.CloseTab(a), "closed a");
                        MuxAssert.IsNull(workspace.ActiveTab, "no active tab");
                        MuxAssert.IsFalse(workspace.HasTabs, "no tabs");

                        WorkspaceTabViewModel stray = new WorkspaceTabViewModel("z", "Z");
                        MuxAssert.IsFalse(workspace.CloseTab(stray), "close unknown returns false");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}

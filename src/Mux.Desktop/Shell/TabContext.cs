namespace Mux.Desktop.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using Avalonia.Controls;
    using Avalonia.Threading;
    using Mux.Desktop.Conversation;
    using Mux.Desktop.Services;
    using Mux.Desktop.Views;

    /// <summary>
    /// All per-tab (per-conversation) state for the desktop workspace, so multiple tabs can run agent turns
    /// concurrently without sharing streaming widgets, turn timers, the runner, or the conversation. One
    /// instance exists per open thread id. The engine side (its own <see cref="AgentLoopTurnRunner"/> and
    /// <see cref="ConversationService"/>) makes concurrent turns independent; the UI side (its own transcript
    /// panel and streaming widgets) makes concurrent streaming land in the right tab even when it is not the
    /// active tab. Undo/redo and the git working tree remain global (see the checkpoint gate in MainWindow).
    /// </summary>
    internal sealed class TabContext
    {
        internal TabContext(string id, AgentLoopTurnRunner runner, StackPanel transcript)
        {
            Id = id;
            Runner = runner;
            Transcript = transcript;
        }

        /// <summary>The thread/session id this tab renders.</summary>
        internal string Id { get; set; }

        /// <summary>This tab's own turn runner (independent endpoint/session state for concurrent turns).</summary>
        internal AgentLoopTurnRunner Runner { get; }

        /// <summary>This tab's conversation service, or null before a thread is opened into it.</summary>
        internal ConversationService? Conversation { get; set; }

        /// <summary>The per-tab event handler subscribed to <see cref="Conversation"/> (kept so it can be
        /// unsubscribed with the same delegate reference).</summary>
        internal EventHandler<Mux.Core.Agent.AgentEvent>? EventHandler { get; set; }

        /// <summary>This tab's transcript panel (the visible content when this tab is active).</summary>
        internal StackPanel Transcript { get; }

        // ---- per-turn engine state ----
        internal CancellationTokenSource? TurnCts { get; set; }
        internal Stopwatch? TurnStopwatch { get; set; }
        internal long? TurnTtftMs { get; set; }
        internal int LastEstimatedTokens { get; set; }

        // ---- per-turn streaming widgets ----
        internal TextBlock? StreamingBlock { get; set; }
        internal Border? AssistantBorder { get; set; }
        internal Border? AssistantContentHost { get; set; }
        internal StackPanel? TaskPlanBody { get; set; }
        internal CollapsibleSection? ThinkingSection { get; set; }
        internal TextBlock? ThinkingText { get; set; }
        internal Border? PendingBubble { get; set; }
        internal TextBlock? PendingText { get; set; }
        internal DispatcherTimer? QuipTimer { get; set; }
        internal int QuipIndex { get; set; }
        internal readonly Dictionary<string, ToolCardView> ToolCards = new Dictionary<string, ToolCardView>(StringComparer.Ordinal);

        // ---- title / persistence state ----
        internal string Title { get; set; } = string.Empty;
        internal bool TitlePinned { get; set; }
        internal bool TitleSummarized { get; set; }
        internal DateTime CreatedUtc { get; set; }

        /// <summary>Whether a turn is currently in flight for this tab.</summary>
        internal bool IsBusy
        {
            get => Conversation != null && Conversation.IsBusy;
        }
    }
}

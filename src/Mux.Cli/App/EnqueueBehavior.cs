namespace Mux.Cli.App
{
    /// <summary>
    /// How the shell handles a submitted prompt while another job is already active. The engine's
    /// scheduler decides run-now-vs-queue by the concurrency cap, so "run now" and "queue after"
    /// collapse to a single "new job" behavior here; the meaningful distinction is starting a new job
    /// versus appending to the focused job's conversation.
    /// </summary>
    public enum EnqueueBehavior
    {
        /// <summary>Prompt the user each time a job is active (show the submit chooser).</summary>
        Ask = 0,

        /// <summary>Always submit as a new job (the scheduler runs or queues it by the concurrency cap).</summary>
        NewJob = 1,

        /// <summary>Always append to the focused job's conversation when it is still active.</summary>
        AddToFocused = 2
    }
}

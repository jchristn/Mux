namespace Mux.Cli.Rendering
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Renders an animated "Thinking..." indicator where a 2-character bright window
    /// slides across the text on a loop, with the rest dimmed.
    /// </summary>
    public class ThinkingAnimation : IDisposable
    {
        #region Private-Members

        private static readonly string _Text = "Thinking...";
        private static readonly int _WindowSize = 2;
        private static readonly int _IntervalMs = 120;

        // ANSI escape codes — avoid Spectre markup to prevent flicker
        private static readonly string _Dim = "\x1b[90m";     // dark grey
        private static readonly string _Bright = "\x1b[97m";  // bright white
        private static readonly string _Reset = "\x1b[0m";

        private CancellationTokenSource _Cts = new CancellationTokenSource();
        private Task? _AnimationTask = null;
        private int _Stopped = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts the animation on a background task. The animation runs until
        /// <see cref="Stop"/> is called or the instance is disposed.
        /// </summary>
        public void Start()
        {
            // Render the initial frame immediately so something is visible
            RenderFrame(0);

            _AnimationTask = Task.Run(async () =>
            {
                int frame = 0;

                try
                {
                    while (!_Cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(_IntervalMs, _Cts.Token).ConfigureAwait(false);
                        frame = (frame + 1) % _Text.Length;

                        if (Interlocked.CompareExchange(ref _Stopped, 0, 0) == 1)
                        {
                            break;
                        }

                        RenderFrame(frame);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on stop
                }
            });
        }

        /// <summary>
        /// Stops the animation and clears the line. Safe to call multiple times.
        /// </summary>
        public void Stop()
        {
            if (Interlocked.Exchange(ref _Stopped, 1) == 0)
            {
                _Cts.Cancel();

                // Clear the animation line
                Console.Write("\r");
                Console.Write(new string(' ', _Text.Length + 10));
                Console.Write("\r");
            }
        }

        /// <summary>
        /// Replaces the animation with a static status message (e.g. retry progress).
        /// The animation resumes if not stopped.
        /// </summary>
        /// <param name="message">The status message to display.</param>
        public void ShowStatus(string message)
        {
            Console.Write("\r");
            Console.Write(new string(' ', Math.Max(_Text.Length + 10, message.Length + 5)));
            Console.Write("\r");
            Console.Write($"{_Dim}{message}{_Reset}");
        }

        /// <summary>
        /// Releases resources used by the animation.
        /// </summary>
        public void Dispose()
        {
            Stop();
            _Cts.Dispose();
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Renders a single animation frame with the bright window at the given position.
        /// </summary>
        /// <param name="windowStart">The starting character index of the bright window.</param>
        private void RenderFrame(int windowStart)
        {
            if (Interlocked.CompareExchange(ref _Stopped, 0, 0) == 1)
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append('\r');

            for (int i = 0; i < _Text.Length; i++)
            {
                // Check if this character is within the bright window (wrapping)
                bool isBright = false;
                for (int w = 0; w < _WindowSize; w++)
                {
                    if ((windowStart + w) % _Text.Length == i)
                    {
                        isBright = true;
                        break;
                    }
                }

                if (isBright)
                {
                    sb.Append(_Bright);
                    sb.Append(_Text[i]);
                    sb.Append(_Reset);
                }
                else
                {
                    sb.Append(_Dim);
                    sb.Append(_Text[i]);
                    sb.Append(_Reset);
                }
            }

            Console.Write(sb.ToString());
        }

        #endregion
    }
}

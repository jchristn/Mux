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

        // ANSI 256-color codes — avoid Spectre markup to prevent flicker
        private static readonly string _DarkGrey = "\x1b[38;5;240m";   // dark grey (base)
        private static readonly string _Grey = "\x1b[38;5;248m";       // grey (flank)
        private static readonly string _LightGrey = "\x1b[38;5;255m";  // light grey (center)
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
            Console.Write($"{_DarkGrey}{message}{_Reset}");
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
        /// Renders a single animation frame. The center 2 characters at windowStart
        /// are light grey, the 1 character on each side is grey, and the rest is dark grey.
        /// All positions wrap around the text length.
        /// </summary>
        /// <param name="windowStart">The starting character index of the light grey center.</param>
        private void RenderFrame(int windowStart)
        {
            if (Interlocked.CompareExchange(ref _Stopped, 0, 0) == 1)
            {
                return;
            }

            int len = _Text.Length;

            // Precompute which tier each character belongs to
            // Center (light grey): windowStart, windowStart+1
            // Flank (grey): windowStart-1, windowStart+2
            // Everything else: dark grey
            int center0 = windowStart % len;
            int center1 = (windowStart + 1) % len;
            int flankLeft = (windowStart - 1 + len) % len;
            int flankRight = (windowStart + 2) % len;

            StringBuilder sb = new StringBuilder();
            sb.Append('\r');

            for (int i = 0; i < len; i++)
            {
                string color;

                if (i == center0 || i == center1)
                {
                    color = _LightGrey;
                }
                else if (i == flankLeft || i == flankRight)
                {
                    color = _Grey;
                }
                else
                {
                    color = _DarkGrey;
                }

                sb.Append(color);
                sb.Append(_Text[i]);
            }

            sb.Append(_Reset);
            Console.Write(sb.ToString());
        }

        #endregion
    }
}

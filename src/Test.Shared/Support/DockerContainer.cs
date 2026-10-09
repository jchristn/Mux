namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A running test container. Disposing it force-removes the container and its anonymous volumes.
    /// </summary>
    public sealed class DockerContainer : IDisposable
    {
        #region Private-Members

        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="DockerContainer"/> class.
        /// </summary>
        /// <param name="name">The container name.</param>
        /// <param name="image">The image.</param>
        public DockerContainer(string name, string image)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Image = image ?? string.Empty;
        }

        #endregion

        #region Public-Members

        /// <summary>The container name.</summary>
        public string Name { get; }

        /// <summary>The image it runs.</summary>
        public string Image { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs a command inside the container.
        /// </summary>
        /// <param name="arguments">The command and its arguments.</param>
        /// <returns>The result.</returns>
        public DockerResult Exec(params string[] arguments)
        {
            return DockerHarness.Run(TimeSpan.FromMinutes(5), new[] { "exec", Name }.Concat(arguments).ToArray());
        }

        /// <summary>
        /// Polls a command inside the container until it exits 0, or fails with the container's recent logs.
        /// </summary>
        /// <param name="timeout">How long to wait.</param>
        /// <param name="cancellationToken">A token to cancel the wait.</param>
        /// <param name="probe">The probe command and its arguments.</param>
        /// <returns>A task that completes when the probe succeeds.</returns>
        /// <exception cref="TimeoutException">Thrown when the probe never succeeds.</exception>
        public async Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken, params string[] probe)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            DockerResult last = new DockerResult(-1, string.Empty, "not run");
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                last = Exec(probe);
                if (last.ExitCode == 0) return;
                if (DockerHarness.Run(TimeSpan.FromSeconds(30), "inspect", "-f", "{{.State.Running}}", Name).StandardOutput.Trim() != "true")
                {
                    throw new InvalidOperationException(Image + " stopped while starting: " + Logs());
                }

                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException(Image + " was not ready after " + timeout.TotalSeconds + "s. Last probe: " + (last.StandardOutput + last.StandardError).Trim() + "\nLogs: " + Logs());
        }

        /// <summary>
        /// The host port a container port is published on.
        /// </summary>
        /// <param name="containerPort">The container port, such as 8701.</param>
        /// <returns>The host port.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the port is not published.</exception>
        public int HostPort(int containerPort)
        {
            string output = DockerHarness.Run(TimeSpan.FromSeconds(30), "port", Name, containerPort + "/tcp").StandardOutput;
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = line.LastIndexOf(':');
                if (colon > 0 && int.TryParse(line.Substring(colon + 1).Trim(), out int port)) return port;
            }

            throw new InvalidOperationException("port " + containerPort + " is not published by " + Name);
        }

        /// <summary>
        /// The last lines of the container log, for failure messages.
        /// </summary>
        /// <returns>The log tail.</returns>
        public string Logs()
        {
            DockerResult logs = DockerHarness.Run(TimeSpan.FromSeconds(30), "logs", "--tail", "40", Name);
            return (logs.StandardOutput + logs.StandardError).Trim();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            DockerHarness.Run(TimeSpan.FromSeconds(120), "rm", "-f", "-v", Name);
        }

        #endregion
    }
}

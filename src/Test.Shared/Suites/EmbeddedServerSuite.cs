namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Desktop.Services;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="EmbeddedServerService"/> — the desktop's optional in-process REST +
    /// dashboard server (row 42 / §12). These cases assert the stopped-state contract and no-op safety
    /// without binding a socket or mutating user settings (a live <c>Start</c> touches the real config
    /// directory and the configured loopback port, which the live server route suite exercises separately):
    /// the singleton is stable, a stopped server exposes null URLs/key and is not running, and
    /// <see cref="EmbeddedServerService.Stop"/> is a safe no-op when already stopped.
    /// </summary>
    public static class EmbeddedServerSuite
    {
        private const string SuiteId = "EmbeddedServer";

        /// <summary>
        /// Builds the embedded-server suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for embedded-server cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Embedded server service (desktop)",
                new List<TestCaseDescriptor>
                {
                    Case("SingletonIsStable", "Instance returns the same process-wide controller", (CancellationToken ct) =>
                    {
                        MuxAssert.IsNotNull(EmbeddedServerService.Instance, "instance not null");
                        MuxAssert.IsTrue(ReferenceEquals(EmbeddedServerService.Instance, EmbeddedServerService.Instance), "same singleton");
                        return Task.CompletedTask;
                    }),

                    Case("StoppedStateExposesNulls", "A stopped server reports not-running with null URLs and key", (CancellationToken ct) =>
                    {
                        EmbeddedServerService service = EmbeddedServerService.Instance;
                        service.Stop();

                        MuxAssert.IsFalse(service.IsRunning, "not running");
                        MuxAssert.IsTrue(service.BaseUrl == null, "base url null when stopped");
                        MuxAssert.IsTrue(service.DashboardUrl == null, "dashboard url null when stopped");
                        MuxAssert.IsTrue(service.HealthUrl == null, "health url null when stopped");
                        MuxAssert.IsTrue(service.ApiKey == null, "api key null when stopped");
                        MuxAssert.IsFalse(service.AuthEnabled, "auth disabled when stopped");
                        return Task.CompletedTask;
                    }),

                    Case("StopIsIdempotent", "Stopping an already-stopped server does not throw", (CancellationToken ct) =>
                    {
                        EmbeddedServerService service = EmbeddedServerService.Instance;
                        service.Stop();
                        service.Stop();
                        MuxAssert.IsFalse(service.IsRunning, "still stopped");
                        return Task.CompletedTask;
                    })
                });
        }

        private static TestCaseDescriptor Case(string id, string name, System.Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }
    }
}

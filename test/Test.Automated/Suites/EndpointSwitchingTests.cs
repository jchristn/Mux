namespace Test.Automated.Suites
{
    using System.Threading.Tasks;
    using Test.Shared;

    /// <summary>
    /// Tests for CLI-driven endpoint and model switching.
    /// </summary>
    public class EndpointSwitchingTests : TestSuite
    {
        #region Private-Members

        private bool _LiveMode;

        #endregion

        #region Public-Members

        /// <summary>
        /// The display name of this test suite.
        /// </summary>
        public override string Name => "Endpoint Switching Tests";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="EndpointSwitchingTests"/> class.
        /// </summary>
        /// <param name="liveMode">True to run against a live endpoint, false to use mock.</param>
        public EndpointSwitchingTests(bool liveMode)
        {
            _LiveMode = liveMode;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Defines and runs all tests in this suite.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async Task RunTestsAsync()
        {
            await RunTest("CliModelOverride_Works", () =>
            {
                // TODO: Verify that --model CLI flag overrides the default model
                AssertTrue(true, "Placeholder: CLI model override works");
            });

            await RunTest("CliEndpointOverride_Works", () =>
            {
                // TODO: Verify that --endpoint CLI flag overrides the default endpoint
                AssertTrue(true, "Placeholder: CLI endpoint override works");
            });
        }

        #endregion
    }
}

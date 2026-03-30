namespace Test.Automated.Suites
{
    using System.Threading.Tasks;
    using Test.Shared;

    /// <summary>
    /// Tests for atomic multi-edit operations.
    /// </summary>
    public class MultiEditTests : TestSuite
    {
        #region Private-Members

        private bool _LiveMode;

        #endregion

        #region Public-Members

        /// <summary>
        /// The display name of this test suite.
        /// </summary>
        public override string Name => "Multi-Edit Tests";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiEditTests"/> class.
        /// </summary>
        /// <param name="liveMode">True to run against a live endpoint, false to use mock.</param>
        public MultiEditTests(bool liveMode)
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
            await RunTest("AtomicMultiEdit_AllOrNothing", () =>
            {
                // TODO: Verify that multi-edit applies all edits atomically or rolls back
                AssertTrue(true, "Placeholder: atomic multi-edit all-or-nothing");
            });
        }

        #endregion
    }
}

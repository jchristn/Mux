namespace Test.Shared
{
    using System;

    /// <summary>
    /// Exception thrown when a Touchstone test assertion fails. Touchstone signals a failed
    /// test by way of a thrown exception; this type gives assertion failures a distinct,
    /// domain-specific exception so they can be distinguished from unexpected runtime errors.
    /// </summary>
    public class AssertionFailedException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AssertionFailedException"/> class.
        /// </summary>
        /// <param name="message">A message describing the assertion that failed. Must not be null.</param>
        public AssertionFailedException(string message)
            : base(message ?? throw new ArgumentNullException(nameof(message)))
        {
        }
    }
}

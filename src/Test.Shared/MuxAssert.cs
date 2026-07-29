namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal assertion helpers for Touchstone test descriptors. Each method throws an
    /// <see cref="AssertionFailedException"/> with contextual detail when its condition is not
    /// met, which Touchstone records as a test failure. Kept intentionally small and free of any
    /// third-party test-framework dependency so it is usable from every runner.
    /// </summary>
    public static class MuxAssert
    {
        /// <summary>
        /// Asserts that two values are equal using the default equality comparer for the type.
        /// </summary>
        /// <typeparam name="T">The type of the values being compared.</typeparam>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The actual value produced by the code under test.</param>
        /// <param name="label">A short label identifying what is being compared, used in the failure message.</param>
        /// <exception cref="AssertionFailedException">Thrown when <paramref name="expected"/> and <paramref name="actual"/> are not equal.</exception>
        public static void AreEqual<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertionFailedException($"{label}: expected '{expected}', actual '{actual}'");
        }

        /// <summary>
        /// Asserts that a condition is true.
        /// </summary>
        /// <param name="condition">The condition expected to be true.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <exception cref="AssertionFailedException">Thrown when <paramref name="condition"/> is false.</exception>
        public static void IsTrue(bool condition, string label)
        {
            if (!condition)
                throw new AssertionFailedException($"{label}: expected true, actual false");
        }

        /// <summary>
        /// Asserts that a condition is false.
        /// </summary>
        /// <param name="condition">The condition expected to be false.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <exception cref="AssertionFailedException">Thrown when <paramref name="condition"/> is true.</exception>
        public static void IsFalse(bool condition, string label)
        {
            if (condition)
                throw new AssertionFailedException($"{label}: expected false, actual true");
        }

        /// <summary>
        /// Asserts that a value is not null.
        /// </summary>
        /// <param name="value">The value expected to be non-null.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <exception cref="AssertionFailedException">Thrown when <paramref name="value"/> is null.</exception>
        public static void IsNotNull(object? value, string label)
        {
            if (value is null)
                throw new AssertionFailedException($"{label}: expected non-null, actual null");
        }

        /// <summary>
        /// Asserts that a string contains an expected substring.
        /// </summary>
        /// <param name="expectedSubstring">The substring expected to be present. Must not be null.</param>
        /// <param name="actual">The string to search. May be null, which fails the assertion.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <exception cref="AssertionFailedException">Thrown when <paramref name="actual"/> is null or does not contain <paramref name="expectedSubstring"/>.</exception>
        public static void Contains(string expectedSubstring, string? actual, string label)
        {
            if (actual is null || !actual.Contains(expectedSubstring))
                throw new AssertionFailedException($"{label}: expected to contain '{expectedSubstring}', actual '{actual}'");
        }

        /// <summary>
        /// Asserts that a string does not contain an unexpected substring.
        /// </summary>
        /// <param name="unexpectedSubstring">The substring expected to be absent. Must not be null.</param>
        /// <param name="actual">The string to search. A null value satisfies the assertion.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <exception cref="AssertionFailedException">Thrown when <paramref name="actual"/> contains <paramref name="unexpectedSubstring"/>.</exception>
        public static void DoesNotContain(string unexpectedSubstring, string? actual, string label)
        {
            if (actual is not null && actual.Contains(unexpectedSubstring))
                throw new AssertionFailedException($"{label}: expected not to contain '{unexpectedSubstring}', actual '{actual}'");
        }

        /// <summary>
        /// Asserts that a value is null.
        /// </summary>
        /// <param name="value">The value expected to be null.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <exception cref="AssertionFailedException">Thrown when <paramref name="value"/> is not null.</exception>
        public static void IsNull(object? value, string label)
        {
            if (value is not null)
                throw new AssertionFailedException($"{label}: expected null, actual '{value}'");
        }

        /// <summary>
        /// Asserts that two values are not equal using the default equality comparer for the type.
        /// </summary>
        /// <typeparam name="T">The type of the values being compared.</typeparam>
        /// <param name="notExpected">The value the actual value must not equal.</param>
        /// <param name="actual">The actual value produced by the code under test.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <exception cref="AssertionFailedException">Thrown when the values are equal.</exception>
        public static void AreNotEqual<T>(T notExpected, T actual, string label)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
                throw new AssertionFailedException($"{label}: expected value to differ from '{notExpected}'");
        }

        /// <summary>
        /// Asserts that a value is exactly of the specified runtime type and returns it cast to that type.
        /// </summary>
        /// <typeparam name="T">The exact expected type.</typeparam>
        /// <param name="value">The value to check.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <returns>The value cast to <typeparamref name="T"/>.</returns>
        /// <exception cref="AssertionFailedException">Thrown when the value is null or not exactly of type <typeparamref name="T"/>.</exception>
        public static T IsType<T>(object? value, string label)
        {
            if (value is null)
                throw new AssertionFailedException($"{label}: expected {typeof(T).Name}, actual null");
            if (value.GetType() != typeof(T))
                throw new AssertionFailedException($"{label}: expected {typeof(T).Name}, actual {value.GetType().Name}");
            return (T)value;
        }

        /// <summary>
        /// Unconditionally fails with the supplied message.
        /// </summary>
        /// <param name="label">A message describing the failure.</param>
        /// <exception cref="AssertionFailedException">Always thrown.</exception>
        public static void Fail(string label)
        {
            throw new AssertionFailedException(label);
        }

        /// <summary>
        /// Asserts that the supplied synchronous action throws an exception assignable to
        /// <typeparamref name="TException"/>.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The action expected to throw. Must not be null.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <returns>The caught exception, for further inspection.</returns>
        /// <exception cref="AssertionFailedException">Thrown when no exception, or an exception of the wrong type, is thrown.</exception>
        public static TException Throws<TException>(Action action, string label)
            where TException : Exception
        {
            if (action is null) throw new ArgumentNullException(nameof(action));

            try
            {
                action();
            }
            catch (TException expected)
            {
                return expected;
            }
            catch (Exception other)
            {
                throw new AssertionFailedException($"{label}: expected {typeof(TException).Name}, actual {other.GetType().Name}");
            }

            throw new AssertionFailedException($"{label}: expected {typeof(TException).Name}, but no exception was thrown");
        }

        /// <summary>
        /// Asserts that the supplied asynchronous action throws an exception assignable to
        /// <typeparamref name="TException"/>.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The asynchronous action expected to throw. Must not be null.</param>
        /// <param name="label">A short label identifying the assertion, used in the failure message.</param>
        /// <returns>The caught exception, for further inspection.</returns>
        /// <exception cref="AssertionFailedException">Thrown when no exception, or an exception of the wrong type, is thrown.</exception>
        public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string label)
            where TException : Exception
        {
            if (action is null) throw new ArgumentNullException(nameof(action));

            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException expected)
            {
                return expected;
            }
            catch (Exception other)
            {
                throw new AssertionFailedException($"{label}: expected {typeof(TException).Name}, actual {other.GetType().Name}");
            }

            throw new AssertionFailedException($"{label}: expected {typeof(TException).Name}, but no exception was thrown");
        }
    }
}

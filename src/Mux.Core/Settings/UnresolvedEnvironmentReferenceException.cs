namespace Mux.Core.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Thrown when an endpoint value that is sent to the provider (the API key, a header, or a cloud
    /// coordinate) still holds an environment variable reference after expansion because the variable is not
    /// set. Raised before any request so the literal reference is never sent as a credential.
    /// </summary>
    public sealed class UnresolvedEnvironmentReferenceException : InvalidOperationException
    {
        /// <summary>
        /// The stable error code reported for this failure in structured output.
        /// </summary>
        public const string ErrorCode = "config_unresolved_env";

        /// <summary>
        /// Initializes a new instance of the <see cref="UnresolvedEnvironmentReferenceException"/> class.
        /// </summary>
        /// <param name="endpointName">The endpoint whose value could not be resolved.</param>
        /// <param name="fieldName">The endpoint field holding the reference, for example <c>apiKey</c>.</param>
        /// <param name="variableNames">The names of the environment variables that are not set.</param>
        public UnresolvedEnvironmentReferenceException(string endpointName, string fieldName, IReadOnlyList<string> variableNames)
            : base(BuildMessage(endpointName, fieldName, variableNames))
        {
            EndpointName = endpointName ?? string.Empty;
            FieldName = fieldName ?? string.Empty;
            VariableNames = variableNames ?? Array.Empty<string>();
        }

        /// <summary>
        /// The endpoint whose value could not be resolved.
        /// </summary>
        public string EndpointName { get; }

        /// <summary>
        /// The endpoint field holding the reference, for example <c>apiKey</c> or <c>headers.Authorization</c>.
        /// </summary>
        public string FieldName { get; }

        /// <summary>
        /// The names of the environment variables that are not set.
        /// </summary>
        public IReadOnlyList<string> VariableNames { get; }

        private static string BuildMessage(string endpointName, string fieldName, IReadOnlyList<string> variableNames)
        {
            IReadOnlyList<string> names = variableNames ?? Array.Empty<string>();
            string noun = names.Count == 1 ? "environment variable" : "environment variables";
            string verb = names.Count == 1 ? "is" : "are";
            return $"Endpoint '{endpointName}': {fieldName} references {noun} {string.Join(", ", names)}, which {verb} not set.";
        }
    }
}

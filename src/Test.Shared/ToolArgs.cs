namespace Test.Shared
{
    using System.Text.Json;

    /// <summary>
    /// Helper for building the <see cref="JsonElement"/> argument payloads that tools receive in their
    /// <c>ExecuteAsync</c> methods. Centralizes serialization so tool suites can express arguments as
    /// anonymous objects.
    /// </summary>
    public static class ToolArgs
    {
        /// <summary>
        /// Serializes an object (typically an anonymous type) into a <see cref="JsonElement"/> suitable
        /// for passing as tool arguments.
        /// </summary>
        /// <param name="value">The value to serialize. Must not be null.</param>
        /// <returns>The serialized <see cref="JsonElement"/>.</returns>
        public static JsonElement From(object value)
        {
            return JsonSerializer.SerializeToElement(value);
        }
    }
}

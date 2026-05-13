namespace SearchConsoleShared
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Shared JSON output helper for the interactive console apps.
    /// </summary>
    public static class JsonConsoleWriter
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Writes an object as formatted JSON.
        /// </summary>
        /// <param name="value">The value to print.</param>
        public static void WriteObject(object value)
        {
            Console.WriteLine(JsonSerializer.Serialize(value, SerializerOptions));
        }
    }
}

namespace Mux.Search.Providers.Tavily
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Converts Tavily image arrays that may contain URL strings or image objects.
    /// </summary>
    public class TavilyApiImageListConverter : JsonConverter<List<TavilyApiImage>>
    {
        /// <inheritdoc />
        public override List<TavilyApiImage> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            List<TavilyApiImage> images = new List<TavilyApiImage>();
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                reader.Skip();
                return images;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return images;
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    string? url = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        images.Add(new TavilyApiImage { Url = url });
                    }

                    continue;
                }

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    TavilyApiImage? image = JsonSerializer.Deserialize<TavilyApiImage>(ref reader, options);
                    if (image != null)
                    {
                        images.Add(image);
                    }

                    continue;
                }

                reader.Skip();
            }

            return images;
        }

        /// <inheritdoc />
        public override void Write(
            Utf8JsonWriter writer,
            List<TavilyApiImage> value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}

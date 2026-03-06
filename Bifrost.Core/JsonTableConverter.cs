using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bifrost.Core;

public class JsonTableConverter : JsonConverter<JsonTable>
{
    public override JsonTable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString()!;
            var parts = name.Split('.');
            return new JsonTable
            {
                Name = parts.Length == 2 ? name : $"dbo.{name}"
            };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var table = new JsonTable();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var prop = reader.GetString()!;
                reader.Read();
                switch (prop)
                {
                    case "name":   table.Name   = reader.GetString()!; break;
                    case "ignore": table.Ignore = reader.GetBoolean();  break;
                    case "where":  table.Where  = reader.GetString();   break;
                    case "query":  table.Query  = reader.GetString();   break;
                }
            }
            if (!table.Name.Contains('.')) table.Name = $"dbo.{table.Name}";
            return table;
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for JsonTable");
    }

    public override void Write(Utf8JsonWriter writer, JsonTable value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Name);
}

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DarkEngine3D_gl_csharp.Engine.Helpers;

/// <summary>
/// Custom JSON converter for <see cref="Vector3"/>.
/// The built-in System.Text.Json Vector3 support in .NET 9 produces empty objects
/// when <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> is set to CamelCase.
/// This converter explicitly reads/writes X, Y, Z properties, bypassing the naming policy.
/// </summary>
public sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject for Vector3");

        float x = 0f, y = 0f, z = 0f;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new Vector3(x, y, z);

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected PropertyName inside Vector3");

            string? propName = reader.GetString();
            if (!reader.Read())
                throw new JsonException("Expected value inside Vector3");

            // Match case-insensitively so both X/x, Y/y, Z/z work
            if (string.Equals(propName, "X", StringComparison.OrdinalIgnoreCase))
                x = reader.GetSingle();
            else if (string.Equals(propName, "Y", StringComparison.OrdinalIgnoreCase))
                y = reader.GetSingle();
            else if (string.Equals(propName, "Z", StringComparison.OrdinalIgnoreCase))
                z = reader.GetSingle();
            else
                throw new JsonException($"Unknown Vector3 property: {propName}");
        }

        throw new JsonException("Unexpected end of Vector3");
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteNumber("Z", value.Z);
        writer.WriteEndObject();
    }
}

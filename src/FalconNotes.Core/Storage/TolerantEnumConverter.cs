using System.Text.Json;
using System.Text.Json.Serialization;

namespace FalconNotes.Core.Storage;

/// <summary>
/// Writes enums as their names and reads names case-insensitively. An unknown name reads as an undefined value
/// (-1) instead of failing the whole document, so one stray setting cannot lose every other one; readers replace
/// undefined values with defaults (see <c>PreferencesService.Normalise</c>).
/// </summary>
public sealed class TolerantEnumConverter : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<T> : JsonConverter<T>
        where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var value)
                && Enum.IsDefined(value)
                ? value
                : (T)Enum.ToObject(typeof(T), -1);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}

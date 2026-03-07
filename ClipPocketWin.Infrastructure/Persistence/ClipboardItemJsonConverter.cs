using System.Text.Json;
using System.Text.Json.Serialization;
using ClipPocketWin.Domain.Models;

namespace ClipPocketWin.Infrastructure.Persistence;

internal sealed class ClipboardItemJsonConverter : JsonConverter<ClipboardItem>
{
    public override ClipboardItem? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        ClipboardItemType type = ResolveClipboardItemType(root);

        return type switch
        {
            ClipboardItemType.Image => DeserializeAs<ImageClipboardItem>(root, options),
            ClipboardItemType.File => DeserializeAs<FileClipboardItem>(root, options),
            _ => DeserializeAs<TextClipboardItem>(root, options)
        };
    }

    public override void Write(Utf8JsonWriter writer, ClipboardItem value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case TextClipboardItem textItem:
                JsonSerializer.Serialize(writer, textItem, options);
                return;
            case ImageClipboardItem imageItem:
                JsonSerializer.Serialize(writer, imageItem, options);
                return;
            case FileClipboardItem fileItem:
                JsonSerializer.Serialize(writer, fileItem, options);
                return;
            default:
                throw new JsonException($"Unsupported clipboard item runtime type '{value.GetType().FullName}'.");
        }
    }

    private static ClipboardItemType ResolveClipboardItemType(JsonElement root)
    {
        if (!root.TryGetProperty("type", out JsonElement typeElement)
            && !root.TryGetProperty("Type", out typeElement))
        {
            return ClipboardItemType.Text;
        }

        if (typeElement.ValueKind == JsonValueKind.String
            && Enum.TryParse(typeElement.GetString(), ignoreCase: true, out ClipboardItemType fromString))
        {
            return fromString;
        }

        if (typeElement.ValueKind == JsonValueKind.Number
            && typeElement.TryGetInt32(out int numericType)
            && Enum.IsDefined(typeof(ClipboardItemType), numericType))
        {
            return (ClipboardItemType)numericType;
        }

        return ClipboardItemType.Text;
    }

    private static T DeserializeAs<T>(JsonElement element, JsonSerializerOptions options)
    {
        T? value = JsonSerializer.Deserialize<T>(element.GetRawText(), options);
        if (value is null)
        {
            throw new JsonException($"Failed to deserialize clipboard item as '{typeof(T).Name}'.");
        }

        return value;
    }
}

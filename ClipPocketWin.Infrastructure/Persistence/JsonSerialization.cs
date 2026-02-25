using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipPocketWin.Infrastructure.Persistence;

internal static class JsonSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };
}

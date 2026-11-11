using System.Net;
using System.Text;
using System.Text.Json;

namespace Ak.AutoTest.Clients;

public sealed record BinaryApiResponse(HttpStatusCode StatusCode, byte[] Content)
{
    internal JsonSerializerOptions SerializerOptions { get; init; } = JsonSerializerOptions.Web;
    public IReadOnlyDictionary<string, string[]> Headers { get; init; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    internal Encoding TextEncoding { get; init; } = Encoding.UTF8;

    public ApiResponse AsText() => new(StatusCode, TextEncoding.GetString(Content))
    {
        Headers = Headers,
        SerializerOptions = SerializerOptions
    };
}

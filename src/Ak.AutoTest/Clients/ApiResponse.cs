using System.Net;
using System.Text.Json;

namespace Ak.AutoTest.Clients;

public sealed record ApiResponse(HttpStatusCode StatusCode, string Body)
{
    internal JsonSerializerOptions SerializerOptions { get; init; } = JsonSerializerOptions.Web;
    public IReadOnlyDictionary<string, string[]> Headers { get; init; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    public T Deserialize<T>(JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(Body, options ?? SerializerOptions)
        ?? throw new InvalidOperationException($"Пустой результат десериализации {typeof(T).Name}. Ответ: {Body}");
}

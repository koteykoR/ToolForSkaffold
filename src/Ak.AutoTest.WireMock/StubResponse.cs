using System.Net;
using System.Text.Json;

namespace Ak.AutoTest.WireMock;

/// <summary>Ответ заглушки с одним явно выбранным форматом тела.</summary>
public sealed class StubResponse
{
    private readonly Dictionary<string, object> _model;

    private StubResponse(HttpStatusCode statusCode, string? contentType,
        IReadOnlyDictionary<string, string>? headers, string? bodyProperty = null, object? body = null)
    {
        if ((int)statusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }
        Dictionary<string, string> responseHeaders = new(StringComparer.OrdinalIgnoreCase);
        if (contentType is not null)
        {
            responseHeaders["Content-Type"] = contentType;
        }
        if (headers is not null)
        {
            foreach (var header in headers)
            {
                responseHeaders[header.Key] = header.Value;
            }
        }
        _model = new() { ["StatusCode"] = (int)statusCode, ["Headers"] = responseHeaders };
        if (bodyProperty is not null)
        {
            _model[bodyProperty] = body!;
        }
    }

    /// <summary>Сериализовать объект; по умолчанию регистр имён свойств сохраняется.</summary>
    public static StubResponse Json(object? body, HttpStatusCode statusCode = HttpStatusCode.OK,
        JsonSerializerOptions? jsonOptions = null, IReadOnlyDictionary<string, string>? headers = null) =>
        new(statusCode, "application/json; charset=utf-8", headers, "Body",
            JsonSerializer.Serialize(body, jsonOptions ?? JsonSerializerOptions.Default));

    /// <summary>Отправить строку без преобразований, в том числе невалидный JSON.</summary>
    public static StubResponse Text(string body, HttpStatusCode statusCode = HttpStatusCode.OK,
        string contentType = "text/plain; charset=utf-8", IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new(statusCode, contentType, headers, "Body", body);
    }

    public static StubResponse Bytes(byte[] body, HttpStatusCode statusCode = HttpStatusCode.OK,
        string contentType = "application/octet-stream", IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new(statusCode, contentType, headers, "BodyAsBytes", body.ToArray());
    }

    public static StubResponse Empty(HttpStatusCode statusCode = HttpStatusCode.NoContent,
        IReadOnlyDictionary<string, string>? headers = null) => new(statusCode, null, headers);

    internal object AdminModel => _model;
}

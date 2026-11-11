using Ak.AutoTest.Reporting;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ak.AutoTest.Clients;

/// <summary>HTTP-транспорт с отчётностью. Коды ошибок возвращаются тесту без автоматических повторов.</summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiClient(string baseUrl, JsonSerializerOptions? jsonOptions = null, TimeSpan? timeout = null)
        : this(new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = timeout ?? TimeSpan.FromSeconds(120) }, jsonOptions, true)
    {
    }

    /// <param name="ownsClient">Удалять ли переданный HttpClient при освобождении транспорта.</param>
    public ApiClient(HttpClient httpClient, JsonSerializerOptions? jsonOptions = null, bool ownsClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
        _jsonOptions = new JsonSerializerOptions(jsonOptions ?? JsonSerializerOptions.Web);
        _jsonOptions.MakeReadOnly(populateMissingResolver: true);
    }

    public Task<ApiResponse> SendJsonAsync(HttpMethod method, string url, object body,
        IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(method, url, JsonSerializer.Serialize(body, _jsonOptions), headers, cancellationToken);

    public async Task<ApiResponse> SendRawAsync(HttpMethod method, string url, string? body = null,
        IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (headers is not null)
        {
            foreach ((string name, string value) in headers)
            {
                request.Headers.Add(name, value);
            }
        }

        return await SendAsync(request, cancellationToken);
    }

    /// <summary>Отправить запрос, в том числе multipart. Владельцем запроса остаётся вызывающий код.</summary>
    public async Task<ApiResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) =>
        (await SendBinaryAsync(request, cancellationToken)).AsText();

    public Task<BinaryApiResponse> SendBinaryAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) =>
        AllureReport.StepAsync($"{request.Method} {request.RequestUri}", async () =>
        {
            if (request.Content is StringContent)
            {
                AllureReport.AttachJson("request", await request.Content.ReadAsStringAsync(cancellationToken));
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
            {
                headers[header.Key] = header.Value.ToArray();
            }

            string? charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
            BinaryApiResponse result = new(response.StatusCode, content)
            {
                Headers = headers,
                SerializerOptions = _jsonOptions,
                TextEncoding = charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset)
            };
            AllureReport.AddStepParameter("statusCode", (int)response.StatusCode);
            AllureReport.AddStepParameter("contentLength", content.Length);
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)))
            {
                AllureReport.AttachJson("response", result.AsText().Body);
            }

            return result;
        });

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}

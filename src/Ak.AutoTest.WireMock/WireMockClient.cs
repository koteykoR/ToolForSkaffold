using Ak.AutoTest.Helpers;
using FluentAssertions;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Ak.AutoTest.WireMock;

/// <summary>Клиент WireMock: регистрирует и удаляет только собственные заглушки.</summary>
/// <remarks>
/// Общий сброс запрещён: WireMock используется параллельными классами тестов.
/// Маски пути/URL, тела и значения заголовков по умолчанию учитывают регистр.
/// </remarks>
public sealed class WireMockClient : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = null };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    private readonly ConcurrentDictionary<Guid, byte> _ownMappings = new();

    public WireMockClient(string baseUrl)
        : this(new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        }, ownsClient: true)
    {
    }

    /// <summary>Переданный HTTP-клиент по умолчанию остаётся во владении вызывающего кода.</summary>
    public WireMockClient(HttpClient httpClient, bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException("У HTTP-клиента должен быть задан BaseAddress.", nameof(httpClient));
        }
        _httpClient = httpClient;
        _ownsClient = ownsClient;
    }

    public async Task EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("__admin/settings", cancellationToken);
            await EnsureSuccessAsync(response, "проверка доступности", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"WireMock недоступен на {_httpClient.BaseAddress}. Проверьте адрес и доступность административного API.",
                ex);
        }
    }

    /// <summary>Удалить собственные заглушки; при ошибке продолжить очистку остальных.</summary>
    /// <remarks>Неудалённые заглушки остаются для повторной попытки. Отмена прерывает очистку.</remarks>
    /// <exception cref="AggregateException">Часть заглушек удалить не удалось.</exception>
    public async Task RemoveOwnStubsAsync(CancellationToken cancellationToken = default)
    {
        var guids = _ownMappings.Keys.ToArray();
        var errors = new List<Exception>();

        foreach (var guid in guids)
        {
            try
            {
                using var response = await _httpClient.DeleteAsync($"__admin/mappings/{guid}", cancellationToken);
                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    await EnsureSuccessAsync(response, $"удаление заглушки {guid}", cancellationToken);
                }

                _ownMappings.TryRemove(guid, out _);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or InvalidOperationException
                || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                errors.Add(ex);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException(
                $"Не удалось удалить {errors.Count} из {guids.Length} заглушек WireMock.",
                errors);
        }
    }

    /// <summary>Зарегистрировать ответ для запроса. Меньшее значение priority имеет больший приоритет.</summary>
    public async Task StubAsync(RequestMatch request, StubResponse response, int priority = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        Guid guid = Guid.NewGuid();
        var mapping = new
        {
            Guid = guid,
            Request = request.CreateAdminModel(),
            Response = response.AdminModel,
            Priority = priority
        };
        using StringContent content = new(JsonSerializer.Serialize(mapping, SerializerOptions), Encoding.UTF8, "application/json");
        // GUID известен заранее: правило можно очистить даже при потере ответа сервера.
        _ownMappings.TryAdd(guid, 0);
        using HttpResponseMessage result = await _httpClient.PostAsync("__admin/mappings", content, cancellationToken);
        await EnsureSuccessAsync(result, $"регистрация заглушки {request.Method} {request.PathPattern ?? request.UrlPattern}", cancellationToken);
    }

    /// <summary>Заглушка скачивания файла с именем и размером в заголовках ответа.</summary>
    public Task StubGetFileAsync(string pathPattern, byte[] content, string fileName,
        string contentType = "application/octet-stream", CancellationToken cancellationToken = default) =>
        StubAsync(new RequestMatch(HttpMethod.Get, pathPattern),
            StubResponse.Bytes(content, contentType: contentType, headers: new Dictionary<string, string>
            {
                ["Content-Disposition"] = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fileName)}",
                ["Content-Length"] = content.Length.ToString(CultureInfo.InvariantCulture)
            }), cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<RecordedRequest>> FindRequestsAsync(RequestMatch requestMatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestMatch);
        object criteria = requestMatch.CreateAdminModel();
        using StringContent content = new(JsonSerializer.Serialize(criteria, SerializerOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.PostAsync("__admin/requests/find", content, cancellationToken);
        await EnsureSuccessAsync(response, $"поиск запросов {requestMatch.Method} {requestMatch.PathPattern ?? requestMatch.UrlPattern}", cancellationToken);
        using JsonDocument entries = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return entries.RootElement.EnumerateArray().Select(ReadRecordedRequest).ToArray();
    }

    /// <summary>Найти строковые тела запросов по условиям текущего сценария.</summary>
    public async Task<IReadOnlyList<string>> FindRequestBodiesAsync(RequestMatch request,
        CancellationToken cancellationToken = default) =>
        (await FindRequestsAsync(request, cancellationToken))
            .Select(entry => entry.Body).OfType<string>().ToArray();

    /// <summary>Найти и десериализовать тела запросов по условиям текущего сценария.</summary>
    public async Task<IReadOnlyList<T>> FindRequestBodiesAsync<T>(RequestMatch request,
        JsonSerializerOptions? jsonOptions = null, CancellationToken cancellationToken = default) =>
        (await FindRequestBodiesAsync(request, cancellationToken))
            .Select(body => JsonSerializer.Deserialize<T>(body, jsonOptions ?? JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException($"Пустой результат разбора запроса {request.Method} {request.PathPattern ?? request.UrlPattern} как {typeof(T).Name}."))
            .ToArray();

    /// <summary>Дождаться точного числа обращений. Условия должны выделять запросы текущего сценария.</summary>
    public Task<IReadOnlyList<RecordedRequest>> WaitForRequestsAsync(RequestMatch request, int expectedCount,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expectedCount, 0);
        return Polling.WaitAsync(
            token => FindRequestsAsync(request, token),
            requests =>
            {
                requests.Count.Should().BeLessThanOrEqualTo(expectedCount, "получены лишние обращения к заглушке");
                return requests.Count == expectedCount;
            }, timeout, cancellationToken,
            description: $"{expectedCount} запросов {request.Method} {request.PathPattern ?? request.UrlPattern}");
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private static RecordedRequest ReadRecordedRequest(JsonElement entry)
    {
        JsonElement request = entry.GetProperty("Request");
        Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase);
        if (request.TryGetProperty("Headers", out JsonElement storedHeaders) && storedHeaders.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty header in storedHeaders.EnumerateObject())
            {
                headers[header.Name] = header.Value.ValueKind == JsonValueKind.Array
                    ? header.Value.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray()
                    : [header.Value.GetString() ?? string.Empty];
            }
        }
        return new RecordedRequest(
            request.GetProperty("Method").GetString()!,
            request.GetProperty("Url").GetString()!,
            headers,
            request.TryGetProperty("Body", out JsonElement body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"WireMock отклонил запрос ({operation}): {(int)response.StatusCode} {body}");
    }
}

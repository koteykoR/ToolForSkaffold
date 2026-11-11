namespace Ak.AutoTest.WireMock;

/// <summary>Запрос из журнала WireMock после ограничения выборки данными теста.</summary>
public sealed record RecordedRequest(string Method, string Url, IReadOnlyDictionary<string, string[]> Headers, string? Body);

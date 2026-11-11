namespace Ak.AutoTest.WireMock;

/// <summary>Условия регистрации заглушки и поиска запросов в журнале WireMock.</summary>
public sealed record RequestMatch(HttpMethod Method, string? PathPattern = null)
{
    public string? UrlPattern { get; init; }
    public string? BodyPattern { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public bool IgnoreUrlCase { get; init; }
    public bool IgnoreBodyCase { get; init; }
    public bool IgnoreHeaderValueCase { get; init; }

    internal object CreateAdminModel()
    {
        ArgumentNullException.ThrowIfNull(Method);
        if ((PathPattern is null) == (UrlPattern is null))
        {
            throw new ArgumentException("Укажите либо PathPattern, либо UrlPattern.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(PathPattern ?? UrlPattern);

        Dictionary<string, object> criteria = new()
        {
            [UrlPattern is null ? "Path" : "Url"] = new
            {
                Matchers = new[] { Matcher("WildcardMatcher", PathPattern ?? UrlPattern!, IgnoreUrlCase) }
            },
            ["Methods"] = new[] { Method.Method }
        };
        if (BodyPattern is not null)
        {
            criteria["Body"] = new { Matcher = Matcher("WildcardMatcher", BodyPattern, IgnoreBodyCase) };
        }
        if (Headers is not null)
        {
            criteria["Headers"] = Headers.Select(header => new
            {
                Name = header.Key,
                // Имена заголовков нечувствительны к регистру независимо от значений.
                IgnoreCase = true,
                Matchers = new[] { Matcher("ExactMatcher", header.Value, IgnoreHeaderValueCase) }
            }).ToArray();
        }
        return criteria;
    }

    private static object Matcher(string name, string pattern, bool ignoreCase) =>
        new { Name = name, Pattern = pattern, IgnoreCase = ignoreCase };
}

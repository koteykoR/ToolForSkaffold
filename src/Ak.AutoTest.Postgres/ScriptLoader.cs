using System.Collections.Concurrent;

namespace Ak.AutoTest.Postgres;

/// <summary>
/// Чтение SQL-скриптов из каталога, переданного проектом сервиса.
/// </summary>
public sealed class ScriptLoader(string directory)
{
    private readonly ConcurrentDictionary<string, string> _cache = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// Загрузить скрипт по пути относительно выбранного каталога, например <c>Insert/SeedGuarantees.sql</c>.
    /// </summary>
    public string Load(string relativePath) => _cache.GetOrAdd(relativePath, path =>
    {
        var fullPath = Path.Combine(
            directory,
            path.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"SQL-скрипт не найден: {fullPath}");
        }

        return File.ReadAllText(fullPath);
    });
}

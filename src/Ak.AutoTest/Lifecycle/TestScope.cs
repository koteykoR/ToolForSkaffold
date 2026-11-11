namespace Ak.AutoTest.Lifecycle;

/// <summary>Ресурсы одного теста или фикстуры. Очистка выполняется в обратном порядке регистрации.</summary>
public sealed class TestScope : IAsyncDisposable
{
    private readonly Stack<(string Name, Func<Task> Cleanup)> _actions = new();
    private readonly object _gate = new();
    private Task? _disposal;
    private bool _closed;

    public static string RunId { get; } = Guid.NewGuid().ToString("N");
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public void RegisterCleanup(string name, Func<Task> cleanup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _actions.Push((name, cleanup));
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closed = true;
            _disposal ??= CleanupAsync(_actions.ToArray());
            return new ValueTask(_disposal);
        }
    }

    private static async Task CleanupAsync((string Name, Func<Task> Cleanup)[] actions)
    {
        List<Exception> errors = [];
        foreach ((string name, Func<Task> cleanup) in actions)
        {
            try
            {
                await cleanup();
            }
            catch (Exception error)
            {
                errors.Add(new InvalidOperationException($"Не удалось очистить ресурс: {name}.", error));
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Ошибки очистки ресурсов теста.", errors);
        }
    }
}

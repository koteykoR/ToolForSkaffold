using System.Diagnostics;

namespace Ak.AutoTest.Helpers;

/// <summary>Ожидание условия и наблюдение за состоянием с ограничением времени.</summary>
public static class Polling
{
    public static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>Вернуть подходящее значение; истечение времени — TimeoutException.</summary>
    /// <remarks>Чтение должно учитывать переданный токен. Параллельные повторные чтения не запускаются.</remarks>
    public static async Task<T> WaitAsync<T>(
        Func<CancellationToken, Task<T>> read,
        Func<T, bool> completed,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        TimeSpan? interval = null,
        string? description = null)
    {
        TimeSpan duration = timeout ?? ProcessingTimeout;
        TimeSpan delay = interval ?? DefaultInterval;
        Validate(duration, delay);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(duration);
        T? lastValue = default;
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                lastValue = await read(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                if (completed(lastValue))
                {
                    return lastValue;
                }

                await Task.Delay(delay, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"Не выполнено условие «{description ?? "ожидание результата"}» за {duration}. Последнее значение: {lastValue}.");
        }
    }

    /// <summary>Наблюдать за состоянием не менее timeout; ошибка проверки немедленно завершает наблюдение.</summary>
    /// <remarks>
    /// Каждая проверка ограничена checkTimeout (по умолчанию timeout). Последняя проверка может
    /// завершиться после интервала наблюдения, но не позже собственного лимита; иначе — TimeoutException.
    /// Делегат должен выполнять ввод-вывод асинхронно и учитывать токен: ожидание ограничено даже при
    /// игнорировании отмены, но принудительно остановить такую операцию невозможно.
    /// </remarks>
    public static async Task ObserveAsync(
        Func<CancellationToken, Task> check,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        TimeSpan? interval = null,
        TimeSpan? checkTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        TimeSpan duration = timeout ?? ObservationTimeout;
        TimeSpan delay = interval ?? DefaultInterval;
        TimeSpan checkLimit = checkTimeout ?? duration;
        Validate(duration, delay);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(checkLimit, TimeSpan.Zero);
        Stopwatch watch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                TimeSpan checkStartedAt = watch.Elapsed;
                deadline.CancelAfter(checkLimit);
                try
                {
                    await check(deadline.Token).WaitAsync(deadline.Token);
                    if (watch.Elapsed - checkStartedAt >= checkLimit)
                    {
                        deadline.Cancel();
                    }

                    deadline.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
                {
                    throw new TimeoutException($"Проверка состояния не завершилась за {checkLimit}.", error);
                }
            }

            TimeSpan remaining = duration - watch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            TimeSpan pause = remaining < delay ? remaining : delay;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(pause.TotalMilliseconds)), cancellationToken);
        }
    }

    private static void Validate(TimeSpan duration, TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
    }
}

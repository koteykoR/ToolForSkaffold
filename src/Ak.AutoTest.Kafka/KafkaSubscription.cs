using Ak.AutoTest.Reporting;
using Confluent.Kafka;

namespace Ak.AutoTest.Kafka;

/// <summary>Изолированное чтение назначенных partition без изменения offset приложения.</summary>
/// <remarks>Использовать последовательно. Позиции должны быть выбраны до действия, порождающего событие.</remarks>
public sealed class KafkaSubscription : IDisposable
{
    private readonly IConsumer<string, string> _consumer;
    private int _reading;

    public KafkaSubscription(ConsumerConfig config, IEnumerable<TopicPartitionOffset> positions)
    {
        TopicPartitionOffset[] assignments = positions.ToArray();
        if (assignments.Length == 0)
        {
            throw new ArgumentException("Нужна хотя бы одна partition с начальной позицией.", nameof(positions));
        }
        ConsumerConfig effectiveConfig = new(config)
        {
            GroupId = $"autotest-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            EnablePartitionEof = false
        };
        _consumer = new ConsumerBuilder<string, string>(effectiveConfig).Build();
        try
        {
            // Offset.End разрешается сейчас: отложенное разрешение после публикации потеряло бы событие.
            TopicPartitionOffset[] resolved = assignments.Select(position => position.Offset == Offset.End
                ? new TopicPartitionOffset(position.TopicPartition, _consumer.QueryWatermarkOffsets(position.TopicPartition, TimeSpan.FromSeconds(10)).High)
                : position).ToArray();
            _consumer.Assign(resolved);
        }
        catch
        {
            _consumer.Dispose();
            throw;
        }
    }

    public Task<ConsumeResult<string, string>> WaitAsync(Func<ConsumeResult<string, string>, bool> matches,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        return AllureReport.StepAsync("Kafka: ожидание сообщения", () => Task.Run(() =>
        {
            if (Interlocked.Exchange(ref _reading, 1) != 0)
            {
                throw new InvalidOperationException("Подписка уже используется другим ожиданием.");
            }
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                while (true)
                {
                    ConsumeResult<string, string> message = _consumer.Consume(deadline.Token);
                    if (matches(message))
                    {
                        AllureReport.AttachJson("message", message.Message.Value);
                        AllureReport.AddStepParameter("position", message.TopicPartitionOffset.ToString());
                        return message;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
            {
                throw new TimeoutException($"Kafka: подходящее сообщение не получено за {timeout}.");
            }
            finally
            {
                Volatile.Write(ref _reading, 0);
            }
        }, cancellationToken));
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _reading) != 0)
        {
            throw new InvalidOperationException("Отмените и дождитесь завершения чтения перед освобождением подписки.");
        }
        _consumer.Dispose();
    }
}

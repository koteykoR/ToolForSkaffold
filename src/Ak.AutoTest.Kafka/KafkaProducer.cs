using Ak.AutoTest.Reporting;
using Confluent.Kafka;
using System.Text.Json;

namespace Ak.AutoTest.Kafka;

public sealed class KafkaProducer : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly JsonSerializerOptions _jsonOptions;

    public KafkaProducer(ProducerConfig config, JsonSerializerOptions? jsonOptions = null)
    {
        ProducerConfig effectiveConfig = new(config) { Acks = Acks.All };
        _producer = new ProducerBuilder<string, string>(effectiveConfig).Build();
        _jsonOptions = new JsonSerializerOptions(jsonOptions ?? JsonSerializerOptions.Default);
        _jsonOptions.MakeReadOnly(populateMissingResolver: true);
    }

    public Task<DeliveryResult<string, string>> PublishAsync<T>(string topic, T value, string? key = null,
        Headers? headers = null, CancellationToken cancellationToken = default) =>
        PublishRawAsync(topic, new Message<string, string> { Key = key!, Value = JsonSerializer.Serialize(value, _jsonOptions), Headers = headers }, cancellationToken);

    /// <summary>Публикация исходного тела, включая намеренно невалидный JSON.</summary>
    public Task<DeliveryResult<string, string>> PublishRawAsync(string topic, Message<string, string> message, CancellationToken cancellationToken = default) =>
        AllureReport.StepAsync($"Kafka: публикация в {topic}", async () =>
        {
            AllureReport.AttachJson("message", message.Value);
            DeliveryResult<string, string> result = await _producer.ProduceAsync(topic, message, cancellationToken);
            if (result.Status != PersistenceStatus.Persisted)
            {
                throw new InvalidOperationException($"Kafka не подтвердила запись в {topic}: {result.Status}.");
            }
            AllureReport.AddStepParameter("partition", result.Partition.Value);
            AllureReport.AddStepParameter("offset", result.Offset.Value);
            return result;
        });

    public void Dispose() => _producer.Dispose();
}

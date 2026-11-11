using Ak.AutoTest.Lifecycle;
using Ak.AutoTest.Reporting;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Ak.AutoTest.MongoDb;

/// <summary>Операции MongoDB с отчётностью; фильтры и правила удаления задаёт проект сервиса.</summary>
public sealed class MongoCollection<T>(IMongoCollection<T> collection)
{
    public string Name => collection.CollectionNamespace.FullName;

    public Task InsertAsync(T document, CancellationToken cancellationToken = default) =>
        AllureReport.StepAsync($"MongoDB: вставка в {Name}", async () =>
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
            AllureReport.AttachJson("document", document.ToJson());
        });

    /// <summary>Зарегистрировать точное удаление до вставки, в том числе на случай потери подтверждения записи.</summary>
    public Task InsertAsync(T document, TestScope resources, FilterDefinition<T> cleanupFilter, CancellationToken cancellationToken = default)
    {
        resources.RegisterCleanup($"MongoDB {Name}", async () => { await DeleteAsync(cleanupFilter); });
        return InsertAsync(document, cancellationToken);
    }

    public Task<T?> FindOneAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default) =>
        AllureReport.StepAsync<T?>($"MongoDB: чтение из {Name}", async () =>
        {
            T? document = await collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            if (document is not null)
            {
                AllureReport.AttachJson("document", document.ToJson());
            }
            return document;
        });

    public Task<long> DeleteAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default) =>
        AllureReport.StepAsync($"MongoDB: удаление из {Name}", async () =>
        {
            DeleteResult result = await collection.DeleteManyAsync(filter, cancellationToken);
            AllureReport.AddStepParameter("deletedCount", result.DeletedCount);
            return result.DeletedCount;
        });
}

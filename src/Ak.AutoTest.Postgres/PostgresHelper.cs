using Dapper;
using Ak.AutoTest.Reporting;
using Npgsql;

namespace Ak.AutoTest.Postgres;

/// <summary>
/// Доступ к PostgreSQL: чтение эталонных данных и подготовка тестовых записей.
/// </summary>
public sealed class PostgresHelper
{
    private readonly string _connectionString;

    public PostgresHelper(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await AllureReport.StepAsync("PostgreSQL: чтение", async () =>
        {
            AllureReport.AttachJson("query", System.Text.Json.JsonSerializer.Serialize(new { sql, parameters }));
            T[] rows = (await connection.QueryAsync<T>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).ToArray();
            AllureReport.AddStepParameter("rowCount", rows.Length);
            return (IReadOnlyList<T>)rows;
        });
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await AllureReport.StepAsync("PostgreSQL: чтение одной строки", async () =>
        {
            AllureReport.AttachJson("query", System.Text.Json.JsonSerializer.Serialize(new { sql, parameters }));
            return await connection.QuerySingleOrDefaultAsync<T>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        });
    }

    /// <summary>Выполнить запрос в транзакции под advisory-блокировкой <paramref name="lockKey"/>.</summary>
    /// <remarks>Вызовы с одним ключом выполняются последовательно. Блокировка снимается при завершении транзакции.</remarks>
    public async Task<IReadOnlyList<T>> QueryLockedAsync<T>(
        long lockKey,
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@LockKey)",
            new { LockKey = lockKey },
            transaction,
            cancellationToken: cancellationToken));

        var rows = (await connection.QueryAsync<T>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken))).ToArray();

        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    public async Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await AllureReport.StepAsync("PostgreSQL: выполнение команды", async () =>
        {
            AllureReport.AttachJson("query", System.Text.Json.JsonSerializer.Serialize(new { sql, parameters }));
            return await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        });
    }
}

namespace Ak.AutoTest.Fixtures;

/// <summary>
/// Асинхронная подготовка и очистка фикстуры: вызываются один раз на класс тестов.
/// </summary>
public interface IAsyncFixture : IAsyncDisposable
{
    Task InitializeAsync();
}

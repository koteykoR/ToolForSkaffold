using Ak.AutoTest.Lifecycle;
using Allure.NUnit;
using NUnit.Framework;
using System.Collections.Concurrent;

namespace Ak.AutoTest.Fixtures;

/// <summary>Одна фикстура на класс: подготовка перед первым тестом, очистка после последнего.</summary>
[AllureNUnit]
public abstract class FixtureTest<TFixture>
    where TFixture : class, IAsyncFixture, new()
{
    private readonly ConcurrentDictionary<string, TestScope> _testScopes = new();

    protected TFixture Fixture { get; private set; } = null!;

    protected TestScope Resources => _testScopes[TestContext.CurrentContext.Test.ID];

    [SetUp]
    public void CreateTestScope() => _testScopes[TestContext.CurrentContext.Test.ID] = new TestScope();

    [TearDown]
    public async Task CleanupTestScopeAsync()
    {
        if (_testScopes.TryRemove(TestContext.CurrentContext.Test.ID, out TestScope? resources))
        {
            await resources.DisposeAsync();
        }
    }

    [OneTimeSetUp]
    public async Task SetUpFixtureAsync()
    {
        Fixture = new TFixture();
        await Fixture.InitializeAsync();
    }

    [OneTimeTearDown]
    public async Task TearDownFixtureAsync()
    {
        if (Fixture is null)
        {
            return;
        }

        await Fixture.DisposeAsync();
    }
}

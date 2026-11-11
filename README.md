# Ak.AutoTest

Общие инструменты интеграционных автотестов на .NET 10, NUnit и Allure. Библиотека не содержит бизнес-моделей, адресов окружения и правил очистки данных конкретного сервиса.

## Пакеты

- `Ak.AutoTest`: HTTP-клиент и проверки ответов, ожидания, Allure, ресурсы теста, базовый жизненный цикл NUnit.
- `Ak.AutoTest.WireMock`: регистрация и удаление собственных правил, поиск запросов по методу, пути, телу и заголовкам, ожидание числа обращений.
- `Ak.AutoTest.Kafka`: публикация с ключом и заголовками; отдельная подписка для ожидания выходных сообщений.
- `Ak.AutoTest.Postgres`: параметризованный SQL, транзакции с advisory lock, чтение файлов SQL.
- `Ak.AutoTest.MongoDb`: операции коллекций с отчётностью и регистрацией точной очистки.

Дополнительные пакеты зависят от `Ak.AutoTest`, но не друг от друга. Проект подключает только нужные технологии. В пакетах лежат DLL и portable PDB со встроенными исходниками: отдельный сервер символов не требуется. Source Link можно добавить после появления удалённого Git-репозитория.

## Локальная сборка и использование

Публикация и удалённый Git пока не настроены. Из корня проекта-потребителя:

```powershell
pwsh -File ../ak-autotest/Pack.ps1 -OutputDirectory ./artifacts/autotest-packages
```

Путь вывода разрешается относительно текущего каталога. Скрипт выполняет restore, build и pack всех пяти проектов. Версия задаётся один раз в `Directory.Build.props`. При изменении реализации увеличивайте версию перед упаковкой: NuGet может использовать старое содержимое из кэша при повторном выпуске того же номера.

В проекте-потребителе укажите пакет и локальный источник (путь примера рассчитан на `test/<project>`):

```xml
<PropertyGroup>
  <RestoreAdditionalProjectSources>$(RestoreAdditionalProjectSources);$(MSBuildThisFileDirectory)../../artifacts/autotest-packages</RestoreAdditionalProjectSources>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Ak.AutoTest" Version="[0.1.0-preview.10]" />
</ItemGroup>
```

Папка с `.nupkg` должна попасть в Docker build context. Исходники библиотеки в образе потребителя не нужны. После появления внутреннего feed пакеты публикуются туда, а дополнительный локальный источник удаляется. `Pack.ps1` ничего не публикует.

## Границы проекта сервиса

В сервисе остаются маршруты API, контракты сообщений, SQL, BSON-проекции, заглушки бизнес-зависимостей, генераторы предметных данных, assertions бизнес-результата и сами сценарии. Небольшой адаптер читает конфигурацию окружения и передаёт клиентам адрес API, параметры Kafka или строку подключения БД. Библиотека не разбирает произвольные манифесты Kubernetes и не предполагает, что зависимости доступны через localhost.

Параметры JSON также задаёт потребитель. Правила сериализации запроса не берутся из настроек оформления Allure.

## HTTP и assertions

`ApiClient` принимает базовый URL либо существующий `HttpClient`. Во втором случае по умолчанию владельцем `HttpClient` остаётся вызывающий код. Ответы 4xx/5xx возвращаются как обычные ответы; повторов запросов нет.

```csharp
using Ak.AutoTest.Assertions;
using Ak.AutoTest.Clients;
using System.Net;
using System.Text.Json;

using var api = new ApiClient("http://localhost:5040", new JsonSerializerOptions(JsonSerializerDefaults.Web));
var response = await api.SendRawAsync(HttpMethod.Post, "items/search", "{\"query\":\"example\"}");
response.ShouldHaveStatusCode(HttpStatusCode.OK);
var items = response.Deserialize<string[]>();
```

`SendJsonAsync` сериализует объект. `SendRawAsync` сохраняет исходную строку, включая невалидный JSON. Для multipart или специальных content headers создайте `HttpRequestMessage` и передайте его в `SendAsync`; владение запросом остаётся у вас. `SendBinaryAsync` возвращает байты без декодирования. В ответах доступны заголовки; `AsText` учитывает charset ответа.

`AssertStatusAndRead<T>` объединяет проверку точного HTTP-кода и разбор ответа. `ShouldHaveHeader` проверяет ожидаемое значение заголовка, `ShouldHaveEmptyBody` — отсутствие тела. Сравнение предметных моделей выполняйте FluentAssertions в проекте сервиса. Конкретный формат ошибок валидации библиотека не навязывает.

## Ожидания

```csharp
using Ak.AutoTest.Helpers;

int attempts = 0;
int result = await Polling.WaitAsync(
    token => Task.FromResult(++attempts),
    value => value == 3,
    timeout: TimeSpan.FromSeconds(5),
    description: "состояние готово");
```

`WaitAsync` возвращает только значение, выполнившее условие. По таймауту выбрасывается `TimeoutException`; в сообщении есть последнее значение. Делегат чтения обязан передавать полученный токен в I/O-операции. Отмена вызывающего кода остаётся `OperationCanceledException`. Значения по умолчанию: ожидание 15 секунд, интервал 400 мс.

`ObserveAsync` проверяет состояние не менее `timeout` (по умолчанию 5 секунд). Отдельный `checkTimeout` ограничивает каждую проверку; если не задан, он равен `timeout`. Последняя проверка может закончиться после интервала наблюдения, но обязана уложиться в свой лимит. Поэтому весь вызов занимает не более `timeout + checkTimeout` с учётом точности таймеров и планировщика. Незавершённая проверка вызывает `TimeoutException`, любая ошибка проверки завершает наблюдение сразу.

Проверка должна возвращать `Task` без блокировки потока и передавать токен в операции ввода-вывода. Метод ограничивает ожидание даже при игнорировании отмены, но остановить такую операцию принудительно не может. Наблюдение отсутствия записи или вызова само по себе не доказывает обработку входного Kafka-сообщения.

## Жизненный цикл

`TestScope` хранит ID области и ID запуска процесса. Зарегистрированные действия выполняются в обратном порядке; ошибка одного не отменяет остальные. Повторное освобождение не повторяет действия. Ошибки возвращаются одним `AggregateException`.

```csharp
using Ak.AutoTest.Lifecycle;

await using var resources = new TestScope();
string file = Path.Combine(Path.GetTempPath(), resources.Id + ".txt");
resources.RegisterCleanup("временный файл", () =>
{
    File.Delete(file);
    return Task.CompletedTask;
});
await File.WriteAllTextAsync(file, "example");
```

Регистрируйте очистку сразу, когда стали известны идентификаторы, до потенциально частично успешной операции. Удаляйте только данные своей области. Библиотека не вычисляет фильтры удаления за проект.

`FixtureTest<TFixture>` создаёт одну фикстуру на класс. Фикстура реализует `IAsyncFixture`, который наследует `IAsyncDisposable` и добавляет `Task InitializeAsync()`. Единственный метод очистки — `ValueTask DisposeAsync()`: в нём удаляют тестовые данные и освобождают клиенты (в `finally`, чтобы ошибка удаления не помешала освобождению). Отдельное свойство `Resources` предоставляет область текущего теста и очищается в NUnit TearDown. Области отдельных тестов изолированы даже при параллельном запуске методов; сама общая фикстура при этом должна быть безопасна для такого использования. Бизнес-метки Allure задаются в тестовых классах.

## WireMock

`WireMockClient` работает с административным API WireMock.Net; целевая версия окружения — 1.7.4. Клиент принимает URL либо готовый `HttpClient` с `BaseAddress` (заканчивающимся `/`). Переданный HTTP-клиент по умолчанию остаётся во владении вызывающего кода; `ownsClient: true` передаёт владение обёртке.

Для регистрации, поиска и ожидания используется один `RequestMatch`: метод, маска пути **либо** полного URL, необязательная маска тела и точные значения заголовков. Условия должны выделять запросы текущего сценария. Маски пути/URL и тела используют `WildcardMatcher`: `*` означает произвольный фрагмент строки, а не JSONPath.

```csharp
using Ak.AutoTest.WireMock;
using System.Net;

using var wireMock = new WireMockClient("http://localhost:8080");
var request = new RequestMatch(HttpMethod.Get, "/customers/123")
{
    Headers = new Dictionary<string, string> { ["X-Tenant"] = "company-a" }
};

try
{
    await wireMock.StubAsync(request,
        StubResponse.Json(new { message = "Customer not found" },
            statusCode: HttpStatusCode.NotFound));

    // Здесь выполняется действие сервиса, вызывающее эту зависимость.
    var calls = await wireMock.WaitForRequestsAsync(request, expectedCount: 1);
}
finally
{
    await wireMock.RemoveOwnStubsAsync();
}
```

`StubResponse.Json` сериализует объект (в том числе `null`) и принимает `JsonSerializerOptions`. По умолчанию имена свойств сохраняются. `Text` отправляет исходную строку, включая невалидный JSON; его тип по умолчанию — `text/plain; charset=utf-8`. `Bytes` возвращает байты, `Empty` — ответ без тела. Для каждого формата можно задать HTTP-статус и заголовки. Переданные заголовки имеют приоритет над значениями по умолчанию. Меньшее значение `priority` в `StubAsync` означает больший приоритет правила.

По умолчанию путь/URL, тело и значения заголовков **учитывают регистр**. В `RequestMatch` настройки независимы: `IgnoreUrlCase` действует на путь либо полный URL, `IgnoreBodyCase` — на тело, `IgnoreHeaderValueCase` — на значения заголовков. Имена HTTP-заголовков всегда сравниваются без учёта регистра.

```csharp
var request = new RequestMatch(HttpMethod.Post, "/events")
{
    BodyPattern = "*ABC*",
    IgnoreBodyCase = true // Путь и значения заголовков остаются строгими.
};
var calls = await wireMock.FindRequestsAsync(request);
```

`FindRequestsAsync` возвращает полные записи: метод, URL, заголовки и тело. `FindRequestBodiesAsync` возвращает строковые тела, а `FindRequestBodiesAsync<T>` десериализует их с переданными настройками JSON. Все три метода принимают `RequestMatch`. `WaitForRequestsAsync` ожидает точное положительное число обращений. Лишние обращения, замеченные во время ожидания, приводят к ошибке; отсутствие последующих дублей этот метод не гарантирует.

Для скачивания файла оставлен короткий `StubGetFileAsync(pathPattern, content, fileName)`: он задаёт байты, имя и размер файла через общий `StubAsync`. Остальные ответы, включая обычный GET с JSON, регистрируются через `StubAsync`. Для полного URL, тела, заголовков и настройки регистра используйте `RequestMatch`; строковых перегрузок поиска и ожидания нет.

Каждый клиент хранит GUID своих правил. `RemoveOwnStubsAsync` удаляет только их, пытается удалить все и сообщает ошибки. Его нужно вызвать до `Dispose`: освобождение HTTP-клиента само по себе правила не удаляет. Журнал запросов общий; удаление правил не очищает его. Multipart и требования конкретного внешнего API остаются в проекте-потребителе.

## Kafka

Создавайте подписку до действия, порождающего событие. Назначьте нужные partition и начальные позиции явно. Для `Offset.End` текущая верхняя граница разрешается при создании подписки, а не после отправки сообщения.

```csharp
using Ak.AutoTest.Kafka;
using Confluent.Kafka;

using var subscription = new KafkaSubscription(
    new ConsumerConfig { BootstrapServers = "localhost:9092" },
    [new TopicPartitionOffset("output", new Partition(0), Offset.End)]);
using var producer = new KafkaProducer(new ProducerConfig { BootstrapServers = "localhost:9092" });
string id = Guid.NewGuid().ToString("N");
var published = await producer.PublishAsync("input", new { Id = id }, key: id);
var received = await subscription.WaitAsync(message => message.Message.Key == id, TimeSpan.FromSeconds(15));
```

Пример предполагает, что сервис переносит ключ во входном событии в выходное, и явно читает только partition 0. Для нескольких partition перечислите их все. В проекте сервиса задавайте подходящее условие корреляции. Подписка использует отдельный group ID, не коммитит offset и не меняет позиции consumer приложения. Одна подписка предназначена для одного последовательного читателя; перед `Dispose` отмените и дождитесь активного ожидания.

`PublishAsync` возвращает подтверждение брокера с partition/offset; оно не означает завершение бизнес-обработки. `PublishRawAsync` принимает исходное сообщение, включая намеренно невалидное тело. Проект сам определяет топики, ключи, заголовки и формат JSON.

## Базы данных

`PostgresHelper` получает строку подключения. `QueryAsync<T>`, `QuerySingleOrDefaultAsync<T>` и `ExecuteAsync` принимают SQL и параметры Dapper; ошибки не скрываются. `QueryLockedAsync<T>` выполняет запрос в транзакции с указанным advisory lock. Имена таблиц и SQL принадлежат сервису. `ScriptLoader` получает каталог скриптов при создании; строки подключения в отчёт не записываются.

`MongoCollection<T>` получает готовый `IMongoCollection<T>`. `InsertAsync`, `FindOneAsync` и `DeleteAsync` добавляют отчётность, сохраняя нативные Mongo-фильтры. Перегрузка `InsertAsync` с `TestScope` регистрирует переданный фильтр очистки до вставки. Фильтр должен однозначно выбирать ваши данные. Конвенции ObjectId, имена баз и коллекций библиотека не задаёт.

Для ожидания состояния БД используйте `Polling` и передавайте его токен в запрос. Для сравнения результата используйте бизнес-assertions проекта; общий DSL запросов для SQL и MongoDB не вводится.

## Отчёты

`AllureReport` оформляет шаги и вложения; вне Allure-контекста выполняет переданное действие без отчётности. `AttachParameters` добавляет диагностические данные вложением, не меняя historyId теста. Аргументы NUnit остаются параметрами сценария. Не помещайте реальные секреты в тела, которые прикрепляются к отчёту.

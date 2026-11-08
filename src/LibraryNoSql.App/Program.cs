using System.Text;
using LibraryNoSql.App.Data;
using LibraryNoSql.App.Models;
using LibraryNoSql.App.Repositories;
using LibraryNoSql.App.Services;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

const string pgConn = "Host=localhost;Port=5433;Database=library_nosql_lab;Username=postgres;Password=postgres";
const string mongoConn = "mongodb://localhost:27017";
const string mongoDb = "library_nosql_lab";
const string redisConn = "localhost:6379";

await using var sqlRepo = new SqlMediaCatalogRepository(pgConn);
await using var mongoRepo = new MongoMediaCatalogRepository(mongoConn, mongoDb);
await using var redisService = new RedisLibraryService(redisConn);
var runner = new BenchmarkRunner(sqlRepo, mongoRepo);

GeneratedDataset? currentDataset = null;

while (true)
{
    Console.WriteLine();
    Console.WriteLine("1. Генерація даних (10k / 50k / 200k)");
    Console.WriteLine("2. Запити PostgreSQL vs MongoDB");
    Console.WriteLine("3. Тест швидкодії (поточний набір)");
    Console.WriteLine("4. Серія тестів (10k -> 50k -> 200k)");
    Console.WriteLine("5. Плани виконання (EXPLAIN)");
    Console.WriteLine("6. Базові запити Redis");
    Console.WriteLine("0. Вихід");
    Console.Write("Вибір: ");

    string? choice = Console.ReadLine()?.Trim();
    if (choice == "0" || choice is null)
    {
        break;
    }

    try
    {
        switch (choice)
        {
            case "1":
                int count = PromptScale();
                currentDataset = CatalogDataGenerator.Generate(count);
                await runner.SeedBothAsync(currentDataset);
                break;

            case "2":
                currentDataset = await EnsureDataAsync(currentDataset, runner, sqlRepo);
                await RunCrudComparisonDemoAsync(sqlRepo, mongoRepo);
                break;

            case "3":
                currentDataset = await EnsureDataAsync(currentDataset, runner, sqlRepo);
                var metrics = await runner.RunSuiteAsync(currentDataset);
                BenchmarkRunner.PrintResults(metrics);
                break;

            case "4":
                foreach (int scale in new[] { 10_000, 50_000, 200_000 })
                {
                    Console.WriteLine($"\n--- Масштаб: {scale:N0} ---");
                    currentDataset = CatalogDataGenerator.Generate(scale);
                    await runner.SeedBothAsync(currentDataset);
                    var scaleMetrics = await runner.RunSuiteAsync(currentDataset);
                    BenchmarkRunner.PrintResults(scaleMetrics);
                }
                break;

            case "5":
                currentDataset = await EnsureDataAsync(currentDataset, runner, sqlRepo);
                await PrintExplainsAsync(sqlRepo, mongoRepo);
                break;

            case "6":
                currentDataset = await EnsureDataAsync(currentDataset, runner, sqlRepo);
                await redisService.RunDemoAsync(mongoRepo, "item-0000001");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Помилка: {ex.Message}");
    }
}

static int PromptScale()
{
    Console.Write("Масштаб (1 = 10 000, 2 = 50 000, 3 = 200 000): ");
    return Console.ReadLine()?.Trim() switch
    {
        "2" => 50_000,
        "3" => 200_000,
        _ => 10_000
    };
}

static async Task<GeneratedDataset> EnsureDataAsync(
    GeneratedDataset? cached,
    BenchmarkRunner runner,
    IMediaCatalogRepository sqlRepo)
{
    if (cached is not null)
    {
        return cached;
    }

    int existing = await sqlRepo.GetBaseItemCountAsync();
    if (existing == 0)
    {
        var generated = CatalogDataGenerator.Generate(10_000);
        await runner.SeedBothAsync(generated);
        return generated;
    }

    return CatalogDataGenerator.Generate(existing);
}

static async Task RunCrudComparisonDemoAsync(IMediaCatalogRepository sql, IMediaCatalogRepository mongo)
{
    const string id = "item-0000001";
    var sqlCard = await sql.GetByIdAsync(id);
    var mongoCard = await mongo.GetByIdAsync(id);

    Console.WriteLine($"[GetById] SQL   : {sqlCard?.Id} | {sqlCard?.ItemType} | {sqlCard?.Title} | авторів: {sqlCard?.Contributors.Count} | атрибутів: {sqlCard?.Attributes.Count}");
    Console.WriteLine($"[GetById] Mongo : {mongoCard?.Id} | {mongoCard?.ItemType} | {mongoCard?.Title} | авторів: {mongoCard?.Contributors.Count} | атрибутів: {mongoCard?.Attributes.Count}");

    var sqlGames = await sql.SearchBoardGamesForPlayersAsync(4, 90, 5);
    var mongoGames = await mongo.SearchBoardGamesForPlayersAsync(4, 90, 5);
    Console.WriteLine($"[SearchSpecs] SQL: {sqlGames.Count} (перший: {sqlGames.FirstOrDefault()?.Id}), Mongo: {mongoGames.Count} (перший: {mongoGames.FirstOrDefault()?.Id})");

    var sqlByRole = await sql.SearchByContributorRoleAsync(1, "author", 5);
    var mongoByRole = await mongo.SearchByContributorRoleAsync(1, "author", 5);
    Console.WriteLine($"[SearchRole]  SQL: {sqlByRole.Count}, Mongo: {mongoByRole.Count}");

    var sqlAgg = await sql.GetGenreTypeStatsAsync();
    var mongoAgg = await mongo.GetGenreTypeStatsAsync();
    Console.WriteLine($"[Aggregation] SQL груп: {sqlAgg.Count}, Mongo груп: {mongoAgg.Count}");
}

static async Task PrintExplainsAsync(IMediaCatalogRepository sql, IMediaCatalogRepository mongo)
{
    Console.WriteLine("\n[1. Читання за ID — PostgreSQL]");
    Console.WriteLine(await sql.ExplainPointReadAsync("item-0000001"));
    Console.WriteLine("[1. Читання за ID — MongoDB]");
    Console.WriteLine(await mongo.ExplainPointReadAsync("item-0000001"));

    Console.WriteLine("\n[2. Фільтрація за атрибутами — PostgreSQL]");
    Console.WriteLine(await sql.ExplainBoardGameSearchAsync(4, 90, 20));
    Console.WriteLine("[2. Фільтрація за атрибутами — MongoDB]");
    Console.WriteLine(await mongo.ExplainBoardGameSearchAsync(4, 90, 20));

    Console.WriteLine("\n[3. Пошук за автором і роллю — PostgreSQL]");
    Console.WriteLine(await sql.ExplainContributorSearchAsync(1, "author", 20));
    Console.WriteLine("[3. Пошук за автором і роллю — MongoDB]");
    Console.WriteLine(await mongo.ExplainContributorSearchAsync(1, "author", 20));
}

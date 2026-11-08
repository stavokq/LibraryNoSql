using System.Collections.Concurrent;
using System.Diagnostics;
using LibraryNoSql.App.Data;
using LibraryNoSql.App.Models;
using LibraryNoSql.App.Repositories;

namespace LibraryNoSql.App.Services;

public record LatencyMetrics(
    string Scenario,
    string Engine,
    int Operations,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double OpsPerSec,
    string Note = "");

public class BenchmarkRunner
{
    private readonly IMediaCatalogRepository _sql;
    private readonly IMediaCatalogRepository _mongo;

    public BenchmarkRunner(IMediaCatalogRepository sql, IMediaCatalogRepository mongo)
    {
        _sql = sql;
        _mongo = mongo;
    }

    public async Task SeedBothAsync(GeneratedDataset dataset)
    {
        var swSql = Stopwatch.StartNew();
        await _sql.ResetAndSeedAsync(dataset);
        swSql.Stop();

        var swMongo = Stopwatch.StartNew();
        await _mongo.ResetAndSeedAsync(dataset);
        swMongo.Stop();

        var sqlStorage = await _sql.GetStorageStatsAsync();
        var mongoStorage = await _mongo.GetStorageStatsAsync();

        Console.WriteLine($"PostgreSQL: {swSql.Elapsed.TotalSeconds:F2} с | рядків: {sqlStorage.Count:N0} | дані: {sqlStorage.DataMb:F1} МБ | індекси: {sqlStorage.IndexMb:F1} МБ | всього: {sqlStorage.TotalMb:F1} МБ");
        Console.WriteLine($"MongoDB   : {swMongo.Elapsed.TotalSeconds:F2} с | док-ів: {mongoStorage.Count:N0} | дані: {mongoStorage.DataMb:F1} МБ | індекси: {mongoStorage.IndexMb:F1} МБ | всього: {mongoStorage.TotalMb:F1} МБ");
    }

    public async Task<List<LatencyMetrics>> RunSuiteAsync(GeneratedDataset dataset)
    {
        var results = new List<LatencyMetrics>();
        var zipfIds = CatalogDataGenerator.SampleZipfIds(dataset.Items, 500, seed: 777);
        var warmupIds = zipfIds.Take(50).ToList();

        foreach (var id in warmupIds)
        {
            await _sql.GetByIdAsync(id);
            await _mongo.GetByIdAsync(id);
        }

        results.Add(await MeasureSequentialAsync(
            "1. Читання за ID",
            _sql.EngineName,
            zipfIds.Count,
            i => _sql.GetByIdAsync(zipfIds[i])));

        results.Add(await MeasureSequentialAsync(
            "1. Читання за ID",
            _mongo.EngineName,
            zipfIds.Count,
            i => _mongo.GetByIdAsync(zipfIds[i])));

        const int searchOps = 200;
        for (int w = 0; w < 20; w++)
        {
            await _sql.SearchBoardGamesForPlayersAsync(4, 90, 20);
            await _mongo.SearchBoardGamesForPlayersAsync(4, 90, 20);
        }

        results.Add(await MeasureSequentialAsync(
            "2. Фільтрація за атрибутами",
            _sql.EngineName,
            searchOps,
            i => _sql.SearchBoardGamesForPlayersAsync(2 + (i % 5), 60 + (i % 4) * 20, 20)));

        results.Add(await MeasureSequentialAsync(
            "2. Фільтрація за атрибутами",
            _mongo.EngineName,
            searchOps,
            i => _mongo.SearchBoardGamesForPlayersAsync(2 + (i % 5), 60 + (i % 4) * 20, 20)));

        string[] roles = ["author", "narrator", "translator"];
        for (int w = 0; w < 20; w++)
        {
            await _sql.SearchByContributorRoleAsync(1 + w, "author", 20);
            await _mongo.SearchByContributorRoleAsync(1 + w, "author", 20);
        }

        results.Add(await MeasureSequentialAsync(
            "3. Пошук за автором і роллю",
            _sql.EngineName,
            searchOps,
            i => _sql.SearchByContributorRoleAsync(1 + (i % 80), roles[i % roles.Length], 20)));

        results.Add(await MeasureSequentialAsync(
            "3. Пошук за автором і роллю",
            _mongo.EngineName,
            searchOps,
            i => _mongo.SearchByContributorRoleAsync(1 + (i % 80), roles[i % roles.Length], 20)));

        string runTag = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()[^6..];
        var extraSql = CatalogDataGenerator.GenerateExtraItems(dataset, $"s{runTag}", 400, seed: 1001);
        var extraMongo = CatalogDataGenerator.GenerateExtraItems(dataset, $"m{runTag}", 400, seed: 1001);

        results.Add(await MeasureConcurrentAsync(
            "4. Вставка (16 потоків)",
            _sql.EngineName,
            extraSql.Count,
            16,
            i => _sql.InsertCardAsync(extraSql[i])));

        results.Add(await MeasureConcurrentAsync(
            "4. Вставка (16 потоків)",
            _mongo.EngineName,
            extraMongo.Count,
            16,
            i => _mongo.InsertCardAsync(extraMongo[i])));

        results.Add(await MeasureConcurrentAsync(
            "5. Резервування (16 потоків)",
            _sql.EngineName,
            400,
            16,
            i => _sql.TryReserveCopyAsync(zipfIds[i % zipfIds.Count])));

        results.Add(await MeasureConcurrentAsync(
            "5. Резервування (16 потоків)",
            _mongo.EngineName,
            400,
            16,
            i => _mongo.TryReserveCopyAsync(zipfIds[i % zipfIds.Count])));

        long sqlUpdatedTotal = 0;
        long mongoUpdatedTotal = 0;
        const int renameOps = 20;

        var sqlRename = await MeasureSequentialAsync(
            "6. Оновлення видавництва",
            _sql.EngineName,
            renameOps,
            async i =>
            {
                int pubId = (i % 20) + 1;
                sqlUpdatedTotal += await _sql.RenamePublisherAsync(pubId, $"Видавництво #{pubId:D3} Оновлено {runTag}", "Україна");
            });
        results.Add(sqlRename with { Note = $"рядків: {sqlUpdatedTotal}" });

        var mongoRename = await MeasureSequentialAsync(
            "6. Оновлення видавництва",
            _mongo.EngineName,
            renameOps,
            async i =>
            {
                int pubId = (i % 20) + 1;
                mongoUpdatedTotal += await _mongo.RenamePublisherAsync(pubId, $"Видавництво #{pubId:D3} Оновлено {runTag}", "Україна");
            });
        results.Add(mongoRename with { Note = $"док-ів: {mongoUpdatedTotal}" });

        const int aggOps = 25;
        await _sql.GetGenreTypeStatsAsync();
        await _mongo.GetGenreTypeStatsAsync();

        results.Add(await MeasureSequentialAsync(
            "7. Агрегація за жанрами",
            _sql.EngineName,
            aggOps,
            _ => _sql.GetGenreTypeStatsAsync()));

        results.Add(await MeasureSequentialAsync(
            "7. Агрегація за жанрами",
            _mongo.EngineName,
            aggOps,
            _ => _mongo.GetGenreTypeStatsAsync()));

        return results;
    }

    public static void PrintResults(IReadOnlyList<LatencyMetrics> metrics)
    {
        Console.WriteLine();
        Console.WriteLine($"{"Сценарій",-30} | {"СУБД",-10} | {"Ops",5} | {"Mean мс",8} | {"p50 мс",8} | {"p95 мс",8} | {"p99 мс",8} | {"Ops/s",9} | Примітка");
        Console.WriteLine(new string('-', 112));

        foreach (var m in metrics)
        {
            Console.WriteLine(
                $"{m.Scenario,-30} | {m.Engine,-10} | {m.Operations,5} | {m.MeanMs,8:F3} | {m.P50Ms,8:F3} | {m.P95Ms,8:F3} | {m.P99Ms,8:F3} | {m.OpsPerSec,9:F1} | {m.Note}");
        }
    }

    private static async Task<LatencyMetrics> MeasureSequentialAsync(
        string scenario,
        string engine,
        int operations,
        Func<int, Task> action)
    {
        var samples = new double[operations];
        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < operations; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            await action(i);
            samples[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }

        totalSw.Stop();
        return BuildMetrics(scenario, engine, operations, samples, totalSw.Elapsed.TotalSeconds);
    }

    private static async Task<LatencyMetrics> MeasureConcurrentAsync(
        string scenario,
        string engine,
        int operations,
        int concurrency,
        Func<int, Task> action)
    {
        var bag = new ConcurrentBag<double>();
        var indices = Enumerable.Range(0, operations);
        var options = new ParallelOptions { MaxDegreeOfParallelism = concurrency };

        var totalSw = Stopwatch.StartNew();
        await Parallel.ForEachAsync(indices, options, async (i, _) =>
        {
            long t0 = Stopwatch.GetTimestamp();
            await action(i);
            bag.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        });
        totalSw.Stop();

        return BuildMetrics(scenario, engine, operations, bag.ToArray(), totalSw.Elapsed.TotalSeconds);
    }

    private static LatencyMetrics BuildMetrics(
        string scenario,
        string engine,
        int operations,
        double[] samples,
        double wallSeconds)
    {
        Array.Sort(samples);
        double mean = samples.Average();
        double p50 = Percentile(samples, 0.50);
        double p95 = Percentile(samples, 0.95);
        double p99 = Percentile(samples, 0.99);
        double rps = operations / Math.Max(wallSeconds, 1e-6);

        return new LatencyMetrics(scenario, engine, operations, mean, p50, p95, p99, rps);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        double pos = (sorted.Length - 1) * p;
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi)
        {
            return sorted[lo];
        }

        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }
}

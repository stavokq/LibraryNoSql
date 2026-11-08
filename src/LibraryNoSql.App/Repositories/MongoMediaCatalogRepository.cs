using System.Text;
using LibraryNoSql.App.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LibraryNoSql.App.Repositories;

public class MongoMediaCatalogRepository : IMediaCatalogRepository
{
    private const string CollectionName = "catalog_items";
    private readonly MongoClient _client;
    private readonly IMongoDatabase _database;
    private IMongoCollection<BsonDocument> _collection;

    public string EngineName => "MongoDB";

    public MongoMediaCatalogRepository(string connectionString, string databaseName)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.MaxConnectionPoolSize = 100;
        settings.MinConnectionPoolSize = 10;
        _client = new MongoClient(settings);
        _database = _client.GetDatabase(databaseName);
        _collection = _database.GetCollection<BsonDocument>(CollectionName);
    }

    public async Task<int> GetBaseItemCountAsync()
    {
        var filter = Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression("^item-"));
        return (int)await _collection.CountDocumentsAsync(filter);
    }

    public async Task ResetAndSeedAsync(GeneratedDataset dataset)
    {
        await _collection.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);

        const int batchSize = 5000;
        var batch = new List<BsonDocument>(batchSize);
        var options = new InsertManyOptions { IsOrdered = false };

        foreach (var item in dataset.Items)
        {
            batch.Add(ToBson(item));
            if (batch.Count == batchSize)
            {
                await _collection.InsertManyAsync(batch, options);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _collection.InsertManyAsync(batch, options);
        }
    }

    public async Task<CatalogCard?> GetByIdAsync(string id)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", id);
        var doc = await _collection.Find(filter).FirstOrDefaultAsync();
        return doc is null ? null : FromBson(doc);
    }

    public async Task<IReadOnlyList<CatalogSummaryItem>> SearchBoardGamesForPlayersAsync(
        int playersCount,
        int maxPlayTimeMin,
        int limit)
    {
        var fb = Builders<BsonDocument>.Filter;
        var filter = fb.And(
            fb.Eq("isActive", true),
            fb.Eq("itemType", "board_game"),
            fb.Lte("specs.minPlayers", playersCount),
            fb.Gte("specs.maxPlayers", playersCount),
            fb.Lte("specs.playTimeMin", maxPlayTimeMin));

        var projection = Builders<BsonDocument>.Projection
            .Include("_id")
            .Include("code")
            .Include("itemType")
            .Include("title")
            .Include("publicationYear")
            .Include("replacementCostMinor")
            .Include("popularityScore");

        var sort = Builders<BsonDocument>.Sort
            .Descending("popularityScore")
            .Ascending("_id");

        var docs = await _collection.Find(filter)
            .Project(projection)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync();

        return docs.Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<CatalogSummaryItem>> SearchByContributorRoleAsync(
        int contributorId,
        string role,
        int limit)
    {
        var elemFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("id", contributorId),
            Builders<BsonDocument>.Filter.Eq("role", role));

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("isActive", true),
            Builders<BsonDocument>.Filter.ElemMatch("contributors", elemFilter));

        var projection = Builders<BsonDocument>.Projection
            .Include("_id")
            .Include("code")
            .Include("itemType")
            .Include("title")
            .Include("publicationYear")
            .Include("replacementCostMinor")
            .Include("popularityScore");

        var sort = Builders<BsonDocument>.Sort
            .Descending("publicationYear")
            .Ascending("_id");

        var docs = await _collection.Find(filter)
            .Project(projection)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync();

        return docs.Select(ToSummary).ToList();
    }

    public Task InsertCardAsync(CatalogCard card) =>
        _collection.InsertOneAsync(ToBson(card));

    public async Task<bool> TryReserveCopyAsync(string id)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id),
            Builders<BsonDocument>.Filter.Gte("availableCopies", 1));

        var update = Builders<BsonDocument>.Update.Inc("availableCopies", -1);
        var res = await _collection.UpdateOneAsync(filter, update);
        return res.ModifiedCount == 1;
    }

    public async Task<long> RenamePublisherAsync(int publisherId, string newName, string newCountry)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("publisher.id", publisherId);
        var update = Builders<BsonDocument>.Update
            .Set("publisher.name", newName)
            .Set("publisher.country", newCountry);

        var res = await _collection.UpdateManyAsync(filter, update);
        return res.ModifiedCount;
    }

    public async Task<IReadOnlyList<GenreTypeStats>> GetGenreTypeStatsAsync()
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("isActive", true)),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "genre", "$genre.code" }, { "type", "$itemType" } } },
                { "itemsCount", new BsonDocument("$sum", 1) },
                { "totalCopies", new BsonDocument("$sum", "$availableCopies") },
                { "avgCost", new BsonDocument("$avg", "$replacementCostMinor") }
            }),
            new BsonDocument("$sort", new BsonDocument { { "_id.genre", 1 }, { "_id.type", 1 } })
        };

        var docs = await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
        return docs.Select(d => new GenreTypeStats
        {
            GenreCode = d["_id"]["genre"].AsString,
            ItemType = d["_id"]["type"].AsString,
            ItemsCount = d["itemsCount"].ToInt64(),
            TotalAvailableCopies = d["totalCopies"].ToInt64(),
            AvgCostMinor = d["avgCost"].IsBsonNull ? 0.0 : d["avgCost"].ToDouble()
        }).ToList();
    }

    public async Task<StorageStats> GetStorageStatsAsync()
    {
        var stats = await _database.RunCommandAsync<BsonDocument>(new BsonDocument("collStats", CollectionName));
        double dataMb = stats.GetValue("storageSize", 0).ToDouble() / (1024.0 * 1024.0);
        double indexMb = stats.GetValue("totalIndexSize", 0).ToDouble() / (1024.0 * 1024.0);
        long count = stats.GetValue("count", 0).ToInt64();
        return new StorageStats(dataMb, indexMb, dataMb + indexMb, count);
    }

    public Task<string> ExplainPointReadAsync(string id)
    {
        var cmd = new BsonDocument
        {
            {
                "explain", new BsonDocument
                {
                    { "find", CollectionName },
                    { "filter", new BsonDocument("_id", id) },
                    { "limit", 1 }
                }
            },
            { "verbosity", "executionStats" }
        };
        return RunExplainSummaryAsync(cmd);
    }

    public Task<string> ExplainBoardGameSearchAsync(int playersCount, int maxPlayTimeMin, int limit)
    {
        var cmd = new BsonDocument
        {
            {
                "explain", new BsonDocument
                {
                    { "find", CollectionName },
                    {
                        "filter", new BsonDocument
                        {
                            { "isActive", true },
                            { "itemType", "board_game" },
                            { "specs.minPlayers", new BsonDocument("$lte", playersCount) },
                            { "specs.maxPlayers", new BsonDocument("$gte", playersCount) },
                            { "specs.playTimeMin", new BsonDocument("$lte", maxPlayTimeMin) }
                        }
                    },
                    { "sort", new BsonDocument { { "popularityScore", -1 }, { "_id", 1 } } },
                    { "limit", limit }
                }
            },
            { "verbosity", "executionStats" }
        };
        return RunExplainSummaryAsync(cmd);
    }

    public Task<string> ExplainContributorSearchAsync(int contributorId, string role, int limit)
    {
        var cmd = new BsonDocument
        {
            {
                "explain", new BsonDocument
                {
                    { "find", CollectionName },
                    {
                        "filter", new BsonDocument
                        {
                            { "isActive", true },
                            {
                                "contributors", new BsonDocument("$elemMatch", new BsonDocument
                                {
                                    { "id", contributorId },
                                    { "role", role }
                                })
                            }
                        }
                    },
                    { "sort", new BsonDocument { { "publicationYear", -1 }, { "_id", 1 } } },
                    { "limit", limit }
                }
            },
            { "verbosity", "executionStats" }
        };
        return RunExplainSummaryAsync(cmd);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string> RunExplainSummaryAsync(BsonDocument explainCmd)
    {
        var res = await _database.RunCommandAsync<BsonDocument>(explainCmd);
        var exec = res.GetValue("executionStats", new BsonDocument()).AsBsonDocument;
        var winPlan = res.GetValue("queryPlanner", new BsonDocument()).AsBsonDocument
            .GetValue("winningPlan", new BsonDocument());

        var sb = new StringBuilder();
        sb.AppendLine($"executionTimeMillis : {exec.GetValue("executionTimeMillis", 0)} ms");
        sb.AppendLine($"nReturned           : {exec.GetValue("nReturned", 0)}");
        sb.AppendLine($"totalKeysExamined   : {exec.GetValue("totalKeysExamined", 0)}");
        sb.AppendLine($"totalDocsExamined   : {exec.GetValue("totalDocsExamined", 0)}");
        sb.AppendLine($"winningPlan         : {winPlan.ToJson()}");
        return sb.ToString();
    }

    private static BsonDocument ToBson(CatalogCard card)
    {
        var specs = new BsonDocument();
        foreach (var attr in card.Attributes)
        {
            string fieldName = ToCamelCase(attr.Key);
            if (attr.ValInt.HasValue)
            {
                specs[fieldName] = attr.ValInt.Value;
            }
            else if (attr.ValBool.HasValue)
            {
                specs[fieldName] = attr.ValBool.Value;
            }
            else if (attr.ValString is not null)
            {
                specs[fieldName] = attr.ValString;
            }
        }

        return new BsonDocument
        {
            { "_id", card.Id },
            { "schemaVersion", card.SchemaVersion },
            { "code", card.Code },
            { "itemType", card.ItemType },
            { "title", card.Title },
            { "publicationYear", card.PublicationYear },
            { "languageCode", card.LanguageCode },
            {
                "publisher", new BsonDocument
                {
                    { "id", card.Publisher.Id },
                    { "name", card.Publisher.Name },
                    { "country", card.Publisher.Country },
                    { "website", card.Publisher.Website is null ? BsonNull.Value : card.Publisher.Website }
                }
            },
            {
                "genre", new BsonDocument
                {
                    { "id", card.Genre.Id },
                    { "code", card.Genre.Code },
                    { "name", card.Genre.Name }
                }
            },
            { "replacementCostMinor", card.ReplacementCostMinor },
            { "availableCopies", card.AvailableCopies },
            { "popularityScore", card.PopularityScore },
            { "isActive", card.IsActive },
            { "createdAt", new BsonDateTime(card.CreatedAt) },
            {
                "contributors", new BsonArray(card.Contributors.Select(c => new BsonDocument
                {
                    { "id", c.Id },
                    { "fullName", c.FullName },
                    { "country", c.Country },
                    { "role", c.Role },
                    { "displayOrder", c.DisplayOrder }
                }))
            },
            {
                "contents", new BsonArray(card.Contents.Select(cnt => new BsonDocument
                {
                    { "position", cnt.Position },
                    { "title", cnt.Title },
                    { "durationSec", cnt.DurationSec.HasValue ? cnt.DurationSec.Value : BsonNull.Value },
                    { "pageOrQty", cnt.PageOrQty.HasValue ? cnt.PageOrQty.Value : BsonNull.Value }
                }))
            },
            { "tags", new BsonArray(card.Tags) },
            { "specs", specs }
        };
    }

    private static CatalogCard FromBson(BsonDocument doc)
    {
        var pub = doc["publisher"].AsBsonDocument;
        var gen = doc["genre"].AsBsonDocument;
        var specs = doc["specs"].AsBsonDocument;

        var attrs = new List<ItemAttributeValue>(specs.ElementCount);
        foreach (var el in specs.Elements)
        {
            string snakeKey = ToSnakeCase(el.Name);
            attrs.Add(el.Value.BsonType switch
            {
                BsonType.Int32 => new ItemAttributeValue(snakeKey, null, el.Value.AsInt32, null),
                BsonType.Boolean => new ItemAttributeValue(snakeKey, null, null, el.Value.AsBoolean),
                BsonType.String => new ItemAttributeValue(snakeKey, el.Value.AsString, null, null),
                _ => new ItemAttributeValue(snakeKey, el.Value.ToString(), null, null)
            });
        }

        return new CatalogCard
        {
            Id = doc["_id"].AsString,
            SchemaVersion = doc["schemaVersion"].AsInt32,
            Code = doc["code"].AsString,
            ItemType = doc["itemType"].AsString,
            Title = doc["title"].AsString,
            PublicationYear = doc["publicationYear"].AsInt32,
            LanguageCode = doc["languageCode"].AsString,
            Publisher = new PublisherInfo(
                pub["id"].AsInt32,
                pub["name"].AsString,
                pub["country"].AsString,
                pub["website"].IsBsonNull ? null : pub["website"].AsString),
            Genre = new GenreInfo(
                gen["id"].AsInt32,
                gen["code"].AsString,
                gen["name"].AsString),
            ReplacementCostMinor = doc["replacementCostMinor"].AsInt32,
            AvailableCopies = doc["availableCopies"].AsInt32,
            PopularityScore = doc["popularityScore"].AsInt32,
            IsActive = doc["isActive"].AsBoolean,
            CreatedAt = doc["createdAt"].ToUniversalTime(),
            Contributors = doc["contributors"].AsBsonArray
                .Select(x => x.AsBsonDocument)
                .Select(c => new ContributorEntry(
                    c["id"].AsInt32,
                    c["fullName"].AsString,
                    c["country"].AsString,
                    c["role"].AsString,
                    c["displayOrder"].AsInt32))
                .ToList(),
            Contents = doc["contents"].AsBsonArray
                .Select(x => x.AsBsonDocument)
                .Select(cnt => new ContentEntry(
                    cnt["position"].AsInt32,
                    cnt["title"].AsString,
                    cnt["durationSec"].IsBsonNull ? null : cnt["durationSec"].AsInt32,
                    cnt["pageOrQty"].IsBsonNull ? null : cnt["pageOrQty"].AsInt32))
                .ToList(),
            Tags = doc["tags"].AsBsonArray.Select(t => t.AsString).ToList(),
            Attributes = attrs
        };
    }

    private static CatalogSummaryItem ToSummary(BsonDocument d) => new()
    {
        Id = d["_id"].AsString,
        Code = d["code"].AsString,
        ItemType = d["itemType"].AsString,
        Title = d["title"].AsString,
        PublicationYear = d["publicationYear"].AsInt32,
        ReplacementCostMinor = d["replacementCostMinor"].AsInt32,
        PopularityScore = d["popularityScore"].AsInt32
    };

    private static string ToCamelCase(string snake)
    {
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return snake;
        }

        var sb = new StringBuilder(parts[0]);
        for (int i = 1; i < parts.Length; i++)
        {
            sb.Append(char.ToUpperInvariant(parts[i][0]));
            if (parts[i].Length > 1)
            {
                sb.Append(parts[i][1..]);
            }
        }
        return sb.ToString();
    }

    private static string ToSnakeCase(string camel)
    {
        var sb = new StringBuilder(camel.Length + 4);
        foreach (char ch in camel)
        {
            if (char.IsUpper(ch))
            {
                sb.Append('_');
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}

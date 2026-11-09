using System.Text;
using Dapper;
using LibraryNoSql.App.Models;
using Npgsql;
using NpgsqlTypes;

namespace LibraryNoSql.App.Repositories;

public class SqlMediaCatalogRepository : IMediaCatalogRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public string EngineName => "PostgreSQL";

    public SqlMediaCatalogRepository(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = 100,
            MinPoolSize = 10,
            CommandTimeout = 300
        };
        _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
    }

    public async Task<int> GetBaseItemCountAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM catalog_items WHERE id LIKE 'item-%';");
    }

    public async Task ResetAndSeedAsync(GeneratedDataset dataset)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        await conn.ExecuteAsync("""
            TRUNCATE TABLE
                catalog_item_attributes,
                catalog_item_tags,
                catalog_item_contents,
                catalog_item_contributors,
                catalog_items,
                tags,
                contributors,
                genres,
                publishers
            RESTART IDENTITY CASCADE;
            """);

        await conn.ExecuteAsync("SET session_replication_role = 'replica';");
        try
        {

        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY publishers (id, name, country, website) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var p in dataset.Publishers)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(p.Id, NpgsqlDbType.Integer);
                await writer.WriteAsync(p.Name, NpgsqlDbType.Varchar);
                await writer.WriteAsync(p.Country, NpgsqlDbType.Varchar);
                await WriteNullableStringAsync(writer, p.Website);
            }
            await writer.CompleteAsync();
        }

        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY genres (id, code, name) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var g in dataset.Genres)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(g.Id, NpgsqlDbType.Integer);
                await writer.WriteAsync(g.Code, NpgsqlDbType.Varchar);
                await writer.WriteAsync(g.Name, NpgsqlDbType.Varchar);
            }
            await writer.CompleteAsync();
        }

        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY contributors (id, full_name, country) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var c in dataset.Contributors)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(c.Id, NpgsqlDbType.Integer);
                await writer.WriteAsync(c.FullName, NpgsqlDbType.Varchar);
                await writer.WriteAsync(c.Country, NpgsqlDbType.Varchar);
            }
            await writer.CompleteAsync();
        }

        var tagMap = dataset.Tags.ToDictionary(t => t.Name, t => t.Id);
        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY tags (id, name) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var t in dataset.Tags)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(t.Id, NpgsqlDbType.Integer);
                await writer.WriteAsync(t.Name, NpgsqlDbType.Varchar);
            }
            await writer.CompleteAsync();
        }

        await using (var writer = await conn.BeginBinaryImportAsync("""
            COPY catalog_items (
                id, code, item_type, title, publication_year, language_code,
                publisher_id, genre_id, replacement_cost_minor, available_copies,
                popularity_score, is_active, created_at
            ) FROM STDIN (FORMAT BINARY)
            """))
        {
            foreach (var item in dataset.Items)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(item.Id, NpgsqlDbType.Varchar);
                await writer.WriteAsync(item.Code, NpgsqlDbType.Varchar);
                await writer.WriteAsync(item.ItemType, NpgsqlDbType.Varchar);
                await writer.WriteAsync(item.Title, NpgsqlDbType.Varchar);
                await writer.WriteAsync(item.PublicationYear, NpgsqlDbType.Integer);
                await writer.WriteAsync(item.LanguageCode, NpgsqlDbType.Varchar);
                await writer.WriteAsync(item.Publisher.Id, NpgsqlDbType.Integer);
                await writer.WriteAsync(item.Genre.Id, NpgsqlDbType.Integer);
                await writer.WriteAsync(item.ReplacementCostMinor, NpgsqlDbType.Integer);
                await writer.WriteAsync(item.AvailableCopies, NpgsqlDbType.Integer);
                await writer.WriteAsync(item.PopularityScore, NpgsqlDbType.Integer);
                await writer.WriteAsync(item.IsActive, NpgsqlDbType.Boolean);
                await writer.WriteAsync(item.CreatedAt, NpgsqlDbType.TimestampTz);
            }
            await writer.CompleteAsync();
        }

        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY catalog_item_contributors (item_id, contributor_id, role, display_order) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var item in dataset.Items)
            {
                foreach (var c in item.Contributors)
                {
                    await writer.StartRowAsync();
                    await writer.WriteAsync(item.Id, NpgsqlDbType.Varchar);
                    await writer.WriteAsync(c.Id, NpgsqlDbType.Integer);
                    await writer.WriteAsync(c.Role, NpgsqlDbType.Varchar);
                    await writer.WriteAsync((short)c.DisplayOrder, NpgsqlDbType.Smallint);
                }
            }
            await writer.CompleteAsync();
        }

        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY catalog_item_contents (item_id, position, title, duration_sec, page_or_qty) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var item in dataset.Items)
            {
                foreach (var cnt in item.Contents)
                {
                    await writer.StartRowAsync();
                    await writer.WriteAsync(item.Id, NpgsqlDbType.Varchar);
                    await writer.WriteAsync((short)cnt.Position, NpgsqlDbType.Smallint);
                    await writer.WriteAsync(cnt.Title, NpgsqlDbType.Varchar);
                    await WriteNullableIntAsync(writer, cnt.DurationSec);
                    await WriteNullableIntAsync(writer, cnt.PageOrQty);
                }
            }
            await writer.CompleteAsync();
        }

        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY catalog_item_tags (item_id, tag_id) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var item in dataset.Items)
            {
                foreach (var tagName in item.Tags)
                {
                    await writer.StartRowAsync();
                    await writer.WriteAsync(item.Id, NpgsqlDbType.Varchar);
                    await writer.WriteAsync(tagMap[tagName], NpgsqlDbType.Integer);
                }
            }
            await writer.CompleteAsync();
        }

        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY catalog_item_attributes (item_id, attr_key, val_string, val_int, val_bool) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var item in dataset.Items)
            {
                foreach (var attr in item.Attributes)
                {
                    await writer.StartRowAsync();
                    await writer.WriteAsync(item.Id, NpgsqlDbType.Varchar);
                    await writer.WriteAsync(attr.Key, NpgsqlDbType.Varchar);
                    await WriteNullableStringAsync(writer, attr.ValString);
                    await WriteNullableIntAsync(writer, attr.ValInt);
                    await WriteNullableBoolAsync(writer, attr.ValBool);
                }
            }
            await writer.CompleteAsync();
        }
        }
        finally
        {
            await conn.ExecuteAsync("SET session_replication_role = 'origin';");
        }

        await conn.ExecuteAsync("ANALYZE;");
    }

    public async Task<CatalogCard?> GetByIdAsync(string id)
    {
        const string sql = """
            SELECT
                ci.id AS Id,
                ci.code AS Code,
                ci.item_type AS ItemType,
                ci.title AS Title,
                ci.publication_year AS PublicationYear,
                ci.language_code AS LanguageCode,
                ci.replacement_cost_minor AS ReplacementCostMinor,
                ci.available_copies AS AvailableCopies,
                ci.popularity_score AS PopularityScore,
                ci.is_active AS IsActive,
                ci.created_at AS CreatedAt,
                p.id AS PubId,
                p.name AS PubName,
                p.country AS PubCountry,
                p.website AS PubWebsite,
                g.id AS GenreId,
                g.code AS GenreCode,
                g.name AS GenreName
            FROM catalog_items ci
            JOIN publishers p ON p.id = ci.publisher_id
            JOIN genres g ON g.id = ci.genre_id
            WHERE ci.id = @Id;

            SELECT
                c.id AS Id,
                c.full_name AS FullName,
                c.country AS Country,
                cic.role AS Role,
                cic.display_order AS DisplayOrder
            FROM catalog_item_contributors cic
            JOIN contributors c ON c.id = cic.contributor_id
            WHERE cic.item_id = @Id
            ORDER BY cic.display_order;

            SELECT
                position AS Position,
                title AS Title,
                duration_sec AS DurationSec,
                page_or_qty AS PageOrQty
            FROM catalog_item_contents
            WHERE item_id = @Id
            ORDER BY position;

            SELECT t.name
            FROM catalog_item_tags cit
            JOIN tags t ON t.id = cit.tag_id
            WHERE cit.item_id = @Id;

            SELECT
                attr_key AS Key,
                val_string AS ValString,
                val_int AS ValInt,
                val_bool AS ValBool
            FROM catalog_item_attributes
            WHERE item_id = @Id;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var multi = await conn.QueryMultipleAsync(sql, new { Id = id });

        var head = await multi.ReadSingleOrDefaultAsync<ItemHeaderRow>();
        if (head is null)
        {
            return null;
        }

        var contribs = (await multi.ReadAsync<ContributorRow>())
            .Select(r => new ContributorEntry(r.Id, r.FullName, r.Country, r.Role, r.DisplayOrder))
            .ToList();

        var contents = (await multi.ReadAsync<ContentRow>())
            .Select(r => new ContentEntry(r.Position, r.Title, r.DurationSec, r.PageOrQty))
            .ToList();

        var tags = (await multi.ReadAsync<string>()).ToList();

        var attrs = (await multi.ReadAsync<AttributeRow>())
            .Select(r => new ItemAttributeValue(r.Key, r.ValString, r.ValInt, r.ValBool))
            .ToList();

        return new CatalogCard
        {
            Id = head.Id,
            SchemaVersion = 1,
            Code = head.Code,
            ItemType = head.ItemType,
            Title = head.Title,
            PublicationYear = head.PublicationYear,
            LanguageCode = head.LanguageCode,
            Publisher = new PublisherInfo(head.PubId, head.PubName, head.PubCountry, head.PubWebsite),
            Genre = new GenreInfo(head.GenreId, head.GenreCode, head.GenreName),
            ReplacementCostMinor = head.ReplacementCostMinor,
            AvailableCopies = head.AvailableCopies,
            PopularityScore = head.PopularityScore,
            IsActive = head.IsActive,
            CreatedAt = head.CreatedAt,
            Contributors = contribs,
            Contents = contents,
            Tags = tags,
            Attributes = attrs
        };
    }

    public async Task<IReadOnlyList<CatalogSummaryItem>> SearchBoardGamesForPlayersAsync(
        int playersCount,
        int maxPlayTimeMin,
        int limit)
    {
        const string sql = """
            SELECT
                ci.id AS Id,
                ci.code AS Code,
                ci.item_type AS ItemType,
                ci.title AS Title,
                ci.publication_year AS PublicationYear,
                ci.replacement_cost_minor AS ReplacementCostMinor,
                ci.popularity_score AS PopularityScore
            FROM catalog_items ci
            JOIN catalog_item_attributes a_min
                ON a_min.item_id = ci.id AND a_min.attr_key = 'min_players'
            JOIN catalog_item_attributes a_max
                ON a_max.item_id = ci.id AND a_max.attr_key = 'max_players'
            JOIN catalog_item_attributes a_time
                ON a_time.item_id = ci.id AND a_time.attr_key = 'play_time_min'
            WHERE ci.is_active = TRUE
              AND ci.item_type = 'board_game'
              AND a_min.val_int <= @Players
              AND a_max.val_int >= @Players
              AND a_time.val_int <= @MaxTime
            ORDER BY ci.popularity_score DESC, ci.id ASC
            LIMIT @Limit;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<CatalogSummaryItem>(sql, new
        {
            Players = playersCount,
            MaxTime = maxPlayTimeMin,
            Limit = limit
        });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CatalogSummaryItem>> SearchByContributorRoleAsync(
        int contributorId,
        string role,
        int limit)
    {
        const string sql = """
            SELECT
                ci.id AS Id,
                ci.code AS Code,
                ci.item_type AS ItemType,
                ci.title AS Title,
                ci.publication_year AS PublicationYear,
                ci.replacement_cost_minor AS ReplacementCostMinor,
                ci.popularity_score AS PopularityScore
            FROM catalog_item_contributors cic
            JOIN catalog_items ci ON ci.id = cic.item_id
            WHERE cic.contributor_id = @ContributorId
              AND cic.role = @Role
              AND ci.is_active = TRUE
            ORDER BY ci.publication_year DESC, ci.id ASC
            LIMIT @Limit;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<CatalogSummaryItem>(sql, new
        {
            ContributorId = contributorId,
            Role = role,
            Limit = limit
        });
        return rows.AsList();
    }

    public async Task InsertCardAsync(CatalogCard card)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var batch = new NpgsqlBatch(conn, tx);

        var cmdItem = new NpgsqlBatchCommand("""
            INSERT INTO catalog_items (
                id, code, item_type, title, publication_year, language_code,
                publisher_id, genre_id, replacement_cost_minor, available_copies,
                popularity_score, is_active, created_at
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13
            );
            """);
        cmdItem.Parameters.AddWithValue(card.Id);
        cmdItem.Parameters.AddWithValue(card.Code);
        cmdItem.Parameters.AddWithValue(card.ItemType);
        cmdItem.Parameters.AddWithValue(card.Title);
        cmdItem.Parameters.AddWithValue(card.PublicationYear);
        cmdItem.Parameters.AddWithValue(card.LanguageCode);
        cmdItem.Parameters.AddWithValue(card.Publisher.Id);
        cmdItem.Parameters.AddWithValue(card.Genre.Id);
        cmdItem.Parameters.AddWithValue(card.ReplacementCostMinor);
        cmdItem.Parameters.AddWithValue(card.AvailableCopies);
        cmdItem.Parameters.AddWithValue(card.PopularityScore);
        cmdItem.Parameters.AddWithValue(card.IsActive);
        cmdItem.Parameters.AddWithValue(card.CreatedAt);
        batch.BatchCommands.Add(cmdItem);

        var cmdContrib = new NpgsqlBatchCommand("""
            INSERT INTO catalog_item_contributors (item_id, contributor_id, role, display_order)
            SELECT $1, u.cid, u.r, u.ord
            FROM UNNEST($2::int[], $3::varchar[], $4::smallint[]) AS u(cid, r, ord);
            """);
        cmdContrib.Parameters.AddWithValue(card.Id);
        cmdContrib.Parameters.AddWithValue(card.Contributors.Select(x => x.Id).ToArray());
        cmdContrib.Parameters.AddWithValue(card.Contributors.Select(x => x.Role).ToArray());
        cmdContrib.Parameters.AddWithValue(card.Contributors.Select(x => (short)x.DisplayOrder).ToArray());
        batch.BatchCommands.Add(cmdContrib);

        var cmdContents = new NpgsqlBatchCommand("""
            INSERT INTO catalog_item_contents (item_id, position, title, duration_sec, page_or_qty)
            SELECT $1, u.pos, u.ttl, u.dur, u.pq
            FROM UNNEST($2::smallint[], $3::varchar[], $4::int[], $5::int[]) AS u(pos, ttl, dur, pq);
            """);
        cmdContents.Parameters.AddWithValue(card.Id);
        cmdContents.Parameters.AddWithValue(card.Contents.Select(x => (short)x.Position).ToArray());
        cmdContents.Parameters.AddWithValue(card.Contents.Select(x => x.Title).ToArray());
        cmdContents.Parameters.Add(new NpgsqlParameter<int?[]>
        {
            TypedValue = card.Contents.Select(x => x.DurationSec).ToArray(),
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer
        });
        cmdContents.Parameters.Add(new NpgsqlParameter<int?[]>
        {
            TypedValue = card.Contents.Select(x => x.PageOrQty).ToArray(),
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer
        });
        batch.BatchCommands.Add(cmdContents);

        var cmdTags = new NpgsqlBatchCommand("""
            INSERT INTO catalog_item_tags (item_id, tag_id)
            SELECT $1, t.id FROM tags t WHERE t.name = ANY($2::varchar[]);
            """);
        cmdTags.Parameters.AddWithValue(card.Id);
        cmdTags.Parameters.AddWithValue(card.Tags.ToArray());
        batch.BatchCommands.Add(cmdTags);

        var cmdAttrs = new NpgsqlBatchCommand("""
            INSERT INTO catalog_item_attributes (item_id, attr_key, val_string, val_int, val_bool)
            SELECT $1, u.k, u.vs, u.vi, u.vb
            FROM UNNEST($2::varchar[], $3::varchar[], $4::int[], $5::boolean[]) AS u(k, vs, vi, vb);
            """);
        cmdAttrs.Parameters.AddWithValue(card.Id);
        cmdAttrs.Parameters.AddWithValue(card.Attributes.Select(x => x.Key).ToArray());
        cmdAttrs.Parameters.Add(new NpgsqlParameter<string?[]>
        {
            TypedValue = card.Attributes.Select(x => x.ValString).ToArray(),
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar
        });
        cmdAttrs.Parameters.Add(new NpgsqlParameter<int?[]>
        {
            TypedValue = card.Attributes.Select(x => x.ValInt).ToArray(),
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer
        });
        cmdAttrs.Parameters.Add(new NpgsqlParameter<bool?[]>
        {
            TypedValue = card.Attributes.Select(x => x.ValBool).ToArray(),
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Boolean
        });
        batch.BatchCommands.Add(cmdAttrs);

        await batch.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    public async Task<bool> TryReserveCopyAsync(string id)
    {
        const string sql = """
            UPDATE catalog_items
            SET available_copies = available_copies - 1
            WHERE id = @Id AND available_copies >= 1;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        int affected = await conn.ExecuteAsync(sql, new { Id = id });
        return affected == 1;
    }

    public async Task<long> RenamePublisherAsync(int publisherId, string newName, string newCountry)
    {
        const string sql = """
            UPDATE publishers
            SET name = @Name, country = @Country
            WHERE id = @Id;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        int affected = await conn.ExecuteAsync(sql, new { Id = publisherId, Name = newName, Country = newCountry });
        return affected;
    }

    public async Task<IReadOnlyList<GenreTypeStats>> GetGenreTypeStatsAsync()
    {
        const string sql = """
            SELECT
                g.code AS GenreCode,
                ci.item_type AS ItemType,
                COUNT(*) AS ItemsCount,
                COALESCE(SUM(ci.available_copies), 0) AS TotalAvailableCopies,
                COALESCE(AVG(ci.replacement_cost_minor), 0) AS AvgCostMinor
            FROM catalog_items ci
            JOIN genres g ON g.id = ci.genre_id
            WHERE ci.is_active = TRUE
            GROUP BY g.code, ci.item_type
            ORDER BY g.code ASC, ci.item_type ASC;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<GenreTypeStats>(sql);
        return rows.AsList();
    }

    public async Task<StorageStats> GetStorageStatsAsync()
    {
        const string sql = """
            SELECT
                (COALESCE(SUM(pg_table_size(c.oid)), 0)::float8 / (1024.0 * 1024.0)) AS DataMb,
                (COALESCE(SUM(pg_indexes_size(c.oid)), 0)::float8 / (1024.0 * 1024.0)) AS IndexMb,
                (COALESCE(SUM(pg_total_relation_size(c.oid)), 0)::float8 / (1024.0 * 1024.0)) AS TotalMb,
                (
                    (SELECT COUNT(*) FROM catalog_items) +
                    (SELECT COUNT(*) FROM catalog_item_contributors) +
                    (SELECT COUNT(*) FROM catalog_item_contents) +
                    (SELECT COUNT(*) FROM catalog_item_tags) +
                    (SELECT COUNT(*) FROM catalog_item_attributes)
                )::bigint AS TotalRows
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND c.relkind = 'r'
              AND c.relname IN (
                  'publishers', 'genres', 'contributors', 'tags', 'catalog_items',
                  'catalog_item_contributors', 'catalog_item_contents',
                  'catalog_item_tags', 'catalog_item_attributes'
              );
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleAsync<StorageRow>(sql);
        return new StorageStats(row.DataMb, row.IndexMb, row.TotalMb, row.TotalRows);
    }

    public async Task<string> ExplainPointReadAsync(string id)
    {
        var sb = new StringBuilder();
        await using var conn = await _dataSource.OpenConnectionAsync();

        var p1 = await conn.QueryAsync<string>("""
            EXPLAIN (ANALYZE, BUFFERS)
            SELECT ci.id, ci.title, p.name, g.name
            FROM catalog_items ci
            JOIN publishers p ON p.id = ci.publisher_id
            JOIN genres g ON g.id = ci.genre_id
            WHERE ci.id = @Id;
            """, new { Id = id });

        var p2 = await conn.QueryAsync<string>("""
            EXPLAIN (ANALYZE, BUFFERS)
            SELECT attr_key, val_string, val_int, val_bool
            FROM catalog_item_attributes
            WHERE item_id = @Id;
            """, new { Id = id });

        sb.AppendLine("[catalog_items]");
        sb.AppendLine(string.Join(Environment.NewLine, p1));
        sb.AppendLine("[catalog_item_attributes]");
        sb.AppendLine(string.Join(Environment.NewLine, p2));
        return sb.ToString();
    }

    public async Task<string> ExplainBoardGameSearchAsync(int playersCount, int maxPlayTimeMin, int limit)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var lines = await conn.QueryAsync<string>("""
            EXPLAIN (ANALYZE, BUFFERS)
            SELECT ci.id, ci.title, ci.popularity_score
            FROM catalog_items ci
            JOIN catalog_item_attributes a_min ON a_min.item_id = ci.id AND a_min.attr_key = 'min_players'
            JOIN catalog_item_attributes a_max ON a_max.item_id = ci.id AND a_max.attr_key = 'max_players'
            JOIN catalog_item_attributes a_time ON a_time.item_id = ci.id AND a_time.attr_key = 'play_time_min'
            WHERE ci.is_active = TRUE
              AND ci.item_type = 'board_game'
              AND a_min.val_int <= @Players
              AND a_max.val_int >= @Players
              AND a_time.val_int <= @MaxTime
            ORDER BY ci.popularity_score DESC, ci.id ASC
            LIMIT @Limit;
            """, new { Players = playersCount, MaxTime = maxPlayTimeMin, Limit = limit });

        return string.Join(Environment.NewLine, lines);
    }

    public async Task<string> ExplainContributorSearchAsync(int contributorId, string role, int limit)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var lines = await conn.QueryAsync<string>("""
            EXPLAIN (ANALYZE, BUFFERS)
            SELECT ci.id, ci.title, ci.publication_year
            FROM catalog_item_contributors cic
            JOIN catalog_items ci ON ci.id = cic.item_id
            WHERE cic.contributor_id = @ContributorId
              AND cic.role = @Role
              AND ci.is_active = TRUE
            ORDER BY ci.publication_year DESC, ci.id ASC
            LIMIT @Limit;
            """, new { ContributorId = contributorId, Role = role, Limit = limit });

        return string.Join(Environment.NewLine, lines);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static Task WriteNullableStringAsync(NpgsqlBinaryImporter writer, string? val) =>
        val is null ? writer.WriteNullAsync() : writer.WriteAsync(val, NpgsqlDbType.Varchar);

    private static Task WriteNullableIntAsync(NpgsqlBinaryImporter writer, int? val) =>
        val is null ? writer.WriteNullAsync() : writer.WriteAsync(val.Value, NpgsqlDbType.Integer);

    private static Task WriteNullableBoolAsync(NpgsqlBinaryImporter writer, bool? val) =>
        val is null ? writer.WriteNullAsync() : writer.WriteAsync(val.Value, NpgsqlDbType.Boolean);

    private sealed class ItemHeaderRow
    {
        public string Id { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int PublicationYear { get; set; }
        public string LanguageCode { get; set; } = string.Empty;
        public int ReplacementCostMinor { get; set; }
        public int AvailableCopies { get; set; }
        public int PopularityScore { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public int PubId { get; set; }
        public string PubName { get; set; } = string.Empty;
        public string PubCountry { get; set; } = string.Empty;
        public string? PubWebsite { get; set; }
        public int GenreId { get; set; }
        public string GenreCode { get; set; } = string.Empty;
        public string GenreName { get; set; } = string.Empty;
    }

    private sealed class ContributorRow
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
    }

    private sealed class ContentRow
    {
        public int Position { get; set; }
        public string Title { get; set; } = string.Empty;
        public int? DurationSec { get; set; }
        public int? PageOrQty { get; set; }
    }

    private sealed class AttributeRow
    {
        public string Key { get; set; } = string.Empty;
        public string? ValString { get; set; }
        public int? ValInt { get; set; }
        public bool? ValBool { get; set; }
    }

    private sealed class StorageRow
    {
        public double DataMb { get; set; }
        public double IndexMb { get; set; }
        public double TotalMb { get; set; }
        public long TotalRows { get; set; }
    }
}

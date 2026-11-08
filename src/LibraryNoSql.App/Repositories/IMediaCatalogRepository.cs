using LibraryNoSql.App.Models;

namespace LibraryNoSql.App.Repositories;

public interface IMediaCatalogRepository : IAsyncDisposable
{
    string EngineName { get; }

    Task<int> GetBaseItemCountAsync();

    Task ResetAndSeedAsync(GeneratedDataset dataset);

    Task<CatalogCard?> GetByIdAsync(string id);

    Task<IReadOnlyList<CatalogSummaryItem>> SearchBoardGamesForPlayersAsync(int playersCount, int maxPlayTimeMin, int limit);

    Task<IReadOnlyList<CatalogSummaryItem>> SearchByContributorRoleAsync(int contributorId, string role, int limit);

    Task InsertCardAsync(CatalogCard card);

    Task<bool> TryReserveCopyAsync(string id);

    Task<long> RenamePublisherAsync(int publisherId, string newName, string newCountry);

    Task<IReadOnlyList<GenreTypeStats>> GetGenreTypeStatsAsync();

    Task<StorageStats> GetStorageStatsAsync();

    Task<string> ExplainPointReadAsync(string id);

    Task<string> ExplainBoardGameSearchAsync(int playersCount, int maxPlayTimeMin, int limit);

    Task<string> ExplainContributorSearchAsync(int contributorId, string role, int limit);
}

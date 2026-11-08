namespace LibraryNoSql.App.Models;

public record PublisherInfo(int Id, string Name, string Country, string? Website);

public record GenreInfo(int Id, string Code, string Name);

public record ContributorEntry(int Id, string FullName, string Country, string Role, int DisplayOrder);

public record ContentEntry(int Position, string Title, int? DurationSec, int? PageOrQty);

public record ItemAttributeValue(string Key, string? ValString, int? ValInt, bool? ValBool);

public class CatalogCard
{
    public required string Id { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public required string Code { get; init; }
    public required string ItemType { get; init; }
    public required string Title { get; init; }
    public int PublicationYear { get; init; }
    public required string LanguageCode { get; init; }
    public required PublisherInfo Publisher { get; init; }
    public required GenreInfo Genre { get; init; }
    public int ReplacementCostMinor { get; init; }
    public int AvailableCopies { get; init; }
    public int PopularityScore { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTime CreatedAt { get; init; }
    public required List<ContributorEntry> Contributors { get; init; }
    public required List<ContentEntry> Contents { get; init; }
    public required List<string> Tags { get; init; }
    public required List<ItemAttributeValue> Attributes { get; init; }
}

public class CatalogSummaryItem
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int PublicationYear { get; set; }
    public int ReplacementCostMinor { get; set; }
    public int PopularityScore { get; set; }
}

public class GenreTypeStats
{
    public string GenreCode { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public long ItemsCount { get; set; }
    public long TotalAvailableCopies { get; set; }
    public double AvgCostMinor { get; set; }
}

public record StorageStats(double DataMb, double IndexMb, double TotalMb, long Count);

public class GeneratedDataset
{
    public required List<PublisherInfo> Publishers { get; init; }
    public required List<GenreInfo> Genres { get; init; }
    public required List<(int Id, string FullName, string Country)> Contributors { get; init; }
    public required List<(int Id, string Name)> Tags { get; init; }
    public required List<CatalogCard> Items { get; init; }
}

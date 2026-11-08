using LibraryNoSql.App.Models;

namespace LibraryNoSql.App.Data;

public static class CatalogDataGenerator
{
    private static readonly (string Code, string Name)[] GenreDefs =
    [
        ("cs", "Комп'ютерні науки"),
        ("sci-fi", "Наукова фантастика"),
        ("history", "Історія"),
        ("economics", "Економіка"),
        ("law", "Правознавство"),
        ("medicine", "Медицина"),
        ("philosophy", "Філософія"),
        ("psychology", "Психологія"),
        ("fantasy", "Фентезі"),
        ("art", "Мистецтво та дизайн"),
        ("periodicals", "Наукова періодика"),
        ("tabletop", "Настільні та освітні ігри")
    ];

    private static readonly string[] Countries =
        ["Україна", "США", "Велика Британія", "Німеччина", "Польща", "Франція", "Канада", "Японія"];

    private static readonly string[] Languages = ["uk", "en", "pl", "de", "fr"];

    private static readonly string[] FirstNames =
        ["Олександр", "Марія", "Іван", "Олена", "Тарас", "Софія", "Андрій", "Наталія", "Дмитро", "Катерина", "Роберт", "Мартін", "Сара", "Томас", "Анна", "Давид"];

    private static readonly string[] LastNames =
        ["Коваленко", "Шевченко", "Бойко", "Ткаченко", "Кравченко", "Мельник", "Бондаренко", "Савченко", "Клеппман", "Фаулер", "Ньюмен", "Гокінс", "Новак", "Мюллер", "Дюпон", "Танака"];

    private static readonly string[] TitleAdjectives =
        ["Сучасні", "Прикладні", "Фундаментальні", "Розподілені", "Класичні", "Аналітичні", "Інтерактивні", "Стратегічні", "Практичні", "Експериментальні"];

    private static readonly string[] TitleNouns =
        ["системи баз даних", "алгоритми", "хроніки", "архітектури", "дослідження", "моделі", "цивілізації", "мережі", "протоколи", "горизонти"];

    private static readonly string[] TagPool =
    [
        "бестселер", "університетський-курс", "практикум", "класика", "новинка",
        "наукове-видання", "українською", "оригінал", "ілюстроване", "коворкінг",
        "рекомендовано", "архів", "монографія", "довідник", "для-початківців",
        "просунутий-рівень", "командна-гра", "логіка", "аудіофонд", "цифрова-ліцензія",
        "програмування", "аналітика", "право", "медицина", "дизайн",
        "історія-україни", "світова-література", "економіка", "філософія", "психологія"
    ];

    private static readonly string[] Bindings = ["hardcover", "paperback"];
    private static readonly string[] Codecs = ["MP3", "AAC", "FLAC"];

    public static GeneratedDataset Generate(int itemCount, int seed = 42)
    {
        var rng = new Random(seed);
        int publisherCount = 80;
        int contributorCount = Math.Max(600, itemCount / 12);

        var publishers = Enumerable.Range(1, publisherCount)
            .Select(id => new PublisherInfo(
                id,
                $"Видавництво #{id:D3}",
                Countries[rng.Next(Countries.Length)],
                $"https://publisher-{id:D3}.example.org"))
            .ToList();

        var genres = GenreDefs
            .Select((g, idx) => new GenreInfo(idx + 1, g.Code, g.Name))
            .ToList();

        var contributors = Enumerable.Range(1, contributorCount)
            .Select(id => (
                Id: id,
                FullName: $"{FirstNames[rng.Next(FirstNames.Length)].Trim()} {LastNames[rng.Next(LastNames.Length)]} #{id}",
                Country: Countries[rng.Next(Countries.Length)]))
            .ToList();

        var tags = TagPool
            .Select((t, idx) => (Id: idx + 1, Name: t))
            .ToList();

        var pubZipf = BuildZipfCdf(publisherCount, 1.15);
        var contribZipf = BuildZipfCdf(contributorCount, 1.05);

        var dataset = new GeneratedDataset
        {
            Publishers = publishers,
            Genres = genres,
            Contributors = contributors,
            Tags = tags,
            Items = new List<CatalogCard>(itemCount)
        };

        for (int i = 1; i <= itemCount; i++)
        {
            dataset.Items.Add(CreateCard(i, rng, dataset, pubZipf, contribZipf));
        }

        return dataset;
    }

    public static List<CatalogCard> GenerateExtraItems(
        GeneratedDataset baseSet,
        string runPrefix,
        int count,
        int seed = 999)
    {
        var rng = new Random(seed);
        var pubZipf = BuildZipfCdf(baseSet.Publishers.Count, 1.15);
        var contribZipf = BuildZipfCdf(baseSet.Contributors.Count, 1.05);
        var list = new List<CatalogCard>(count);

        for (int i = 1; i <= count; i++)
        {
            var card = CreateCard(1_000_000 + i, rng, baseSet, pubZipf, contribZipf);
            list.Add(new CatalogCard
            {
                Id = $"{runPrefix}-{i:D5}",
                SchemaVersion = card.SchemaVersion,
                Code = $"TMP-{runPrefix.ToUpperInvariant()}-{i:D5}",
                ItemType = card.ItemType,
                Title = card.Title,
                PublicationYear = card.PublicationYear,
                LanguageCode = card.LanguageCode,
                Publisher = card.Publisher,
                Genre = card.Genre,
                ReplacementCostMinor = card.ReplacementCostMinor,
                AvailableCopies = card.AvailableCopies,
                PopularityScore = card.PopularityScore,
                IsActive = card.IsActive,
                CreatedAt = card.CreatedAt,
                Contributors = card.Contributors,
                Contents = card.Contents,
                Tags = card.Tags,
                Attributes = card.Attributes
            });
        }

        return list;
    }

    public static List<string> SampleZipfIds(IReadOnlyList<CatalogCard> items, int sampleCount, int seed = 777)
    {
        var rng = new Random(seed);
        int limit = Math.Min(items.Count, 5000);
        var cdf = BuildZipfCdf(limit, 1.15);
        var result = new List<string>(sampleCount);

        for (int i = 0; i < sampleCount; i++)
        {
            int rankIdx = SampleZipfIndex(rng, cdf);
            result.Add(items[rankIdx].Id);
        }

        return result;
    }

    private static CatalogCard CreateCard(
        int index,
        Random rng,
        GeneratedDataset ds,
        double[] pubZipf,
        double[] contribZipf)
    {
        double roll = rng.NextDouble();
        string itemType = roll switch
        {
            < 0.45 => "printed_book",
            < 0.65 => "audiobook",
            < 0.85 => "periodical",
            _ => "board_game"
        };

        var publisher = ds.Publishers[SampleZipfIndex(rng, pubZipf)];
        var genre = itemType == "board_game"
            ? ds.Genres[^1]
            : ds.Genres[rng.Next(ds.Genres.Count - 1)];

        int contribCount = rng.Next(1, 4);
        var usedContrib = new HashSet<(int, string)>();
        var itemContribs = new List<ContributorEntry>(contribCount + 1);
        int order = 1;

        string primaryRole = itemType == "board_game" ? "designer" : "author";
        for (int c = 0; c < contribCount; c++)
        {
            var person = ds.Contributors[SampleZipfIndex(rng, contribZipf)];
            if (usedContrib.Add((person.Id, primaryRole)))
            {
                itemContribs.Add(new ContributorEntry(person.Id, person.FullName, person.Country, primaryRole, order++));
            }
        }

        if (itemType == "audiobook")
        {
            var narrator = ds.Contributors[SampleZipfIndex(rng, contribZipf)];
            if (usedContrib.Add((narrator.Id, "narrator")))
            {
                itemContribs.Add(new ContributorEntry(narrator.Id, narrator.FullName, narrator.Country, "narrator", order++));
            }
        }
        else if (rng.NextDouble() < 0.35)
        {
            string extraRole = itemType == "periodical" ? "editor" : "translator";
            var extra = ds.Contributors[SampleZipfIndex(rng, contribZipf)];
            if (usedContrib.Add((extra.Id, extraRole)))
            {
                itemContribs.Add(new ContributorEntry(extra.Id, extra.FullName, extra.Country, extraRole, order++));
            }
        }

        int contentCount = rng.Next(3, 8);
        var contents = new List<ContentEntry>(contentCount);
        for (int pos = 1; pos <= contentCount; pos++)
        {
            contents.Add(itemType switch
            {
                "audiobook" => new ContentEntry(pos, $"Трек {pos}", rng.Next(420, 2400), null),
                "board_game" => new ContentEntry(pos, $"Компонент набору #{pos}", null, rng.Next(1, 60)),
                "periodical" => new ContentEntry(pos, $"Стаття випуску #{pos}", null, rng.Next(8, 30)),
                _ => new ContentEntry(pos, $"Розділ {pos}", null, pos * 35)
            });
        }

        int tagCount = rng.Next(2, 6);
        var selectedTags = new HashSet<string>();
        while (selectedTags.Count < tagCount)
        {
            selectedTags.Add(ds.Tags[rng.Next(ds.Tags.Count)].Name);
        }

        var attrs = itemType switch
        {
            "printed_book" => new List<ItemAttributeValue>
            {
                new("pages", null, rng.Next(120, 960), null),
                new("binding", Bindings[rng.Next(Bindings.Length)], null, null),
                new("udc_code", $"004.{rng.Next(10, 99)}", null, null),
                new("illustrated", null, null, rng.NextDouble() < 0.3)
            },
            "audiobook" => new List<ItemAttributeValue>
            {
                new("duration_sec", null, contents.Sum(x => x.DurationSec ?? 900), null),
                new("audio_codec", Codecs[rng.Next(Codecs.Length)], null, null),
                new("bitrate_kbps", null, rng.NextDouble() < 0.4 ? 960 : 320, null),
                new("abridged", null, null, rng.NextDouble() < 0.15)
            },
            "periodical" => new List<ItemAttributeValue>
            {
                new("issue_number", null, rng.Next(1, 240), null),
                new("issn", $"ISSN-{rng.Next(1000, 9999)}-{rng.Next(1000, 9999)}", null, null),
                new("peer_reviewed", null, null, rng.NextDouble() < 0.8),
                new("max_digital_seats", null, rng.Next(5, 100), null)
            },
            _ => BuildBoardGameAttributes(rng)
        };

        int popularity = (int)Math.Round(10000.0 / Math.Pow((index % 2000) + 1, 0.85)) + rng.Next(0, 150);

        return new CatalogCard
        {
            Id = $"item-{index:D7}",
            SchemaVersion = 1,
            Code = $"LIB-{itemType[..3].ToUpperInvariant()}-{index:D7}",
            ItemType = itemType,
            Title = $"{TitleAdjectives[rng.Next(TitleAdjectives.Length)]} {TitleNouns[rng.Next(TitleNouns.Length)]} #{index}",
            PublicationYear = rng.Next(1995, 2027),
            LanguageCode = Languages[rng.Next(Languages.Length)],
            Publisher = publisher,
            Genre = genre,
            ReplacementCostMinor = rng.Next(150, 2500) * 100,
            AvailableCopies = rng.Next(1, 15),
            PopularityScore = popularity,
            IsActive = rng.NextDouble() < 0.96,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index * 3),
            Contributors = itemContribs,
            Contents = contents,
            Tags = selectedTags.ToList(),
            Attributes = attrs
        };
    }

    private static List<ItemAttributeValue> BuildBoardGameAttributes(Random rng)
    {
        int minP = rng.Next(1, 5);
        int maxP = minP + rng.Next(1, 6);
        return
        [
            new("min_players", null, minP, null),
            new("max_players", null, maxP, null),
            new("play_time_min", null, rng.Next(20, 180), null),
            new("age_rating", null, rng.Next(6, 18), null)
        ];
    }

    private static double[] BuildZipfCdf(int n, double s)
    {
        var cdf = new double[n];
        double sum = 0;
        for (int i = 1; i <= n; i++)
        {
            sum += 1.0 / Math.Pow(i, s);
            cdf[i - 1] = sum;
        }
        for (int i = 0; i < n; i++)
        {
            cdf[i] /= sum;
        }
        return cdf;
    }

    private static int SampleZipfIndex(Random rng, double[] cdf)
    {
        double u = rng.NextDouble();
        int idx = Array.BinarySearch(cdf, u);
        if (idx < 0)
        {
            idx = ~idx;
        }
        return Math.Clamp(idx, 0, cdf.Length - 1);
    }
}

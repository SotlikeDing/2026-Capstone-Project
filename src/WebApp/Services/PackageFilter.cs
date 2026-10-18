namespace WebApp.Services;

/// <summary>
/// What a visitor typed/picked in a <c>PackageFilterBar</c>. All filtering is client-side over the
/// list the page already loaded; <see cref="Apply{T}"/> is told how to read each field from the
/// page's own model (hunting packages and PH/VG service packages share the bar).
/// </summary>
public sealed class PackageFilterState
{
    public const string SortNewest = "newest";
    public const string SortPriceAsc = "price-asc";
    public const string SortPriceDesc = "price-desc";
    public const string SortDaysAsc = "days-asc";
    public const string SortTitle = "title";

    public const int MaxSearchLength = 100;

    public string Search { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? MaxDays { get; set; }
    public string Sort { get; set; } = SortNewest;

    public bool IsActive =>
        !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Region) || !string.IsNullOrWhiteSpace(Species) ||
        !string.IsNullOrWhiteSpace(Status) || MinPrice.HasValue || MaxPrice.HasValue || MaxDays.HasValue;

    /// <summary>Min above max can't match anything - the bar shows this instead of an empty list.</summary>
    public bool PriceRangeInvalid => MinPrice.HasValue && MaxPrice.HasValue && MinPrice > MaxPrice;

    public void Clear()
    {
        Search = Region = Species = Status = string.Empty;
        MinPrice = MaxPrice = null;
        MaxDays = null;
        Sort = SortNewest;
    }

    public IEnumerable<T> Apply<T>(IEnumerable<T> items, PackageFilterFields<T> f)
    {
        var search = Search.Trim();
        var query = items.Where(p =>
            (search.Length == 0 || f.Text(p).Any(t => !string.IsNullOrEmpty(t) && t.Contains(search, StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrWhiteSpace(Region) || f.Region == null || f.Region(p).Any(r => string.Equals(r?.Trim(), Region, StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrWhiteSpace(Species) || f.Species == null || f.Species(p).Any(s => string.Equals(s?.Trim(), Species, StringComparison.OrdinalIgnoreCase))) &&
            (string.IsNullOrWhiteSpace(Status) || f.Status == null || f.Status(p) == Status) &&
            (PriceRangeInvalid || ((!MinPrice.HasValue || f.Price(p) >= MinPrice) && (!MaxPrice.HasValue || f.Price(p) <= MaxPrice))) &&
            (!MaxDays.HasValue || f.Days == null || f.Days(p) <= MaxDays));

        return Sort switch
        {
            SortPriceAsc => query.OrderBy(f.Price).ThenBy(f.Title, StringComparer.OrdinalIgnoreCase),
            SortPriceDesc => query.OrderByDescending(f.Price).ThenBy(f.Title, StringComparer.OrdinalIgnoreCase),
            SortDaysAsc when f.Days != null => query.OrderBy(f.Days).ThenBy(f.Price),
            SortTitle => query.OrderBy(f.Title, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(f.Id),
        };
    }

    /// <summary>Distinct, trimmed, sorted option values for a region/species dropdown.</summary>
    public static List<string> Options(IEnumerable<string?> values) => values
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

/// <summary>How to read the filterable fields from one kind of package. Null = that filter doesn't apply.</summary>
public sealed class PackageFilterFields<T>
{
    public required Func<T, int> Id { get; init; }
    public required Func<T, string> Title { get; init; }
    public required Func<T, decimal> Price { get; init; }
    /// <summary>Every piece of text the free-text search looks in.</summary>
    public required Func<T, IEnumerable<string>> Text { get; init; }
    public Func<T, IEnumerable<string>>? Region { get; init; }
    public Func<T, IEnumerable<string>>? Species { get; init; }
    public Func<T, int>? Days { get; init; }
    public Func<T, string>? Status { get; init; }
}

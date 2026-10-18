namespace WebApp.Services;

/// <summary>
/// Formats amounts in the currency they were quoted in. Package rates are in US$ (international
/// hunters); an outfitter confirms each booking in US$ or rand (its price breakdown's Currency).
/// Local hunters also see roughly what a US$ amount is in rand - PayFast charges it in rand at
/// the admin's UsdToZarRate.
/// </summary>
public static class Money
{
    /// <summary>
    /// Currency of package prices and new quotes (admin setting PackagePricesInZar). Global for the whole
    /// site, refreshed whenever the public settings are read (MainLayout does so on every first render).
    /// </summary>
    public static string PackageCurrency { get; set; } = "USD";

    /// <summary>"R" or "US$", for labels like "Vanaf prys per jagter (R)".</summary>
    public static string PackageCurrencySymbol => IsZar(PackageCurrency) ? "R" : "US$";

    public static bool IsZar(string? currency) => string.Equals(currency, "ZAR", StringComparison.OrdinalIgnoreCase);

    /// <summary>"R 81 000" or "US$ 4 500"; no currency means the package currency (a quote that didn't record one).</summary>
    public static string Format(decimal amount, string? currency, string format = "N0")
        => (IsZar(currency ?? PackageCurrency) ? "R " : "US$ ") + amount.ToString(format);

    /// <summary>"≈ R 81 000" for a US$ amount (empty for rand or without a rate).</summary>
    public static string ZarHint(decimal amount, string? currency, decimal usdToZarRate)
        => IsZar(currency ?? PackageCurrency) || usdToZarRate <= 0 ? string.Empty : "≈ R " + (amount * usdToZarRate).ToString("N0");
}

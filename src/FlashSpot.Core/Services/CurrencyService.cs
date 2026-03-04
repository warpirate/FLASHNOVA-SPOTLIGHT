using System.Net.Http;
using System.Text.Json;

namespace FlashSpot.Core.Services;

public sealed class CurrencyService : IDisposable
{
    private readonly HttpClient _httpClient;
    private Dictionary<string, decimal>? _rates;
    private DateTime _ratesFetchedAt;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private static readonly Dictionary<string, string> _currencyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dollar"] = "USD", ["dollars"] = "USD", ["usd"] = "USD", ["$"] = "USD",
        ["euro"] = "EUR", ["euros"] = "EUR", ["eur"] = "EUR",
        ["pound"] = "GBP", ["pounds"] = "GBP", ["gbp"] = "GBP", ["sterling"] = "GBP",
        ["yen"] = "JPY", ["jpy"] = "JPY",
        ["yuan"] = "CNY", ["cny"] = "CNY", ["rmb"] = "CNY",
        ["rupee"] = "INR", ["rupees"] = "INR", ["inr"] = "INR",
        ["won"] = "KRW", ["krw"] = "KRW",
        ["franc"] = "CHF", ["francs"] = "CHF", ["chf"] = "CHF",
        ["real"] = "BRL", ["reais"] = "BRL", ["brl"] = "BRL",
        ["ruble"] = "RUB", ["rubles"] = "RUB", ["rub"] = "RUB",
        ["peso"] = "MXN", ["pesos"] = "MXN", ["mxn"] = "MXN",
        ["cad"] = "CAD", ["canadian dollar"] = "CAD",
        ["aud"] = "AUD", ["australian dollar"] = "AUD",
        ["nzd"] = "NZD",
        ["sek"] = "SEK", ["krona"] = "SEK",
        ["nok"] = "NOK", ["krone"] = "NOK",
        ["dkk"] = "DKK",
        ["pln"] = "PLN", ["zloty"] = "PLN",
        ["try"] = "TRY", ["lira"] = "TRY",
        ["sgd"] = "SGD",
        ["hkd"] = "HKD",
        ["thb"] = "THB", ["baht"] = "THB",
        ["zar"] = "ZAR", ["rand"] = "ZAR",
        ["aed"] = "AED", ["dirham"] = "AED",
        ["sar"] = "SAR", ["riyal"] = "SAR",
        ["pkr"] = "PKR",
    };

    public CurrencyService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task<CurrencyConversionResult?> ConvertAsync(
        decimal amount, string fromCurrency, string toCurrency, CancellationToken ct)
    {
        var from = ResolveCurrencyCode(fromCurrency);
        var to = ResolveCurrencyCode(toCurrency);

        if (from is null || to is null)
        {
            return null;
        }

        var rates = await GetRatesAsync(ct);
        if (rates is null)
        {
            return null;
        }

        if (!rates.TryGetValue(from, out var fromRate) || !rates.TryGetValue(to, out var toRate))
        {
            return null;
        }

        var converted = amount / fromRate * toRate;

        return new CurrencyConversionResult(amount, from, converted, to);
    }

    public static string? ResolveCurrencyCode(string input)
    {
        var trimmed = input.Trim();

        // Direct 3-letter code
        if (trimmed.Length == 3 && trimmed.All(char.IsLetter))
        {
            return trimmed.ToUpperInvariant();
        }

        return _currencyNames.TryGetValue(trimmed, out var code) ? code : null;
    }

    private async Task<Dictionary<string, decimal>?> GetRatesAsync(CancellationToken ct)
    {
        if (_rates is not null && DateTime.UtcNow - _ratesFetchedAt < CacheDuration)
        {
            return _rates;
        }

        try
        {
            var url = "https://open.er-api.com/v6/latest/USD";
            var response = await _httpClient.GetStringAsync(url, ct);
            var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("rates", out var ratesElement))
            {
                return null;
            }

            var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in ratesElement.EnumerateObject())
            {
                if (prop.Value.TryGetDecimal(out var rate))
                {
                    rates[prop.Name] = rate;
                }
            }

            _rates = rates;
            _ratesFetchedAt = DateTime.UtcNow;
            return rates;
        }
        catch
        {
            return _rates; // Return stale cache if available
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed record CurrencyConversionResult(decimal FromAmount, string FromCode, decimal ToAmount, string ToCode);

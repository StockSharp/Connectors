namespace StockSharp.CoinSpot.Native;

static class CoinSpotExtensions
{
	public static (string BaseCurrency, string QuoteCurrency)
		ToCoinSpotCurrencies(this string nativeSymbol)
	{
		nativeSymbol = nativeSymbol.ThrowIfEmpty(
			nameof(nativeSymbol)).Trim();
		var parts = nativeSymbol.Split(
			['_', '/', '-'],
			StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries);
		return parts.Length switch
		{
			1 => (parts[0].ToUpperInvariant(), "AUD"),
			2 => (
				parts[0].ToUpperInvariant(),
				parts[1].ToUpperInvariant()),
			_ => throw new FormatException(
				$"Invalid CoinSpot market '{nativeSymbol}'."),
		};
	}

	public static string CreateSecurityCode(
		string baseCurrency,
		string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency))
			.Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency))
				.Trim().ToUpperInvariant();

	public static string ToCoinSpotSymbol(this string securityCode)
	{
		var (baseCurrency, quoteCurrency) =
			securityCode.ToCoinSpotCurrencies();
		return quoteCurrency.EqualsIgnoreCase("AUD")
			? baseCurrency.ToLowerInvariant()
			: $"{baseCurrency}_{quoteCurrency}".ToLowerInvariant();
	}

	public static string ToCoinSpotPath(this CoinSpotMarket market)
	{
		if (market is null)
			throw new ArgumentNullException(nameof(market));
		return market.QuoteUnit.EqualsIgnoreCase("AUD")
			? market.BaseUnit.ToLowerInvariant()
			: $"{market.BaseUnit.ToLowerInvariant()}/" +
				market.QuoteUnit.ToLowerInvariant();
	}

	public static SecurityId ToStockSharp(this CoinSpotMarket market)
		=> new()
		{
			SecurityCode = market?.SecurityCode ??
				throw new ArgumentNullException(nameof(market)),
			BoardCode = BoardCodes.CoinSpot,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(
			value, true, out var currency)
				? currency
				: null;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);
}

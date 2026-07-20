namespace StockSharp.VariationalOmni.Native;

static class VariationalOmniExtensions
{
	public static SecurityId ToStockSharp(this VariationalOmniListing listing)
	{
		ArgumentNullException.ThrowIfNull(listing);
		return new()
		{
			SecurityCode = listing.Ticker.ThrowIfEmpty(nameof(listing.Ticker))
				.Trim().ToUpperInvariant(),
			BoardCode = BoardCodes.VariationalOmni,
		};
	}

	public static decimal? ToDecimal(this string value)
		=> !value.IsEmpty() && decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var number)
				? number
				: null;

	public static decimal? ToPositiveDecimal(this string value)
		=> value.ToDecimal() is decimal number && number > 0 ? number : null;

	public static decimal? ToNonNegativeDecimal(this string value)
		=> value.ToDecimal() is decimal number && number >= 0 ? number : null;

	public static DateTime? ToVariationalOmniTime(this string value)
	{
		if (value.IsEmpty() || !DateTime.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var time))
			return null;
		return DateTime.SpecifyKind(time, DateTimeKind.Utc);
	}

	public static VariationalOmniQuote GetBestQuote(
		this VariationalOmniListing listing)
	{
		var quotes = listing?.Quotes;
		if (quotes is null)
			return null;
		return new[] { quotes.Base, quotes.Size1K, quotes.Size100K, quotes.Size1M }
			.FirstOrDefault(static quote =>
				quote?.Bid.ToPositiveDecimal() is not null ||
				quote?.Ask.ToPositiveDecimal() is not null);
	}

	public static decimal? GetTotal(this VariationalOmniOpenInterest value)
	{
		if (value is null)
			return null;
		var longValue = value.Long.ToNonNegativeDecimal();
		var shortValue = value.Short.ToNonNegativeDecimal();
		if (longValue is null && shortValue is null)
			return null;
		return (longValue ?? 0m) + (shortValue ?? 0m);
	}
}

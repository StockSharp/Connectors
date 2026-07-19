namespace StockSharp.Bitkub.Native;

static class BitkubExtensions
{
	public static string NormalizeSymbol(this string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim()
			.Replace('/', '_').Replace('-', '_').ToUpperInvariant();

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.NormalizeSymbol(),
			BoardCode = BoardCodes.Bitkub,
		};

	public static (string baseAsset, string quoteAsset) SplitSymbol(
		this string symbol)
	{
		symbol = symbol.NormalizeSymbol();
		var separator = symbol.IndexOf('_');
		if (separator <= 0 || separator + 1 >= symbol.Length)
			throw new FormatException(
				$"Bitkub symbol '{symbol}' is not in BASE_QUOTE format.");
		return (symbol[..separator], symbol[(separator + 1)..]);
	}

	public static string ToLegacyCancelSymbol(this string symbol)
	{
		var (baseAsset, quoteAsset) = symbol.SplitSymbol();
		return $"{quoteAsset}_{baseAsset}".ToLowerInvariant();
	}

	public static string ToWireSymbol(this string symbol)
		=> symbol.NormalizeSymbol().ToLowerInvariant();

	public static DateTime FromBitkubTimestamp(this long value)
	{
		if (value <= 0)
			return DateTime.UtcNow;
		return value < 10_000_000_000L
			? DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime
			: DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
	}

	public static long ToMilliseconds(this DateTime value)
	{
		var utc = value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(value,
				DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};
		return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
	}

	public static decimal GetStep(int scale)
	{
		if (scale <= 0)
			return 1m;
		if (scale > 28)
			scale = 28;
		var value = 1m;
		for (var i = 0; i < scale; i++)
			value /= 10m;
		return value;
	}

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static BitkubSides ToBitkub(this Sides side)
		=> side == Sides.Buy ? BitkubSides.Buy : BitkubSides.Sell;

	public static Sides ToStockSharp(this BitkubSides side)
		=> side == BitkubSides.Buy ? Sides.Buy : Sides.Sell;

	public static BitkubSides ToBitkubSide(this string side)
		=> side.EqualsIgnoreCase("BUY") ? BitkubSides.Buy :
			side.EqualsIgnoreCase("SELL") ? BitkubSides.Sell :
			throw new FormatException($"Unsupported Bitkub side '{side}'.");

	public static OrderTypes ToStockSharp(this BitkubOrderTypes type)
		=> type switch
		{
			BitkubOrderTypes.Market => OrderTypes.Market,
			BitkubOrderTypes.StopLimit => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToStockSharp(this BitkubOrderStatuses status)
		=> status switch
		{
			BitkubOrderStatuses.New or BitkubOrderStatuses.Open or
				BitkubOrderStatuses.PartiallyFilled or
				BitkubOrderStatuses.Untriggered or
				BitkubOrderStatuses.Unfilled => OrderStates.Active,
			BitkubOrderStatuses.Filled or
				BitkubOrderStatuses.PartiallyFilledCanceled or
				BitkubOrderStatuses.Canceled or
				BitkubOrderStatuses.Cancelled => OrderStates.Done,
			BitkubOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static string CreateClientId(long transactionId, string userOrderId)
	{
		var source = userOrderId.IsEmpty()
			? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
			: userOrderId.Trim();
		var value = new string(source.Where(static character =>
			character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or
				>= '0' and <= '9' or '_' or '-').ToArray());
		if (value.IsEmpty())
			value = $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
		return value.Length <= 40 ? value : value[..40];
	}

	public static long ParseTransactionId(string clientId)
		=> clientId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;
}

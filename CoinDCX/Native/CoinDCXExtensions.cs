namespace StockSharp.CoinDCX.Native;

static class CoinDCXExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(8),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(3),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static string NormalizeMarket(this string market)
		=> market.ThrowIfEmpty(nameof(market)).Trim().Replace("/", string.Empty)
			.Replace("-", string.Empty).Replace("_", string.Empty)
			.ToUpperInvariant();

	public static string NormalizePair(this string pair)
		=> pair.ThrowIfEmpty(nameof(pair)).Trim().ToUpperInvariant();

	public static SecurityId ToStockSharp(this string market)
		=> new()
		{
			SecurityCode = market.NormalizeMarket(),
			BoardCode = BoardCodes.CoinDCX,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static CoinDCXSides ToCoinDCX(this Sides side)
		=> side == Sides.Buy ? CoinDCXSides.Buy : CoinDCXSides.Sell;

	public static Sides ToStockSharp(this CoinDCXSides side)
		=> side == CoinDCXSides.Buy ? Sides.Buy : Sides.Sell;

	public static OrderTypes ToStockSharp(this CoinDCXOrderTypes type)
		=> type == CoinDCXOrderTypes.MarketOrder
			? OrderTypes.Market
			: OrderTypes.Limit;

	public static OrderStates ToStockSharp(this CoinDCXOrderStatuses status)
		=> status switch
		{
			CoinDCXOrderStatuses.Init or CoinDCXOrderStatuses.Open or
				CoinDCXOrderStatuses.PartiallyFilled or
				CoinDCXOrderStatuses.Untriggered => OrderStates.Active,
			CoinDCXOrderStatuses.Filled or CoinDCXOrderStatuses.PartiallyCancelled or
				CoinDCXOrderStatuses.Cancelled => OrderStates.Done,
			CoinDCXOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static string ToCoinDCXInterval(this TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromMinutes(1)) return "1m";
		if (timeFrame == TimeSpan.FromMinutes(5)) return "5m";
		if (timeFrame == TimeSpan.FromMinutes(15)) return "15m";
		if (timeFrame == TimeSpan.FromMinutes(30)) return "30m";
		if (timeFrame == TimeSpan.FromHours(1)) return "1h";
		if (timeFrame == TimeSpan.FromHours(2)) return "2h";
		if (timeFrame == TimeSpan.FromHours(4)) return "4h";
		if (timeFrame == TimeSpan.FromHours(6)) return "6h";
		if (timeFrame == TimeSpan.FromHours(8)) return "8h";
		if (timeFrame == TimeSpan.FromDays(1)) return "1d";
		if (timeFrame == TimeSpan.FromDays(3)) return "3d";
		if (timeFrame == TimeSpan.FromDays(7)) return "1w";
		if (timeFrame == TimeSpan.FromDays(30)) return "1M";
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"Unsupported CoinDCX candle interval.");
	}

	public static TimeSpan ToTimeFrame(this string interval)
		=> interval switch
		{
			"1m" => TimeSpan.FromMinutes(1),
			"5m" => TimeSpan.FromMinutes(5),
			"15m" => TimeSpan.FromMinutes(15),
			"30m" => TimeSpan.FromMinutes(30),
			"1h" => TimeSpan.FromHours(1),
			"2h" => TimeSpan.FromHours(2),
			"4h" => TimeSpan.FromHours(4),
			"6h" => TimeSpan.FromHours(6),
			"8h" => TimeSpan.FromHours(8),
			"1d" => TimeSpan.FromDays(1),
			"3d" => TimeSpan.FromDays(3),
			"1w" => TimeSpan.FromDays(7),
			"1M" => TimeSpan.FromDays(30),
			_ => throw new FormatException(
				$"Unsupported CoinDCX candle interval '{interval}'."),
		};

	public static DateTime FromMilliseconds(this long value, DateTime fallback)
		=> value > 0
			? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime
			: fallback;

	public static DateTime FromMilliseconds(this decimal value, DateTime fallback)
		=> value > 0 && value <= long.MaxValue
			? decimal.Truncate(value).To<long>().FromMilliseconds(fallback)
			: fallback;

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

	public static DateTime ParseTimestamp(this string value, DateTime fallback)
		=> DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var timestamp)
			? timestamp
			: fallback;

	public static string CreateClientId(long transactionId, string userOrderId)
	{
		var source = userOrderId.IsEmpty()
			? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
			: userOrderId.Trim();
		var value = new string(source.Where(static character =>
			char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray());
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

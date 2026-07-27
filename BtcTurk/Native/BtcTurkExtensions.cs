namespace StockSharp.BtcTurk.Native;

static class BtcTurkExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static string NormalizeSymbol(this string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant()
			.Replace('_', '/').Replace('-', '/');
		var parts = symbol.Split('/', StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"BtcTurk symbol '{symbol}' is not in BASE/QUOTE format.");
		return CreateSecurityCode(parts[0], parts[1]);
	}

	public static string CreateSecurityCode(string numerator,
		string denominator)
		=> $"{numerator.ThrowIfEmpty(nameof(numerator)).Trim().ToUpperInvariant()}/" +
			denominator.ThrowIfEmpty(nameof(denominator)).Trim()
				.ToUpperInvariant();

	public static string ToNativeSymbol(this string symbol)
		=> symbol.NormalizeSymbol().Replace("/", string.Empty);

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.NormalizeSymbol(),
			BoardCode = BoardCodes.BtcTurk,
		};

	public static DateTime FromUnixMilliseconds(this long value)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(value), DateTimeKind.Utc);

	public static DateTime FromUnixSeconds(this long value)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddSeconds(value), DateTimeKind.Utc);

	public static long ToUnixMilliseconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalMilliseconds;

	public static long ToUnixSeconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalSeconds;

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(value,
				DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};

	public static decimal ParseDecimal(string value)
	{
		if (value.IsEmpty())
			return 0m;
		value = value.Trim().Replace(',', '.');
		return decimal.TryParse(value, NumberStyles.Number |
			NumberStyles.AllowExponent, CultureInfo.InvariantCulture,
			out var result)
				? result
				: throw new FormatException(
					$"BtcTurk decimal value '{value}' is invalid.");
	}

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static string ToBtcTurkResolution(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1"
			: timeFrame == TimeSpan.FromMinutes(5) ? "5"
			: timeFrame == TimeSpan.FromMinutes(15) ? "15"
			: timeFrame == TimeSpan.FromMinutes(30) ? "30"
			: timeFrame == TimeSpan.FromHours(1) ? "60"
			: timeFrame == TimeSpan.FromHours(4) ? "240"
			: timeFrame == TimeSpan.FromDays(1) ? "1D"
			: timeFrame == TimeSpan.FromDays(7) ? "1W"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Unsupported BtcTurk candle interval.");

	public static decimal? GetStep(int precision)
	{
		if (precision is < 0 or > 28)
			return null;
		var step = 1m;
		for (var i = 0; i < precision; i++)
			step /= 10m;
		return step;
	}

	public static Sides ToStockSharp(this BtcTurkSides side)
		=> side == BtcTurkSides.Buy ? Sides.Buy : Sides.Sell;

	public static BtcTurkSides ToBtcTurk(this Sides side)
		=> side == Sides.Buy ? BtcTurkSides.Buy : BtcTurkSides.Sell;

	public static OrderTypes ToStockSharp(this BtcTurkOrderMethods method)
		=> method switch
		{
			BtcTurkOrderMethods.Market => OrderTypes.Market,
			BtcTurkOrderMethods.StopLimit or BtcTurkOrderMethods.StopMarket =>
				OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToStockSharp(this BtcTurkOrderStatuses status)
		=> status switch
		{
			BtcTurkOrderStatuses.Untouched or
				BtcTurkOrderStatuses.Partial => OrderStates.Active,
			BtcTurkOrderStatuses.Closed or
				BtcTurkOrderStatuses.Canceled or
				BtcTurkOrderStatuses.Expired => OrderStates.Done,
			BtcTurkOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static SecurityStates ToStockSharp(this BtcTurkMarketStatuses status)
		=> status == BtcTurkMarketStatuses.Trading
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string CreateClientOrderId(long transactionId,
		string userOrderId)
	{
		var source = userOrderId.IsEmpty()
			? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
			: userOrderId.Trim();
		var value = new string(source.Where(static character =>
			char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
			.ToArray());
		if (value.IsEmpty())
			value = $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
		return value.Length <= 64 ? value : value[..64];
	}

	public static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-",
			StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;
}

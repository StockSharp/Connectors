namespace StockSharp.Toobit.Native;

static class ToobitExtensions
{
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(7), "1w" },
		{ TimeSpan.FromDays(30), "1M" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame)
			?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan ToTimeFrame(this string interval)
		=> TimeFrames.TryGetKey2(interval)
			?? throw new ArgumentOutOfRangeException(nameof(interval), interval, LocalizedStrings.InvalidValue);

	public static SecurityId ToStockSharp(this string securityCode, string boardCode)
	{
		if (securityCode.IsEmpty())
			throw new ArgumentNullException(nameof(securityCode));
		if (boardCode.IsEmpty())
			throw new ArgumentNullException(nameof(boardCode));

		return new()
		{
			SecurityCode = securityCode.ToUpperInvariant(),
			BoardCode = boardCode,
		};
	}

	public static string ToNative(this SecurityId securityId)
		=> securityId.SecurityCode;

	public static ToobitOrderSides ToNative(this Sides side, bool isClose)
		=> (side, isClose) switch
		{
			(Sides.Buy, false) => ToobitOrderSides.BuyOpen,
			(Sides.Sell, false) => ToobitOrderSides.SellOpen,
			(Sides.Buy, true) => ToobitOrderSides.BuyClose,
			(Sides.Sell, true) => ToobitOrderSides.SellClose,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static ToobitOrderSides ToSpotNative(this Sides side)
		=> side switch
		{
			Sides.Buy => ToobitOrderSides.Buy,
			Sides.Sell => ToobitOrderSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToStockSharp(this ToobitOrderSides side)
		=> side switch
		{
			ToobitOrderSides.Buy or ToobitOrderSides.BuyOpen or ToobitOrderSides.BuyClose => Sides.Buy,
			ToobitOrderSides.Sell or ToobitOrderSides.SellOpen or ToobitOrderSides.SellClose => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static ToobitTimeInForce ToNative(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			null or TimeInForce.PutInQueue => ToobitTimeInForce.Gtc,
			TimeInForce.CancelBalance => ToobitTimeInForce.Ioc,
			TimeInForce.MatchOrCancel => ToobitTimeInForce.Fok,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue),
		};

	public static TimeInForce? ToStockSharp(this ToobitTimeInForce? timeInForce)
		=> timeInForce switch
		{
			null => null,
			ToobitTimeInForce.Gtc => TimeInForce.PutInQueue,
			ToobitTimeInForce.Ioc => TimeInForce.CancelBalance,
			ToobitTimeInForce.Fok => TimeInForce.MatchOrCancel,
			ToobitTimeInForce.PostOnly => TimeInForce.PutInQueue,
			_ => null,
		};

	public static OrderStates ToStockSharp(this ToobitOrderStatuses? status)
		=> status switch
		{
			ToobitOrderStatuses.PendingNew or ToobitOrderStatuses.New or ToobitOrderStatuses.PartiallyFilled or ToobitOrderStatuses.PendingCancel or
			ToobitOrderStatuses.OrderNew => OrderStates.Active,
			ToobitOrderStatuses.Filled or ToobitOrderStatuses.Canceled or ToobitOrderStatuses.OrderFilled or
			ToobitOrderStatuses.OrderCanceled or ToobitOrderStatuses.Expired => OrderStates.Done,
			ToobitOrderStatuses.Rejected or ToobitOrderStatuses.OrderRejected or ToobitOrderStatuses.OrderFailed or
			ToobitOrderStatuses.OrderNotEffective => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static OrderStates ToStockSharp(this ToobitOrderStatuses status)
		=> ((ToobitOrderStatuses?)status).ToStockSharp();

	public static long? ToLongId(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	public static DateTime? ToUtcTime(this string value)
		=> value.ToLongId() is long unix && unix > 0 ? unix.FromUnix(false) : null;

	public static DateTime ToUtcTime(this long unix)
		=> unix.FromUnix(false);

	public static long ToUnixMilliseconds(this DateTime time)
		=> (long)time.ToUniversalTime().ToUnix(false);

	public static long ParseVersion(string version)
	{
		if (version.IsEmpty())
			return 0;

		var separator = version.IndexOf('_');
		var number = separator < 0 ? version : version[..separator];
		return number.ToLongId() ?? 0;
	}

	public static string CreateClientOrderId(long transactionId, string explicitId)
	{
		var result = explicitId.IsEmpty() ? $"ss-{transactionId}" : explicitId;
		if (result.Length > 36)
			result = result[..36];

		if (result.Any(static c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')))
			throw new ArgumentException("Client order ID may contain only letters, digits, underscore, dash, and dot.", nameof(explicitId));

		return result;
	}

	public static long? ExtractTransactionId(string clientOrderId)
	{
		if (clientOrderId.IsEmpty())
			return null;

		var separator = clientOrderId.LastIndexOf('-');
		return (separator < 0 ? clientOrderId : clientOrderId[(separator + 1)..]).ToLongId();
	}
}

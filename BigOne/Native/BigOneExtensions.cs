namespace StockSharp.BigOne.Native;

static class BigOneExtensions
{
	private static readonly PairSet<TimeSpan, string> _spotPeriods = new()
	{
		{ TimeSpan.FromMinutes(1), "min1" },
		{ TimeSpan.FromMinutes(5), "min5" },
		{ TimeSpan.FromMinutes(15), "min15" },
		{ TimeSpan.FromMinutes(30), "min30" },
		{ TimeSpan.FromHours(1), "hour1" },
		{ TimeSpan.FromHours(3), "hour3" },
		{ TimeSpan.FromHours(4), "hour4" },
		{ TimeSpan.FromHours(6), "hour6" },
		{ TimeSpan.FromHours(12), "hour12" },
		{ TimeSpan.FromDays(1), "day1" },
		{ TimeSpan.FromDays(7), "week1" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "month1" },
	};

	private static readonly Dictionary<TimeSpan, string>
		_contractPeriods = new()
		{
			[TimeSpan.FromMinutes(1)] = "1MIN",
			[TimeSpan.FromMinutes(5)] = "5MIN",
			[TimeSpan.FromMinutes(15)] = "15MIN",
			[TimeSpan.FromMinutes(30)] = "30MIN",
			[TimeSpan.FromHours(1)] = "1H",
			[TimeSpan.FromHours(4)] = "4H",
			[TimeSpan.FromHours(6)] = "6H",
			[TimeSpan.FromHours(12)] = "12H",
			[TimeSpan.FromDays(1)] = "1D",
		};

	public static IEnumerable<TimeSpan> TimeFrames => _spotPeriods.Keys;

	public static string ToBigOneResolution(this TimeSpan timeFrame)
		=> timeFrame.ToBigOneSpotPeriod();

	public static string ToBigOneInterval(this TimeSpan timeFrame)
		=> timeFrame.ToBigOneSpotPeriod();

	public static string ToBigOneSpotPeriod(this TimeSpan timeFrame)
		=> _spotPeriods.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(
				nameof(timeFrame), timeFrame,
				"Unsupported BigONE spot candle period.");

	public static string ToBigOneSpotStreamPeriod(
		this TimeSpan timeFrame)
		=> timeFrame.ToBigOneSpotPeriod().ToUpperInvariant();

	public static string ToBigOneContractPeriod(
		this TimeSpan timeFrame)
		=> _contractPeriods.TryGetValue(timeFrame, out var period)
			? period
			: throw new ArgumentOutOfRangeException(
				nameof(timeFrame), timeFrame,
				"Unsupported BigONE contract candle period.");

	public static bool IsContractPeriodSupported(
		this TimeSpan timeFrame)
		=> _contractPeriods.ContainsKey(timeFrame);

	public static string CreateSecurityCode(
		string baseCurrency, string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency)).Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency)).Trim()
				.ToUpperInvariant();

	public static string ToBigOneSecurityCode(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim()
			.ToUpperInvariant();
		if (!value.Contains('/') &&
			!value.Contains('_') &&
			!value.Contains('-'))
			return value;
		var parts = SplitSecurityCode(value);
		return CreateSecurityCode(parts[0], parts[1]);
	}

	public static string ToBigOneAccountSymbol(
		this string securityCode)
		=> ToBigOneSpotSymbol(securityCode);

	public static string ToBigOneMarketSymbol(
		this string securityCode)
		=> ToBigOneSpotSymbol(securityCode);

	public static string ToBigOneSpotSymbol(
		this string securityCode)
	{
		var parts = SplitSecurityCode(securityCode);
		return $"{parts[0]}-{parts[1]}".ToUpperInvariant();
	}

	public static SecurityId ToBigOneSecurityId(
		this string securityCode)
		=> new()
		{
			SecurityCode = securityCode.ToBigOneSecurityCode(),
			BoardCode = BoardCodes.BigOne,
		};

	public static SecurityId ToStockSharp(
		this BigOneSymbol symbol)
		=> new()
		{
			SecurityCode = symbol?.SecurityCode ??
				throw new ArgumentNullException(nameof(symbol)),
			BoardCode = BoardCodes.BigOne,
		};

	public static DateTime FromBigOneMilliseconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(
				NormalizeTimestamp(timestamp)),
			DateTimeKind.Utc);

	public static DateTime FromBigOneSeconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddSeconds(timestamp),
			DateTimeKind.Utc);

	public static long ToBigOneMilliseconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch)
			.TotalMilliseconds;

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(
				value, DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};

	public static long NormalizeTimestamp(long value)
	{
		if (value <= 0)
			return 0;
		while (value > 99_999_999_999_999)
			value /= 1000;
		if (value > 99_999_999_999)
			return value;
		if (value > 999_999_999)
			return value * 1000;
		return value;
	}

	public static decimal? GetStep(int precision)
	{
		if (precision is < 0 or > 28)
			return null;
		var step = 1m;
		for (var index = 0; index < precision; index++)
			step /= 10m;
		return step;
	}

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static string ToBigOne(this Sides side)
		=> side switch
		{
			Sides.Buy => "BID",
			Sides.Sell => "ASK",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static string ToBigOneContract(this Sides side)
		=> side switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string side)
		=> side?.Trim().ToUpperInvariant() switch
		{
			"BUY" or "BID" => Sides.Buy,
			"SELL" or "ASK" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this BigOneOrder order)
		=> order?.Type?.Trim().ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOP_LIMIT" or "STOP_MARKET" or "UNTRIGGERED" =>
				OrderTypes.Conditional,
			_ => order?.StopPrice is > 0
				? OrderTypes.Conditional
				: OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.Trim().ToUpperInvariant() switch
		{
			"PENDING" or "OPENING" or "NEW" or
				"PARTIALLY_FILLED" or "UNTRIGGERED" or
				"PENDING_CANCEL" or "FIRED" => OrderStates.Active,
			"FILLED" or "CANCELLED" or "CANCELED" or
				"PARTIALLY_CANCELED" or "PARTIALLY_CANCELLED" =>
				OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string CreateClientId(long transactionId)
		=> "ss-" + transactionId.ToString(
			CultureInfo.InvariantCulture);

	private static string[] SplitSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var parts = value.Split(['/', '_', '-'],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Invalid BigONE security code '{value}'.");
		return parts;
	}
}

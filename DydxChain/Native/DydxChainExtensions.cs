namespace StockSharp.DydxChain.Native;

static class DydxChainExtensions
{
	public const string ChainId = "dydx-mainnet-1";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	public static string NormalizeAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var decoded = DydxChainSigner.DecodeAddress(value);
		if (!decoded.Prefix.Equals("dydx", StringComparison.Ordinal))
			throw new FormatException(
				$"dYdX address '{value}' must use the dydx prefix.");
		if (decoded.Data.Length != 20)
			throw new FormatException(
				$"dYdX address '{value}' has an invalid payload length.");
		return value.ToLowerInvariant();
	}

	public static string NormalizeTicker(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 128 || value.Any(static ch =>
			!char.IsAsciiLetterOrDigit(ch) &&
			ch is not ('-' or '_' or '.' or ',')))
			throw new FormatException($"Invalid dYdX ticker '{value}'.");
		return value;
	}

	public static SecurityId ToStockSharp(this string ticker)
		=> new()
		{
			SecurityCode = ticker.NormalizeTicker(),
			BoardCode = BoardCodes.DydxChain,
		};

	public static decimal ParseDecimal(this string value, string field,
		bool isNonNegative = false)
	{
		if (value.IsEmpty() || !decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ||
			isNonNegative && result < 0)
			throw new InvalidDataException(
				$"dYdX returned invalid {field} '{value}'.");
		return result;
	}

	public static decimal? TryParseDecimal(this string value)
		=> !value.IsEmpty() && decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static uint ParseUInt32(this string value, string field)
	{
		if (!uint.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"dYdX returned invalid {field} '{value}'.");
		return result;
	}

	public static ulong ParseUInt64(this string value, string field)
	{
		if (!ulong.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"dYdX returned invalid {field} '{value}'.");
		return result;
	}

	public static DateTime ParseUtcTime(this string value, string field)
	{
		if (value.IsEmpty() || !DateTime.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException(
				$"dYdX returned invalid {field} '{value}'.");
		return DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static uint ToUnixSeconds32(this DateTime value)
	{
		value = value.EnsureUtc();
		var seconds = checked((long)(value - DateTime.UnixEpoch).TotalSeconds);
		return checked((uint)seconds);
	}

	public static DydxChainCandleResolutions ToDydxChain(
		this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => DydxChainCandleResolutions.OneMinute,
			{ TotalMinutes: 5 } => DydxChainCandleResolutions.FiveMinutes,
			{ TotalMinutes: 15 } => DydxChainCandleResolutions.FifteenMinutes,
			{ TotalMinutes: 30 } => DydxChainCandleResolutions.ThirtyMinutes,
			{ TotalHours: 1 } => DydxChainCandleResolutions.OneHour,
			{ TotalHours: 4 } => DydxChainCandleResolutions.FourHours,
			{ TotalDays: 1 } => DydxChainCandleResolutions.OneDay,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Unsupported dYdX candle time frame."),
		};

	public static TimeSpan ToStockSharp(
		this DydxChainCandleResolutions resolution)
		=> resolution switch
		{
			DydxChainCandleResolutions.OneMinute => TimeSpan.FromMinutes(1),
			DydxChainCandleResolutions.FiveMinutes => TimeSpan.FromMinutes(5),
			DydxChainCandleResolutions.FifteenMinutes =>
				TimeSpan.FromMinutes(15),
			DydxChainCandleResolutions.ThirtyMinutes =>
				TimeSpan.FromMinutes(30),
			DydxChainCandleResolutions.OneHour => TimeSpan.FromHours(1),
			DydxChainCandleResolutions.FourHours => TimeSpan.FromHours(4),
			DydxChainCandleResolutions.OneDay => TimeSpan.FromDays(1),
			_ => throw new ArgumentOutOfRangeException(nameof(resolution),
				resolution, "Unsupported dYdX candle resolution."),
		};

	public static string ToWire(this DydxChainCandleResolutions resolution)
		=> resolution switch
		{
			DydxChainCandleResolutions.OneMinute => "1MIN",
			DydxChainCandleResolutions.FiveMinutes => "5MINS",
			DydxChainCandleResolutions.FifteenMinutes => "15MINS",
			DydxChainCandleResolutions.ThirtyMinutes => "30MINS",
			DydxChainCandleResolutions.OneHour => "1HOUR",
			DydxChainCandleResolutions.FourHours => "4HOURS",
			DydxChainCandleResolutions.OneDay => "1DAY",
			_ => throw new ArgumentOutOfRangeException(nameof(resolution)),
		};

	public static Sides ToStockSharp(this DydxChainOrderSides side)
		=> side switch
		{
			DydxChainOrderSides.Buy => Sides.Buy,
			DydxChainOrderSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side)),
		};

	public static DydxChainProtoSides ToDydxChain(this Sides side)
		=> side switch
		{
			Sides.Buy => DydxChainProtoSides.Buy,
			Sides.Sell => DydxChainProtoSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side)),
		};

	public static OrderTypes ToStockSharp(this DydxChainOrderTypes type)
		=> type switch
		{
			DydxChainOrderTypes.Limit => OrderTypes.Limit,
			DydxChainOrderTypes.Market => OrderTypes.Market,
			_ => OrderTypes.Conditional,
		};

	public static OrderStates ToStockSharp(this DydxChainOrderStatuses status)
		=> status switch
		{
			DydxChainOrderStatuses.BestEffortOpened or
			DydxChainOrderStatuses.Open or
			DydxChainOrderStatuses.Untriggered => OrderStates.Active,
			DydxChainOrderStatuses.Filled or
			DydxChainOrderStatuses.BestEffortCanceled or
			DydxChainOrderStatuses.Canceled => OrderStates.Done,
			DydxChainOrderStatuses.Error => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(status)),
		};

	public static TimeInForce ToStockSharp(
		this DydxChainTimeInForces? timeInForce)
		=> timeInForce switch
		{
			DydxChainTimeInForces.ImmediateOrCancel =>
				TimeInForce.CancelBalance,
			DydxChainTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static DydxChainProtoTimeInForces ToDydxChain(
		this TimeInForce? timeInForce, bool isPostOnly, OrderTypes orderType)
	{
		if (isPostOnly)
		{
			if (orderType != OrderTypes.Limit)
				throw new NotSupportedException(
					"dYdX post-only orders must be limit orders.");
			return DydxChainProtoTimeInForces.PostOnly;
		}
		return timeInForce switch
		{
			TimeInForce.CancelBalance =>
				DydxChainProtoTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel =>
				DydxChainProtoTimeInForces.FillOrKill,
			TimeInForce.PutInQueue or null when orderType == OrderTypes.Market =>
				DydxChainProtoTimeInForces.ImmediateOrCancel,
			TimeInForce.PutInQueue or null =>
				DydxChainProtoTimeInForces.Unspecified,
			_ => throw new NotSupportedException(
				$"dYdX does not support time in force '{timeInForce}'."),
		};
	}

	public static ulong ToQuantums(this decimal size, DydxChainMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (size <= 0 || market.StepBaseQuantums <= 0)
			throw new ArgumentOutOfRangeException(nameof(size));
		var raw = size * Pow10(-market.AtomicResolution);
		var step = (decimal)market.StepBaseQuantums;
		var rounded = decimal.Floor(raw / step) * step;
		if (rounded < step)
			rounded = step;
		return ToUInt64(rounded, "order size quantums");
	}

	public static ulong ToSubticks(this decimal price, DydxChainMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (price <= 0 || market.SubticksPerTick <= 0)
			throw new ArgumentOutOfRangeException(nameof(price));
		var exponent = checked(market.AtomicResolution -
			market.QuantumConversionExponent + 6);
		var raw = price * Pow10(exponent);
		var step = (decimal)market.SubticksPerTick;
		var rounded = decimal.Floor(raw / step) * step;
		if (rounded < step)
			rounded = step;
		return ToUInt64(rounded, "order price subticks");
	}

	public static CurrencyTypes? ToCurrency(this string ticker)
	{
		var quote = ticker?.Split('-').LastOrDefault()?.ToUpperInvariant();
		return quote switch
		{
			"USD" or "USDC" or "USDT" => CurrencyTypes.USD,
			"EUR" => CurrencyTypes.EUR,
			"GBP" => CurrencyTypes.GBP,
			"JPY" => CurrencyTypes.JPY,
			_ => null,
		};
	}

	private static decimal Pow10(int exponent)
	{
		if (exponent is < -28 or > 28)
			throw new OverflowException(
				$"dYdX decimal exponent '{exponent}' is outside decimal range.");
		var result = 1m;
		if (exponent >= 0)
			for (var index = 0; index < exponent; index++)
				result *= 10m;
		else
			for (var index = 0; index > exponent; index--)
				result /= 10m;
		return result;
	}

	private static ulong ToUInt64(decimal value, string field)
	{
		if (value <= 0 || value > ulong.MaxValue || value != decimal.Truncate(value))
			throw new OverflowException(
				$"dYdX {field} '{value}' is outside uint64 range.");
		return decimal.ToUInt64(value);
	}
}

namespace StockSharp.Grvt.Native;

static class GrvtExtensions
{
	public static readonly PairSet<TimeSpan, GrvtCandlestickIntervals>
		TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), GrvtCandlestickIntervals.Minute1 },
		{ TimeSpan.FromMinutes(3), GrvtCandlestickIntervals.Minute3 },
		{ TimeSpan.FromMinutes(5), GrvtCandlestickIntervals.Minute5 },
		{ TimeSpan.FromMinutes(15), GrvtCandlestickIntervals.Minute15 },
		{ TimeSpan.FromMinutes(30), GrvtCandlestickIntervals.Minute30 },
		{ TimeSpan.FromHours(1), GrvtCandlestickIntervals.Hour1 },
		{ TimeSpan.FromHours(2), GrvtCandlestickIntervals.Hour2 },
		{ TimeSpan.FromHours(4), GrvtCandlestickIntervals.Hour4 },
		{ TimeSpan.FromHours(6), GrvtCandlestickIntervals.Hour6 },
		{ TimeSpan.FromHours(8), GrvtCandlestickIntervals.Hour8 },
		{ TimeSpan.FromHours(12), GrvtCandlestickIntervals.Hour12 },
		{ TimeSpan.FromDays(1), GrvtCandlestickIntervals.Day1 },
		{ TimeSpan.FromDays(3), GrvtCandlestickIntervals.Day3 },
		{ TimeSpan.FromDays(5), GrvtCandlestickIntervals.Day5 },
		{ TimeSpan.FromDays(7), GrvtCandlestickIntervals.Week1 },
		{ TimeSpan.FromDays(14), GrvtCandlestickIntervals.Week2 },
		{ TimeSpan.FromDays(21), GrvtCandlestickIntervals.Week3 },
		{ TimeSpan.FromDays(28), GrvtCandlestickIntervals.Week4 },
	};

	public static GrvtCandlestickIntervals ToGrvtInterval(
		this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var interval)
			? interval
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"GRVT does not support this candle interval.");

	public static string ToWire(this GrvtCandlestickIntervals interval)
		=> interval switch
		{
			GrvtCandlestickIntervals.Minute1 => "CI_1_M",
			GrvtCandlestickIntervals.Minute3 => "CI_3_M",
			GrvtCandlestickIntervals.Minute5 => "CI_5_M",
			GrvtCandlestickIntervals.Minute15 => "CI_15_M",
			GrvtCandlestickIntervals.Minute30 => "CI_30_M",
			GrvtCandlestickIntervals.Hour1 => "CI_1_H",
			GrvtCandlestickIntervals.Hour2 => "CI_2_H",
			GrvtCandlestickIntervals.Hour4 => "CI_4_H",
			GrvtCandlestickIntervals.Hour6 => "CI_6_H",
			GrvtCandlestickIntervals.Hour8 => "CI_8_H",
			GrvtCandlestickIntervals.Hour12 => "CI_12_H",
			GrvtCandlestickIntervals.Day1 => "CI_1_D",
			GrvtCandlestickIntervals.Day3 => "CI_3_D",
			GrvtCandlestickIntervals.Day5 => "CI_5_D",
			GrvtCandlestickIntervals.Week1 => "CI_1_W",
			GrvtCandlestickIntervals.Week2 => "CI_2_W",
			GrvtCandlestickIntervals.Week3 => "CI_3_W",
			GrvtCandlestickIntervals.Week4 => "CI_4_W",
			_ => throw new ArgumentOutOfRangeException(nameof(interval), interval,
				"Unknown GRVT candle interval."),
		};

	public static SecurityId ToStockSharp(this string instrument)
		=> new()
		{
			SecurityCode = instrument.ThrowIfEmpty(nameof(instrument))
				.ToUpperInvariant(),
			BoardCode = BoardCodes.Grvt,
		};

	public static SecurityTypes ToSecurityType(this GrvtInstrumentKinds kind)
		=> kind switch
		{
			GrvtInstrumentKinds.Perpetual or GrvtInstrumentKinds.Future =>
				SecurityTypes.Future,
			GrvtInstrumentKinds.Call or GrvtInstrumentKinds.Put =>
				SecurityTypes.Option,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
				"Unknown GRVT instrument kind."),
		};

	public static OptionTypes? ToOptionType(this GrvtInstrumentKinds kind)
		=> kind switch
		{
			GrvtInstrumentKinds.Call => OptionTypes.Call,
			GrvtInstrumentKinds.Put => OptionTypes.Put,
			_ => null,
		};

	public static DateTime? TryGetExpiry(this GrvtInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		if (instrument.Kind == GrvtInstrumentKinds.Perpetual)
			return null;
		var parts = instrument.Instrument?.Split('_',
			StringSplitOptions.RemoveEmptyEntries) ?? [];
		var index = instrument.Kind is GrvtInstrumentKinds.Call or
			GrvtInstrumentKinds.Put
			? parts.Length - 2
			: parts.Length - 1;
		if (index < 0 || !DateTime.TryParseExact(parts[index], "ddMMMyy",
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var expiry))
			return null;
		return DateTime.SpecifyKind(expiry, DateTimeKind.Utc);
	}

	public static decimal? TryGetStrike(this GrvtInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		if (instrument.Kind is not (GrvtInstrumentKinds.Call or
			GrvtInstrumentKinds.Put))
			return null;
		var part = instrument.Instrument?.Split('_',
			StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
		return part.ToDecimal();
	}

	public static decimal? ToDecimal(this string value)
		=> !value.IsEmpty() && decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var number)
			? number
			: null;

	public static decimal ParseRequiredDecimal(this string value, string field)
		=> value.ToDecimal() is decimal number
			? number
			: throw new InvalidDataException(
				$"GRVT returned an invalid {field} value '{value}'.");

	public static CurrencyTypes? ToCurrency(this string value)
	{
		if (value.IsEmpty())
			return null;

		return value.ToUpperInvariant() switch
		{
			"USDC" or "USDT" => CurrencyTypes.USD,
			_ => System.Enum.TryParse<CurrencyTypes>(value, true,
				out var currency)
				? currency
				: null,
		};
	}

	public static string ToWire(this decimal value)
		=> value.ToString("0.############################",
			CultureInfo.InvariantCulture);

	public static DateTime ToGrvtTime(this string nanoseconds)
	{
		if (!BigInteger.TryParse(nanoseconds, NumberStyles.None,
			CultureInfo.InvariantCulture, out var value) || value < 0)
			throw new InvalidDataException(
				$"GRVT timestamp '{nanoseconds}' is invalid.");
		var ticks = value / 100;
		if (ticks > long.MaxValue)
			throw new InvalidDataException(
				$"GRVT timestamp '{nanoseconds}' is out of range.");
		try
		{
			return DateTime.SpecifyKind(
				DateTime.UnixEpoch.AddTicks((long)ticks), DateTimeKind.Utc);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				$"GRVT timestamp '{nanoseconds}' is out of range.", error);
		}
	}

	public static long ToUnixNanoseconds(this DateTime time)
	{
		if (time.Kind != DateTimeKind.Utc)
			time = time.ToUniversalTime();
		var ticks = time.Ticks - DateTime.UnixEpoch.Ticks;
		if (ticks < 0 || ticks > long.MaxValue / 100)
			throw new ArgumentOutOfRangeException(nameof(time));
		return ticks * 100;
	}

	public static string ToGrvtNanoseconds(this DateTime time)
		=> time.ToUnixNanoseconds().ToString(CultureInfo.InvariantCulture);

	public static GrvtTimeInForces ToGrvt(this TimeInForce? timeInForce,
		bool isMarket)
		=> timeInForce switch
		{
			TimeInForce.CancelBalance => GrvtTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => GrvtTimeInForces.FillOrKill,
			_ when isMarket => GrvtTimeInForces.ImmediateOrCancel,
			_ => GrvtTimeInForces.GoodTillTime,
		};

	public static TimeInForce ToStockSharp(this GrvtTimeInForces timeInForce)
		=> timeInForce switch
		{
			GrvtTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			GrvtTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToStockSharp(this GrvtOrderStatuses status)
		=> status switch
		{
			GrvtOrderStatuses.Pending or GrvtOrderStatuses.Open =>
				OrderStates.Active,
			GrvtOrderStatuses.Filled or GrvtOrderStatuses.Cancelled =>
				OrderStates.Done,
			GrvtOrderStatuses.Rejected => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status,
				"Unknown GRVT order status."),
		};

	public static GrvtTriggerTypesNative ToNative(this GrvtTriggerTypes type)
		=> type switch
		{
			GrvtTriggerTypes.None => GrvtTriggerTypesNative.Unspecified,
			GrvtTriggerTypes.TakeProfit => GrvtTriggerTypesNative.TakeProfit,
			GrvtTriggerTypes.StopLoss => GrvtTriggerTypesNative.StopLoss,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type,
				"Unknown GRVT trigger type."),
		};

	public static GrvtTriggerPricesNative ToNative(this GrvtTriggerPrices price)
		=> price switch
		{
			GrvtTriggerPrices.Index => GrvtTriggerPricesNative.Index,
			GrvtTriggerPrices.Last => GrvtTriggerPricesNative.Last,
			GrvtTriggerPrices.Mid => GrvtTriggerPricesNative.Mid,
			GrvtTriggerPrices.Mark => GrvtTriggerPricesNative.Mark,
			_ => throw new ArgumentOutOfRangeException(nameof(price), price,
				"Unknown GRVT trigger price source."),
		};

	public static GrvtTriggerTypes ToStockSharp(
		this GrvtTriggerTypesNative type)
		=> type switch
		{
			GrvtTriggerTypesNative.Unspecified => GrvtTriggerTypes.None,
			GrvtTriggerTypesNative.TakeProfit => GrvtTriggerTypes.TakeProfit,
			GrvtTriggerTypesNative.StopLoss => GrvtTriggerTypes.StopLoss,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type,
				"Unknown GRVT trigger type."),
		};

	public static GrvtTriggerPrices ToStockSharp(
		this GrvtTriggerPricesNative price)
		=> price switch
		{
			GrvtTriggerPricesNative.Index => GrvtTriggerPrices.Index,
			GrvtTriggerPricesNative.Last => GrvtTriggerPrices.Last,
			GrvtTriggerPricesNative.Mid => GrvtTriggerPrices.Mid,
			GrvtTriggerPricesNative.Mark => GrvtTriggerPrices.Mark,
			_ => GrvtTriggerPrices.Mark,
		};

	public static string CreateClientOrderId(long transactionId,
		string userOrderId)
	{
		const ulong clientRange = 1UL << 63;
		if (!userOrderId.IsEmpty())
		{
			if (!ulong.TryParse(userOrderId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var parsed) ||
				parsed < clientRange)
				throw new InvalidOperationException(
					"A GRVT user order ID must be an unsigned integer in " +
					"the client range [2^63, 2^64-1].");
			return parsed.ToString(CultureInfo.InvariantCulture);
		}
		if (transactionId <= 0)
			throw new InvalidOperationException(
				"GRVT requires a positive transaction ID.");
		return (clientRange | (ulong)transactionId)
			.ToString(CultureInfo.InvariantCulture);
	}

	public static long ToTransactionId(this string clientOrderId)
	{
		if (!ulong.TryParse(clientOrderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var value))
			return 0;
		return (long)(value & long.MaxValue);
	}
}

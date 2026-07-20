namespace StockSharp.ApexOmni.Native;

static class ApexOmniExtensions
{
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1" },
		{ TimeSpan.FromMinutes(3), "3" },
		{ TimeSpan.FromMinutes(5), "5" },
		{ TimeSpan.FromMinutes(15), "15" },
		{ TimeSpan.FromMinutes(30), "30" },
		{ TimeSpan.FromHours(1), "60" },
		{ TimeSpan.FromHours(2), "120" },
		{ TimeSpan.FromHours(4), "240" },
		{ TimeSpan.FromHours(6), "360" },
		{ TimeSpan.FromHours(12), "720" },
		{ TimeSpan.FromDays(1), "D" },
		{ TimeSpan.FromDays(7), "W" },
		{ TimeSpan.FromDays(30), "M" },
	};

	public static string ToApexOmniInterval(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var interval)
			? interval
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"ApeX Omni does not support this candle interval.");

	public static SecurityId ToStockSharp(this ApexOmniContract instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		return new()
		{
			SecurityCode = instrument.Symbol.ThrowIfEmpty(nameof(instrument.Symbol))
				.ToUpperInvariant(),
			BoardCode = BoardCodes.ApexOmni,
		};
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
				$"ApeX Omni returned an invalid {field} value '{value}'.");

	public static string ToWire(this decimal value)
		=> value.ToString("0.############################",
			CultureInfo.InvariantCulture);

	public static CurrencyTypes? ToCurrency(this string value)
	{
		if (value.IsEmpty())
			return null;
		return value.ToUpperInvariant() switch
		{
			"USDC" or "USDT" => CurrencyTypes.USD,
			_ => Enum.TryParse<CurrencyTypes>(value, true,
				out var currency) ? currency : null,
		};
	}

	public static DateTime ToApexOmniTime(this long value)
	{
		try
		{
			if (value >= 100_000_000_000_000L)
				return DateTime.SpecifyKind(DateTime.UnixEpoch.AddTicks(value * 10),
					DateTimeKind.Utc);
			if (value >= 100_000_000_000L)
				return DateTime.SpecifyKind(DateTime.UnixEpoch.AddMilliseconds(value),
					DateTimeKind.Utc);
			return DateTime.SpecifyKind(DateTime.UnixEpoch.AddSeconds(value),
				DateTimeKind.Utc);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				$"ApeX Omni timestamp '{value}' is out of range.", error);
		}
	}

	public static long ToUnixMilliseconds(this DateTime time)
	{
		if (time.Kind != DateTimeKind.Utc)
			time = time.ToUniversalTime();
		return (time.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
	}

	public static ApexOmniNativeSides ToApexOmni(this Sides side)
		=> side switch
		{
			Sides.Buy => ApexOmniNativeSides.Buy,
			Sides.Sell => ApexOmniNativeSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static Sides ToStockSharp(this ApexOmniNativeSides side)
		=> side switch
		{
			ApexOmniNativeSides.Buy => Sides.Buy,
			ApexOmniNativeSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static Sides ToStockSharp(this ApexOmniPositionSides side)
		=> side switch
		{
			ApexOmniPositionSides.Long => Sides.Buy,
			ApexOmniPositionSides.Short => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static ApexOmniTimeInForces ToApexOmni(this TimeInForce? value,
		bool isPostOnly, bool isMarket)
		=> isPostOnly
			? ApexOmniTimeInForces.PostOnly
			: value switch
			{
				TimeInForce.CancelBalance =>
					ApexOmniTimeInForces.ImmediateOrCancel,
				TimeInForce.MatchOrCancel => ApexOmniTimeInForces.FillOrKill,
				_ when isMarket => ApexOmniTimeInForces.ImmediateOrCancel,
				_ => ApexOmniTimeInForces.GoodTilCancel,
			};

	public static TimeInForce ToStockSharp(this ApexOmniTimeInForces value)
		=> value switch
		{
			ApexOmniTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			ApexOmniTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToStockSharp(this ApexOmniOrderStatuses status)
		=> status switch
		{
			ApexOmniOrderStatuses.Pending or ApexOmniOrderStatuses.Open or
			ApexOmniOrderStatuses.Untriggered => OrderStates.Active,
			ApexOmniOrderStatuses.Filled or ApexOmniOrderStatuses.Canceled or
			ApexOmniOrderStatuses.Expired => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status,
				"Unknown ApeX Omni order status."),
		};

	public static ApexOmniTriggerPriceTypes ToNative(
		this ApexOmniTriggerPrices value)
		=> value switch
		{
			ApexOmniTriggerPrices.Market => ApexOmniTriggerPriceTypes.Market,
			ApexOmniTriggerPrices.Index => ApexOmniTriggerPriceTypes.Index,
			ApexOmniTriggerPrices.Oracle => ApexOmniTriggerPriceTypes.Oracle,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static ApexOmniTriggerPrices ToStockSharp(
		this ApexOmniTriggerPriceTypes value)
		=> value switch
		{
			ApexOmniTriggerPriceTypes.Market => ApexOmniTriggerPrices.Market,
			ApexOmniTriggerPriceTypes.Index => ApexOmniTriggerPrices.Index,
			ApexOmniTriggerPriceTypes.Oracle => ApexOmniTriggerPrices.Oracle,
			_ => ApexOmniTriggerPrices.Market,
		};

	public static long ToTransactionId(this string clientId)
		=> long.TryParse(clientId, NumberStyles.None, CultureInfo.InvariantCulture,
			out var value) && value > 0 ? value : 0;
}

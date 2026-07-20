namespace StockSharp.StandX.Native;

static class StandXExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromSeconds(3),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static string ToWire(this StandXChains chain)
		=> chain switch
		{
			StandXChains.Bsc => "bsc",
			StandXChains.Solana => "solana",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported StandX wallet chain."),
		};

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).Trim()
				.ToUpperInvariant(),
			BoardCode = BoardCodes.StandX,
		};

	public static SecurityId ToCurrencySecurity(this string currency)
		=> new()
		{
			SecurityCode = currency.ThrowIfEmpty(nameof(currency)).Trim()
				.ToUpperInvariant(),
			BoardCode = BoardCodes.StandX,
		};

	public static decimal? ToDecimal(this string value)
		=> !value.IsEmpty() && decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var number)
				? number
				: null;

	public static decimal ParseRequiredDecimal(this string value, string field)
		=> value.ToDecimal() ?? throw new InvalidDataException(
			$"StandX returned an invalid {field}.");

	public static string ToWire(this decimal value)
		=> value.ToString("0.############################",
			CultureInfo.InvariantCulture);

	public static DateTime? ToStandXTime(this string value)
	{
		if (value.IsEmpty() || !DateTime.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var time))
			return null;
		return DateTime.SpecifyKind(time, DateTimeKind.Utc);
	}

	public static DateTime ToStandXTime(this long milliseconds)
	{
		try
		{
			return DateTime.SpecifyKind(
				DateTime.UnixEpoch.AddMilliseconds(milliseconds),
				DateTimeKind.Utc);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"StandX returned a timestamp outside the UTC range.", error);
		}
	}

	public static long ToStandXMilliseconds(this DateTime time)
	{
		if (time.Kind != DateTimeKind.Utc)
			time = time.ToUniversalTime();
		return checked((time.Ticks - DateTime.UnixEpoch.Ticks) /
			TimeSpan.TicksPerMillisecond);
	}

	public static long ToStandXUnixSeconds(this DateTime time)
	{
		if (time.Kind != DateTimeKind.Utc)
			time = time.ToUniversalTime();
		return checked((time.Ticks - DateTime.UnixEpoch.Ticks) /
			TimeSpan.TicksPerSecond);
	}

	public static DateTime ToStandXSeconds(this long seconds)
	{
		try
		{
			return DateTime.SpecifyKind(
				DateTime.UnixEpoch.AddSeconds(seconds), DateTimeKind.Utc);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"StandX returned a timestamp outside the UTC range.", error);
		}
	}

	public static decimal GetStep(int decimals, string field)
	{
		if (decimals is < 0 or > 18)
			throw new InvalidDataException(
				$"StandX returned an invalid {field} precision '{decimals}'.");
		var step = 1m;
		for (var index = 0; index < decimals; index++)
			step /= 10m;
		return step;
	}

	public static string ToStandXResolution(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ Ticks: 30_000_000 } => "3S",
			{ Ticks: 600_000_000 } => "1",
			{ Ticks: 3_000_000_000 } => "5",
			{ Ticks: 9_000_000_000 } => "15",
			{ Ticks: 36_000_000_000 } => "60",
			{ Ticks: 864_000_000_000 } => "1D",
			{ Ticks: 6_048_000_000_000 } => "1W",
			_ => throw new NotSupportedException(
				$"StandX does not support candle time frame '{timeFrame}'."),
		};

	public static StandXApiSides ToStandX(this Sides side)
		=> side switch
		{
			Sides.Buy => StandXApiSides.Buy,
			Sides.Sell => StandXApiSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported StandX order side."),
		};

	public static Sides ToStockSharp(this StandXApiSides side)
		=> side switch
		{
			StandXApiSides.Buy => Sides.Buy,
			StandXApiSides.Sell => Sides.Sell,
			_ => throw new InvalidDataException(
				$"StandX returned unsupported side '{side}'."),
		};

	public static StandXApiMarginModes ToStandX(
		this StandXMarginModes mode)
		=> mode switch
		{
			StandXMarginModes.Cross => StandXApiMarginModes.Cross,
			StandXMarginModes.Isolated => StandXApiMarginModes.Isolated,
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode,
				"Unsupported StandX margin mode."),
		};

	public static StandXMarginModes ToStockSharp(
		this StandXApiMarginModes mode)
		=> mode switch
		{
			StandXApiMarginModes.Cross => StandXMarginModes.Cross,
			StandXApiMarginModes.Isolated => StandXMarginModes.Isolated,
			_ => throw new InvalidDataException(
				$"StandX returned unsupported margin mode '{mode}'."),
		};

	public static OrderStates ToStockSharp(this StandXOrderStatuses status)
		=> status switch
		{
			StandXOrderStatuses.New or StandXOrderStatuses.Open or
				StandXOrderStatuses.Untriggered => OrderStates.Active,
			StandXOrderStatuses.Canceled or StandXOrderStatuses.Filled =>
				OrderStates.Done,
			StandXOrderStatuses.Rejected => OrderStates.Failed,
			_ => throw new InvalidDataException(
				$"StandX returned unsupported order status '{status}'."),
		};

	public static OrderTypes ToStockSharp(this StandXApiOrderTypes type)
		=> type switch
		{
			StandXApiOrderTypes.Limit => OrderTypes.Limit,
			StandXApiOrderTypes.Market => OrderTypes.Market,
			_ => throw new InvalidDataException(
				$"StandX returned unsupported order type '{type}'."),
		};

	public static TimeInForce? ToStockSharp(this StandXTimeInForces value)
		=> value switch
		{
			StandXTimeInForces.GoodTillCanceled => null,
			StandXTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			StandXTimeInForces.AddLiquidityOnly => null,
			_ => throw new InvalidDataException(
				$"StandX returned unsupported time-in-force '{value}'."),
		};

	public static bool IsTrading(this StandXSymbolInfo symbol)
	{
		ArgumentNullException.ThrowIfNull(symbol);
		if (symbol.IsEnabled == false)
			return false;
		return symbol.Status is null or StandXSymbolStatuses.Trading;
	}

	public static StandXCandle[] ToCandles(this StandXCandleSeries series)
	{
		ArgumentNullException.ThrowIfNull(series);
		if (series.Status.EqualsIgnoreCase("no_data"))
			return [];
		if (!series.Status.EqualsIgnoreCase("ok"))
			throw new InvalidDataException(
				$"StandX candle request returned status '{series.Status}'.");
		var timestamps = series.Timestamps ?? [];
		var count = timestamps.Length;
		if ((series.OpenPrices?.Length ?? -1) != count ||
			(series.HighPrices?.Length ?? -1) != count ||
			(series.LowPrices?.Length ?? -1) != count ||
			(series.ClosePrices?.Length ?? -1) != count ||
			(series.Volumes?.Length ?? -1) != count)
			throw new InvalidDataException(
				"StandX returned candle arrays with inconsistent lengths.");
		var result = new StandXCandle[count];
		for (var index = 0; index < count; index++)
		{
			if (timestamps[index] <= 0)
				throw new InvalidDataException(
					"StandX returned a candle with an invalid timestamp.");
			result[index] = new()
			{
				OpenTime = timestamps[index].ToStandXSeconds(),
				OpenPrice = series.OpenPrices[index],
				HighPrice = series.HighPrices[index],
				LowPrice = series.LowPrices[index],
				ClosePrice = series.ClosePrices[index],
				Volume = series.Volumes[index],
			};
		}
		return result;
	}

	public static string ToBase64UrlPadding(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value))
			.Replace('-', '+').Replace('_', '/');
		return value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
	}

	public static byte[] DecodeBase64Url(this string value, string field)
	{
		try
		{
			return Convert.FromBase64String(value.ToBase64UrlPadding());
		}
		catch (FormatException error)
		{
			throw new InvalidDataException(
				$"StandX returned invalid base64url {field}.", error);
		}
	}
}

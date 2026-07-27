namespace StockSharp.Nubra.Native;

static class NubraExtensions
{
	private const decimal _priceScale = 100m;

	public static decimal ToPrice(this long value)
		=> value / _priceScale;

	public static long ToNativePrice(this decimal value, string parameterName)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(parameterName, value, "Nubra prices cannot be negative.");

		var scaled = value * _priceScale;
		if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				value,
				"Nubra prices must have at most two decimal places and fit Int64 exchange units.");
		}

		return decimal.ToInt64(scaled);
	}

	public static SecurityId ToSecurityId(this NubraInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		return new()
		{
			SecurityCode = instrument.StockName
				.IsEmpty(instrument.NubraName)
				.IsEmpty(instrument.Asset)
				.ThrowIfEmpty(nameof(instrument.StockName)),
			BoardCode = instrument.Exchange
				.ThrowIfEmpty(nameof(instrument.Exchange))
				.ToUpperInvariant(),
			Native = instrument.RefId.ToString(CultureInfo.InvariantCulture),
		};
	}

	public static SecurityTypes ToSecurityType(this NubraInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		var derivative = instrument.DerivativeType?.ToUpperInvariant();
		var asset = instrument.AssetType?.ToUpperInvariant();

		if (derivative is "OPT" or "OPTION" ||
			!instrument.OptionType.IsEmpty() &&
			!instrument.OptionType.EqualsIgnoreCase("N/A"))
			return SecurityTypes.Option;
		if (derivative is "FUT" or "FUTURE")
			return SecurityTypes.Future;
		if (derivative is "INDEX" ||
			asset?.Contains("INDEX", StringComparison.Ordinal) == true &&
			derivative.IsEmpty())
			return SecurityTypes.Index;
		if (asset?.Contains("CURRENCY", StringComparison.Ordinal) == true ||
			asset?.Contains("FOREX", StringComparison.Ordinal) == true)
			return SecurityTypes.Currency;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"CE" or "CALL" => OptionTypes.Call,
			"PE" or "PUT" => OptionTypes.Put,
			_ => null,
		};

	public static DateTime? ToExpiry(this long value)
	{
		if (value <= 0)
			return null;

		return DateTime.TryParseExact(
			value.ToString(CultureInfo.InvariantCulture),
			"yyyyMMdd",
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out var date)
				? DateTime.SpecifyKind(date, DateTimeKind.Utc)
				: null;
	}

	public static DateTime ToNubraTime(this long value, DateTime fallback)
	{
		try
		{
			if (value >= 100_000_000_000_000L)
				return DateTimeOffset.FromUnixTimeMilliseconds(value / 1_000_000L).UtcDateTime;
			if (value >= 100_000_000_000L)
				return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
			if (value >= 1_000_000_000L)
				return DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
		}

		return fallback.Kind == DateTimeKind.Utc ? fallback : fallback.ToUniversalTime();
	}

	public static DateTime? ToNubraTime(this string value)
		=> DateTimeOffset.TryParse(
			value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var time)
				? time.UtcDateTime
				: null;

	public static string ToNative(this NubraProducts value)
		=> value switch
		{
			NubraProducts.Cnc => "CNC",
			NubraProducts.Iday => "IDAY",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static NubraProducts ToProduct(this string value)
		=> value.EqualsIgnoreCase("IDAY")
			? NubraProducts.Iday
			: NubraProducts.Cnc;

	public static string ToNative(this Sides value)
		=> value == Sides.Buy ? "BUY" : "SELL";

	public static Sides ToSide(this string value)
		=> value.EqualsIgnoreCase("BUY") ? Sides.Buy : Sides.Sell;

	public static OrderStates ToOrderState(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"PENDING" or "RECEIVED" or "CREATED" or "TRIGGER_PENDING" => OrderStates.Pending,
			"OPEN" or "ACCEPTED" or "MODIFIED" or "GTT" => OrderStates.Active,
			"EXECUTED" or "FILLED" or "COMPLETE" or "CANCELLED" or "CANCELED" or "EXPIRED" => OrderStates.Done,
			"REJECTED" or "FAILED" or "ERROR" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static OrderTypes ToOrderType(this NubraOrder order)
	{
		ArgumentNullException.ThrowIfNull(order);
		if (order.IntentOrderType?.Contains("STOP", StringComparison.OrdinalIgnoreCase) == true)
			return OrderTypes.Conditional;
		return order.PriceType.EqualsIgnoreCase("MARKET")
			? OrderTypes.Market
			: OrderTypes.Limit;
	}

	public static TimeInForce? ToTimeInForce(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"DAY" => TimeInForce.PutInQueue,
			_ => null,
		};

	public static string ToNativeInterval(this TimeSpan value)
	{
		if (value == TimeSpan.FromSeconds(1))
			return "1s";
		if (value == TimeSpan.FromMinutes(1))
			return "1m";
		if (value == TimeSpan.FromMinutes(2))
			return "2m";
		if (value == TimeSpan.FromMinutes(3))
			return "3m";
		if (value == TimeSpan.FromMinutes(5))
			return "5m";
		if (value == TimeSpan.FromMinutes(15))
			return "15m";
		if (value == TimeSpan.FromMinutes(30))
			return "30m";
		if (value == TimeSpan.FromHours(1))
			return "1h";
		if (value == TimeSpan.FromDays(1))
			return "1d";
		if (value == TimeSpan.FromDays(7))
			return "1w";
		if (value == TimeSpan.FromDays(30))
			return "1mth";

		throw new ArgumentOutOfRangeException(
			nameof(value),
			value,
			"Nubra does not support this candle interval.");
	}

	public static string ToChartType(this SecurityTypes type)
		=> type switch
		{
			SecurityTypes.Index => "INDEX",
			SecurityTypes.Option => "OPT",
			SecurityTypes.Future => "FUT",
			_ => "STOCK",
		};
}

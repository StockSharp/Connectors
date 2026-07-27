namespace StockSharp.HdfcSecurities.Native;

static class HdfcExtensions
{
	public static SecurityId ToSecurityId(this HdfcInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		return new()
		{
			SecurityCode = instrument.SymbolName
				.IsEmpty(instrument.UnderlyingSymbol)
				.IsEmpty(instrument.SecurityId)
				.ThrowIfEmpty(nameof(instrument.SymbolName)),
			BoardCode = instrument.Exchange
				.ThrowIfEmpty(nameof(instrument.Exchange))
				.ToUpperInvariant(),
			Native = CreateInstrumentKey(
				instrument.Exchange,
				instrument.SecurityId),
		};
	}

	public static SecurityTypes ToSecurityType(
		this HdfcInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		var segment = instrument.InstrumentSegment?.ToUpperInvariant();
		if (segment?.StartsWith("OPT", StringComparison.Ordinal) == true)
			return SecurityTypes.Option;
		if (segment?.StartsWith("FUT", StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
		if (segment is "INDEX")
			return SecurityTypes.Index;
		if (segment is "COM")
			return SecurityTypes.Commodity;
		if (segment is "UNDCUR")
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

	public static DateTime? ToExpiry(this string value)
	{
		if (value.IsEmpty())
			return null;
		var formats = new[]
		{
			"yyyy-MM-dd",
			"yyyyMMdd",
			"dd-MMM-yy",
			"dd MMM yyyy",
		};
		return DateTime.TryParseExact(
			value.Trim(),
			formats,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces,
			out var date)
				? DateTime.SpecifyKind(date.Date, DateTimeKind.Utc)
				: null;
	}

	public static DateTime ToHdfcTime(this string value, DateTime fallback)
	{
		if (!value.IsEmpty())
		{
			if (DateTimeOffset.TryParseExact(
				value.Trim(),
				"yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
				CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces,
				out var timestamp))
				return timestamp.UtcDateTime;

			var localFormats = new[]
			{
				"dd/MM/yyyy HH:mm:ss",
				"yyyy-MM-dd HH:mm:ss",
			};
			if (DateTime.TryParseExact(
				value.Trim(),
				localFormats,
				CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces,
				out var localTime))
			{
				return new DateTimeOffset(
					DateTime.SpecifyKind(
						localTime,
						DateTimeKind.Unspecified),
					TimeSpan.FromMinutes(330)).UtcDateTime;
			}
		}
		return fallback.Kind == DateTimeKind.Utc
			? fallback
			: fallback.ToUniversalTime();
	}

	public static DateTime ToHdfcTime(this long value, DateTime fallback)
	{
		try
		{
			if (value >= 100_000_000_000_000_000L)
				return DateTimeOffset.FromUnixTimeMilliseconds(
					value / 1_000_000L).UtcDateTime;
			if (value >= 100_000_000_000_000L)
				return DateTimeOffset.FromUnixTimeMilliseconds(
					value / 1_000L).UtcDateTime;
			if (value >= 100_000_000_000L)
				return DateTimeOffset.FromUnixTimeMilliseconds(value)
					.UtcDateTime;
			if (value >= 1_000_000_000L)
				return DateTimeOffset.FromUnixTimeSeconds(value)
					.UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
		}
		return fallback.Kind == DateTimeKind.Utc
			? fallback
			: fallback.ToUniversalTime();
	}

	public static string ToNative(this HdfcProducts value)
		=> value switch
		{
			HdfcProducts.Delivery => "DELIVERY",
			HdfcProducts.Overnight => "OVERNIGHT",
			HdfcProducts.Intraday => "INTRADAY",
			HdfcProducts.Mtf => "MTF",
			HdfcProducts.CollateralSell => "COLL-SELL",
			HdfcProducts.Encash => "ENCASH",
			_ => throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				null),
		};

	public static HdfcProducts ToProduct(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"OVERNIGHT" => HdfcProducts.Overnight,
			"INTRADAY" => HdfcProducts.Intraday,
			"MTF" => HdfcProducts.Mtf,
			"COLL-SELL" => HdfcProducts.CollateralSell,
			"ENCASH" => HdfcProducts.Encash,
			_ => HdfcProducts.Delivery,
		};

	public static string ToNative(this Sides value)
		=> value == Sides.Buy ? "BUY" : "SELL";

	public static Sides ToSide(this string value)
		=> value.EqualsIgnoreCase("BUY") ||
			value.EqualsIgnoreCase("Buy")
				? Sides.Buy
				: Sides.Sell;

	public static OrderTypes ToOrderType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"SL" or "SL-L" or "SL-M" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.Replace(" ", string.Empty, StringComparison.Ordinal)
			.Replace("_", string.Empty, StringComparison.Ordinal)
			.ToUpperInvariant() switch
		{
			"OPEN" or "ACTIVE" or "ACCEPTED" or "MODIFIED" =>
				OrderStates.Active,
			"TRADED" or "FILLED" or "COMPLETE" or "COMPLETED" or
				"CANCELLED" or "CANCELED" or "EXPIRED" =>
				OrderStates.Done,
			"REJECTED" or "FAILED" or "ERROR" =>
				OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static TimeInForce? ToTimeInForce(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"DAY" or "GTD" => TimeInForce.PutInQueue,
			_ => null,
		};

	public static string ToStreamId(this HdfcInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		var token = instrument.ExchangeSecurityId
			.ThrowIfEmpty(nameof(instrument.ExchangeSecurityId));
		var exchange = instrument.Exchange?.ToUpperInvariant();
		var segment = instrument.InstrumentSegment?.ToUpperInvariant();
		var prefix = (exchange, segment) switch
		{
			("BSE", "INDEX") => "BSE_INDEX_",
			("BSE", "EQUITY") => "BSE_",
			("BSE", _) => "BFO_",
			("NSE", "INDEX") => "NSE_INDEX_",
			("NSE", "FUTCUR" or "OPTCUR" or "UNDCUR") => "NCD_",
			("NSE", "EQUITY") => "NSE_",
			("NSE", _) => "NFO_",
			("MCX", _) => "MCX_",
			_ => throw new ArgumentOutOfRangeException(
				nameof(instrument.Exchange),
				instrument.Exchange,
				"Unsupported HDFC Securities exchange."),
		};
		return prefix + token;
	}

	public static string ToStreamId(this PacketType type, long instrumentId)
	{
		var prefix = type switch
		{
			PacketType.NseCmAll or PacketType.NseCmCirc => "NSE_",
			PacketType.NseCdAll or PacketType.NseCdCirc or
				PacketType.NseCdOi => "NCD_",
			PacketType.NseIndex => "NSE_INDEX_",
			PacketType.NseFoAll or PacketType.NseFoCirc or
				PacketType.NseFoOi or PacketType.NseFoGreek => "NFO_",
			PacketType.BseCm => "BSE_",
			PacketType.BseIndex => "BSE_INDEX_",
			PacketType.BseFoAll or PacketType.BseFoOi or
				PacketType.BseFoGreek => "BFO_",
			PacketType.McxPkt => "MCX_",
			_ => null,
		};
		return prefix == null
			? null
			: prefix + instrumentId.ToString(CultureInfo.InvariantCulture);
	}

	public static string CreateInstrumentKey(string exchange, string securityId)
		=> $"{exchange?.ToUpperInvariant()}|{securityId?.ToUpperInvariant()}";

	public static (string exchange, string securityId) ParseInstrumentKey(
		this object native)
	{
		var value = native?.ToString();
		var separator = value?.IndexOf('|') ?? -1;
		if (separator <= 0 || separator == value.Length - 1)
			throw new FormatException("Invalid HDFC Securities instrument key.");
		return (value[..separator], value[(separator + 1)..]);
	}
}

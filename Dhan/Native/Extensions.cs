namespace StockSharp.Dhan.Native;

static class Extensions
{
	private static readonly TimeSpan _indiaOffset = TimeSpan.FromMinutes(330);

	public static string ToBoardCode(this string exchange, string segment)
	{
		segment = segment?.ToUpperInvariant();
		if (segment == "I")
			return "IDX_I";

		return (exchange?.ToUpperInvariant(), segment) switch
		{
			("NSE", "E") => "NSE_EQ",
			("NSE", "D") => "NSE_FNO",
			("NSE", "C") => "NSE_CURRENCY",
			("BSE", "E") => "BSE_EQ",
			("BSE", "D") => "BSE_FNO",
			("BSE", "C") => "BSE_CURRENCY",
			("MCX", "M") => "MCX_COMM",
			_ => throw new ArgumentOutOfRangeException(nameof(segment), $"{exchange}:{segment}", "Unsupported Dhan exchange segment."),
		};
	}

	public static DhanExchangeSegments ToExchangeSegment(this string boardCode)
		=> boardCode?.ToUpperInvariant() switch
		{
			"IDX_I" => DhanExchangeSegments.Index,
			"NSE_EQ" => DhanExchangeSegments.NseEquity,
			"NSE_FNO" => DhanExchangeSegments.NseDerivatives,
			"NSE_CURRENCY" => DhanExchangeSegments.NseCurrency,
			"BSE_EQ" => DhanExchangeSegments.BseEquity,
			"MCX_COMM" => DhanExchangeSegments.McxCommodity,
			"BSE_CURRENCY" => DhanExchangeSegments.BseCurrency,
			"BSE_FNO" => DhanExchangeSegments.BseDerivatives,
			_ => throw new ArgumentOutOfRangeException(nameof(boardCode), boardCode, "Unsupported Dhan exchange segment."),
		};

	public static string ToBoardCode(this DhanExchangeSegments segment)
		=> segment switch
		{
			DhanExchangeSegments.Index => "IDX_I",
			DhanExchangeSegments.NseEquity => "NSE_EQ",
			DhanExchangeSegments.NseDerivatives => "NSE_FNO",
			DhanExchangeSegments.NseCurrency => "NSE_CURRENCY",
			DhanExchangeSegments.BseEquity => "BSE_EQ",
			DhanExchangeSegments.McxCommodity => "MCX_COMM",
			DhanExchangeSegments.BseCurrency => "BSE_CURRENCY",
			DhanExchangeSegments.BseDerivatives => "BSE_FNO",
			_ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null),
		};

	public static string ToInstrumentKey(this string boardCode, string securityId)
		=> $"{boardCode.ThrowIfEmpty(nameof(boardCode))}|{securityId.ThrowIfEmpty(nameof(securityId))}";

	public static (string boardCode, string securityId) ParseInstrumentKey(this string key)
	{
		var parts = key?.Split('|');
		if (parts?.Length != 2 || parts[0].IsEmpty() || parts[1].IsEmpty())
			throw new FormatException($"Invalid Dhan instrument key '{key}'.");
		parts[0].ToExchangeSegment();
		return (parts[0], parts[1]);
	}

	public static string ToInstrumentKey(this SecurityId securityId)
	{
		if (securityId.Native is string native && !native.IsEmpty())
			return native;
		if (securityId.SecurityCode?.Contains('|') == true)
			return securityId.SecurityCode;
		throw new InvalidOperationException("Dhan security identifier is missing. Select the security through Dhan lookup so SecurityId.Native contains exchangeSegment|securityId.");
	}

	public static SecurityId ToSecurityId(this DhanInstrument instrument)
	{
		var boardCode = instrument.Exchange.ToBoardCode(instrument.Segment);
		return new()
		{
			SecurityCode = instrument.DisplayName.IsEmpty() ? instrument.SymbolName : instrument.DisplayName,
			BoardCode = boardCode,
			Native = boardCode.ToInstrumentKey(instrument.SecurityId),
		};
	}

	public static SecurityId ToSecurityId(this DhanExchangeSegments segment, string securityId, string symbol = null)
	{
		var boardCode = segment.ToBoardCode();
		return new()
		{
			SecurityCode = symbol.IsEmpty() ? securityId : symbol,
			BoardCode = boardCode,
			Native = boardCode.ToInstrumentKey(securityId),
		};
	}

	public static SecurityTypes ToSecurityType(this DhanInstrument instrument)
	{
		var type = instrument.Instrument?.ToUpperInvariant();
		if (type == "INDEX")
			return SecurityTypes.Index;
		if (type?.StartsWith("FUT", StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
		if (type?.StartsWith("OPT", StringComparison.Ordinal) == true)
			return SecurityTypes.Option;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string optionType)
		=> optionType?.ToUpperInvariant() switch
		{
			"CE" or "CALL" => OptionTypes.Call,
			"PE" or "PUT" => OptionTypes.Put,
			_ => null,
		};

	public static string ToNative(this DhanProducts product)
		=> product switch
		{
			DhanProducts.Delivery => "CNC",
			DhanProducts.Intraday => "INTRADAY",
			DhanProducts.Margin => "MARGIN",
			DhanProducts.MarginTradingFacility => "MTF",
			DhanProducts.Cover => "CO",
			DhanProducts.Bracket => "BO",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static DhanProducts ToProduct(this string product)
		=> product?.ToUpperInvariant() switch
		{
			"C" or "CNC" => DhanProducts.Delivery,
			"I" or "INTRADAY" => DhanProducts.Intraday,
			"M" or "MARGIN" => DhanProducts.Margin,
			"F" or "MTF" => DhanProducts.MarginTradingFacility,
			"V" or "CO" => DhanProducts.Cover,
			"B" or "BO" => DhanProducts.Bracket,
			_ => DhanProducts.Intraday,
		};

	public static string ToNative(this DhanAfterMarketTimes time)
		=> time switch
		{
			DhanAfterMarketTimes.PreOpen => "PRE_OPEN",
			DhanAfterMarketTimes.Open => "OPEN",
			DhanAfterMarketTimes.Open30 => "OPEN_30",
			DhanAfterMarketTimes.Open60 => "OPEN_60",
			_ => throw new ArgumentOutOfRangeException(nameof(time), time, null),
		};

	public static string ToNative(this DhanOrderLegs? leg)
		=> leg switch
		{
			DhanOrderLegs.Target => "TARGET_LEG",
			DhanOrderLegs.StopLoss => "STOP_LOSS_LEG",
			_ => "ENTRY_LEG",
		};

	public static DhanOrderLegs ToOrderLeg(this string leg)
		=> leg?.ToUpperInvariant() switch
		{
			"TARGET_LEG" => DhanOrderLegs.Target,
			"STOP_LOSS_LEG" => DhanOrderLegs.StopLoss,
			_ => DhanOrderLegs.Entry,
		};

	public static string ToNative(this DhanForeverOrderFlags flag)
		=> flag == DhanForeverOrderFlags.OneCancelsOther ? "OCO" : "SINGLE";

	public static DhanForeverOrderFlags ToForeverFlag(this string flag)
		=> flag.EqualsIgnoreCase("OCO") ? DhanForeverOrderFlags.OneCancelsOther : DhanForeverOrderFlags.Single;

	public static string ToNative(this Sides side) => side == Sides.Buy ? "BUY" : "SELL";
	public static Sides ToSide(this string side) => side?.ToUpperInvariant() is "BUY" or "B" ? Sides.Buy : Sides.Sell;
	public static string ToNative(this TimeInForce? timeInForce) => timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";
	public static TimeInForce ToTimeInForce(this string validity) => validity.EqualsIgnoreCase("IOC") ? TimeInForce.CancelBalance : TimeInForce.PutInQueue;

	public static string ToNative(this OrderTypes orderType, decimal? triggerPrice)
		=> orderType switch
		{
			OrderTypes.Market when triggerPrice is > 0 => "STOP_LOSS_MARKET",
			OrderTypes.Market => "MARKET",
			OrderTypes.Limit when triggerPrice is > 0 => "STOP_LOSS",
			OrderTypes.Limit => "LIMIT",
			OrderTypes.Conditional when triggerPrice is > 0 => "STOP_LOSS",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unsupported Dhan order type."),
		};

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MARKET" or "MKT" => OrderTypes.Market,
			"STOP_LOSS" or "STOP_LOSS_MARKET" or "SL" or "SLM" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string status)
	{
		status = status?.Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
		if (status is "REJECTED")
			return OrderStates.Failed;
		if (status is "CANCELLED" or "CANCELED" or "TRADED" or "EXPIRED" or "CLOSED")
			return OrderStates.Done;
		return OrderStates.Active;
	}

	public static DateTime? ToDhanTime(this string value)
	{
		if (value.IsEmpty())
			return null;

		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var timestamp) &&
			(value.Contains('Z') || value.Contains('+')))
			return timestamp.UtcDateTime;

		if (DateTime.TryParseExact(value,
			["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd H:mm:ss", "dd-MM-yyyy HH:mm:ss"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
			return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _indiaOffset).UtcDateTime;

		return null;
	}

	public static DateTime ToIndiaTime(this DateTime value)
	{
		if (value.Kind == DateTimeKind.Unspecified)
			return value;
		return new DateTimeOffset(value.ToUniversalTime()).ToOffset(_indiaOffset).DateTime;
	}

	public static int ToNativeMinutes(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			var value when value == TimeSpan.FromMinutes(1) => 1,
			var value when value == TimeSpan.FromMinutes(5) => 5,
			var value when value == TimeSpan.FromMinutes(15) => 15,
			var value when value == TimeSpan.FromMinutes(25) => 25,
			var value when value == TimeSpan.FromHours(1) => 60,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Dhan candle interval."),
		};
}

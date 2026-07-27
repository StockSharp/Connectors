namespace StockSharp.Ventura.Native;

static class VenturaExtensions
{
	public static SecurityId ToSecurityId(this VenturaInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		return new()
		{
			SecurityCode = instrument.TradingSymbol
				.IsEmpty(instrument.Name)
				.IsEmpty(instrument.ExchangeToken)
				.ThrowIfEmpty(nameof(instrument.TradingSymbol)),
			BoardCode = instrument.Exchange
				.ThrowIfEmpty(nameof(instrument.Exchange))
				.ToUpperInvariant(),
			Native = CreateInstrumentKey(
				instrument.Exchange,
				instrument.ExchangeToken),
		};
	}

	public static SecurityTypes ToSecurityType(
		this VenturaInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		var type = instrument.Instrument?.ToUpperInvariant();
		var segment = instrument.Segment?.ToUpperInvariant();
		if (segment == "INDICES" || type is "INDEX" or "IDX")
			return SecurityTypes.Index;
		if (type is "CE" or "PE" or "CALL" or "PUT" ||
			segment?.Contains("OPT", StringComparison.Ordinal) == true)
			return SecurityTypes.Option;
		if (type is "FUT" or "FUTURE" ||
			segment?.Contains("FUT", StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
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
			"dd/MM/yyyy",
			"yyyy-MM-dd",
			"dd-MMM-yyyy",
			"dd-MMM-yy",
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

	public static DateTime ToVenturaTime(
		this string value,
		DateTime fallback)
	{
		if (!value.IsEmpty())
		{
			var formats = new[]
			{
				"dd/MM/yyyy HH:mm:ss",
				"yyyy-MM-dd HH:mm:ss",
				"dd-MMM-yyyy'T'HH:mm:ss",
				"dd-MMM-yy'T'HH:mm:ss",
				"yyyy-MM-dd'T'HH:mm:ss",
			};
			if (DateTime.TryParseExact(
				value.Trim(),
				formats,
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
			if (DateTimeOffset.TryParse(
				value,
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal,
				out var timestamp))
				return timestamp.UtcDateTime;
		}
		return fallback.Kind == DateTimeKind.Utc
			? fallback
			: fallback.ToUniversalTime();
	}

	public static string ToNative(this VenturaProducts value)
		=> value switch
		{
			VenturaProducts.CashAndCarry => "C",
			VenturaProducts.Intraday => "I",
			VenturaProducts.Margin => "M",
			VenturaProducts.Mtf => "F",
			_ => throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				null),
		};

	public static VenturaProducts ToProduct(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"I" or "INTRADAY" => VenturaProducts.Intraday,
			"M" or "MARGIN" => VenturaProducts.Margin,
			"F" or "MTF" => VenturaProducts.Mtf,
			_ => VenturaProducts.CashAndCarry,
		};

	public static string ToNative(this Sides value)
		=> value == Sides.Buy ? "B" : "S";

	public static Sides ToSide(this string value)
		=> value.EqualsIgnoreCase("B") ||
			value.EqualsIgnoreCase("BUY")
				? Sides.Buy
				: Sides.Sell;

	public static OrderTypes ToOrderType(this JToken value)
	{
		if (value == null || value.Type == JTokenType.Null)
			return OrderTypes.Limit;
		if (value.Type == JTokenType.Integer)
		{
			return value.Value<int>() switch
			{
				1 => OrderTypes.Market,
				2 => OrderTypes.Limit,
				3 or 4 => OrderTypes.Conditional,
				_ => OrderTypes.Limit,
			};
		}
		return value.Value<string>().ToOrderType();
	}

	public static OrderTypes ToOrderType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"MKT" or "MARKET" => OrderTypes.Market,
			"SL" or "SLM" or "SL-M" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string value)
	{
		var status = value?
			.Replace(" ", string.Empty, StringComparison.Ordinal)
			.Replace("_", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.ToUpperInvariant();
		if (status.IsEmpty())
			return OrderStates.Pending;
		if (status.Contains("REJECT", StringComparison.Ordinal) ||
			status.Contains("FAIL", StringComparison.Ordinal) ||
			status.Contains("ERROR", StringComparison.Ordinal) ||
			status.Contains("FREEZE", StringComparison.Ordinal))
			return OrderStates.Failed;
		if (status == "CANCEL" ||
			status.Contains("CANCELCONFIRM", StringComparison.Ordinal) ||
			status.Contains("CANCELLED", StringComparison.Ordinal) ||
			status.Contains("CANCELED", StringComparison.Ordinal) ||
			status.Contains("TRADECONFIRM", StringComparison.Ordinal) ||
			status.Contains("TRADED", StringComparison.Ordinal) ||
			status.Contains("FILLED", StringComparison.Ordinal) ||
			status.Contains("COMPLETE", StringComparison.Ordinal) ||
			status.Contains("EXECUTED", StringComparison.Ordinal))
			return OrderStates.Done;
		if (status.Contains("CONFIRM", StringComparison.Ordinal) ||
			status.Contains("OPEN", StringComparison.Ordinal) ||
			status.Contains("ACTIVE", StringComparison.Ordinal) ||
			status.Contains("MODIF", StringComparison.Ordinal) ||
			status.Contains("TRIGGER", StringComparison.Ordinal))
			return OrderStates.Active;
		return OrderStates.Pending;
	}

	public static TimeInForce? ToTimeInForce(this JToken value)
	{
		if (value == null || value.Type == JTokenType.Null)
			return null;
		if (value.Type == JTokenType.Integer)
			return value.Value<int>() == 1
				? TimeInForce.CancelBalance
				: TimeInForce.PutInQueue;
		return value.Value<string>().EqualsIgnoreCase("IOC")
			? TimeInForce.CancelBalance
			: TimeInForce.PutInQueue;
	}

	public static string ToStreamAction(
		this VenturaInstrument instrument,
		bool depth)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		if (instrument.ToSecurityType() == SecurityTypes.Index)
		{
			if (depth)
				throw new NotSupportedException(
					"Ventura EaseAPI does not provide index market depth.");
			return "index:ltp";
		}
		var exchange = instrument.Exchange?.ToUpperInvariant() switch
		{
			"NSE" => "nse",
			"BSE" => "bse",
			"NFO" => "fno",
			"BFO" => "bfo",
			_ => throw new ArgumentOutOfRangeException(
				nameof(instrument.Exchange),
				instrument.Exchange,
				"Unsupported Ventura EaseAPI exchange."),
		};
		return $"{exchange}:{(depth ? "ltp_depth" : "ltp")}";
	}

	public static string ToStreamToken(this VenturaInstrument instrument)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		return (instrument.ToSecurityType() == SecurityTypes.Index
				? instrument.TradingSymbol
				: instrument.ExchangeToken)
			.ThrowIfEmpty("Ventura EaseAPI stream token");
	}

	public static string ToStreamKey(
		this VenturaInstrument instrument,
		bool depth)
		=> CreateStreamKey(
			instrument.ToStreamAction(depth),
			instrument.ToStreamToken());

	public static string ToOrderSegment(this VenturaInstrument instrument)
		=> instrument.ToSecurityType() is
			SecurityTypes.Future or SecurityTypes.Option
				? "D"
				: "E";

	public static string CreateInstrumentKey(
		string exchange,
		string exchangeToken)
		=> $"{exchange?.ToUpperInvariant()}|{exchangeToken?.ToUpperInvariant()}";

	public static (string exchange, string exchangeToken) ParseInstrumentKey(
		this object native)
	{
		var value = native?.ToString();
		var separator = value?.IndexOf('|') ?? -1;
		if (separator <= 0 || separator == value.Length - 1)
		{
			throw new FormatException(
				"Invalid Ventura EaseAPI instrument key.");
		}
		return (value[..separator], value[(separator + 1)..]);
	}

	public static string CreateStreamKey(string action, string token)
		=> $"{action?.ToLowerInvariant()}|{token?.ToUpperInvariant()}";
}

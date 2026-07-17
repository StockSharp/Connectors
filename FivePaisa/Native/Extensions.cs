namespace StockSharp.FivePaisa.Native;

static class Extensions
{
	private static readonly DateTime _unixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	private static readonly TimeZoneInfo _indiaTimeZone = GetIndiaTimeZone();
	private static readonly Regex _jsonDate = new(@"^/Date\((?<value>-?\d+)(?:[+-]\d{4})?\)/$", RegexOptions.Compiled);

	public static string ToBoardCode(this string exchange, string exchangeType)
		=> (exchange?.ToUpperInvariant(), exchangeType?.ToUpperInvariant()) switch
		{
			("N", "C") => "NSE",
			("N", "D") => "NFO",
			("N", "U") => "CDS",
			("B", "C") => "BSE",
			("B", "D") => "BFO",
			("B", "U") => "BCD",
			("M", "D") => "MCX",
			_ => throw new ArgumentOutOfRangeException(nameof(exchangeType), $"{exchange}:{exchangeType}", "Unsupported 5paisa exchange segment."),
		};

	public static (string exchange, string exchangeType) ToNativeExchange(this string boardCode)
		=> boardCode?.ToUpperInvariant() switch
		{
			"NSE" or "NSE_EQ" => ("N", "C"),
			"NFO" or "NSE_FNO" => ("N", "D"),
			"CDS" or "NSE_CURRENCY" => ("N", "U"),
			"BSE" or "BSE_EQ" => ("B", "C"),
			"BFO" or "BSE_FNO" => ("B", "D"),
			"BCD" or "BSE_CURRENCY" => ("B", "U"),
			"MCX" or "MCX_COMM" => ("M", "D"),
			_ => throw new ArgumentOutOfRangeException(nameof(boardCode), boardCode, "Unsupported 5paisa board."),
		};

	public static string ToInstrumentKey(this string exchange, string exchangeType, long scripCode)
		=> $"{exchange.ThrowIfEmpty(nameof(exchange))}|{exchangeType.ThrowIfEmpty(nameof(exchangeType))}|{scripCode.ToString(CultureInfo.InvariantCulture)}";

	public static (string exchange, string exchangeType, long scripCode) ParseInstrumentKey(this string key)
	{
		var parts = key?.Split('|');
		if (parts?.Length != 3 || parts[0].IsEmpty() || parts[1].IsEmpty() ||
			!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var scripCode) || scripCode <= 0)
			throw new FormatException($"Invalid 5paisa instrument key '{key}'.");

		parts[0].ToBoardCode(parts[1]);
		return (parts[0].ToUpperInvariant(), parts[1].ToUpperInvariant(), scripCode);
	}

	public static string ToInstrumentKey(this SecurityId securityId)
	{
		if (securityId.Native is string native && !native.IsEmpty())
		{
			native.ParseInstrumentKey();
			return native;
		}

		if (securityId.SecurityCode?.Split('|') is { Length: 3 })
		{
			securityId.SecurityCode.ParseInstrumentKey();
			return securityId.SecurityCode;
		}

		throw new InvalidOperationException("5paisa security identifier is missing. Select the security through 5paisa lookup so SecurityId.Native contains exchange|exchangeType|scripCode.");
	}

	public static SecurityId ToSecurityId(this FivePaisaInstrument instrument)
	{
		var boardCode = instrument.Exchange.ToBoardCode(instrument.ExchangeType);
		return new()
		{
			SecurityCode = instrument.Name.IsEmpty() ? instrument.FullName : instrument.Name,
			BoardCode = boardCode,
			Native = instrument.Exchange.ToInstrumentKey(instrument.ExchangeType, instrument.ScripCode),
		};
	}

	public static SecurityId ToSecurityId(this string exchange, string exchangeType, long scripCode, string symbol = null)
	{
		var boardCode = exchange.ToBoardCode(exchangeType);
		return new()
		{
			SecurityCode = symbol.IsEmpty() ? scripCode.ToString(CultureInfo.InvariantCulture) : symbol,
			BoardCode = boardCode,
			Native = exchange.ToInstrumentKey(exchangeType, scripCode),
		};
	}

	public static SecurityTypes ToSecurityType(this FivePaisaInstrument instrument)
	{
		var type = instrument.ScripType?.Replace(" ", string.Empty).ToUpperInvariant();
		if (type?.Contains("INDEX", StringComparison.Ordinal) == true || type is "IDX" or "I" or "EQ")
			return SecurityTypes.Index;
		if (type?.Contains("CE", StringComparison.Ordinal) == true || type?.Contains("PE", StringComparison.Ordinal) == true ||
			type?.Contains("CALL", StringComparison.Ordinal) == true || type?.Contains("PUT", StringComparison.Ordinal) == true ||
			instrument.StrikeRate > 0)
			return SecurityTypes.Option;
		if (type?.Contains("FUT", StringComparison.Ordinal) == true || instrument.ExchangeType.EqualsIgnoreCase("D") || instrument.Expiry != null)
			return SecurityTypes.Future;
		if (instrument.ExchangeType.EqualsIgnoreCase("U"))
			return SecurityTypes.Currency;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this FivePaisaInstrument instrument)
	{
		var type = $"{instrument.ScripType} {instrument.Name} {instrument.FullName}".ToUpperInvariant();
		if (type.Contains(" CE", StringComparison.Ordinal) || type.EndsWith("CE", StringComparison.Ordinal) || type.Contains("CALL", StringComparison.Ordinal))
			return OptionTypes.Call;
		if (type.Contains(" PE", StringComparison.Ordinal) || type.EndsWith("PE", StringComparison.Ordinal) || type.Contains("PUT", StringComparison.Ordinal))
			return OptionTypes.Put;
		return null;
	}

	public static string ToNative(this Sides side) => side == Sides.Buy ? "B" : "S";
	public static Sides ToSide(this string side) => side?.ToUpperInvariant() is "B" or "BUY" ? Sides.Buy : Sides.Sell;

	public static bool ToIsIntraday(this FivePaisaProducts product)
		=> product == FivePaisaProducts.Intraday;

	public static FivePaisaProducts ToProduct(this string value)
		=> value?.ToUpperInvariant() is "D" or "DELIVERY" or "CNC" ? FivePaisaProducts.Delivery : FivePaisaProducts.Intraday;

	public static int ToNative(this TimeInForce? timeInForce)
		=> timeInForce == TimeInForce.CancelBalance ? 3 : 0;

	public static TimeInForce ToTimeInForce(this int value)
		=> value == 3 ? TimeInForce.CancelBalance : TimeInForce.PutInQueue;

	public static OrderTypes ToOrderType(this string atMarket, string withStopLoss)
	{
		if (withStopLoss.EqualsIgnoreCase("Y"))
			return OrderTypes.Conditional;
		return atMarket.EqualsIgnoreCase("Y") ? OrderTypes.Market : OrderTypes.Limit;
	}

	public static OrderStates ToOrderState(this string status)
	{
		status = status?.Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
		if (status?.Contains("REJECT", StringComparison.Ordinal) == true || status?.Contains("FAIL", StringComparison.Ordinal) == true ||
			status?.Contains("ERROR", StringComparison.Ordinal) == true)
			return OrderStates.Failed;
		if (status?.Contains("CANCEL", StringComparison.Ordinal) == true || status?.Contains("EXECUT", StringComparison.Ordinal) == true ||
			status?.Contains("TRADED", StringComparison.Ordinal) == true || status?.Contains("COMPLETE", StringComparison.Ordinal) == true ||
			status?.Contains("EXPIRED", StringComparison.Ordinal) == true)
			return OrderStates.Done;
		return OrderStates.Active;
	}

	public static string ToCandleInterval(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			var value when value == TimeSpan.FromMinutes(1) => "1m",
			var value when value == TimeSpan.FromMinutes(5) => "5m",
			var value when value == TimeSpan.FromMinutes(10) => "10m",
			var value when value == TimeSpan.FromMinutes(15) => "15m",
			var value when value == TimeSpan.FromMinutes(30) => "30m",
			var value when value == TimeSpan.FromHours(1) => "60m",
			var value when value == TimeSpan.FromDays(1) => "1d",
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported 5paisa candle interval."),
		};

	public static DateTime? ToFivePaisaTime(this string value)
	{
		if (value.IsEmpty())
			return null;

		var match = _jsonDate.Match(value);
		if (match.Success && long.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
			return EnsureSensible(_unixEpoch.AddMilliseconds(milliseconds));

		if ((value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(value, @"[+-]\d{2}:?\d{2}$")) &&
			DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal, out var utc))
			return EnsureSensible(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

		if (DateTime.TryParseExact(value,
			["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd H:mm:ss", "dd/MM/yyyy HH:mm:ss", "dd-MM-yyyy HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
			return EnsureSensible(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _indiaTimeZone));

		return null;
	}

	public static DateTime FromUnixMilliseconds(this long value)
		=> _unixEpoch.AddMilliseconds(value);

	public static DateTime FromUnixSeconds(this long value)
		=> _unixEpoch.AddSeconds(value);

	public static DateTime FromIndiaSecondsOfDay(this long value, DateTime utcDate)
	{
		if (value is < 0 or >= 86400)
			throw new ArgumentOutOfRangeException(nameof(value), value, "Seconds since midnight must be within one day.");
		var indiaDate = TimeZoneInfo.ConvertTimeFromUtc(utcDate.ToUniversalTime(), _indiaTimeZone).Date;
		return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(indiaDate.AddSeconds(value), DateTimeKind.Unspecified), _indiaTimeZone);
	}

	public static DateTime ToIndiaTime(this DateTime value)
	{
		if (value.Kind == DateTimeKind.Unspecified)
			return value;
		return TimeZoneInfo.ConvertTimeFromUtc(value.ToUniversalTime(), _indiaTimeZone);
	}

	public static string ToDepthInstrument(this string instrumentKey)
	{
		var (exchange, exchangeType, scripCode) = instrumentKey.ParseInstrumentKey();
		if (exchange != "N")
			throw new InvalidOperationException("5paisa 20-level market depth is available only for NSE instruments.");
		return $"{exchange.ToLowerInvariant()}{exchangeType.ToLowerInvariant()}{scripCode.ToString(CultureInfo.InvariantCulture)}";
	}

	private static TimeZoneInfo GetIndiaTimeZone()
	{
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
		}
		catch (TimeZoneNotFoundException)
		{
			return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
		}
	}

	private static DateTime? EnsureSensible(DateTime value)
		=> value.Year >= 2000 ? value : null;
}

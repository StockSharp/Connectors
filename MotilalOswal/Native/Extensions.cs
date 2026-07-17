namespace StockSharp.MotilalOswal.Native;

static class Extensions
{
	private static readonly TimeZoneInfo _indiaTimeZone = GetIndiaTimeZone();
	private static readonly DateTime _motilalEpoch = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	public static string ToBoardCode(this string exchange)
		=> exchange?.ToUpperInvariant() switch
		{
			"NSE" => "NSE",
			"BSE" => "BSE",
			"NSEFO" => "NFO",
			"NSECD" => "CDS",
			"MCX" => "MCX",
			"NCDEX" => "NCDEX",
			"BSEFO" => "BFO",
			"BSECD" => "BCD",
			_ => throw new ArgumentOutOfRangeException(nameof(exchange), exchange, "Unsupported Motilal Oswal exchange segment."),
		};

	public static string ToNativeExchange(this string boardCode)
		=> boardCode?.ToUpperInvariant() switch
		{
			"NSE" or "NSE_EQ" => "NSE",
			"BSE" or "BSE_EQ" => "BSE",
			"NFO" or "NSE_FNO" => "NSEFO",
			"CDS" or "NSE_CURRENCY" => "NSECD",
			"MCX" or "MCX_COMM" => "MCX",
			"NCDEX" or "NCO" => "NCDEX",
			"BFO" or "BSE_FNO" => "BSEFO",
			"BCD" or "BSE_CURRENCY" => "BSECD",
			_ => throw new ArgumentOutOfRangeException(nameof(boardCode), boardCode, "Unsupported Motilal Oswal board."),
		};

	public static string ToInstrumentKey(this string exchange, long scripCode)
		=> $"{exchange.ThrowIfEmpty(nameof(exchange)).ToUpperInvariant()}|{scripCode.ToString(CultureInfo.InvariantCulture)}";

	public static (string exchange, long scripCode) ParseInstrumentKey(this string key)
	{
		var parts = key?.Split('|');
		if (parts?.Length != 2 || parts[0].IsEmpty() ||
			!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var scripCode) || scripCode <= 0)
			throw new FormatException($"Invalid Motilal Oswal instrument key '{key}'.");

		parts[0].ToBoardCode();
		return (parts[0].ToUpperInvariant(), scripCode);
	}

	public static string ToInstrumentKey(this SecurityId securityId)
	{
		if (securityId.Native is string native && !native.IsEmpty())
		{
			native.ParseInstrumentKey();
			return native;
		}

		if (securityId.SecurityCode?.Split('|') is { Length: 2 })
		{
			securityId.SecurityCode.ParseInstrumentKey();
			return securityId.SecurityCode;
		}

		throw new InvalidOperationException("Motilal Oswal symbol token is missing. Select the security through MO API lookup so SecurityId.Native contains exchange|scripCode.");
	}

	public static SecurityId ToSecurityId(this MotilalOswalInstrument instrument)
	{
		var code = instrument.ShortName.IsEmpty() ? instrument.Name.IsEmpty() ? instrument.ScripCode.ToString(CultureInfo.InvariantCulture) : instrument.Name : instrument.ShortName;
		return new()
		{
			SecurityCode = code,
			BoardCode = instrument.ExchangeName.ToBoardCode(),
			Native = instrument.ExchangeName.ToInstrumentKey(instrument.ScripCode),
		};
	}

	public static SecurityId ToSecurityId(this string exchange, long scripCode, string symbol = null)
		=> new()
		{
			SecurityCode = symbol.IsEmpty() ? scripCode.ToString(CultureInfo.InvariantCulture) : symbol,
			BoardCode = exchange.ToBoardCode(),
			Native = exchange.ToInstrumentKey(scripCode),
		};

	public static SecurityTypes ToSecurityType(this MotilalOswalInstrument instrument)
	{
		if (instrument.IsIndex || instrument.IndexIdentifier != 0)
			return SecurityTypes.Index;

		var type = instrument.InstrumentName?.Replace(" ", string.Empty).ToUpperInvariant();
		if (type?.Contains("OPT", StringComparison.Ordinal) == true || instrument.OptionType?.ToUpperInvariant() is "CE" or "PE")
			return SecurityTypes.Option;
		if (type?.Contains("FUT", StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
		if (instrument.ExchangeName.EqualsIgnoreCase("NSECD") || instrument.ExchangeName.EqualsIgnoreCase("BSECD"))
			return SecurityTypes.Currency;
		if (instrument.ExchangeName.EqualsIgnoreCase("MCX") || instrument.ExchangeName.EqualsIgnoreCase("NCDEX"))
			return SecurityTypes.Commodity;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string optionType)
		=> optionType?.Trim().ToUpperInvariant() switch
		{
			"CE" or "CALL" => OptionTypes.Call,
			"PE" or "PUT" => OptionTypes.Put,
			_ => null,
		};

	public static DateTime? ToExpiry(this long seconds)
	{
		if (seconds <= 0)
			return null;
		try
		{
			return ToUtcFromIndia(_motilalEpoch.AddSeconds(seconds));
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	public static DateTime FromMotilalSeconds(this int seconds)
		=> ToUtcFromIndia(_motilalEpoch.AddSeconds(seconds));

	public static DateTime? ToMotilalTime(this string value)
	{
		if (value.IsEmpty() || value.Trim() is "0" or "-" or "NA")
			return null;

		if ((value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(value, @"[+-]\d{2}:?\d{2}$")) &&
			DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal, out var utc))
			return DateTime.SpecifyKind(utc, DateTimeKind.Utc);

		if (DateTime.TryParseExact(value.Trim(),
			["dd-MMM-yyyy HH:mm:ss", "d-MMM-yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy", "dd-MM-yyyy", "dd-MMM-yyyy", "yyyy-MM-dd HH:mm:ss"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
			return ToUtcFromIndia(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));

		return null;
	}

	public static string ToGoodTillDate(this DateTime? date)
	{
		if (date == null)
			return string.Empty;
		var value = date.Value;
		if (value.Kind != DateTimeKind.Unspecified)
			value = TimeZoneInfo.ConvertTimeFromUtc(value.ToUniversalTime(), _indiaTimeZone);
		return value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
	}

	public static char ToWireExchange(this string exchange)
		=> exchange?.ToUpperInvariant() switch
		{
			"NSE" or "NSEFO" => 'N',
			"BSE" => 'B',
			"MCX" => 'M',
			"NCDEX" => 'D',
			"NSECD" => 'C',
			"BSEFO" or "BSECD" => 'G',
			_ => throw new ArgumentOutOfRangeException(nameof(exchange), exchange, "Unsupported Motilal Oswal broadcast exchange."),
		};

	public static char ToWireExchangeType(this string exchange)
		=> exchange?.ToUpperInvariant() is "NSE" or "BSE" ? 'C' : 'D';

	public static string FromWireExchange(this char exchange, long scripCode)
		=> exchange switch
		{
			'N' => scripCode <= 34999 || scripCode is >= 888801 and <= 888820 ? "NSE" : "NSEFO",
			'B' => "BSE",
			'M' => "MCX",
			'D' => "NCDEX",
			'C' => "NSECD",
			'G' => "BSEFO",
			_ => throw new InvalidDataException($"Unknown Motilal Oswal broadcast exchange '{exchange}'."),
		};

	public static string ToNative(this Sides side) => side == Sides.Buy ? "BUY" : "SELL";
	public static Sides ToSide(this string side) => side.EqualsIgnoreCase("BUY") ? Sides.Buy : Sides.Sell;

	public static string ToNative(this MotilalOswalProducts product)
		=> product switch
		{
			MotilalOswalProducts.Normal => "NORMAL",
			MotilalOswalProducts.Delivery => "DELIVERY",
			MotilalOswalProducts.SellFromDp => "SELLFROMDP",
			MotilalOswalProducts.ValuePlus => "VALUEPLUS",
			MotilalOswalProducts.Btst => "BTST",
			MotilalOswalProducts.Mtf => "MTF",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static MotilalOswalProducts ToProduct(this string product)
		=> product?.Replace(" ", string.Empty).ToUpperInvariant() switch
		{
			"DELIVERY" => MotilalOswalProducts.Delivery,
			"SELLFROMDP" => MotilalOswalProducts.SellFromDp,
			"VALUEPLUS" => MotilalOswalProducts.ValuePlus,
			"BTST" => MotilalOswalProducts.Btst,
			"MTF" => MotilalOswalProducts.Mtf,
			_ => MotilalOswalProducts.Normal,
		};

	public static string ToNative(this MotilalOswalOrderDurations duration)
		=> duration switch
		{
			MotilalOswalOrderDurations.Day => "DAY",
			MotilalOswalOrderDurations.GoodTillCancelled => "GTC",
			MotilalOswalOrderDurations.GoodTillDate => "GTD",
			MotilalOswalOrderDurations.ImmediateOrCancel => "IOC",
			_ => throw new ArgumentOutOfRangeException(nameof(duration), duration, null),
		};

	public static MotilalOswalOrderDurations ToDuration(this string duration)
		=> duration?.ToUpperInvariant() switch
		{
			"GTC" => MotilalOswalOrderDurations.GoodTillCancelled,
			"GTD" => MotilalOswalOrderDurations.GoodTillDate,
			"IOC" => MotilalOswalOrderDurations.ImmediateOrCancel,
			_ => MotilalOswalOrderDurations.Day,
		};

	public static MotilalOswalOrderDurations ToDuration(this TimeInForce? timeInForce, MotilalOswalOrderDurations? requested)
		=> timeInForce == TimeInForce.CancelBalance ? MotilalOswalOrderDurations.ImmediateOrCancel : requested ?? MotilalOswalOrderDurations.Day;

	public static string ToNative(this OrderTypes orderType)
		=> orderType switch
		{
			OrderTypes.Limit => "LIMIT",
			OrderTypes.Market => "MARKET",
			OrderTypes.Conditional => "STOPLOSS",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Motilal Oswal supports market, limit, and stop-loss orders."),
		};

	public static OrderTypes ToOrderType(this MotilalOswalOrder order)
	{
		if (order.TriggerPrice > 0 || order.OrderType?.Contains("STOP", StringComparison.OrdinalIgnoreCase) == true)
			return OrderTypes.Conditional;
		if (order.OrderType.EqualsIgnoreCase("MARKET") || order.BookType.EqualsIgnoreCase("MARKET"))
			return OrderTypes.Market;
		return OrderTypes.Limit;
	}

	public static OrderStates ToOrderState(this string status)
	{
		status = status?.Replace(" ", string.Empty).ToUpperInvariant();
		if (status is "REJECTED" or "ERROR" || status?.Contains("REJECT", StringComparison.Ordinal) == true)
			return OrderStates.Failed;
		if (status is "CANCEL" or "CANCELLED" or "CANCELED" or "TRADED" or "COMPLETE" or "COMPLETED")
			return OrderStates.Done;
		if (status is "UNKNOWN" or "SENT" || status.IsEmpty())
			return OrderStates.Pending;
		return OrderStates.Active;
	}

	public static decimal GetNetQuantity(this MotilalOswalPosition position)
		=> position.BuyQuantity - position.SellQuantity;

	public static decimal GetAveragePrice(this MotilalOswalPosition position)
	{
		var net = position.GetNetQuantity();
		if (net > 0 && position.BuyQuantity > 0)
			return position.BuyAmount / position.BuyQuantity;
		if (net < 0 && position.SellQuantity > 0)
			return position.SellAmount / position.SellQuantity;
		return 0;
	}

	private static DateTime ToUtcFromIndia(DateTime local)
		=> TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _indiaTimeZone);

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
}

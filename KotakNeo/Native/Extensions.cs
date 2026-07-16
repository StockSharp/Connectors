namespace StockSharp.KotakNeo.Native;

static class KotakNeoExtensions
{
	private static readonly TimeSpan _indiaOffset = TimeSpan.FromMinutes(330);

	public static string ToBoardCode(this string exchangeSegment)
		=> exchangeSegment?.ToLowerInvariant() switch
		{
			"nse_cm" => "NSE",
			"bse_cm" => "BSE",
			"nse_fo" => "NFO",
			"bse_fo" => "BFO",
			"cde_fo" => "CDS",
			"bcs-fo" or "bcs_fo" => "BCD",
			"mcx_fo" => "MCX",
			_ => throw new ArgumentOutOfRangeException(nameof(exchangeSegment), exchangeSegment, "Unsupported Kotak Neo exchange segment."),
		};

	public static string ToExchangeSegment(this string boardCode)
		=> boardCode?.ToUpperInvariant() switch
		{
			"NSE" => "nse_cm",
			"BSE" => "bse_cm",
			"NFO" => "nse_fo",
			"BFO" => "bse_fo",
			"CDS" => "cde_fo",
			"BCD" => "bcs-fo",
			"MCX" => "mcx_fo",
			_ => throw new ArgumentOutOfRangeException(nameof(boardCode), boardCode, "Unsupported Kotak Neo board."),
		};

	public static string ToInstrumentKey(this string exchangeSegment, string token)
		=> $"{exchangeSegment.ThrowIfEmpty(nameof(exchangeSegment))}|{token.ThrowIfEmpty(nameof(token))}";

	public static (string exchangeSegment, string token) ParseInstrumentKey(this string key)
	{
		var parts = key?.Split('|');
		if (parts?.Length != 2 || parts[0].IsEmpty() || parts[1].IsEmpty())
			throw new FormatException($"Invalid Kotak Neo instrument key '{key}'.");
		parts[0].ToBoardCode();
		return (parts[0], parts[1]);
	}

	public static string ToInstrumentKey(this SecurityId securityId)
	{
		if (securityId.Native is string native && !native.IsEmpty())
			return native;
		if (securityId.SecurityCode?.Contains('|') == true)
			return securityId.SecurityCode;
		throw new InvalidOperationException("Kotak Neo instrument token is missing. Select the security through Kotak Neo lookup so SecurityId.Native contains exchangeSegment|token.");
	}

	public static SecurityId ToSecurityId(this KotakNeoInstrument instrument)
		=> CreateSecurityId(instrument.ExchangeSegment, instrument.Token, instrument.TradingSymbol ?? instrument.Symbol);

	public static SecurityId CreateSecurityId(string exchangeSegment, string token, string symbol = null)
		=> new()
		{
			SecurityCode = symbol.IsEmpty() ? token : symbol,
			BoardCode = exchangeSegment.ToBoardCode(),
			Native = token.IsEmpty() ? null : exchangeSegment.ToInstrumentKey(token),
		};

	public static SecurityTypes ToSecurityType(this KotakNeoInstrument instrument)
	{
		var type = (instrument.InstrumentType ?? instrument.InstrumentName)?.ToUpperInvariant();
		if (type?.Contains("INDEX", StringComparison.Ordinal) == true || instrument.Group.EqualsIgnoreCase("IX"))
			return SecurityTypes.Index;
		if (type?.Contains("OPT", StringComparison.Ordinal) == true || instrument.OptionType is "CE" or "PE")
			return SecurityTypes.Option;
		if (type?.Contains("FUT", StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
		if (instrument.ExchangeSegment.EqualsIgnoreCase("cde_fo"))
			return SecurityTypes.Currency;
		if (instrument.ExchangeSegment.EqualsIgnoreCase("mcx_fo"))
			return SecurityTypes.Commodity;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string optionType)
		=> optionType?.ToUpperInvariant() switch
		{
			"CE" => OptionTypes.Call,
			"PE" => OptionTypes.Put,
			_ => null,
		};

	public static string ToNative(this KotakNeoProducts product)
		=> product switch
		{
			KotakNeoProducts.Normal => "NRML",
			KotakNeoProducts.CashAndCarry => "CNC",
			KotakNeoProducts.Intraday => "MIS",
			KotakNeoProducts.Cover => "CO",
			KotakNeoProducts.Bracket => "BO",
			KotakNeoProducts.MarginTradingFacility => "MTF",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static KotakNeoProducts ToProduct(this string product)
		=> product?.ToUpperInvariant() switch
		{
			"CNC" => KotakNeoProducts.CashAndCarry,
			"MIS" or "INTRADAY" => KotakNeoProducts.Intraday,
			"CO" => KotakNeoProducts.Cover,
			"BO" => KotakNeoProducts.Bracket,
			"MTF" => KotakNeoProducts.MarginTradingFacility,
			_ => KotakNeoProducts.Normal,
		};

	public static string ToNative(this Sides side) => side == Sides.Buy ? "B" : "S";
	public static Sides ToSide(this string side) => side?.ToUpperInvariant() is "B" or "BUY" ? Sides.Buy : Sides.Sell;
	public static string ToNative(this TimeInForce? timeInForce) => timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";
	public static TimeInForce ToTimeInForce(this string validity) => validity.EqualsIgnoreCase("IOC") ? TimeInForce.CancelBalance : TimeInForce.PutInQueue;

	public static string ToNative(this OrderTypes orderType, decimal? triggerPrice)
		=> orderType switch
		{
			OrderTypes.Market when triggerPrice is > 0 => "SL-M",
			OrderTypes.Market => "MKT",
			OrderTypes.Limit when triggerPrice is > 0 => "SL",
			OrderTypes.Limit => "L",
			OrderTypes.Conditional when triggerPrice is > 0 => "SL",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unsupported Kotak Neo order type."),
		};

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MKT" or "MARKET" => OrderTypes.Market,
			"SL" or "SL-M" or "SLM" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string status)
	{
		status = status?.Replace("_", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
		if (status is "rejected" or "cancelrejected" or "modifyrejected")
			return OrderStates.Failed;
		if (status is "cancelled" or "canceled" or "complete" or "completed" or "traded" or "expired")
			return OrderStates.Done;
		return OrderStates.Active;
	}

	public static DateTime? ToKotakTime(this string value, string date = null)
	{
		if (value.IsEmpty())
			return null;

		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var offset) &&
			(value.Contains('Z') || value.Contains('+')))
			return offset.UtcDateTime;

		if (DateTime.TryParseExact(value,
			["dd-MMM-yyyy HH:mm:ss", "dd-MMM-yyyy H:mm:ss", "yyyy/MM/dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
			return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _indiaOffset).UtcDateTime;

		if (!date.IsEmpty() && DateTime.TryParseExact($"{date} {value}",
			["dd-MMM-yyyy HH:mm:ss", "dd-MMM-yyyy H:mm:ss"], CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out local))
			return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _indiaOffset).UtcDateTime;

		return null;
	}
}

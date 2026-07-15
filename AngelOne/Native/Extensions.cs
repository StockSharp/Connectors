namespace StockSharp.AngelOne.Native;

static class Extensions
{
	private const decimal _masterPriceScale = 100m;

	public static AngelOneExchangeTypes ToExchangeType(this string exchange)
		=> exchange?.ToUpperInvariant() switch
		{
			"NSE" => AngelOneExchangeTypes.NseCash,
			"NFO" => AngelOneExchangeTypes.NseDerivatives,
			"BSE" => AngelOneExchangeTypes.BseCash,
			"BFO" => AngelOneExchangeTypes.BseDerivatives,
			"MCX" => AngelOneExchangeTypes.McxDerivatives,
			"NCDEX" or "NCO" => AngelOneExchangeTypes.NcdexDerivatives,
			"CDS" => AngelOneExchangeTypes.CurrencyDerivatives,
			_ => throw new ArgumentOutOfRangeException(nameof(exchange), exchange, "Unsupported Angel One exchange segment."),
		};

	public static string ToExchange(this AngelOneExchangeTypes exchangeType)
		=> exchangeType switch
		{
			AngelOneExchangeTypes.NseCash => "NSE",
			AngelOneExchangeTypes.NseDerivatives => "NFO",
			AngelOneExchangeTypes.BseCash => "BSE",
			AngelOneExchangeTypes.BseDerivatives => "BFO",
			AngelOneExchangeTypes.McxDerivatives => "MCX",
			AngelOneExchangeTypes.NcdexDerivatives => "NCDEX",
			AngelOneExchangeTypes.CurrencyDerivatives => "CDS",
			_ => throw new ArgumentOutOfRangeException(nameof(exchangeType), exchangeType, null),
		};

	public static string ToInstrumentKey(this AngelOneExchangeTypes exchangeType, string token)
		=> $"{(byte)exchangeType}|{token.ThrowIfEmpty(nameof(token))}";

	public static (AngelOneExchangeTypes exchangeType, string token) ParseInstrumentKey(this string key)
	{
		var parts = key?.Split('|');
		if (parts?.Length != 2 || !byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exchangeType) || parts[1].IsEmpty())
			throw new FormatException($"Invalid Angel One instrument key '{key}'.");
		return ((AngelOneExchangeTypes)exchangeType, parts[1]);
	}

	public static string ToInstrumentKey(this SecurityId securityId)
	{
		if (securityId.Native is string native && !native.IsEmpty())
			return native;
		if (securityId.SecurityCode?.Contains('|') == true)
			return securityId.SecurityCode;

		throw new InvalidOperationException("Angel One instrument token is missing. Select the security through Angel One lookup so SecurityId.Native contains exchangeType|token.");
	}

	public static SecurityId ToSecurityId(this AngelOneInstrument instrument)
	{
		var exchangeType = instrument.Exchange.ToExchangeType();
		return new()
		{
			SecurityCode = instrument.Symbol.IsEmpty() ? instrument.Token : instrument.Symbol,
			BoardCode = instrument.Exchange,
			Native = exchangeType.ToInstrumentKey(instrument.Token),
		};
	}

	public static SecurityId ToSecurityId(this AngelOneExchangeTypes exchangeType, string token, string symbol = null)
		=> new()
		{
			SecurityCode = symbol.IsEmpty() ? token : symbol,
			BoardCode = exchangeType.ToExchange(),
			Native = token.IsEmpty() ? null : exchangeType.ToInstrumentKey(token),
		};

	public static SecurityId CreateSecurityId(this string exchange, string token, string symbol = null)
		=> new()
		{
			SecurityCode = symbol.IsEmpty() ? token : symbol,
			BoardCode = exchange,
			Native = token.IsEmpty() ? null : exchange.ToExchangeType().ToInstrumentKey(token),
		};

	public static SecurityTypes ToSecurityType(this AngelOneInstrument instrument)
	{
		var type = instrument.InstrumentType?.ToUpperInvariant();
		if (type is "AMXIDX" or "INDEX")
			return SecurityTypes.Index;
		if (type?.Contains("OPT", StringComparison.Ordinal) == true || type is "CE" or "PE")
			return SecurityTypes.Option;
		if (type?.Contains("FUT", StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
		if (instrument.Exchange.EqualsIgnoreCase("CDS"))
			return SecurityTypes.Currency;
		if (instrument.Exchange is "MCX" or "NCDEX" or "NCO")
			return SecurityTypes.Commodity;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string instrumentType)
	{
		instrumentType = instrumentType?.ToUpperInvariant();
		if (instrumentType?.EndsWith("CE", StringComparison.Ordinal) == true || instrumentType?.Contains("CALL", StringComparison.Ordinal) == true)
			return OptionTypes.Call;
		if (instrumentType?.EndsWith("PE", StringComparison.Ordinal) == true || instrumentType?.Contains("PUT", StringComparison.Ordinal) == true)
			return OptionTypes.Put;
		return null;
	}

	public static DateTime? ToExpiry(this string expiry)
	{
		if (expiry.IsEmpty())
			return null;
		return DateTime.TryParseExact(expiry, ["ddMMMyyyy", "dd-MMM-yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var value)
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: null;
	}

	public static decimal ToStreamPrice(this long value, AngelOneExchangeTypes exchangeType)
		=> value / (exchangeType == AngelOneExchangeTypes.CurrencyDerivatives ? 10_000_000m : 100m);
	public static decimal ToMasterPrice(this decimal value, AngelOneExchangeTypes exchangeType)
		=> value / (exchangeType == AngelOneExchangeTypes.CurrencyDerivatives ? 10_000_000m : _masterPriceScale);

	public static string ToNative(this AngelOneProducts product)
		=> product switch
		{
			AngelOneProducts.Delivery => "DELIVERY",
			AngelOneProducts.CarryForward => "CARRYFORWARD",
			AngelOneProducts.Margin => "MARGIN",
			AngelOneProducts.Intraday => "INTRADAY",
			AngelOneProducts.Bracket => "BO",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static AngelOneProducts ToProduct(this string product)
		=> product?.ToUpperInvariant() switch
		{
			"CARRYFORWARD" => AngelOneProducts.CarryForward,
			"MARGIN" => AngelOneProducts.Margin,
			"INTRADAY" => AngelOneProducts.Intraday,
			"BO" => AngelOneProducts.Bracket,
			_ => AngelOneProducts.Delivery,
		};

	public static string ToNative(this AngelOneOrderVarieties variety)
		=> variety switch
		{
			AngelOneOrderVarieties.Normal => "NORMAL",
			AngelOneOrderVarieties.StopLoss => "STOPLOSS",
			AngelOneOrderVarieties.Robo => "ROBO",
			AngelOneOrderVarieties.AfterMarket => "AMO",
			_ => throw new ArgumentOutOfRangeException(nameof(variety), variety, null),
		};

	public static AngelOneOrderVarieties ToVariety(this string variety)
		=> variety?.ToUpperInvariant() switch
		{
			"STOPLOSS" => AngelOneOrderVarieties.StopLoss,
			"ROBO" => AngelOneOrderVarieties.Robo,
			"AMO" => AngelOneOrderVarieties.AfterMarket,
			_ => AngelOneOrderVarieties.Normal,
		};

	public static string ToNative(this Sides side) => side == Sides.Buy ? "BUY" : "SELL";
	public static Sides ToSide(this string side) => side.EqualsIgnoreCase("BUY") ? Sides.Buy : Sides.Sell;
	public static string ToNative(this TimeInForce? timeInForce) => timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";
	public static TimeInForce ToTimeInForce(this string duration) => duration.EqualsIgnoreCase("IOC") ? TimeInForce.CancelBalance : TimeInForce.PutInQueue;

	public static string ToNative(this OrderTypes orderType, decimal? triggerPrice)
		=> orderType switch
		{
			OrderTypes.Market when triggerPrice is > 0 => "STOPLOSS_MARKET",
			OrderTypes.Market => "MARKET",
			OrderTypes.Limit when triggerPrice is > 0 => "STOPLOSS_LIMIT",
			OrderTypes.Limit => "LIMIT",
			OrderTypes.Conditional when triggerPrice is > 0 => "STOPLOSS_LIMIT",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unsupported Angel One order type."),
		};

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"LIMIT" => OrderTypes.Limit,
			"STOPLOSS_LIMIT" or "STOPLOSS_MARKET" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string status)
	{
		status = status?.ToLowerInvariant();
		if (status is "complete" or "cancelled" or "canceled")
			return OrderStates.Done;
		if (status is "rejected" or "cancel rejected" or "modify rejected")
			return OrderStates.Failed;
		return OrderStates.Active;
	}

	public static decimal? ToDecimal(this string value)
	{
		if (value.IsEmpty())
			return null;
		return decimal.TryParse(value.Replace(" ", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
	}

	public static DateTime? ToAngelTime(this string value, DateTime? date = null)
	{
		if (value.IsEmpty())
			return null;

		if (DateTimeOffset.TryParseExact(value, "yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var timestamp))
			return timestamp.UtcDateTime;

		if (DateTime.TryParseExact(value,
			["dd-MMM-yyyy HH:mm:ss", "dd-MMM-yyyy H:mm:ss", "yyyy-MM-dd HH:mm:ss"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
			return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromMinutes(330)).UtcDateTime;

		if (date is not null && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time))
		{
			var localDate = date.Value.Kind == DateTimeKind.Unspecified
				? date.Value.Date
				: new DateTimeOffset(date.Value.ToUniversalTime()).ToOffset(TimeSpan.FromMinutes(330)).Date;
			return new DateTimeOffset(DateTime.SpecifyKind(localDate + time, DateTimeKind.Unspecified), TimeSpan.FromMinutes(330)).UtcDateTime;
		}

		return null;
	}

	public static (string interval, int maxDays) ToNative(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			var value when value == TimeSpan.FromMinutes(1) => ("ONE_MINUTE", 30),
			var value when value == TimeSpan.FromMinutes(3) => ("THREE_MINUTE", 60),
			var value when value == TimeSpan.FromMinutes(5) => ("FIVE_MINUTE", 100),
			var value when value == TimeSpan.FromMinutes(10) => ("TEN_MINUTE", 100),
			var value when value == TimeSpan.FromMinutes(15) => ("FIFTEEN_MINUTE", 200),
			var value when value == TimeSpan.FromMinutes(30) => ("THIRTY_MINUTE", 200),
			var value when value == TimeSpan.FromHours(1) => ("ONE_HOUR", 400),
			var value when value == TimeSpan.FromDays(1) => ("ONE_DAY", 2000),
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Angel One candle interval."),
		};
}

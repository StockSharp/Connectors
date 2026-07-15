namespace StockSharp.Upstox.Native;

static class Extensions
{
	public static string ToNative(this UpstoxProducts product)
		=> product switch
		{
			UpstoxProducts.Delivery => "D",
			UpstoxProducts.Intraday => "I",
			UpstoxProducts.Margin => "MTF",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static string ToNative(this UpstoxFeedModes mode)
		=> mode switch
		{
			UpstoxFeedModes.Ltpc => "ltpc",
			UpstoxFeedModes.Full => "full",
			UpstoxFeedModes.OptionGreeks => "option_greeks",
			UpstoxFeedModes.FullD30 => "full_d30",
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
		};

	public static SecurityId ToSecurityId(this UpstoxInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.TradingSymbol.IsEmpty() ? instrument.InstrumentKey : instrument.TradingSymbol,
			BoardCode = instrument.Segment.IsEmpty() ? instrument.Exchange : instrument.Segment,
			Native = instrument.InstrumentKey,
		};

	public static SecurityId ToUpstoxSecurityId(this string instrumentKey, string tradingSymbol = null, string exchange = null)
	{
		var separator = instrumentKey?.IndexOf('|') ?? -1;
		return new()
		{
			SecurityCode = tradingSymbol.IsEmpty() ? instrumentKey : tradingSymbol,
			BoardCode = exchange.IsEmpty() && separator > 0 ? instrumentKey[..separator] : exchange,
			Native = instrumentKey,
		};
	}

	public static string ToInstrumentKey(this SecurityId securityId)
	{
		if (securityId.Native is string native && !native.IsEmpty())
			return native;
		if (securityId.SecurityCode?.Contains('|') == true)
			return securityId.SecurityCode;

		throw new InvalidOperationException("Upstox instrument key is missing. Select the security through the Upstox lookup so SecurityId.Native contains the instrument_key.");
	}

	public static SecurityTypes ToSecurityType(this UpstoxInstrument instrument)
	{
		if (instrument.Segment?.EndsWith("_INDEX", StringComparison.OrdinalIgnoreCase) == true || instrument.InstrumentType.EqualsIgnoreCase("INDEX"))
			return SecurityTypes.Index;
		if (instrument.InstrumentType is "CE" or "PE")
			return SecurityTypes.Option;
		if (instrument.InstrumentType.EqualsIgnoreCase("FUT"))
			return SecurityTypes.Future;
		if (instrument.AssetType.EqualsIgnoreCase("CUR") || instrument.Segment?.StartsWith("NCD_", StringComparison.OrdinalIgnoreCase) == true || instrument.Segment?.StartsWith("BCD_", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Currency;
		if (instrument.Segment?.StartsWith("MCX_", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Commodity;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string instrumentType)
		=> instrumentType switch
		{
			"CE" => OptionTypes.Call,
			"PE" => OptionTypes.Put,
			_ => null,
		};

	public static string ToNative(this Sides side)
		=> side == Sides.Buy ? "BUY" : "SELL";

	public static Sides ToSide(this string side)
		=> side.EqualsIgnoreCase("BUY") ? Sides.Buy : Sides.Sell;

	public static string ToNative(this TimeInForce? timeInForce)
		=> timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";

	public static TimeInForce ToTimeInForce(this string validity)
		=> validity.EqualsIgnoreCase("IOC") ? TimeInForce.CancelBalance : TimeInForce.PutInQueue;

	public static string ToNative(this OrderTypes orderType, decimal? triggerPrice)
		=> orderType switch
		{
			OrderTypes.Market when triggerPrice is > 0 => "SL-M",
			OrderTypes.Market => "MARKET",
			OrderTypes.Limit when triggerPrice is > 0 => "SL",
			OrderTypes.Limit => "LIMIT",
			OrderTypes.Conditional when triggerPrice is > 0 => "SL",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unsupported Upstox order type."),
		};

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType switch
		{
			"MARKET" => OrderTypes.Market,
			"LIMIT" => OrderTypes.Limit,
			"SL" or "SL-M" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string status)
	{
		status = status?.ToLowerInvariant();
		if (status is "complete" or "cancelled")
			return OrderStates.Done;
		if (status is "rejected" or "modify rejected" or "cancel rejected")
			return OrderStates.Failed;
		return OrderStates.Active;
	}

	public static DateTime? ToUpstoxTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var offset))
			return offset.UtcDateTime;
		if (!DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
			return null;
		return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromMinutes(330)).UtcDateTime;
	}

	public static (string unit, int interval) ToNative(this TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromTicks(TimeHelper.TicksPerMonth))
			return ("months", 1);
		if (timeFrame == TimeSpan.FromDays(7))
			return ("weeks", 1);
		if (timeFrame == TimeSpan.FromDays(1))
			return ("days", 1);
		if (timeFrame.TotalHours is >= 1 and <= 5 && timeFrame.TotalHours == Math.Truncate(timeFrame.TotalHours))
			return ("hours", (int)timeFrame.TotalHours);
		if (timeFrame.TotalMinutes is >= 1 and <= 300 && timeFrame.TotalMinutes == Math.Truncate(timeFrame.TotalMinutes))
			return ("minutes", (int)timeFrame.TotalMinutes);

		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Upstox V3 candle interval.");
	}
}

namespace StockSharp.MiraeSharekhan;

internal static class MiraeSharekhanExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static string ToNative(this TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromMinutes(1)) return "1minute";
		if (timeFrame == TimeSpan.FromMinutes(5)) return "5minute";
		if (timeFrame == TimeSpan.FromMinutes(15)) return "15minute";
		if (timeFrame == TimeSpan.FromMinutes(30)) return "30minute";
		if (timeFrame == TimeSpan.FromHours(1)) return "60minute";
		if (timeFrame == TimeSpan.FromDays(1)) return "daily";
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static string ToNative(this MiraeSharekhanProducts product)
		=> product switch
		{
			MiraeSharekhanProducts.Investment => "INVESTMENT",
			MiraeSharekhanProducts.BigTrade => "BIGTRADE",
			MiraeSharekhanProducts.BigTradePlus => "BIGTRADEPLUS",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static MiraeSharekhanProducts? ToProduct(this string value)
		=> value?.Replace("+", "PLUS", StringComparison.Ordinal).ToUpperInvariant() switch
		{
			"INV" or "INVESTMENT" => MiraeSharekhanProducts.Investment,
			"BT" or "BIGTRADE" => MiraeSharekhanProducts.BigTrade,
			"BTPLUS" or "BIGTRADEPLUS" => MiraeSharekhanProducts.BigTradePlus,
			_ => null,
		};

	public static string ToNative(this MiraeSharekhanInstrumentTypes type)
		=> type switch
		{
			MiraeSharekhanInstrumentTypes.Equity => "EQ",
			MiraeSharekhanInstrumentTypes.StockFuture => "FS",
			MiraeSharekhanInstrumentTypes.IndexFuture => "FI",
			MiraeSharekhanInstrumentTypes.StockOption => "OS",
			MiraeSharekhanInstrumentTypes.IndexOption => "OI",
			MiraeSharekhanInstrumentTypes.CurrencyFuture => "FUTCUR",
			MiraeSharekhanInstrumentTypes.CurrencyOption => "OPTCUR",
			MiraeSharekhanInstrumentTypes.CommodityFuture => "FUTCOM",
			MiraeSharekhanInstrumentTypes.CommodityOption => "OPTCOM",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static MiraeSharekhanInstrumentTypes? ToInstrumentType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"EQ" or "EQUITY" => MiraeSharekhanInstrumentTypes.Equity,
			"FS" or "FUTSTK" => MiraeSharekhanInstrumentTypes.StockFuture,
			"FI" or "FUTIDX" => MiraeSharekhanInstrumentTypes.IndexFuture,
			"OS" or "OPTSTK" => MiraeSharekhanInstrumentTypes.StockOption,
			"OI" or "OPTIDX" => MiraeSharekhanInstrumentTypes.IndexOption,
			"FUTCUR" => MiraeSharekhanInstrumentTypes.CurrencyFuture,
			"OPTCUR" => MiraeSharekhanInstrumentTypes.CurrencyOption,
			"FUTCOM" or "FUTCOMMODITY" => MiraeSharekhanInstrumentTypes.CommodityFuture,
			"OPTCOM" or "OPTCOMMODITY" => MiraeSharekhanInstrumentTypes.CommodityOption,
			_ => null,
		};

	public static string ToNative(this Sides side)
		=> side == Sides.Buy ? "B" : "S";

	public static Sides ToSide(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"B" or "BUY" or "BM" => Sides.Buy,
			_ => Sides.Sell,
		};

	public static OrderStates ToOrderState(this string status)
	{
		var value = status?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
		if (value is "COMPLETE" or "COMPLETED" or "TRADED" or "EXECUTED" or "CANCELLED" or "CANCELED")
			return OrderStates.Done;
		if (value is "REJECTED" or "FAILED" or "ERROR")
			return OrderStates.Failed;
		if (value is "OPEN" or "ACTIVE" or "PENDING" or "PARTIALLYFILLED" or "PARTTRADED")
			return OrderStates.Active;
		return OrderStates.Pending;
	}

	public static SecurityTypes? ToSecurityType(this MiraeSharekhanInstrument instrument)
	{
		var type = instrument.InstrumentType.ToInstrumentType();
		if (type is MiraeSharekhanInstrumentTypes.StockFuture or MiraeSharekhanInstrumentTypes.IndexFuture or
			MiraeSharekhanInstrumentTypes.CurrencyFuture or MiraeSharekhanInstrumentTypes.CommodityFuture)
			return SecurityTypes.Future;
		if (type is MiraeSharekhanInstrumentTypes.StockOption or MiraeSharekhanInstrumentTypes.IndexOption or
			MiraeSharekhanInstrumentTypes.CurrencyOption or MiraeSharekhanInstrumentTypes.CommodityOption)
			return SecurityTypes.Option;
		if (instrument.InstrumentType.ContainsIgnoreCase("INDEX"))
			return SecurityTypes.Index;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"CE" or "CALL" => OptionTypes.Call,
			"PE" or "PUT" => OptionTypes.Put,
			_ => null,
		};

	public static DateTime? ParseIndiaTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		if (DateTime.TryParseExact(value,
			["dd/MM/yyyy", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
			return new DateTimeOffset(DateTime.SpecifyKind(exact, DateTimeKind.Unspecified),
				TimeSpan.FromHours(5.5)).UtcDateTime;
		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
		{
			if (epoch > 10_000_000_000)
				return DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime;
			if (epoch > 1_000_000_000)
				return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
		}
		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces,
			out var offset) && (value.Contains('+') || value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)))
			return offset.UtcDateTime;
		if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
			return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
				TimeSpan.FromHours(5.5)).UtcDateTime;
		return null;
	}

	public static string ToStreamKey(this string exchange, long scripCode)
		=> exchange.ToUpperInvariant() + scripCode.ToString(CultureInfo.InvariantCulture);

	public static string ToNativeExchange(this string boardCode)
		=> boardCode?.ToUpperInvariant() switch
		{
			"NSE" or "NSE_EQ" => "NC",
			"BSE" or "BSE_EQ" => "BC",
			"NFO" or "NSE_FNO" => "NF",
			"BFO" or "BSE_FNO" => "BF",
			"CDS" or "NSE_CURRENCY" => "RN",
			"BCD" or "BSE_CURRENCY" => "RB",
			"MCX" or "MCX_COMM" => "MX",
			_ => boardCode?.ToUpperInvariant(),
		};
}

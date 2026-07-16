namespace StockSharp.Zerodha;

internal static class ZerodhaExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static string ToNative(this TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromMinutes(1)) return "minute";
		if (timeFrame == TimeSpan.FromMinutes(3)) return "3minute";
		if (timeFrame == TimeSpan.FromMinutes(5)) return "5minute";
		if (timeFrame == TimeSpan.FromMinutes(10)) return "10minute";
		if (timeFrame == TimeSpan.FromMinutes(15)) return "15minute";
		if (timeFrame == TimeSpan.FromMinutes(30)) return "30minute";
		if (timeFrame == TimeSpan.FromHours(1)) return "60minute";
		if (timeFrame == TimeSpan.FromDays(1)) return "day";
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static string ToNative(this ZerodhaProducts product)
		=> product switch
		{
			ZerodhaProducts.CashAndCarry => "CNC",
			ZerodhaProducts.Normal => "NRML",
			ZerodhaProducts.Intraday => "MIS",
			ZerodhaProducts.Cover => "CO",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static ZerodhaProducts? ToProduct(this string product)
		=> product?.ToUpperInvariant() switch
		{
			"CNC" => ZerodhaProducts.CashAndCarry,
			"NRML" => ZerodhaProducts.Normal,
			"MIS" => ZerodhaProducts.Intraday,
			"CO" => ZerodhaProducts.Cover,
			_ => null,
		};

	public static string ToNative(this ZerodhaOrderVarieties variety)
		=> variety switch
		{
			ZerodhaOrderVarieties.Regular => "regular",
			ZerodhaOrderVarieties.AfterMarket => "amo",
			ZerodhaOrderVarieties.Cover => "co",
			ZerodhaOrderVarieties.Iceberg => "iceberg",
			ZerodhaOrderVarieties.Auction => "auction",
			_ => throw new ArgumentOutOfRangeException(nameof(variety), variety, null),
		};

	public static ZerodhaOrderVarieties ToVariety(this string variety)
		=> variety?.ToLowerInvariant() switch
		{
			"amo" => ZerodhaOrderVarieties.AfterMarket,
			"co" => ZerodhaOrderVarieties.Cover,
			"iceberg" => ZerodhaOrderVarieties.Iceberg,
			"auction" => ZerodhaOrderVarieties.Auction,
			_ => ZerodhaOrderVarieties.Regular,
		};

	public static SecurityId ToSecurityId(this string symbol, string exchange, long? token = null)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = exchange,
			Native = token,
		};

	public static SecurityTypes? ToSecurityType(this KiteInstrument instrument)
		=> instrument.InstrumentType?.ToUpperInvariant() switch
		{
			"EQ" => SecurityTypes.Stock,
			"FUT" => SecurityTypes.Future,
			"CE" or "PE" => SecurityTypes.Option,
			"MF" => SecurityTypes.Fund,
			_ when instrument.Segment.ContainsIgnoreCase("INDICES") => SecurityTypes.Index,
			_ => null,
		};

	public static OptionTypes? ToOptionType(this string instrumentType)
		=> instrumentType?.ToUpperInvariant() switch
		{
			"CE" => OptionTypes.Call,
			"PE" => OptionTypes.Put,
			_ => null,
		};

	public static DateTime? ParseKiteTime(this string value)
	{
		if (value.IsEmpty())
			return null;

		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var withOffset) &&
			(value.Contains('+') || value.EndsWith('Z')))
			return withOffset.UtcDateTime;

		if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var local))
			return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
				TimeSpan.FromHours(5.5)).UtcDateTime;
		return null;
	}

	public static OrderStates ToOrderState(this string status)
	{
		var value = status?.ToUpperInvariant();
		if (value is "COMPLETE" or "CANCELLED")
			return OrderStates.Done;
		if (value == "REJECTED")
			return OrderStates.Failed;
		if (value?.Contains("PENDING", StringComparison.Ordinal) == true || value == "OPEN" ||
			value == "TRIGGER PENDING")
			return OrderStates.Active;
		return OrderStates.Pending;
	}

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"LIMIT" => OrderTypes.Limit,
			"SL" or "SL-M" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce? ToTimeInForce(this string validity)
		=> validity?.ToUpperInvariant() switch
		{
			"DAY" or "TTL" => TimeInForce.PutInQueue,
			"IOC" => TimeInForce.CancelBalance,
			_ => null,
		};

	public static long? ToLongId(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
}

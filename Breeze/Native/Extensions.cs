namespace StockSharp.Breeze.Native;

static class Extensions
{
	private static readonly TimeSpan _indiaOffset = TimeSpan.FromHours(5.5);

	public static string ToNative(this BreezeProducts product)
		=> product.GetAttributeOfType<EnumMemberAttribute>()?.Value ?? product.ToString().ToLowerInvariant();

	public static BreezeProducts ToProduct(this BreezeInstrument instrument)
		=> instrument.Kind switch
		{
			BreezeInstrumentKinds.Future => BreezeProducts.Futures,
			BreezeInstrumentKinds.Option => BreezeProducts.Options,
			_ => BreezeProducts.Cash,
		};

	public static string ToExchangeCode(this BreezeInstrument instrument)
		=> instrument.BoardCode.EqualsIgnoreCase("NFO") ? "NFO" : "NSE";

	public static string ToNative(this Sides side) => side == Sides.Buy ? "buy" : "sell";

	public static string ToNative(this TimeInForce? timeInForce)
		=> timeInForce == TimeInForce.MatchOrCancel ? "ioc" : "day";

	public static Sides ToSide(this string value)
		=> value.EqualsIgnoreCase("buy") || value.EqualsIgnoreCase("b") ? Sides.Buy : Sides.Sell;

	public static TimeInForce ToTimeInForce(this string value)
		=> value.EqualsIgnoreCase("ioc") || value.EqualsIgnoreCase("i") ? TimeInForce.MatchOrCancel : TimeInForce.PutInQueue;

	public static OrderStates ToOrderState(this string value)
	{
		if (value.IsEmpty())
			return OrderStates.None;
		if (value.ContainsIgnoreCase("executed") || value.EqualsIgnoreCase("completed") || value.EqualsIgnoreCase("e"))
			return OrderStates.Done;
		if (value.ContainsIgnoreCase("cancel") || value.ContainsIgnoreCase("expired") || value.EqualsIgnoreCase("c") || value.EqualsIgnoreCase("x"))
			return OrderStates.Done;
		if (value.ContainsIgnoreCase("reject") || value.ContainsIgnoreCase("failed") || value.EqualsIgnoreCase("r") || value.EqualsIgnoreCase("j"))
			return OrderStates.Failed;
		return OrderStates.Active;
	}

	public static SecurityTypes ToSecurityType(this BreezeInstrument instrument)
		=> instrument.Kind switch
		{
			BreezeInstrumentKinds.Future => SecurityTypes.Future,
			BreezeInstrumentKinds.Option => SecurityTypes.Option,
			_ => SecurityTypes.Stock,
		};

	public static string ToNativeId(this BreezeInstrument instrument)
		=> string.Join("|", instrument.BoardCode, instrument.Token, instrument.StockCode,
			instrument.Kind.ToString(), instrument.ExpiryDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
			instrument.OptionType?.ToString() ?? string.Empty,
			instrument.StrikePrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

	public static SecurityId ToSecurityId(this BreezeInstrument instrument)
	{
		var code = instrument.Kind switch
		{
			BreezeInstrumentKinds.Future => $"{instrument.StockCode}-{instrument.ExpiryDate:yyyyMMdd}-FUT",
			BreezeInstrumentKinds.Option => $"{instrument.StockCode}-{instrument.ExpiryDate:yyyyMMdd}-{instrument.StrikePrice:0.########}-{(instrument.OptionType == OptionTypes.Call ? "CE" : "PE")}",
			_ => instrument.StockCode,
		};
		return new SecurityId { SecurityCode = code, BoardCode = instrument.BoardCode, Native = instrument.ToNativeId() };
	}

	public static BreezeInstrument ToBreezeInstrument(this SecurityId securityId)
	{
		var native = securityId.Native as string;
		if (native.IsEmpty())
			throw new InvalidOperationException($"Breeze native instrument identifier is missing for '{securityId}'. Run security lookup first.");
		var parts = native.Split('|');
		if (parts.Length < 7 || parts[0].IsEmpty() || parts[1].IsEmpty() || parts[2].IsEmpty())
			throw new FormatException($"Invalid Breeze native instrument identifier '{native}'.");
		if (!Enum.TryParse(parts[3], true, out BreezeInstrumentKinds kind))
			throw new FormatException($"Invalid Breeze instrument kind '{parts[3]}'.");

		DateTime? expiry = null;
		if (!parts[4].IsEmpty())
		{
			if (!DateTime.TryParseExact(parts[4], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
				throw new FormatException($"Invalid Breeze expiry '{parts[4]}'.");
			expiry = date;
		}

		OptionTypes? optionType = null;
		if (!parts[5].IsEmpty() && Enum.TryParse(parts[5], true, out OptionTypes parsedOption))
			optionType = parsedOption;
		return new BreezeInstrument
		{
			BoardCode = parts[0],
			Token = parts[1],
			StockCode = parts[2],
			Kind = kind,
			ExpiryDate = expiry,
			OptionType = optionType,
			StrikePrice = decimal.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out var strike) ? strike : null,
		};
	}

	public static string ToStreamToken(this BreezeInstrument instrument, bool depth)
		=> $"4.{(depth ? 2 : 1)}!{instrument.Token}";

	public static string ToHistoryInterval(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalSeconds: 1 } => "1second",
			{ TotalMinutes: 1 } => "1minute",
			{ TotalMinutes: 5 } => "5minute",
			{ TotalMinutes: 30 } => "30minute",
			{ TotalDays: 1 } => "1day",
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Breeze supports 1 second, 1 minute, 5 minute, 30 minute, and 1 day candles."),
		};

	public static string ToOhlcEvent(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalSeconds: 1 } => "1SEC",
			{ TotalMinutes: 1 } => "1MIN",
			{ TotalMinutes: 5 } => "5MIN",
			{ TotalMinutes: 30 } => "30MIN",
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Breeze live OHLC supports 1 second, 1 minute, 5 minute, and 30 minute candles."),
		};

	public static DateTime ToIndiaTime(this DateTime time)
		=> time.ToUniversalTime().Add(_indiaOffset);

	public static DateTime FromIndiaTime(this DateTime time)
		=> DateTime.SpecifyKind(time, DateTimeKind.Unspecified).Subtract(_indiaOffset).SpecifyKind(DateTimeKind.Utc);

	public static DateTime? ParseBreezeTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		var formats = new[] { "dd-MMM-yyyy HH:mm:ss", "dd-MMM-yyyy HH:mm", "dd-MMM-yyyy", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-ddTHH:mm:ssZ" };
		return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var result)
			? result.FromIndiaTime()
			: null;
	}

	private static DateTime SpecifyKind(this DateTime value, DateTimeKind kind) => DateTime.SpecifyKind(value, kind);
}

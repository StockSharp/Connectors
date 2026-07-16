namespace StockSharp.Groww;

static class GrowwExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromTicks(TimeHelper.TicksPerMonth),
	];

	public static SecurityId ToSecurityId(this GrowwInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.TradingSymbol,
			BoardCode = instrument.Exchange,
			Native = GrowwSecurityInfo.FromInstrument(instrument).ToNative(),
		};

	public static SecurityTypes ToSecurityType(this GrowwInstrument instrument)
		=> instrument.InstrumentType?.ToUpperInvariant() switch
		{
			"IDX" or "INDEX" => SecurityTypes.Index,
			"FUT" or "FUTURE" => SecurityTypes.Future,
			"CE" or "PE" or "OPTION" => SecurityTypes.Option,
			"ETF" => SecurityTypes.Fund,
			_ when instrument.Segment.EqualsIgnoreCase("COMMODITY") => SecurityTypes.Commodity,
			_ => SecurityTypes.Stock,
		};

	public static OptionTypes? ToOptionType(this string instrumentType)
		=> instrumentType?.ToUpperInvariant() switch
		{
			"CE" => OptionTypes.Call,
			"PE" => OptionTypes.Put,
			_ => null,
		};

	public static GrowwSecurityInfo ToGroww(this SecurityId securityId, SecurityTypes? securityType = null)
	{
		if (GrowwSecurityInfo.TryParse(securityId.Native, out var info))
			return info;

		var board = securityId.BoardCode?.ToUpperInvariant() ?? string.Empty;
		var exchange = board.StartsWith("BSE", StringComparison.Ordinal) ? "BSE"
			: board.StartsWith("MCX", StringComparison.Ordinal) ? "MCX"
			: "NSE";
		var segment = board.Contains("FNO", StringComparison.Ordinal) || securityType is SecurityTypes.Future or SecurityTypes.Option
			? "FNO"
			: board.Contains("COMM", StringComparison.Ordinal) || exchange == "MCX"
				? "COMMODITY"
				: "CASH";
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));

		return new()
		{
			Exchange = exchange,
			Segment = segment,
			TradingSymbol = code,
			GrowwSymbol = $"{exchange}-{code}",
			InstrumentType = securityType switch
			{
				SecurityTypes.Index => "IDX",
				SecurityTypes.Future => "FUT",
				SecurityTypes.Option => "OPTION",
				_ => "EQ",
			},
		};
	}

	public static string ToNative(this GrowwProducts product)
		=> product switch
		{
			GrowwProducts.Delivery => "CNC",
			GrowwProducts.Intraday => "MIS",
			GrowwProducts.Normal => "NRML",
			GrowwProducts.MarginTradingFacility => "MTF",
			GrowwProducts.Arbitrage => "ARB",
			GrowwProducts.Cover => "CO",
			GrowwProducts.Bracket => "BO",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static string ToNative(this GrowwValidities validity)
		=> validity switch
		{
			GrowwValidities.Day => "DAY",
			GrowwValidities.ImmediateOrCancel => "IOC",
			GrowwValidities.EndOfSession => "EOS",
			GrowwValidities.GoodTillCancelled => "GTC",
			GrowwValidities.GoodTillDate => "GTD",
			_ => throw new ArgumentOutOfRangeException(nameof(validity), validity, null),
		};

	public static string ToGrowwValidity(this TimeInForce? timeInForce)
		=> timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";

	public static TimeInForce ToTimeInForce(this string validity)
		=> validity.EqualsIgnoreCase("IOC") ? TimeInForce.CancelBalance : TimeInForce.PutInQueue;

	public static string ToNative(this Sides side) => side == Sides.Buy ? "BUY" : "SELL";
	public static Sides ToSide(this string side) => side.EqualsIgnoreCase("BUY") || side.EqualsIgnoreCase("B") ? Sides.Buy : Sides.Sell;

	public static string ToGrowwOrderType(this OrderTypes orderType, decimal? triggerPrice)
		=> orderType switch
		{
			OrderTypes.Market when triggerPrice is > 0 => "SL_M",
			OrderTypes.Market => "MARKET",
			OrderTypes.Limit when triggerPrice is > 0 => "SL",
			OrderTypes.Limit => "LIMIT",
			OrderTypes.Conditional when triggerPrice is > 0 => "SL",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unsupported Groww order type."),
		};

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MARKET" or "MKT" => OrderTypes.Market,
			"SL" or "SL_M" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"REJECTED" or "FAILED" => OrderStates.Failed,
			"EXECUTED" or "DELIVERY_AWAITED" or "CANCELLED" or "COMPLETED" => OrderStates.Done,
			_ => OrderStates.Active,
		};
}

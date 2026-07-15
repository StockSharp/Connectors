namespace StockSharp.TastyTrade;

static class Extensions
{
	public static string ToScope(this TastyTradeScopes scopes)
	{
		var values = new List<string>();
		if (scopes.HasFlag(TastyTradeScopes.Read))
			values.Add("read");
		if (scopes.HasFlag(TastyTradeScopes.Trade))
			values.Add("trade");
		return values.Join(" ");
	}

	public static SecurityTypes ToSecurityType(this TastyInstrumentTypes type)
		=> type switch
		{
			TastyInstrumentTypes.Cryptocurrency => SecurityTypes.CryptoCurrency,
			TastyInstrumentTypes.Equity => SecurityTypes.Stock,
			TastyInstrumentTypes.EquityOption or TastyInstrumentTypes.FutureOption => SecurityTypes.Option,
			TastyInstrumentTypes.Future => SecurityTypes.Future,
			_ => SecurityTypes.Stock,
		};

	public static TastyInstrumentTypes ToNative(this SecurityTypes? type, string symbol)
		=> type switch
		{
			SecurityTypes.CryptoCurrency => TastyInstrumentTypes.Cryptocurrency,
			SecurityTypes.Option => symbol?.StartsWith("./", StringComparison.Ordinal) == true ? TastyInstrumentTypes.FutureOption : TastyInstrumentTypes.EquityOption,
			SecurityTypes.Future => TastyInstrumentTypes.Future,
			_ => TastyInstrumentTypes.Equity,
		};

	public static TastyInstrumentTypes ToNative(this SecurityTypes type, string symbol)
		=> ((SecurityTypes?)type).ToNative(symbol);

	public static TastyLegActions ToNative(this Sides side, OrderPositionEffects? effect, TastyInstrumentTypes type)
	{
		if (type == TastyInstrumentTypes.Future)
			return side == Sides.Buy ? TastyLegActions.Buy : TastyLegActions.Sell;
		return (side, effect) switch
		{
			(Sides.Buy, OrderPositionEffects.CloseOnly) => TastyLegActions.BuyToClose,
			(Sides.Sell, OrderPositionEffects.CloseOnly) => TastyLegActions.SellToClose,
			(Sides.Sell, _) => TastyLegActions.SellToOpen,
			_ => TastyLegActions.BuyToOpen,
		};
	}

	public static Sides ToSide(this TastyLegActions action)
		=> action is TastyLegActions.Sell or TastyLegActions.SellToClose or TastyLegActions.SellToOpen ? Sides.Sell : Sides.Buy;

	public static TastyOrderTypes ToNative(this OrderTypes? type, decimal? stopPrice)
	{
		if (stopPrice is not null)
			return type == OrderTypes.Market ? TastyOrderTypes.Stop : TastyOrderTypes.StopLimit;
		return type == OrderTypes.Market ? TastyOrderTypes.Market : TastyOrderTypes.Limit;
	}

	public static OrderTypes ToOrderType(this TastyOrderTypes type)
		=> type is TastyOrderTypes.Market or TastyOrderTypes.NotionalMarket or TastyOrderTypes.Stop ? OrderTypes.Market : OrderTypes.Limit;

	public static TastyTimeInForces ToNative(this TimeInForce? value, DateTime? tillDate, TastyTradeOrderCondition condition)
	{
		if (value == TimeInForce.MatchOrCancel)
			return TastyTimeInForces.ImmediateOrCancel;
		if (tillDate is not null)
			return TastyTimeInForces.GoodTillDate;
		if (value == TimeInForce.CancelBalance)
			return condition?.IsOvernight == true ? TastyTimeInForces.GoodTillCancelledExtendedOvernight
				: condition?.IsExtendedHours == true ? TastyTimeInForces.GoodTillCancelledExtended
				: TastyTimeInForces.GoodTillCancelled;
		return condition?.IsOvernight == true ? TastyTimeInForces.ExtendedOvernight
			: condition?.IsExtendedHours == true ? TastyTimeInForces.Extended
			: TastyTimeInForces.Day;
	}

	public static TimeInForce ToTimeInForce(this TastyTimeInForces value)
		=> value switch
		{
			TastyTimeInForces.ImmediateOrCancel => TimeInForce.MatchOrCancel,
			TastyTimeInForces.GoodTillCancelled or TastyTimeInForces.GoodTillCancelledExtended or TastyTimeInForces.GoodTillCancelledExtendedOvernight => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToOrderState(this TastyOrderStatuses status)
		=> status switch
		{
			TastyOrderStatuses.Filled => OrderStates.Done,
			TastyOrderStatuses.Cancelled or TastyOrderStatuses.Canceled or TastyOrderStatuses.Expired or TastyOrderStatuses.Removed or TastyOrderStatuses.PartiallyRemoved => OrderStates.Done,
			TastyOrderStatuses.Rejected => OrderStates.Failed,
			TastyOrderStatuses.Live or TastyOrderStatuses.Routed or TastyOrderStatuses.CancelRequested or TastyOrderStatuses.ReplaceRequested => OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static string ToBoardCode(this TastyInstrument instrument)
		=> instrument?.ListedMarket.IsEmpty(instrument?.Exchange).IsEmpty("TASTYTRADE").ToUpperInvariant();

	public static DateTime ToUtcTime(this long? value)
		=> value is > 0 ? value.Value.FromUnix(false) : DateTime.UtcNow;

	public static string ToDxPeriod(this TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromDays(7))
			return "1w";
		if (timeFrame.TotalDays >= 1 && timeFrame.TotalDays % 1 == 0)
			return $"{(int)timeFrame.TotalDays}d";
		if (timeFrame.TotalHours >= 1 && timeFrame.TotalHours % 1 == 0)
			return $"{(int)timeFrame.TotalHours}h";
		if (timeFrame.TotalMinutes >= 1 && timeFrame.TotalMinutes % 1 == 0)
			return $"{(int)timeFrame.TotalMinutes}m";
		if (timeFrame.TotalSeconds >= 1 && timeFrame.TotalSeconds % 1 == 0)
			return $"{(int)timeFrame.TotalSeconds}s";
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static TimeSpan FromDxPeriod(this string symbol)
	{
		var start = symbol.LastIndexOf("{=", StringComparison.Ordinal);
		var end = symbol.LastIndexOf('}');
		if (start < 0 || end <= start + 2)
			throw new FormatException($"DXLink candle symbol '{symbol}' does not contain a period.");
		var period = symbol.Substring(start + 2, end - start - 2);
		var suffixLength = period.EndsWith("mo", StringComparison.Ordinal) ? 2 : 1;
		var value = period[..^suffixLength].To<int>();
		return period[^suffixLength..] switch
		{
			"s" => TimeSpan.FromSeconds(value),
			"m" => TimeSpan.FromMinutes(value),
			"h" => TimeSpan.FromHours(value),
			"d" => TimeSpan.FromDays(value),
			"w" => TimeSpan.FromDays(value * 7),
			"mo" => TimeSpan.FromDays(value * 30),
			_ => throw new FormatException($"Unknown DXLink candle period '{period}'."),
		};
	}

	public static bool IsSnapshotComplete(this DxEvent data)
		=> (data.EventFlags & ((1 << 3) | (1 << 4))) != 0;
}

namespace StockSharp.SierraChartDtc;

internal static class SierraChartDtcExtensions
{
	public static DtcSecurityTypes ToNative(this SecurityTypes value)
		=> value switch
		{
			SecurityTypes.Future => DtcSecurityTypes.Futures,
			SecurityTypes.Stock => DtcSecurityTypes.Stock,
			SecurityTypes.Currency or SecurityTypes.CryptoCurrency => DtcSecurityTypes.Forex,
			SecurityTypes.Index => DtcSecurityTypes.Index,
			SecurityTypes.Option => DtcSecurityTypes.StockOption,
			SecurityTypes.Bond => DtcSecurityTypes.Bond,
			SecurityTypes.Fund => DtcSecurityTypes.MutualFund,
			_ => DtcSecurityTypes.Unset,
		};

	public static SecurityTypes? ToStockSharp(this DtcSecurityTypes value)
		=> value switch
		{
			DtcSecurityTypes.Futures or DtcSecurityTypes.FuturesStrategy => SecurityTypes.Future,
			DtcSecurityTypes.Stock => SecurityTypes.Stock,
			DtcSecurityTypes.Forex => SecurityTypes.Currency,
			DtcSecurityTypes.Index => SecurityTypes.Index,
			DtcSecurityTypes.StockOption or DtcSecurityTypes.FuturesOption or DtcSecurityTypes.IndexOption => SecurityTypes.Option,
			DtcSecurityTypes.Bond => SecurityTypes.Bond,
			DtcSecurityTypes.MutualFund => SecurityTypes.Fund,
			_ => null,
		};

	public static DtcBuySells ToNative(this Sides side)
		=> side == Sides.Buy ? DtcBuySells.Buy : DtcBuySells.Sell;

	public static Sides ToStockSharp(this DtcBuySells side)
		=> side == DtcBuySells.Sell ? Sides.Sell : Sides.Buy;

	public static DtcTimeInForces ToNative(this TimeInForce? value, DateTime? tillDate)
		=> tillDate != null ? DtcTimeInForces.GoodTillDateTime : value switch
		{
			TimeInForce.CancelBalance => DtcTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => DtcTimeInForces.FillOrKill,
			_ => DtcTimeInForces.GoodTillCanceled,
		};

	public static TimeInForce? ToStockSharp(this DtcTimeInForces value)
		=> value switch
		{
			DtcTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			DtcTimeInForces.FillOrKill or DtcTimeInForces.AllOrNone => TimeInForce.MatchOrCancel,
			DtcTimeInForces.Day or DtcTimeInForces.GoodTillCanceled or DtcTimeInForces.GoodTillDateTime => TimeInForce.PutInQueue,
			_ => null,
		};

	public static OrderStates ToStockSharp(this DtcOrderStatuses value)
		=> value switch
		{
			DtcOrderStatuses.Filled => OrderStates.Done,
			DtcOrderStatuses.Canceled => OrderStates.Done,
			DtcOrderStatuses.Rejected => OrderStates.Failed,
			DtcOrderStatuses.Open or DtcOrderStatuses.PartiallyFilled => OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static SecurityStates? ToStockSharp(this DtcTradingStatuses value)
		=> value switch
		{
			DtcTradingStatuses.Open => SecurityStates.Trading,
			DtcTradingStatuses.PreOpen or DtcTradingStatuses.Closed or DtcTradingStatuses.Halted => SecurityStates.Stoped,
			_ => null,
		};

	public static DtcOrderTypes ToNative(this OrderTypes value, decimal? stopPrice, decimal price)
		=> value switch
		{
			OrderTypes.Market when stopPrice != null => DtcOrderTypes.Stop,
			OrderTypes.Limit when stopPrice != null => DtcOrderTypes.StopLimit,
			OrderTypes.Conditional when price > 0 => DtcOrderTypes.StopLimit,
			OrderTypes.Conditional => DtcOrderTypes.Stop,
			OrderTypes.Market => DtcOrderTypes.Market,
			OrderTypes.Limit => DtcOrderTypes.Limit,
			_ => throw new NotSupportedException($"DTC order type '{value}' is not supported."),
		};

	public static OrderTypes ToStockSharp(this DtcOrderTypes value)
		=> value switch
		{
			DtcOrderTypes.Market => OrderTypes.Market,
			DtcOrderTypes.Limit => OrderTypes.Limit,
			DtcOrderTypes.Stop or DtcOrderTypes.StopLimit or DtcOrderTypes.MarketIfTouched or DtcOrderTypes.LimitIfTouched => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	public static long? ToLongId(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
}

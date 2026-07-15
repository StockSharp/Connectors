namespace StockSharp.TradeStation.Native;

static class Extensions
{
	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static SecurityTypes ToSecurityType(this TradeStationAssetType type)
		=> type switch
		{
			TradeStationAssetType.Stock => SecurityTypes.Stock,
			TradeStationAssetType.StockOption or TradeStationAssetType.FutureOption or TradeStationAssetType.CurrencyOption or TradeStationAssetType.IndexOption => SecurityTypes.Option,
			TradeStationAssetType.Future => SecurityTypes.Future,
			TradeStationAssetType.Forex => SecurityTypes.Currency,
			TradeStationAssetType.Index => SecurityTypes.Index,
			TradeStationAssetType.Crypto => SecurityTypes.CryptoCurrency,
			_ => SecurityTypes.Stock,
		};

	public static Sides ToSide(this string action)
		=> action?.StartsWithIgnoreCase("Buy") == true ? Sides.Buy : Sides.Sell;

	public static OrderStates ToOrderState(this TradeStationOrderStatus status)
		=> status switch
		{
			TradeStationOrderStatus.Filled => OrderStates.Done,
			TradeStationOrderStatus.Canceled or TradeStationOrderStatus.Expired or TradeStationOrderStatus.Out or TradeStationOrderStatus.TradeServerCanceled => OrderStates.Done,
			TradeStationOrderStatus.Rejected or TradeStationOrderStatus.Broken => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static OrderTypes ToOrderType(this TradeStationOrderType type)
		=> type switch
		{
			TradeStationOrderType.Market => OrderTypes.Market,
			TradeStationOrderType.Limit => OrderTypes.Limit,
			_ => OrderTypes.Conditional,
		};

	public static TradeStationOrderType ToNative(this OrderTypes type, decimal? stopPrice)
		=> stopPrice is null
			? type == OrderTypes.Market ? TradeStationOrderType.Market : TradeStationOrderType.Limit
			: type == OrderTypes.Market ? TradeStationOrderType.StopMarket : TradeStationOrderType.StopLimit;

	public static TradeStationDuration ToNative(this TimeInForce timeInForce, DateTime? tillDate)
		=> tillDate is not null ? TradeStationDuration.GoodTillDate : timeInForce switch
		{
			TimeInForce.CancelBalance => TradeStationDuration.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => TradeStationDuration.FillOrKill,
			_ => TradeStationDuration.GoodTillCanceled,
		};

	public static TradeStationTradeAction ToNative(this Sides side, OrderPositionEffects? effect)
		=> (side, effect) switch
		{
			(Sides.Buy, OrderPositionEffects.OpenOnly) => TradeStationTradeAction.BuyToOpen,
			(Sides.Buy, OrderPositionEffects.CloseOnly) => TradeStationTradeAction.BuyToClose,
			(Sides.Sell, OrderPositionEffects.OpenOnly) => TradeStationTradeAction.SellToOpen,
			(Sides.Sell, OrderPositionEffects.CloseOnly) => TradeStationTradeAction.SellToClose,
			(Sides.Buy, _) => TradeStationTradeAction.Buy,
			_ => TradeStationTradeAction.Sell,
		};
}

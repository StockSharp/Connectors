namespace StockSharp.Rithmic.Native;

internal static class Extensions
{
	private static readonly DateTime _epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	public static DateTime ToDateTime(this int ssboe, int usecs)
		=> _epoch.AddSeconds(ssboe).AddTicks(usecs * 10);

	public static (int ssboe, int usecs) ToSsboe(this DateTime dt)
	{
		var span = dt - _epoch;
		var ssboe = (int)span.TotalSeconds;
		var usecs = (int)((span.Ticks % TimeSpan.TicksPerSecond) / 10);
		return (ssboe, usecs);
	}

	public static Sides ToSide(this RequestNewOrder.Types.TransactionType type) => type switch
	{
		RequestNewOrder.Types.TransactionType.Buy => Sides.Buy,
		RequestNewOrder.Types.TransactionType.Sell => Sides.Sell,
		_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
	};

	public static Sides ToSide(this RithmicOrderNotification.Types.TransactionType type) => type switch
	{
		RithmicOrderNotification.Types.TransactionType.Buy => Sides.Buy,
		RithmicOrderNotification.Types.TransactionType.Sell => Sides.Sell,
		RithmicOrderNotification.Types.TransactionType.Ss => Sides.Sell,
		_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
	};

	public static Sides ToSide(this ExchangeOrderNotification.Types.TransactionType type) => type switch
	{
		ExchangeOrderNotification.Types.TransactionType.Buy => Sides.Buy,
		ExchangeOrderNotification.Types.TransactionType.Sell => Sides.Sell,
		ExchangeOrderNotification.Types.TransactionType.Ss => Sides.Sell,
		_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
	};

	public static Sides? ToSide(this LastTrade.Types.TransactionType type) => type switch
	{
		LastTrade.Types.TransactionType.Buy => Sides.Buy,
		LastTrade.Types.TransactionType.Sell => Sides.Sell,
		_ => null,
	};

	public static RequestNewOrder.Types.TransactionType ToTransactionType(this Sides side) => side switch
	{
		Sides.Buy => RequestNewOrder.Types.TransactionType.Buy,
		Sides.Sell => RequestNewOrder.Types.TransactionType.Sell,
		_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
	};

	public static RequestNewOrder.Types.PriceType ToPriceType(this OrderTypes orderType) => orderType switch
	{
		OrderTypes.Limit => RequestNewOrder.Types.PriceType.Limit,
		OrderTypes.Market => RequestNewOrder.Types.PriceType.Market,
		_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, null),
	};

	public static RequestNewOrder.Types.Duration ToDuration(this TimeInForce? tif) => tif switch
	{
		TimeInForce.PutInQueue or null => RequestNewOrder.Types.Duration.Day,
		TimeInForce.CancelBalance => RequestNewOrder.Types.Duration.Ioc,
		TimeInForce.MatchOrCancel => RequestNewOrder.Types.Duration.Fok,
		_ => RequestNewOrder.Types.Duration.Day,
	};

	public static OrderTypes ToOrderType(this RithmicOrderNotification.Types.PriceType type) => type switch
	{
		RithmicOrderNotification.Types.PriceType.Limit => OrderTypes.Limit,
		RithmicOrderNotification.Types.PriceType.Market => OrderTypes.Market,
		RithmicOrderNotification.Types.PriceType.StopLimit => OrderTypes.Conditional,
		RithmicOrderNotification.Types.PriceType.StopMarket => OrderTypes.Conditional,
		_ => OrderTypes.Limit,
	};

	public static OrderStates ToOrderState(this RithmicOrderNotification.Types.NotifyType type) => type switch
	{
		RithmicOrderNotification.Types.NotifyType.Open or
		RithmicOrderNotification.Types.NotifyType.Modified => OrderStates.Active,

		RithmicOrderNotification.Types.NotifyType.Complete => OrderStates.Done,

		RithmicOrderNotification.Types.NotifyType.OrderRcvdFromClnt or
		RithmicOrderNotification.Types.NotifyType.OpenPending or
		RithmicOrderNotification.Types.NotifyType.ModifyPending or
		RithmicOrderNotification.Types.NotifyType.CancelPending or
		RithmicOrderNotification.Types.NotifyType.OrderSentToExch or
		RithmicOrderNotification.Types.NotifyType.TriggerPending => OrderStates.Pending,

		RithmicOrderNotification.Types.NotifyType.ModificationFailed or
		RithmicOrderNotification.Types.NotifyType.CancellationFailed => OrderStates.Failed,

		_ => OrderStates.None,
	};

	public static SecurityTypes? ToSecurityType(this string instrumentType)
	{
		if (instrumentType.IsEmptyOrWhiteSpace())
			return null;

		return instrumentType.ToUpperInvariant() switch
		{
			"FUTURE" => SecurityTypes.Future,
			"FUTURE OPTION" or "FUTURE_OPTION" => SecurityTypes.Option,
			"EQUITY" => SecurityTypes.Stock,
			"EQUITY OPTION" or "EQUITY_OPTION" => SecurityTypes.Option,
			"INDEX" => SecurityTypes.Index,
			"SPREAD" => SecurityTypes.Future,
			_ => null,
		};
	}
}

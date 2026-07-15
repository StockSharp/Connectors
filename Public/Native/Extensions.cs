namespace StockSharp.Public.Native;

static class Extensions
{
	public static PublicInstrumentTypes ToNative(this SecurityTypes type, string symbol)
		=> ((SecurityTypes?)type).ToNative(symbol);

	public static PublicInstrumentTypes ToNative(this SecurityTypes? type, string symbol)
		=> type switch
		{
			SecurityTypes.Option => PublicInstrumentTypes.Option,
			SecurityTypes.CryptoCurrency => PublicInstrumentTypes.Crypto,
			SecurityTypes.Index => PublicInstrumentTypes.Index,
			SecurityTypes.Bond => PublicInstrumentTypes.Bond,
			_ when symbol?.Contains(' ') == true => PublicInstrumentTypes.Option,
			_ => PublicInstrumentTypes.Equity,
		};

	public static SecurityTypes ToSecurityType(this PublicInstrumentTypes type)
		=> type switch
		{
			PublicInstrumentTypes.Option => SecurityTypes.Option,
			PublicInstrumentTypes.Crypto => SecurityTypes.CryptoCurrency,
			PublicInstrumentTypes.Index => SecurityTypes.Index,
			PublicInstrumentTypes.Bond or PublicInstrumentTypes.Treasury => SecurityTypes.Bond,
			_ => SecurityTypes.Stock,
		};

	public static PublicOrderSides ToNative(this Sides side)
		=> side == Sides.Buy ? PublicOrderSides.Buy : PublicOrderSides.Sell;

	public static Sides ToSide(this PublicOrderSides side)
		=> side == PublicOrderSides.Buy ? Sides.Buy : Sides.Sell;

	public static PublicOrderTypes ToNative(this OrderTypes? type, decimal? stopPrice)
		=> type == OrderTypes.Market
			? stopPrice is null ? PublicOrderTypes.Market : PublicOrderTypes.Stop
			: stopPrice is null ? PublicOrderTypes.Limit : PublicOrderTypes.StopLimit;

	public static OrderTypes ToOrderType(this PublicOrderTypes type)
		=> type is PublicOrderTypes.Market or PublicOrderTypes.Stop ? OrderTypes.Market : OrderTypes.Limit;

	public static OrderStates ToOrderState(this PublicOrderStatuses status)
		=> status switch
		{
			PublicOrderStatuses.Filled => OrderStates.Done,
			PublicOrderStatuses.Cancelled or PublicOrderStatuses.QueuedCancelled or PublicOrderStatuses.Rejected or PublicOrderStatuses.Expired or PublicOrderStatuses.Replaced => OrderStates.Failed,
			PublicOrderStatuses.New or PublicOrderStatuses.PartiallyFilled => OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static TimeInForce ToTimeInForce(this PublicTimeInForces value)
		=> TimeInForce.PutInQueue;
}

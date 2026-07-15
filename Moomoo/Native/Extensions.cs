namespace StockSharp.Moomoo.Native;

static class Extensions
{
	public static SecurityTypes ToSecurityType(this QotCommon.SecurityType type)
		=> type switch
		{
			QotCommon.SecurityType.SecurityType_Bond => SecurityTypes.Bond,
			QotCommon.SecurityType.SecurityType_Index => SecurityTypes.Index,
			QotCommon.SecurityType.SecurityType_Drvt => SecurityTypes.Option,
			QotCommon.SecurityType.SecurityType_Crypto => SecurityTypes.CryptoCurrency,
			_ => SecurityTypes.Stock,
		};

	public static IEnumerable<QotCommon.SecurityType> ToNativeTypes(this IEnumerable<SecurityTypes> types)
	{
		foreach (var type in types)
		{
			switch (type)
			{
				case SecurityTypes.Stock:
					yield return QotCommon.SecurityType.SecurityType_Eqty;
					yield return QotCommon.SecurityType.SecurityType_Trust;
					break;
				case SecurityTypes.Option:
					yield return QotCommon.SecurityType.SecurityType_Drvt;
					break;
				case SecurityTypes.Index:
					yield return QotCommon.SecurityType.SecurityType_Index;
					break;
				case SecurityTypes.Bond:
					yield return QotCommon.SecurityType.SecurityType_Bond;
					break;
				case SecurityTypes.CryptoCurrency:
					yield return QotCommon.SecurityType.SecurityType_Crypto;
					break;
			}
		}
	}

	public static string ToBoardCode(this QotCommon.ExchType exchange)
		=> exchange switch
		{
			QotCommon.ExchType.ExchType_US_NYSE => BoardCodes.Nyse,
			QotCommon.ExchType.ExchType_US_Nasdaq => BoardCodes.Nasdaq,
			QotCommon.ExchType.ExchType_US_AMEX => BoardCodes.Amex,
			QotCommon.ExchType.ExchType_US_Option => "OPRA",
			_ => "MOOMOO",
		};

	public static TrdCommon.TrdSide ToNative(this Sides side, OrderPositionEffects? effect)
		=> (side, effect) switch
		{
			(Sides.Sell, OrderPositionEffects.OpenOnly) => TrdCommon.TrdSide.TrdSide_SellShort,
			(Sides.Buy, OrderPositionEffects.CloseOnly) => TrdCommon.TrdSide.TrdSide_BuyBack,
			(Sides.Buy, _) => TrdCommon.TrdSide.TrdSide_Buy,
			_ => TrdCommon.TrdSide.TrdSide_Sell,
		};

	public static Sides ToSide(this TrdCommon.TrdSide side)
		=> side is TrdCommon.TrdSide.TrdSide_Buy or TrdCommon.TrdSide.TrdSide_BuyBack ? Sides.Buy : Sides.Sell;

	public static TrdCommon.OrderType ToNative(this OrderTypes? type, decimal? stopPrice)
		=> type == OrderTypes.Market
			? stopPrice is null ? TrdCommon.OrderType.OrderType_Market : TrdCommon.OrderType.OrderType_Stop
			: stopPrice is null ? TrdCommon.OrderType.OrderType_Normal : TrdCommon.OrderType.OrderType_StopLimit;

	public static OrderTypes ToOrderType(this TrdCommon.OrderType type)
		=> type is TrdCommon.OrderType.OrderType_Market or TrdCommon.OrderType.OrderType_Stop ? OrderTypes.Market : OrderTypes.Limit;

	public static OrderStates ToOrderState(this TrdCommon.OrderStatus status)
		=> status switch
		{
			TrdCommon.OrderStatus.OrderStatus_Submitted or TrdCommon.OrderStatus.OrderStatus_Filled_Part => OrderStates.Active,
			TrdCommon.OrderStatus.OrderStatus_Filled_All or TrdCommon.OrderStatus.OrderStatus_Cancelled_Part or
			TrdCommon.OrderStatus.OrderStatus_Cancelled_All or TrdCommon.OrderStatus.OrderStatus_Deleted or
			TrdCommon.OrderStatus.OrderStatus_FillCancelled => OrderStates.Done,
			TrdCommon.OrderStatus.OrderStatus_SubmitFailed or TrdCommon.OrderStatus.OrderStatus_TimeOut or
			TrdCommon.OrderStatus.OrderStatus_Failed => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static CurrencyTypes? ToCurrency(this TrdCommon.Currency value)
		=> value switch
		{
			TrdCommon.Currency.Currency_USD => CurrencyTypes.USD,
			TrdCommon.Currency.Currency_HKD => CurrencyTypes.HKD,
			TrdCommon.Currency.Currency_CNH => CurrencyTypes.CNY,
			TrdCommon.Currency.Currency_JPY => CurrencyTypes.JPY,
			TrdCommon.Currency.Currency_SGD => CurrencyTypes.SGD,
			TrdCommon.Currency.Currency_AUD => CurrencyTypes.AUD,
			TrdCommon.Currency.Currency_CAD => CurrencyTypes.CAD,
			_ => null,
		};

	public static Common.Session ToNative(this MoomooSessions session)
		=> session switch
		{
			MoomooSessions.Regular => Common.Session.Session_RTH,
			MoomooSessions.Extended => Common.Session.Session_ETH,
			MoomooSessions.Overnight => Common.Session.Session_OVERNIGHT,
			_ => Common.Session.Session_ALL,
		};
}

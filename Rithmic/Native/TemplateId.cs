namespace StockSharp.Rithmic.Native;

internal static class TemplateId
{
	// Login/Logout/Heartbeat
	public const int RequestLogin = 10;
	public const int ResponseLogin = 11;
	public const int RequestLogout = 12;
	public const int ResponseLogout = 13;
	public const int RequestRithmicSystemInfo = 16;
	public const int ResponseRithmicSystemInfo = 17;
	public const int RequestHeartbeat = 18;
	public const int ResponseHeartbeat = 19;

	// Market Data
	public const int RequestMarketDataUpdate = 100;
	public const int ResponseMarketDataUpdate = 101;
	public const int LastTrade = 150;
	public const int BestBidOffer = 151;
	public const int OrderBook = 152;
	public const int OpenInterest = 153;
	public const int MarketMode = 154;

	// Bars
	public const int RequestTimeBarUpdate = 200;
	public const int ResponseTimeBarUpdate = 201;
	public const int RequestTimeBarReplay = 202;
	public const int ResponseTimeBarReplay = 203;
	public const int RequestTickBarUpdate = 204;
	public const int ResponseTickBarUpdate = 205;
	public const int RequestTickBarReplay = 206;
	public const int ResponseTickBarReplay = 207;
	public const int TimeBar = 250;
	public const int TickBar = 251;

	// Reference Data
	public const int RequestReferenceData = 14;
	public const int ResponseReferenceData = 15;
	public const int RequestSearchSymbols = 26;
	public const int ResponseSearchSymbols = 27;
	public const int RequestProductCodes = 28;
	public const int ResponseProductCodes = 29;

	// Order Management (Order Plant)
	public const int RequestLoginInfo = 300;
	public const int ResponseLoginInfo = 301;
	public const int RequestAccountList = 302;
	public const int ResponseAccountList = 303;
	public const int RequestTradeRoutes = 310;
	public const int ResponseTradeRoutes = 311;
	public const int RequestNewOrder = 312;
	public const int ResponseNewOrder = 313;
	public const int RequestModifyOrder = 314;
	public const int ResponseModifyOrder = 315;
	public const int RequestCancelOrder = 316;
	public const int ResponseCancelOrder = 317;
	public const int RequestShowOrders = 320;
	public const int ResponseShowOrders = 321;
	public const int RequestSubscribeForOrderUpdates = 308;
	public const int ResponseSubscribeForOrderUpdates = 309;

	// Notifications
	public const int RithmicOrderNotification = 351;
	public const int ExchangeOrderNotification = 352;
	public const int ForcedLogout = 360;

	// PnL (PnL Plant)
	public const int RequestPnLPositionSnapshot = 400;
	public const int ResponsePnLPositionSnapshot = 401;
	public const int RequestPnLPositionUpdates = 402;
	public const int ResponsePnLPositionUpdates = 403;
	public const int InstrumentPnLPositionUpdate = 450;
	public const int AccountPnLPositionUpdate = 451;

	// System Gateway
	public const int RequestRithmicSystemGatewayInfo = 20;
	public const int ResponseRithmicSystemGatewayInfo = 21;
}

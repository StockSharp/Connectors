namespace StockSharp.InteractiveBrokers.Native;

enum RequestMessages
{
	SubscribeMarketData = 1,
	UnSubscribeMarketData = 2,
	
	RegisterOrder = 3,
	CancelOrder = 4,
	RequestOpenOrders = 5,
	RequestAccountData = 6,
	RequestTrades = 7,
	RequestIds = 8,
	RequestContractData = 9,
	
	SubscribeMarketDepth = 10,
	UnSubscribeMarketDepth = 11,
	
	SubscribeNewsBulletins = 12,
	UnSubscribeNewsBulletins = 13,
	
	SetServerLogLevel = 14,
	RequestAutoOpenOrders = 15,
	RequestAllOpenOrders = 16,
	RequestPortfolios = 17,
	
	RequestFinancialAdvisor = 18,
	ReplaceFinancialAdvisor = 19,

	SubscribeHistoricalData = 20,
	
	ExerciseOptions = 21,
	
	SubscribeScanner = 22,
	UnSubscribeScanner = 23,
	RequestScannerParameters = 24,
	
	UnSubscribeHistoricalData = 25,

	RequestCurrentTime = 49,

	SubscribeRealTimeCandles = 50,
	UnSubscribeRealTimeCandles = 51,

	SubscribeFundamentalData = 52,
	UnSubscribeFundamentalData = 53,

	SubscribeCalcImpliedVolatility = 54,
	SubscribeCalcOptionPrice = 55,
	UnSubscribeCalcImpliedVolatility = 56,
	UnSubscribeCalcOptionPrice = 57,

	RequestGlobalCancel = 58,

	RequestMarketDataType = 59,

	SubscribePosition = 61,
	SubscribeAccountSummary = 62,
	UnSubscribeAccountSummary = 63,
	UnSubscribePosition = 64,

	VerifyRequest = 65,
	VerifyMessage = 66,

	QueryDisplayGroups = 67,
	SubscribeToGroupEvents = 68,
	UpdateDisplayGroup = 69,
	UnSubscribeFromGroupEvents = 70,

	StartApi = 71,

	VerifyAndAuthRequest = 72,
	VerifyAndAuthMessage = 73,

	RequestPositionsMulti = 74,
	CancelPositionsMulti = 75,
	RequestAccountUpdatesMulti = 76,
	CancelAccountUpdatesMulti = 77,

	RequestSecurityDefinitionOptionParameters = 78,
	RequestSoftDollarTiers = 79,

	RequestFamilyCodes = 80,
	RequestMatchingSymbols = 81,
	RequestMktDepthExchanges = 82,
	RequestSmartComponents = 83,
	RequestNewsArticle = 84,
	RequestNewsProviders = 85,
	RequestHistoricalNews = 86,
	RequestHeadTimestamp = 87,
	RequestHistogramData = 88,
	CancelHistogramData = 89,
	CancelHeadTimestamp = 90,
	RequestMarketRule = 91,
	ReqPnL = 92,
	CancelPnL = 93,
	ReqPnLSingle = 94,
	CancelPnLSingle = 95,
	ReqHistoricalTicks = 96,
	ReqTickByTickData = 97,
	CancelTickByTickData = 98,
	ReqCompletedOrders = 99,
	ReqWshMetaData = 100,
	CancelWshMetaData = 101,
	ReqWshEventData = 102,
	CancelWshEventData = 103,
	RequestUserInfo = 104,
}
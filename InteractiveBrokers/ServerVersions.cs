namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Versions.
/// </summary>
public enum ServerVersions
{
	/// <summary>
	/// Version <see cref="V1"/>.
	/// </summary>
	V1 = 1,

	/// <summary>
	/// Version <see cref="V2"/>.
	/// </summary>
	V2 = 2,

	/// <summary>
	/// Version <see cref="V3"/>.
	/// </summary>
	V3 = 3,

	/// <summary>
	/// Version <see cref="V4"/>.
	/// </summary>
	V4 = 4,

	/// <summary>
	/// Version <see cref="V5"/>.
	/// </summary>
	V5 = 5,

	/// <summary>
	/// Version <see cref="V6"/>.
	/// </summary>
	V6 = 6,

	/// <summary>
	/// Version <see cref="V7"/>.
	/// </summary>
	V7 = 7,

	/// <summary>
	/// Version <see cref="V8"/>.
	/// </summary>
	V8 = 8,

	/// <summary>
	/// Version <see cref="V9"/>.
	/// </summary>
	V9 = 9,

	/// <summary>
	/// Version <see cref="V10"/>.
	/// </summary>
	V10 = 10,

	/// <summary>
	/// Version <see cref="V11"/>.
	/// </summary>
	V11 = 11,

	/// <summary>
	/// Version <see cref="V12"/>.
	/// </summary>
	V12 = 12,

	/// <summary>
	/// Version <see cref="V13"/>.
	/// </summary>
	V13 = 13,

	/// <summary>
	/// Version <see cref="V14"/>.
	/// </summary>
	V14 = 14,

	/// <summary>
	/// Version <see cref="V15"/>.
	/// </summary>
	V15 = 15,

	/// <summary>
	/// Version <see cref="V16"/>.
	/// </summary>
	V16 = 16,

	/// <summary>
	/// Version <see cref="V17"/>.
	/// </summary>
	V17 = 17,

	/// <summary>
	/// Version <see cref="V18"/>.
	/// </summary>
	V18 = 18,

	/// <summary>
	/// Version <see cref="V19"/>.
	/// </summary>
	V19 = 19,

	/// <summary>
	/// Version <see cref="V20"/>.
	/// </summary>
	V20 = 20,

	/// <summary>
	/// Version <see cref="V21"/>.
	/// </summary>
	V21 = 21,

	/// <summary>
	/// Version <see cref="V22"/>.
	/// </summary>
	V22 = 22,

	/// <summary>
	/// Version <see cref="V23"/>.
	/// </summary>
	V23 = 23,

	/// <summary>
	/// Version <see cref="HistoricalData"/>.
	/// </summary>
	HistoricalData = 24,

	/// <summary>
	/// Version <see cref="V25"/>.
	/// </summary>
	V25 = 25,

	/// <summary>
	/// Version <see cref="V26"/>.
	/// </summary>
	V26 = 26,

	/// <summary>
	/// Version <see cref="V27"/>.
	/// </summary>
	V27 = 27,

	/// <summary>
	/// Version <see cref="V28"/>.
	/// </summary>
	V28 = 28,

	/// <summary>
	/// Version <see cref="V29"/>.
	/// </summary>
	V29 = 29,

	/// <summary>
	/// Version <see cref="V30"/>.
	/// </summary>
	V30 = 30,

	/// <summary>
	/// Version <see cref="V31"/>.
	/// </summary>
	V31 = 31,

	/// <summary>
	/// Version <see cref="V32"/>.
	/// </summary>
	V32 = 32,

	/// <summary>
	/// Version <see cref="CurrentTime"/>.
	/// </summary>
	CurrentTime = 33,

	/// <summary>
	/// Version <see cref="RealTimeBars"/>.
	/// </summary>
	RealTimeBars = 34,

	/// <summary>
	/// Version <see cref="SShortComboLegs"/>.
	/// </summary>
	SShortComboLegs = 35,

	/// <summary>
	/// Version <see cref="WhatIfOrders"/>.
	/// </summary>
	WhatIfOrders = 36,

	/// <summary>
	/// Version <see cref="ContractConId"/>.
	/// </summary>
	ContractConId = 37,

	/// <summary>
	/// Version <see cref="V38"/>.
	/// </summary>
	V38 = 38,

	/// <summary>
	/// Version <see cref="PtaOrders"/>.
	/// </summary>
	PtaOrders = 39,

	/// <summary>
	/// Version <see cref="ScaleOrders2"/>.
	/// </summary>
	ScaleOrders2 = 40,

	/// <summary>
	/// Version <see cref="AlgoOrders"/>.
	/// </summary>
	AlgoOrders = 41,

	/// <summary>
	/// Version <see cref="ExecDataChain"/>.
	/// </summary>
	ExecDataChain = 42,

	/// <summary>
	/// Version <see cref="NotHeld"/>.
	/// </summary>
	NotHeld = 44,

	/// <summary>
	/// Version <see cref="SecIdType"/>.
	/// </summary>
	SecIdType = 45,

	/// <summary>
	/// Version <see cref="PlaceOrderConId"/>.
	/// </summary>
	PlaceOrderConId = 46,

	/// <summary>
	/// Version <see cref="ReqMarketDataConId"/>.
	/// </summary>
	ReqMarketDataConId = 47,

	/// <summary>
	/// Version <see cref="ReqCalcImpliedVolat"/>.
	/// </summary>
	ReqCalcImpliedVolat = 49,

	/// <summary>
	/// Version <see cref="CancelCalcOptionPrice"/>.
	/// </summary>
	CancelCalcOptionPrice = 50,

	/// <summary>
	/// Version <see cref="SShortXOld"/>.
	/// </summary>
	SShortXOld = 51,

	/// <summary>
	/// Version <see cref="SShortX"/>.
	/// </summary>
	SShortX = 52,

	/// <summary>
	/// Version <see cref="ReqGlobalCancel"/>.
	/// </summary>
	ReqGlobalCancel = 53,

	/// <summary>
	/// Version <see cref="HedgeOrders"/>.
	/// </summary>
	HedgeOrders = 54,

	/// <summary>
	/// Version <see cref="ReqMarketDataType"/>.
	/// </summary>
	ReqMarketDataType = 55,

	/// <summary>
	/// Version <see cref="OptOutSmartRoute"/>.
	/// </summary>
	OptOutSmartRoute = 56,

	/// <summary>
	/// Version <see cref="SmartComboRoutingParams"/>.
	/// </summary>
	SmartComboRoutingParams = 57,

	/// <summary>
	/// Version <see cref="DeltaNeutralConId"/>.
	/// </summary>
	DeltaNeutralConId = 58,

	/// <summary>
	/// Version <see cref="ScaleOrders3"/>.
	/// </summary>
	ScaleOrders3 = 60,

	/// <summary>
	/// Version <see cref="OrderComboLegsPrice"/>.
	/// </summary>
	OrderComboLegsPrice = 61,

	/// <summary>
	/// Version <see cref="TrailingPercent"/>.
	/// </summary>
	TrailingPercent = 62,

	/// <summary>
	/// Version <see cref="V63"/>.
	/// </summary>
	V63 = 63,

	/// <summary>
	/// Version <see cref="DeltaNeutralOpenClose"/>.
	/// </summary>
	DeltaNeutralOpenClose = 66,

	/// <summary>
	/// Version <see cref="AcctSummary"/>.
	/// </summary>
	AcctSummary = 67,

	/// <summary>
	/// Version <see cref="TradingClass"/>.
	/// </summary>
	TradingClass = 68,

	/// <summary>
	/// Version <see cref="ScaleTable"/>.
	/// </summary>
	ScaleTable = 69,

	/// <summary>
	/// Version <see cref="Linking"/>.
	/// </summary>
	Linking = 70,

	/// <summary>
	/// Version <see cref="AlgoId"/>.
	/// </summary>
	AlgoId = 71,

	/// <summary>
	/// Version <see cref="OptionalCaps"/>.
	/// </summary>
	OptionalCaps = 72,

	/// <summary>
	/// Version <see cref="OrderSolicited"/>.
	/// </summary>
	OrderSolicited = 73,

	/// <summary>
	/// Version <see cref="LinkingAuth"/>.
	/// </summary>
	LinkingAuth = 74,

	/// <summary>
	/// Version <see cref="PrimaryExch"/>.
	/// </summary>
	PrimaryExch = 75,

	/// <summary>
	/// Version <see cref="RandomSizeAndPrice"/>.
	/// </summary>
	RandomSizeAndPrice = 76,

	/// <summary>
	/// Version <see cref="V100"/>.
	/// </summary>
	V100 = 100,

	/// <summary>
	/// Version <see cref="FractionalPositions"/>.
	/// </summary>
	FractionalPositions = 101,

	/// <summary>
	/// Version <see cref="PeggedToBenchmark"/>.
	/// </summary>
	PeggedToBenchmark = 102,

	/// <summary>
	/// Version <see cref="ModelsSupport"/>.
	/// </summary>
	ModelsSupport = 103,

	/// <summary>
	/// Version <see cref="SeqDefOptParamRef"/>.
	/// </summary>
	SeqDefOptParamRef = 104,

	/// <summary>
	/// Version <see cref="ExtOperator"/>.
	/// </summary>
	ExtOperator = 105,

	/// <summary>
	/// Version <see cref="SoftDollarTier"/>.
	/// </summary>
	SoftDollarTier = 106,

	/// <summary>
	/// Version <see cref="ReqFamilyCodes"/>.
	/// </summary>
	ReqFamilyCodes = 107,

	/// <summary>
	/// Version <see cref="ReqMatchingSymbols"/>.
	/// </summary>
	ReqMatchingSymbols = 108,

	/// <summary>
	/// Version <see cref="PastLimit"/>.
	/// </summary>
	PastLimit = 109,

	/// <summary>
	/// Version <see cref="MarketDepthMultiplier"/>.
	/// </summary>
	MarketDepthMultiplier = 110,

	/// <summary>
	/// Version <see cref="CashQty"/>.
	/// </summary>
	CashQty = 111,

	/// <summary>
	/// Version <see cref="ReqMarketDepthExchanges"/>.
	/// </summary>
	ReqMarketDepthExchanges = 112,

	/// <summary>
	/// Version <see cref="TickNews"/>.
	/// </summary>
	TickNews = 113,

	/// <summary>
	/// Version <see cref="SmartComponents"/>.
	/// </summary>
	SmartComponents = 114,

	/// <summary>
	/// Version <see cref="ReqNewsProvider"/>.
	/// </summary>
	ReqNewsProvider = 115,

	/// <summary>
	/// Version <see cref="ReqNewsArticle"/>.
	/// </summary>
	ReqNewsArticle = 116,

	/// <summary>
	/// Version <see cref="ReqHistNews"/>.
	/// </summary>
	ReqHistNews = 117,

	/// <summary>
	/// Version <see cref="ReqHeadTimeStamp"/>.
	/// </summary>
	ReqHeadTimeStamp = 118,

	/// <summary>
	/// Version <see cref="ReqHistogramData"/>.
	/// </summary>
	ReqHistogramData = 119,

	/// <summary>
	/// Version <see cref="ServiceDataType"/>.
	/// </summary>
	ServiceDataType = 120,

	/// <summary>
	/// Version <see cref="AggGroup"/>.
	/// </summary>
	AggGroup = 121,

	/// <summary>
	/// Version <see cref="UnderlyingInfo"/>.
	/// </summary>
	UnderlyingInfo = 122,

	/// <summary>
	/// Version <see cref="CancelHeadTimeStamp"/>.
	/// </summary>
	CancelHeadTimeStamp = 123,

	/// <summary>
	/// Version <see cref="SyntRealtimeBars"/>.
	/// </summary>
	SyntRealtimeBars = 124,

	/// <summary>
	/// Version <see cref="CfdReRoute"/>.
	/// </summary>
	CfdReRoute = 125,

	/// <summary>
	/// Version <see cref="MarketRules"/>.
	/// </summary>
	MarketRules = 126,

	/// <summary>
	/// Version <see cref="PnL"/>.
	/// </summary>
	PnL = 127,

	/// <summary>
	/// Version <see cref="NewsQueryOrigins"/>.
	/// </summary>
	NewsQueryOrigins = 128,

	/// <summary>
	/// Version <see cref="UnrealPnL"/>.
	/// </summary>
	UnrealPnL = 129,

	/// <summary>
	/// Version <see cref="HistoricalTicks"/>.
	/// </summary>
	HistoricalTicks = 130,

	/// <summary>
	/// Version <see cref="MarketCapPrice"/>.
	/// </summary>
	MarketCapPrice = 131,

	/// <summary>
	/// Version <see cref="PreOpenBidAsk"/>.
	/// </summary>
	PreOpenBidAsk = 132,

	/// <summary>
	/// Version <see cref="RealExpDate"/>.
	/// </summary>
	RealExpDate = 134,

	/// <summary>
	/// Version <see cref="RealPnL"/>.
	/// </summary>
	RealPnL = 135,

	/// <summary>
	/// Version <see cref="LastLiquidity"/>.
	/// </summary>
	LastLiquidity = 136,

	/// <summary>
	/// Version <see cref="TickByTick"/>.
	/// </summary>
	TickByTick = 137,

	/// <summary>
	/// Version <see cref="DecisionMaker"/>.
	/// </summary>
	DecisionMaker = 138,

	/// <summary>
	/// Version <see cref="MifidExecution"/>.
	/// </summary>
	MifidExecution = 139,

	/// <summary>
	/// Version <see cref="TickByTickIgnoreSize"/>.
	/// </summary>
	TickByTickIgnoreSize = 140,

	/// <summary>
	/// Version <see cref="AutoPriceForHedge"/>.
	/// </summary>
	AutoPriceForHedge = 141,

	/// <summary>
	/// Version <see cref="WhatIfExtFields"/>.
	/// </summary>
	WhatIfExtFields = 142,

	/// <summary>
	/// Version <see cref="ScannerGenericOpts"/>.
	/// </summary>
	ScannerGenericOpts = 143,

	/// <summary>
	/// Version <see cref="ApiBindOrders"/>.
	/// </summary>
	ApiBindOrders = 144,

	/// <summary>
	/// Version <see cref="OrderContainer"/>.
	/// </summary>
	OrderContainer = 145,

	/// <summary>
	/// Version <see cref="SmartDepth"/>.
	/// </summary>
	SmartDepth = 146,

	/// <summary>
	/// Version <see cref="RemoveAllNullCasting"/>.
	/// </summary>
	RemoveAllNullCasting = 147,

	/// <summary>
	/// Version <see cref="DPegOrders"/>.
	/// </summary>
	DPegOrders = 148,

	/// <summary>
	/// Version <see cref="MarketDepthPrimeExchange"/>.
	/// </summary>
	MarketDepthPrimeExchange = 149,

	/// <summary>
	/// Version <see cref="CompletedOrders"/>.
	/// </summary>
	CompletedOrders = 150,

	/// <summary>
	/// Version <see cref="PriceMgmtAlgo"/>.
	/// </summary>
	PriceMgmtAlgo = 151,

	/// <summary>
	/// Version <see cref="StockType"/>.
	/// </summary>
	StockType = 152,

	/// <summary>
	/// Version <see cref="EncodeMsgASCII7"/>.
	/// </summary>
	EncodeMsgASCII7 = 153,

	/// <summary>
	/// Version <see cref="SendAllFamilyCodes"/>.
	/// </summary>
	SendAllFamilyCodes = 154,

	/// <summary>
	/// Version <see cref="NoDefaultOpenClose"/>.
	/// </summary>
	NoDefaultOpenClose = 155,

	/// <summary>
	/// Version <see cref="PriceBasedVolatility"/>.
	/// </summary>
	PriceBasedVolatility = 156,

	/// <summary>
	/// Version <see cref="ReplaceFaEnd"/>.
	/// </summary>
	ReplaceFaEnd = 157,

	/// <summary>
	/// Version <see cref="Duration"/>.
	/// </summary>
	Duration = 158,

	/// <summary>
	/// Version <see cref="MarketDataInShares"/>.
	/// </summary>
	MarketDataInShares = 159,

	/// <summary>
	/// Version <see cref="PostToAts"/>.
	/// </summary>
	PostToAts = 160,

	/// <summary>
	/// Version <see cref="WsheCalendar"/>.
	/// </summary>
	WsheCalendar = 161,

	/// <summary>
	/// Version <see cref="AutoCancelParent"/>.
	/// </summary>
	AutoCancelParent = 162,

	/// <summary>
	/// Version <see cref="FractionalSizeSupport"/>.
	/// </summary>
	FractionalSizeSupport = 163,

	/// <summary>
	/// Version <see cref="SizeRules"/>.
	/// </summary>
	SizeRules = 164,

	/// <summary>
	/// Version <see cref="HistoricalSchedule"/>.
	/// </summary>
	HistoricalSchedule = 165,

	/// <summary>
	/// Version <see cref="AdvancedOrderReject"/>.
	/// </summary>
	AdvancedOrderReject = 166,

	/// <summary>
	/// Version <see cref="UserInfo"/>.
	/// </summary>
	UserInfo = 167,

	/// <summary>
	/// Version <see cref="CryptoAggregatedTrades"/>.
	/// </summary>
	CryptoAggregatedTrades = 168,

	/// <summary>
	/// Version <see cref="ManualOrderTime"/>.
	/// </summary>
	ManualOrderTime = 169,

	/// <summary>
	/// Version <see cref="PegBestPegMinOffsets"/>.
	/// </summary>
	PegBestPegMinOffsets = 170,

	/// <summary>
	/// Version <see cref="MinServerVerWshEventDataFilters"/>.
	/// </summary>
	MinServerVerWshEventDataFilters = 171,

	/// <summary>
	/// Version <see cref="MinServerVerIpoPrices"/>.
	/// </summary>
	MinServerVerIpoPrices = 172,

	/// <summary>
	/// Version <see cref="MinServerVerWshEventDataFiltersDate"/>.
	/// </summary>
	MinServerVerWshEventDataFiltersDate = 173,

	/// <summary>
	/// Version <see cref="MinServerVerInstrumentTimeZone"/>.
	/// </summary>
	MinServerVerInstrumentTimeZone = 174,

	/// <summary>
	/// Version <see cref="MinServerVerHmdsMarketDataInShares"/>.
	/// </summary>
	MinServerVerHmdsMarketDataInShares = 175,

	/// <summary>
	/// Version <see cref="MinServerVerBondIssuerId"/>.
	/// </summary>
	MinServerVerBondIssuerId = 176,

	/// <summary>
	/// Version <see cref="MinServerVerFaProfileDesupport"/>.
	/// </summary>
	MinServerVerFaProfileDesupport = 177,

	/// <summary>
	/// Version <see cref="MinServerVerPendingPriceRevision"/>.
	/// </summary>
	MinServerVerPendingPriceRevision = 178,

	/// <summary>
	/// Version <see cref="MinServerVerFundDataFields"/>.
	/// </summary>
	MinServerVerFundDataFields = 179,

	/// <summary>
	/// Version <see cref="MinServerVerManualOrderTimeExerciseOptions"/>.
	/// </summary>
	MinServerVerManualOrderTimeExerciseOptions = 180,

	/// <summary>
	/// Version <see cref="MinServerVerOpenOrderAdStrategy"/>.
	/// </summary>
	MinServerVerOpenOrderAdStrategy = 181,

	/// <summary>
	/// Version <see cref="MinServerVerLastTradeDate"/>.
	/// </summary>
	MinServerVerLastTradeDate = 182,

	/// <summary>
	/// Version <see cref="MinServerVerCustomerAccount"/>.
	/// </summary>
	MinServerVerCustomerAccount = 183,

	/// <summary>
	/// Version <see cref="MinServerVerProfessionalCustomer"/>.
	/// </summary>
	MinServerVerProfessionalCustomer = 184,

	/// <summary>
	/// Version <see cref="MinServerVerBondAccruedInterest"/>.
	/// </summary>
	MinServerVerBondAccruedInterest = 185,

	/// <summary>
	/// Version <see cref="MinServerVerIneligibilityReasons"/>.
	/// </summary>
	MinServerVerIneligibilityReasons = 186,

	/// <summary>
	/// Version <see cref="MinServerVerRfqFields"/>.
	/// </summary>
	MinServerVerRfqFields = 187,
}
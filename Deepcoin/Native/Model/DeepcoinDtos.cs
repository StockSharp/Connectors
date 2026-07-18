namespace StockSharp.Deepcoin.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinProductTypes
{
	[EnumMember(Value = "SPOT")]
	Spot,

	[EnumMember(Value = "SWAP")]
	Swap,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinInstrumentStates
{
	[EnumMember(Value = "live")]
	Live,

	[EnumMember(Value = "suspend")]
	Suspend,

	[EnumMember(Value = "preopen")]
	Preopen,

	[EnumMember(Value = "settlement")]
	Settlement,
}

enum DeepcoinRestCandleIntervals
{
	Minute1,
	Minute5,
	Minute15,
	Minute30,
	Hour1,
	Hour4,
	Hour12,
	Day1,
	Week1,
	Month1,
	Year1,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinWsCandleIntervals
{
	[EnumMember(Value = "1m")]
	Minute1,

	[EnumMember(Value = "5m")]
	Minute5,

	[EnumMember(Value = "15m")]
	Minute15,

	[EnumMember(Value = "30m")]
	Minute30,

	[EnumMember(Value = "1h")]
	Hour1,

	[EnumMember(Value = "4h")]
	Hour4,

	[EnumMember(Value = "12h")]
	Hour12,

	[EnumMember(Value = "1d")]
	Day1,

	[EnumMember(Value = "1w")]
	Week1,

	[EnumMember(Value = "1o")]
	Month1,

	[EnumMember(Value = "1y")]
	Year1,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinApiOrderTypes
{
	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "post_only")]
	PostOnly,

	[EnumMember(Value = "ioc")]
	ImmediateOrCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinApiOrderStates
{
	[EnumMember(Value = "live")]
	Live,

	[EnumMember(Value = "partially_filled")]
	PartiallyFilled,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "canceled")]
	Canceled,

	[EnumMember(Value = "failed")]
	Failed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinTradingModes
{
	[EnumMember(Value = "cash")]
	Cash,

	[EnumMember(Value = "cross")]
	Cross,

	[EnumMember(Value = "isolated")]
	Isolated,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinPositionModes
{
	[EnumMember(Value = "merge")]
	Merge,

	[EnumMember(Value = "split")]
	Split,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinTargetCurrencies
{
	[EnumMember(Value = "base_ccy")]
	BaseCurrency,

	[EnumMember(Value = "quote_ccy")]
	QuoteCurrency,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinTriggerPriceTypes
{
	[EnumMember(Value = "last")]
	Last,

	[EnumMember(Value = "index")]
	Index,

	[EnumMember(Value = "mark")]
	Mark,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinExecutionTypes
{
	[EnumMember(Value = "T")]
	Taker,

	[EnumMember(Value = "M")]
	Maker,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinWsSubscriptionActions
{
	[EnumMember(Value = "1")]
	Subscribe,

	[EnumMember(Value = "2")]
	Unsubscribe,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinWsTopics
{
	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "book")]
	Book,

	[EnumMember(Value = "kline")]
	Kline,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinPublicActions
{
	[EnumMember(Value = "RecvTopicAction")]
	Subscription,

	[EnumMember(Value = "PO")]
	Market,

	[EnumMember(Value = "PMT")]
	Trade,

	[EnumMember(Value = "PMO")]
	Book,

	[EnumMember(Value = "PK")]
	Kline,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinBookUpdateTypes
{
	[EnumMember(Value = "f")]
	Full,

	[EnumMember(Value = "i")]
	Increment,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinLegacyDirections
{
	[EnumMember(Value = "0")]
	Buy,

	[EnumMember(Value = "1")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinLegacyPositionDirections
{
	[EnumMember(Value = "0")]
	Net,

	[EnumMember(Value = "1")]
	Long,

	[EnumMember(Value = "2")]
	Short,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinLegacyOrderStates
{
	[EnumMember(Value = "0")]
	AllTraded,

	[EnumMember(Value = "1")]
	PartiallyFilledActive,

	[EnumMember(Value = "2")]
	PartiallyFilledDone,

	[EnumMember(Value = "3")]
	Live,

	[EnumMember(Value = "4")]
	NotQueued,

	[EnumMember(Value = "5")]
	Canceled,

	[EnumMember(Value = "a")]
	Unknown,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinPrivateActions
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "PushAccount")]
	PushAccount,

	[EnumMember(Value = "PushAccountDetail")]
	PushAccountDetail,

	[EnumMember(Value = "PushOrder")]
	PushOrder,

	[EnumMember(Value = "PushPosition")]
	PushPosition,

	[EnumMember(Value = "PushTrade")]
	PushTrade,

	[EnumMember(Value = "PushTriggerOrder")]
	PushTriggerOrder,

	[EnumMember(Value = "error")]
	Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DeepcoinPrivateTables
{
	[EnumMember(Value = "Account")]
	Account,

	[EnumMember(Value = "AccountDetail")]
	AccountDetail,

	[EnumMember(Value = "Order")]
	Order,

	[EnumMember(Value = "Position")]
	Position,

	[EnumMember(Value = "Trade")]
	Trade,

	[EnumMember(Value = "TriggerOrder")]
	TriggerOrder,
}

interface IDeepcoinQuery
{
	string ToQueryString();
}

sealed class DeepcoinQueryBuilder
{
	private readonly StringBuilder _value = new();

	public DeepcoinQueryBuilder Add(string name, string value)
	{
		if (value.IsEmpty())
			return this;
		if (_value.Length > 0)
			_value.Append('&');
		_value.Append(Uri.EscapeDataString(name));
		_value.Append('=');
		_value.Append(Uri.EscapeDataString(value));
		return this;
	}

	public DeepcoinQueryBuilder Add(string name, int? value)
		=> value is null ? this : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

	public DeepcoinQueryBuilder Add(string name, long? value)
		=> value is null ? this : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

	public override string ToString() => _value.ToString();
}

sealed class DeepcoinEmptyQuery : IDeepcoinQuery
{
	public string ToQueryString() => string.Empty;
}

sealed class DeepcoinInstrumentsQuery : IDeepcoinQuery
{
	public DeepcoinProductTypes ProductType { get; init; }
	public string InstrumentId { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instType", ProductType.ToWire())
		.Add("instId", InstrumentId)
		.ToString();
}

sealed class DeepcoinTickersQuery : IDeepcoinQuery
{
	public DeepcoinProductTypes ProductType { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instType", ProductType.ToWire())
		.ToString();
}

sealed class DeepcoinBookQuery : IDeepcoinQuery
{
	public string InstrumentId { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instId", InstrumentId)
		.Add("limit", Limit)
		.ToString();
}

sealed class DeepcoinTradesQuery : IDeepcoinQuery
{
	public string InstrumentId { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instId", InstrumentId)
		.Add("limit", Limit)
		.ToString();
}

sealed class DeepcoinCandlesQuery : IDeepcoinQuery
{
	public string InstrumentId { get; init; }
	public DeepcoinRestCandleIntervals Interval { get; init; }
	public long? EndTime { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instId", InstrumentId)
		.Add("bar", Interval.ToWire())
		.Add("endTime", EndTime)
		.Add("limit", Limit)
		.ToString();
}

sealed class DeepcoinBalancesQuery : IDeepcoinQuery
{
	public DeepcoinProductTypes ProductType { get; init; }
	public string Currency { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instType", ProductType.ToWire())
		.Add("ccy", Currency)
		.ToString();
}

sealed class DeepcoinPositionsQuery : IDeepcoinQuery
{
	public DeepcoinProductTypes ProductType { get; init; }
	public string InstrumentId { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instType", ProductType.ToWire())
		.Add("instId", InstrumentId)
		.ToString();
}

sealed class DeepcoinPendingOrdersQuery : IDeepcoinQuery
{
	public string InstrumentId { get; init; }
	public int Page { get; init; }
	public int Limit { get; init; }
	public string OrderId { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instId", InstrumentId)
		.Add("page", Page)
		.Add("limit", Limit)
		.Add("ordId", OrderId)
		.ToString();
}

sealed class DeepcoinOrdersHistoryQuery : IDeepcoinQuery
{
	public DeepcoinProductTypes ProductType { get; init; }
	public string InstrumentId { get; init; }
	public DeepcoinApiOrderTypes? OrderType { get; init; }
	public DeepcoinApiOrderStates? State { get; init; }
	public string StartId { get; init; }
	public string EndId { get; init; }
	public int Limit { get; init; }
	public string OrderId { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instType", ProductType.ToWire())
		.Add("instId", InstrumentId)
		.Add("ordType", OrderType?.ToWire())
		.Add("state", State?.ToWire())
		.Add("startTime", StartId)
		.Add("endTime", EndId)
		.Add("limit", Limit)
		.Add("ordId", OrderId)
		.ToString();
}

sealed class DeepcoinFillsQuery : IDeepcoinQuery
{
	public DeepcoinProductTypes ProductType { get; init; }
	public string InstrumentId { get; init; }
	public string OrderId { get; init; }
	public string After { get; init; }
	public string Before { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("instType", ProductType.ToWire())
		.Add("instId", InstrumentId)
		.Add("ordId", OrderId)
		.Add("after", After)
		.Add("before", Before)
		.Add("startTime", StartTime)
		.Add("endTime", EndTime)
		.Add("limit", Limit)
		.ToString();
}

sealed class DeepcoinListenKeyQuery : IDeepcoinQuery
{
	public string ListenKey { get; init; }

	public string ToQueryString() => new DeepcoinQueryBuilder()
		.Add("listenkey", ListenKey)
		.ToString();
}

sealed class DeepcoinResponse<TData>
{
	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }

	[JsonProperty("data")]
	public TData Data { get; init; }
}

sealed class DeepcoinResponseHeader
{
	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }
}

sealed class DeepcoinServerTime
{
	[JsonProperty("ts")]
	public long Timestamp { get; init; }
}

sealed class DeepcoinInstrument
{
	[JsonProperty("instType")]
	public DeepcoinProductTypes ProductType { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("uly")]
	public string Underlying { get; init; }

	[JsonProperty("baseCcy")]
	public string BaseCurrency { get; init; }

	[JsonProperty("quoteCcy")]
	public string QuoteCurrency { get; init; }

	[JsonProperty("ctVal")]
	public string ContractValue { get; init; }

	[JsonProperty("ctValCcy")]
	public string ContractValueCurrency { get; init; }

	[JsonProperty("listTime")]
	public string ListTime { get; init; }

	[JsonProperty("lever")]
	public string MaximumLeverage { get; init; }

	[JsonProperty("tickSz")]
	public string TickSize { get; init; }

	[JsonProperty("lotSz")]
	public string LotSize { get; init; }

	[JsonProperty("minSz")]
	public string MinimumSize { get; init; }

	[JsonProperty("state")]
	public DeepcoinInstrumentStates State { get; init; }

	[JsonProperty("maxLmtSz")]
	public string MaximumLimitSize { get; init; }

	[JsonProperty("maxMktSz")]
	public string MaximumMarketSize { get; init; }
}

sealed class DeepcoinTicker
{
	[JsonProperty("instType")]
	public DeepcoinProductTypes ProductType { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("last")]
	public string LastPrice { get; init; }

	[JsonProperty("lastSz")]
	public string LastSize { get; init; }

	[JsonProperty("askPx")]
	public string AskPrice { get; init; }

	[JsonProperty("askSz")]
	public string AskSize { get; init; }

	[JsonProperty("bidPx")]
	public string BidPrice { get; init; }

	[JsonProperty("bidSz")]
	public string BidSize { get; init; }

	[JsonProperty("open24h")]
	public string OpenPrice { get; init; }

	[JsonProperty("high24h")]
	public string HighPrice { get; init; }

	[JsonProperty("low24h")]
	public string LowPrice { get; init; }

	[JsonProperty("volCcy24h")]
	public string QuoteVolume { get; init; }

	[JsonProperty("vol24h")]
	public string BaseVolume { get; init; }

	[JsonProperty("ts")]
	public string Timestamp { get; init; }
}

sealed class DeepcoinBook
{
	[JsonProperty("bids")]
	public DeepcoinBookLevel[] Bids { get; init; }

	[JsonProperty("asks")]
	public DeepcoinBookLevel[] Asks { get; init; }
}

[JsonConverter(typeof(DeepcoinBookLevelConverter))]
sealed class DeepcoinBookLevel
{
	public string Price { get; init; }
	public string Size { get; init; }
}

sealed class DeepcoinTrade
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("tradeId")]
	public string TradeId { get; init; }

	[JsonProperty("px")]
	public string Price { get; init; }

	[JsonProperty("sz")]
	public string Size { get; init; }

	[JsonProperty("side")]
	public DeepcoinSides Side { get; init; }

	[JsonProperty("ts")]
	public string Timestamp { get; init; }
}

[JsonConverter(typeof(DeepcoinCandleConverter))]
sealed class DeepcoinCandle
{
	public long Timestamp { get; init; }
	public string Open { get; init; }
	public string High { get; init; }
	public string Low { get; init; }
	public string Close { get; init; }
	public string BaseVolume { get; init; }
	public string QuoteVolume { get; init; }
}

sealed class DeepcoinBalance
{
	[JsonProperty("ccy")]
	public string Currency { get; init; }

	[JsonProperty("bal")]
	public string Balance { get; init; }

	[JsonProperty("frozenBal")]
	public string FrozenBalance { get; init; }

	[JsonProperty("availBal")]
	public string AvailableBalance { get; init; }

	[JsonProperty("unrealizedProfit")]
	public string UnrealizedProfit { get; init; }

	[JsonProperty("equity")]
	public string Equity { get; init; }
}

sealed class DeepcoinPosition
{
	[JsonProperty("instType")]
	public DeepcoinProductTypes ProductType { get; init; }

	[JsonProperty("mgnMode")]
	public DeepcoinTradingModes TradingMode { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("posId")]
	public string PositionId { get; init; }

	[JsonProperty("posSide")]
	public DeepcoinPositionSides? PositionSide { get; init; }

	[JsonProperty("pos")]
	public string Position { get; init; }

	[JsonProperty("avgPx")]
	public string AveragePrice { get; init; }

	[JsonProperty("lever")]
	public string Leverage { get; init; }

	[JsonProperty("liqPx")]
	public string LiquidationPrice { get; init; }

	[JsonProperty("useMargin")]
	public string UsedMargin { get; init; }

	[JsonProperty("unrealizedProfit")]
	public string UnrealizedProfit { get; init; }

	[JsonProperty("lastPx")]
	public string LastPrice { get; init; }

	[JsonProperty("tpTriggerPx")]
	public string TakeProfitTriggerPrice { get; init; }

	[JsonProperty("slTriggerPx")]
	public string StopLossTriggerPrice { get; init; }

	[JsonProperty("mrgPosition")]
	public DeepcoinPositionModes? PositionMode { get; init; }

	[JsonProperty("ccy")]
	public string Currency { get; init; }

	[JsonProperty("uTime")]
	public string UpdateTime { get; init; }

	[JsonProperty("cTime")]
	public string CreateTime { get; init; }
}

sealed class DeepcoinOrder
{
	[JsonProperty("instType")]
	public DeepcoinProductTypes ProductType { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("tgtCcy")]
	public DeepcoinTargetCurrencies? TargetCurrency { get; init; }

	[JsonProperty("ccy")]
	public string Currency { get; init; }

	[JsonProperty("ordId")]
	public string OrderId { get; init; }

	[JsonProperty("clOrdId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("px")]
	public string Price { get; init; }

	[JsonProperty("sz")]
	public string Size { get; init; }

	[JsonProperty("pnl")]
	public string ProfitLoss { get; init; }

	[JsonProperty("ordType")]
	public DeepcoinApiOrderTypes OrderType { get; init; }

	[JsonProperty("side")]
	public DeepcoinSides Side { get; init; }

	[JsonProperty("posSide")]
	public DeepcoinPositionSides? PositionSide { get; init; }

	[JsonProperty("tdMode")]
	public DeepcoinTradingModes TradingMode { get; init; }

	[JsonProperty("mrgPosition")]
	public DeepcoinPositionModes? PositionMode { get; init; }

	[JsonProperty("reduceOnly")]
	[JsonConverter(typeof(DeepcoinNullableBooleanConverter))]
	public bool? IsReduceOnly { get; init; }

	[JsonProperty("accFillSz")]
	public string AccumulatedFillSize { get; init; }

	[JsonProperty("fillPx")]
	public string LastFillPrice { get; init; }

	[JsonProperty("tradeId")]
	public string LastTradeId { get; init; }

	[JsonProperty("fillSz")]
	public string LastFillSize { get; init; }

	[JsonProperty("fillTime")]
	public string LastFillTime { get; init; }

	[JsonProperty("avgPx")]
	public string AveragePrice { get; init; }

	[JsonProperty("state")]
	public DeepcoinApiOrderStates State { get; init; }

	[JsonProperty("lever")]
	public string Leverage { get; init; }

	[JsonProperty("tpTriggerPx")]
	public string TakeProfitTriggerPrice { get; init; }

	[JsonProperty("tpTriggerPxType")]
	public DeepcoinTriggerPriceTypes? TakeProfitTriggerPriceType { get; init; }

	[JsonProperty("tpOrdPx")]
	public string TakeProfitOrderPrice { get; init; }

	[JsonProperty("slTriggerPx")]
	public string StopLossTriggerPrice { get; init; }

	[JsonProperty("slTriggerPxType")]
	public DeepcoinTriggerPriceTypes? StopLossTriggerPriceType { get; init; }

	[JsonProperty("slOrdPx")]
	public string StopLossOrderPrice { get; init; }

	[JsonProperty("feeCcy")]
	public string FeeCurrency { get; init; }

	[JsonProperty("fee")]
	public string Fee { get; init; }

	[JsonProperty("uTime")]
	public string UpdateTime { get; init; }

	[JsonProperty("cTime")]
	public string CreateTime { get; init; }
}

sealed class DeepcoinFill
{
	[JsonProperty("instType")]
	public DeepcoinProductTypes ProductType { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("tradeId")]
	public string TradeId { get; init; }

	[JsonProperty("ordId")]
	public string OrderId { get; init; }

	[JsonProperty("billId")]
	public string BillId { get; init; }

	[JsonProperty("fillPx")]
	public string Price { get; init; }

	[JsonProperty("fillSz")]
	public string Size { get; init; }

	[JsonProperty("side")]
	public DeepcoinSides Side { get; init; }

	[JsonProperty("posSide")]
	public DeepcoinPositionSides? PositionSide { get; init; }

	[JsonProperty("execType")]
	public DeepcoinExecutionTypes ExecutionType { get; init; }

	[JsonProperty("feeCcy")]
	public string FeeCurrency { get; init; }

	[JsonProperty("fee")]
	public string Fee { get; init; }

	[JsonProperty("ts")]
	public string Timestamp { get; init; }
}

sealed class DeepcoinPlaceOrderRequest
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("tdMode")]
	public DeepcoinTradingModes TradingMode { get; init; }

	[JsonProperty("ccy")]
	public string Currency { get; init; }

	[JsonProperty("clOrdId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("side")]
	public DeepcoinSides Side { get; init; }

	[JsonProperty("posSide")]
	public DeepcoinPositionSides? PositionSide { get; init; }

	[JsonProperty("mrgPosition")]
	public DeepcoinPositionModes? PositionMode { get; init; }

	[JsonProperty("closePosId")]
	public string ClosePositionId { get; init; }

	[JsonProperty("ordType")]
	public DeepcoinApiOrderTypes OrderType { get; init; }

	[JsonProperty("sz")]
	public string Size { get; init; }

	[JsonProperty("px")]
	public string Price { get; init; }

	[JsonProperty("reduceOnly")]
	public bool? IsReduceOnly { get; init; }

	[JsonProperty("tgtCcy")]
	public DeepcoinTargetCurrencies? TargetCurrency { get; init; }

	[JsonProperty("tpTriggerPx")]
	public string TakeProfitTriggerPrice { get; init; }

	[JsonProperty("slTriggerPx")]
	public string StopLossTriggerPrice { get; init; }
}

sealed class DeepcoinOperationResult
{
	[JsonProperty("ordId")]
	public string OrderId { get; init; }

	[JsonProperty("clOrdId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("sCode")]
	public string Code { get; init; }

	[JsonProperty("sMsg")]
	public string Message { get; init; }
}

sealed class DeepcoinCancelOrderRequest
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("ordId")]
	public string OrderId { get; init; }
}

sealed class DeepcoinAmendOrderRequest
{
	[JsonProperty("ordId")]
	public string OrderId { get; init; }

	[JsonProperty("clOrdId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("newPx")]
	public decimal? NewPrice { get; init; }

	[JsonProperty("newSz")]
	public decimal? NewSize { get; init; }

	[JsonProperty("newTpTriggerPx")]
	public decimal? NewTakeProfitTriggerPrice { get; init; }

	[JsonProperty("newSlTriggerPx")]
	public decimal? NewStopLossTriggerPrice { get; init; }

	[JsonProperty("cancelTp")]
	public bool? IsTakeProfitCanceled { get; init; }

	[JsonProperty("cancelSl")]
	public bool? IsStopLossCanceled { get; init; }
}

sealed class DeepcoinBatchCancelRequest
{
	[JsonProperty("ordIds")]
	public string[] OrderIds { get; init; }
}

sealed class DeepcoinBatchCancelResult
{
	[JsonProperty("errorList")]
	public DeepcoinBatchCancelError[] Errors { get; init; }
}

sealed class DeepcoinBatchCancelError
{
	[JsonProperty("ordId")]
	public string OrderId { get; init; }

	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }
}

sealed class DeepcoinSetLeverageRequest
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("lever")]
	public int Leverage { get; init; }

	[JsonProperty("mgnMode")]
	public DeepcoinTradingModes TradingMode { get; init; }

	[JsonProperty("mrgPosition")]
	public DeepcoinPositionModes PositionMode { get; init; }
}

sealed class DeepcoinSetLeverageResult
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("lever")]
	public int Leverage { get; init; }

	[JsonProperty("mgnMode")]
	public DeepcoinTradingModes TradingMode { get; init; }

	[JsonProperty("mrgPosition")]
	public DeepcoinPositionModes PositionMode { get; init; }

	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }
}

sealed class DeepcoinListenKey
{
	[JsonProperty("listenkey")]
	public string Value { get; init; }

	[JsonProperty("expire_time")]
	public long ExpireTime { get; init; }
}

sealed class DeepcoinWsSubscriptionRequest
{
	[JsonProperty("Action")]
	public DeepcoinWsSubscriptionActions Action { get; init; }

	[JsonProperty("Symbol")]
	public string Symbol { get; init; }

	[JsonProperty("LocalNo")]
	public long LocalNumber { get; init; }

	[JsonProperty("ResumeNo")]
	public int ResumeNumber { get; init; }

	[JsonProperty("Topic")]
	public DeepcoinWsTopics Topic { get; init; }

	[JsonProperty("Count")]
	public int? Count { get; init; }

	[JsonProperty("PeriodID")]
	public DeepcoinWsCandleIntervals? Period { get; init; }
}

sealed class DeepcoinPublicHeader
{
	[JsonProperty("a")]
	public DeepcoinPublicActions Action { get; init; }

	[JsonProperty("m")]
	public string Message { get; init; }
}

sealed class DeepcoinWsSubscriptionEnvelope
{
	[JsonProperty("m")]
	public string Message { get; init; }

	[JsonProperty("d")]
	public DeepcoinWsSubscriptionResult Data { get; init; }
}

sealed class DeepcoinWsSubscriptionResult
{
	[JsonProperty("A")]
	public DeepcoinWsSubscriptionActions Action { get; init; }

	[JsonProperty("L")]
	public long LocalNumber { get; init; }

	[JsonProperty("T")]
	public DeepcoinWsTopics Topic { get; init; }

	[JsonProperty("S")]
	public string Symbol { get; init; }

	[JsonProperty("C")]
	public int Count { get; init; }
}

sealed class DeepcoinWsMarketEnvelope
{
	[JsonProperty("tt")]
	public long TradingTime { get; init; }

	[JsonProperty("d")]
	[JsonConverter(typeof(DeepcoinSingleOrArrayConverter<DeepcoinWsTicker>))]
	public DeepcoinWsTicker[] Data { get; init; }
}

sealed class DeepcoinWsTicker
{
	[JsonProperty("I")]
	public string InstrumentId { get; init; }

	[JsonProperty("U")]
	public long Timestamp { get; init; }

	[JsonProperty("O")]
	public decimal? OpenPrice { get; init; }

	[JsonProperty("H")]
	public decimal? HighPrice { get; init; }

	[JsonProperty("L")]
	public decimal? LowPrice { get; init; }

	[JsonProperty("V")]
	public decimal? BaseVolume { get; init; }

	[JsonProperty("T")]
	public decimal? QuoteVolume { get; init; }

	[JsonProperty("N")]
	public decimal? LastPrice { get; init; }

	[JsonProperty("M")]
	public decimal? MarkPrice { get; init; }

	[JsonProperty("D")]
	public decimal? UnderlyingPrice { get; init; }

	[JsonProperty("F")]
	public decimal? MinimumPrice { get; init; }

	[JsonProperty("C")]
	public decimal? MaximumPrice { get; init; }

	[JsonProperty("BP1")]
	public decimal? BidPrice { get; init; }

	[JsonProperty("AP1")]
	public decimal? AskPrice { get; init; }
}

sealed class DeepcoinWsTradeEnvelope
{
	[JsonProperty("i")]
	public string InstrumentId { get; init; }

	[JsonProperty("d")]
	public DeepcoinWsTrade[] Data { get; init; }
}

sealed class DeepcoinWsTrade
{
	[JsonProperty("TradeID")]
	public string TradeId { get; init; }

	[JsonProperty("D")]
	public DeepcoinLegacyDirections Direction { get; init; }

	[JsonProperty("P")]
	public decimal Price { get; init; }

	[JsonProperty("V")]
	public decimal Volume { get; init; }

	[JsonProperty("T")]
	public long Timestamp { get; init; }
}

sealed class DeepcoinWsBookEnvelope
{
	[JsonProperty("i")]
	public string InstrumentId { get; init; }

	[JsonProperty("t")]
	public DeepcoinBookUpdateTypes UpdateType { get; init; }

	[JsonProperty("d")]
	public DeepcoinWsBook Data { get; init; }

	[JsonProperty("pt")]
	public long Timestamp { get; init; }
}

sealed class DeepcoinWsBook
{
	[JsonProperty("b")]
	public DeepcoinBookLevel[] Bids { get; init; }

	[JsonProperty("a")]
	public DeepcoinBookLevel[] Asks { get; init; }
}

sealed class DeepcoinWsCandleEnvelope
{
	[JsonProperty("I")]
	public string InstrumentId { get; init; }

	[JsonProperty("P")]
	public DeepcoinWsCandleIntervals Period { get; init; }

	[JsonProperty("d")]
	public DeepcoinCandle[] Data { get; init; }
}

sealed class DeepcoinPrivateCommand
{
	[JsonProperty("action")]
	public DeepcoinPrivateActions Action { get; init; }

	[JsonProperty("tables")]
	public DeepcoinPrivateTables[] Tables { get; init; }
}

sealed class DeepcoinPrivateHeader
{
	[JsonProperty("action")]
	public DeepcoinPrivateActions Action { get; init; }

	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }
}

sealed class DeepcoinPrivateEnvelope<TData>
{
	[JsonProperty("action")]
	public DeepcoinPrivateActions Action { get; init; }

	[JsonProperty("result")]
	public DeepcoinPrivateResult<TData>[] Result { get; init; }
}

sealed class DeepcoinPrivateResult<TData>
{
	[JsonProperty("table")]
	public DeepcoinPrivateTables Table { get; init; }

	[JsonProperty("data")]
	public TData Data { get; init; }
}

sealed class DeepcoinPrivateAsset
{
	[JsonProperty("C")]
	public string Currency { get; init; }

	[JsonProperty("B")]
	public decimal Balance { get; init; }

	[JsonProperty("a")]
	public decimal Available { get; init; }

	[JsonProperty("W")]
	public decimal Withdrawable { get; init; }

	[JsonProperty("u")]
	public decimal UsedMargin { get; init; }

	[JsonProperty("c")]
	public decimal CloseProfit { get; init; }
}

sealed class DeepcoinPrivateOrder
{
	[JsonProperty("L")]
	public string LocalId { get; init; }

	[JsonProperty("I")]
	public string InstrumentId { get; init; }

	[JsonProperty("O")]
	public string PriceType { get; init; }

	[JsonProperty("D")]
	public DeepcoinLegacyDirections Direction { get; init; }

	[JsonProperty("o")]
	public string OffsetFlag { get; init; }

	[JsonProperty("P")]
	public decimal Price { get; init; }

	[JsonProperty("V")]
	public decimal Volume { get; init; }

	[JsonProperty("OT")]
	public string OrderType { get; init; }

	[JsonProperty("i")]
	public int IsCrossMarginValue { get; init; }

	[JsonProperty("OS")]
	public string OrderId { get; init; }

	[JsonProperty("l")]
	public decimal Leverage { get; init; }

	[JsonProperty("Or")]
	public DeepcoinLegacyOrderStates State { get; init; }

	[JsonProperty("v")]
	public decimal FilledVolume { get; init; }

	[JsonProperty("IT")]
	public long InsertTime { get; init; }

	[JsonProperty("U")]
	public long UpdateTime { get; init; }

	[JsonProperty("UM")]
	public long UpdateMilliseconds { get; init; }

	[JsonProperty("p")]
	public DeepcoinLegacyPositionDirections PositionDirection { get; init; }

	[JsonProperty("t")]
	public decimal AveragePrice { get; init; }
}

sealed class DeepcoinPrivatePosition
{
	[JsonProperty("I")]
	public string InstrumentId { get; init; }

	[JsonProperty("p")]
	public DeepcoinLegacyPositionDirections Direction { get; init; }

	[JsonProperty("Po")]
	public decimal Position { get; init; }

	[JsonProperty("u")]
	public decimal UsedMargin { get; init; }

	[JsonProperty("c")]
	public decimal CloseProfit { get; init; }

	[JsonProperty("OP")]
	public decimal AveragePrice { get; init; }

	[JsonProperty("l")]
	public decimal Leverage { get; init; }

	[JsonProperty("i")]
	public int IsCrossMarginValue { get; init; }

	[JsonProperty("U")]
	public long UpdateTime { get; init; }
}

sealed class DeepcoinPrivateTrade
{
	[JsonProperty("TI")]
	public string TradeId { get; init; }

	[JsonProperty("D")]
	public DeepcoinLegacyDirections Direction { get; init; }

	[JsonProperty("OS")]
	public string OrderId { get; init; }

	[JsonProperty("I")]
	public string InstrumentId { get; init; }

	[JsonProperty("P")]
	public decimal Price { get; init; }

	[JsonProperty("V")]
	public decimal Volume { get; init; }

	[JsonProperty("TT")]
	public long TradeTime { get; init; }

	[JsonProperty("F")]
	public decimal Fee { get; init; }

	[JsonProperty("f")]
	public string FeeCurrency { get; init; }
}

sealed class DeepcoinNullableBooleanConverter : JsonConverter<bool?>
{
	public override bool? ReadJson(JsonReader reader, Type objectType, bool? existingValue,
		bool hasExistingValue, JsonSerializer serializer)
		=> reader.TokenType switch
		{
			JsonToken.Null => null,
			JsonToken.Boolean => (bool)reader.Value,
			JsonToken.String when bool.TryParse((string)reader.Value, out var value) => value,
			JsonToken.Integer => Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture) != 0,
			_ => throw new JsonSerializationException("Deepcoin boolean value has an invalid type."),
		};

	public override void WriteJson(JsonWriter writer, bool? value, JsonSerializer serializer)
	{
		if (value is null)
			writer.WriteNull();
		else
			writer.WriteValue(value.Value);
	}
}

sealed class DeepcoinBookLevelConverter : JsonConverter<DeepcoinBookLevel>
{
	public override DeepcoinBookLevel ReadJson(JsonReader reader, Type objectType,
		DeepcoinBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Deepcoin order-book level must be an array.");

		var level = new DeepcoinBookLevel
		{
			Price = DeepcoinJson.ReadString(reader, "order-book price"),
			Size = DeepcoinJson.ReadString(reader, "order-book size"),
		};
		DeepcoinJson.SkipToEndArray(reader, "order-book level");
		return level;
	}

	public override void WriteJson(JsonWriter writer, DeepcoinBookLevel value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class DeepcoinCandleConverter : JsonConverter<DeepcoinCandle>
{
	public override DeepcoinCandle ReadJson(JsonReader reader, Type objectType,
		DeepcoinCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Deepcoin candle must be an array.");

		var candle = new DeepcoinCandle
		{
			Timestamp = DeepcoinJson.ReadInt64(reader, "candle timestamp"),
			Open = DeepcoinJson.ReadString(reader, "candle open"),
			High = DeepcoinJson.ReadString(reader, "candle high"),
			Low = DeepcoinJson.ReadString(reader, "candle low"),
			Close = DeepcoinJson.ReadString(reader, "candle close"),
			BaseVolume = DeepcoinJson.ReadString(reader, "candle base volume"),
			QuoteVolume = DeepcoinJson.ReadString(reader, "candle quote volume"),
		};
		DeepcoinJson.SkipToEndArray(reader, "candle");
		return candle;
	}

	public override void WriteJson(JsonWriter writer, DeepcoinCandle value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class DeepcoinSingleOrArrayConverter<TData> : JsonConverter<TData[]>
{
	public override TData[] ReadJson(JsonReader reader, Type objectType, TData[] existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return [];
		if (reader.TokenType == JsonToken.StartObject)
			return [serializer.Deserialize<TData>(reader)];
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Deepcoin payload must be an object or array.");

		var result = new List<TData>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			result.Add(serializer.Deserialize<TData>(reader));
		if (reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Deepcoin payload array is not terminated.");
		return [.. result];
	}

	public override void WriteJson(JsonWriter writer, TData[] value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

static class DeepcoinJson
{
	public static string ReadString(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException($"Deepcoin {field} is missing.");
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or JsonToken.Float or JsonToken.Boolean))
			throw new JsonSerializationException($"Deepcoin {field} has an invalid type.");
		return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
	}

	public static long ReadInt64(JsonReader reader, string field)
	{
		var value = ReadString(reader, field);
		if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			throw new JsonSerializationException($"Deepcoin {field} is not an integer.");
		return result;
	}

	public static void SkipToEndArray(JsonReader reader, string valueName)
	{
		while (reader.Read())
		{
			if (reader.TokenType == JsonToken.EndArray)
				return;
			if (reader.TokenType is JsonToken.StartArray or JsonToken.StartObject)
				reader.Skip();
		}
		throw new JsonSerializationException($"Deepcoin {valueName} is not terminated.");
	}
}

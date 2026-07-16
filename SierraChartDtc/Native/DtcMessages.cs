namespace StockSharp.SierraChartDtc.Native;

internal abstract class DtcMessage
{
	protected DtcMessage(DtcMessageTypes type)
	{
		Type = type;
	}

	public DtcMessageTypes Type { get; }
}

internal sealed class DtcUnknownMessage : DtcMessage
{
	public DtcUnknownMessage(DtcMessageTypes type)
		: base(type)
	{
	}
}

internal sealed class DtcEncodingRequest : DtcMessage
{
	public DtcEncodingRequest()
		: base(DtcMessageTypes.EncodingRequest)
	{
	}

	public int ProtocolVersion { get; set; } = DtcProtocol.CurrentVersion;
	public DtcEncodings Encoding { get; set; } = DtcEncodings.BinaryWithVariableLengthStrings;
}

internal sealed class DtcEncodingResponse : DtcMessage
{
	public DtcEncodingResponse()
		: base(DtcMessageTypes.EncodingResponse)
	{
	}

	public int ProtocolVersion { get; set; }
	public DtcEncodings Encoding { get; set; }
	public string ProtocolType { get; set; }
}

internal sealed class DtcLogonRequest : DtcMessage
{
	public DtcLogonRequest()
		: base(DtcMessageTypes.LogonRequest)
	{
	}

	public int ProtocolVersion { get; set; } = DtcProtocol.CurrentVersion;
	public string UserName { get; set; }
	public string Password { get; set; }
	public string GeneralText { get; set; }
	public int Integer1 { get; set; }
	public int Integer2 { get; set; }
	public int HeartbeatIntervalSeconds { get; set; }
	public string TradeAccount { get; set; }
	public string HardwareIdentifier { get; set; }
	public string ClientName { get; set; }
	public int MarketDataTransmissionInterval { get; set; }
}

internal sealed class DtcLogonResponse : DtcMessage
{
	public DtcLogonResponse()
		: base(DtcMessageTypes.LogonResponse)
	{
	}

	public int ProtocolVersion { get; set; }
	public DtcLogonStatuses Result { get; set; }
	public string ResultText { get; set; }
	public string ReconnectAddress { get; set; }
	public int Integer1 { get; set; }
	public string ServerName { get; set; }
	public bool IsMarketDepthBestBidAsk { get; set; }
	public bool IsTradingSupported { get; set; }
	public bool IsOcoSupported { get; set; }
	public bool IsCancelReplaceSupported { get; set; }
	public string SymbolExchangeDelimiter { get; set; }
	public bool IsSecurityDefinitionsSupported { get; set; }
	public bool IsHistoricalPriceDataSupported { get; set; }
	public bool IsResubscribeRequired { get; set; }
	public bool IsMarketDepthSupported { get; set; }
	public bool IsOneHistoricalRequestPerConnection { get; set; }
	public bool IsBracketOrdersSupported { get; set; }
	public bool IsMultiplePositionsSupported { get; set; }
	public bool IsMarketDataSupported { get; set; }
}

internal sealed class DtcHeartbeat : DtcMessage
{
	public DtcHeartbeat()
		: base(DtcMessageTypes.Heartbeat)
	{
	}

	public uint DroppedMessages { get; set; }
	public DateTime CurrentTime { get; set; }
}

internal sealed class DtcLogoff : DtcMessage
{
	public DtcLogoff()
		: base(DtcMessageTypes.Logoff)
	{
	}

	public string Reason { get; set; }
	public bool IsReconnectDisabled { get; set; }
}

internal sealed class DtcMarketDataFeedStatus : DtcMessage
{
	public DtcMarketDataFeedStatus()
		: base(DtcMessageTypes.MarketDataFeedStatus)
	{
	}

	public DtcMarketDataFeedStatuses Status { get; set; }
}

internal sealed class DtcMarketDataFeedSymbolStatus : DtcMessage
{
	public DtcMarketDataFeedSymbolStatus()
		: base(DtcMessageTypes.MarketDataFeedSymbolStatus)
	{
	}

	public uint SymbolId { get; set; }
	public DtcMarketDataFeedStatuses Status { get; set; }
}

internal sealed class DtcTradingSymbolStatus : DtcMessage
{
	public DtcTradingSymbolStatus()
		: base(DtcMessageTypes.TradingSymbolStatus)
	{
	}

	public uint SymbolId { get; set; }
	public DtcTradingStatuses Status { get; set; }
}

internal sealed class DtcMarketDataRequest : DtcMessage
{
	public DtcMarketDataRequest()
		: base(DtcMessageTypes.MarketDataRequest)
	{
	}

	public DtcRequestActions Action { get; set; }
	public uint SymbolId { get; set; }
	public string Symbol { get; set; }
	public string Exchange { get; set; }
	public uint UpdateIntervalMilliseconds { get; set; }
}

internal sealed class DtcMarketDepthRequest : DtcMessage
{
	public DtcMarketDepthRequest()
		: base(DtcMessageTypes.MarketDepthRequest)
	{
	}

	public DtcRequestActions Action { get; set; }
	public uint SymbolId { get; set; }
	public string Symbol { get; set; }
	public string Exchange { get; set; }
	public int Levels { get; set; }
}

internal sealed class DtcMarketDataSnapshot : DtcMessage
{
	public DtcMarketDataSnapshot()
		: base(DtcMessageTypes.MarketDataSnapshot)
	{
	}

	public uint SymbolId { get; set; }
	public decimal? SettlementPrice { get; set; }
	public decimal? OpenPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal? Volume { get; set; }
	public long? TradesCount { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? BidPrice { get; set; }
	public decimal? AskPrice { get; set; }
	public decimal? AskVolume { get; set; }
	public decimal? BidVolume { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? LastVolume { get; set; }
	public DateTime? LastTime { get; set; }
	public DateTime? BidAskTime { get; set; }
	public DateTime? SettlementTime { get; set; }
	public DateTime? TradingSessionDate { get; set; }
	public DtcTradingStatuses TradingStatus { get; set; }
}

internal sealed class DtcTradeUpdate : DtcMessage
{
	public DtcTradeUpdate(DtcMessageTypes type)
		: base(type)
	{
	}

	public uint SymbolId { get; set; }
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public DateTime? Time { get; set; }
	public DtcAtBidOrAsks AtBidOrAsk { get; set; }
	public byte UnbundledIndicator { get; set; }
	public byte TradeCondition { get; set; }
}

internal sealed class DtcBidAskUpdate : DtcMessage
{
	public DtcBidAskUpdate(DtcMessageTypes type)
		: base(type)
	{
	}

	public uint SymbolId { get; set; }
	public decimal? BidPrice { get; set; }
	public decimal? BidVolume { get; set; }
	public decimal? AskPrice { get; set; }
	public decimal? AskVolume { get; set; }
	public DateTime? Time { get; set; }
}

internal enum DtcSessionUpdateFields
{
	Open,
	High,
	Low,
	Settlement,
	Volume,
	OpenInterest,
	TradesCount,
	TradingSessionDate,
}

internal sealed class DtcSessionUpdate : DtcMessage
{
	public DtcSessionUpdate(DtcMessageTypes type)
		: base(type)
	{
	}

	public uint SymbolId { get; set; }
	public DtcSessionUpdateFields Field { get; set; }
	public decimal? Value { get; set; }
	public DateTime? Time { get; set; }
}

internal sealed class DtcDepthUpdate : DtcMessage
{
	public DtcDepthUpdate(DtcMessageTypes type)
		: base(type)
	{
	}

	public uint SymbolId { get; set; }
	public DtcAtBidOrAsks Side { get; set; }
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public long? OrdersCount { get; set; }
	public int Level { get; set; }
	public DtcDepthUpdateTypes UpdateType { get; set; }
	public DtcFinalUpdates FinalUpdate { get; set; }
	public bool IsSnapshot { get; set; }
	public bool IsFirstSnapshot { get; set; }
	public DateTime? Time { get; set; }
}

internal sealed class DtcSymbolsForExchangeRequest : DtcMessage
{
	public DtcSymbolsForExchangeRequest()
		: base(DtcMessageTypes.SymbolsForExchangeRequest)
	{
	}

	public int RequestId { get; set; }
	public string Exchange { get; set; }
	public DtcSecurityTypes SecurityType { get; set; }
	public DtcRequestActions Action { get; set; } = DtcRequestActions.Subscribe;
	public string Symbol { get; set; }
}

internal sealed class DtcSymbolSearchRequest : DtcMessage
{
	public DtcSymbolSearchRequest()
		: base(DtcMessageTypes.SymbolSearchRequest)
	{
	}

	public int RequestId { get; set; }
	public string SearchText { get; set; }
	public string Exchange { get; set; }
	public DtcSecurityTypes SecurityType { get; set; }
	public DtcSearchTypes SearchType { get; set; } = DtcSearchTypes.BySymbol;
}

internal sealed class DtcSecurityDefinitionRequest : DtcMessage
{
	public DtcSecurityDefinitionRequest()
		: base(DtcMessageTypes.SecurityDefinitionForSymbolRequest)
	{
	}

	public int RequestId { get; set; }
	public string Symbol { get; set; }
	public string Exchange { get; set; }
}

internal sealed class DtcSecurityDefinition : DtcMessage
{
	public DtcSecurityDefinition(DtcMessageTypes type)
		: base(type)
	{
	}

	public int RequestId { get; set; }
	public bool IsFinal { get; set; }
	public bool IsBidAskOnly { get; set; }
	public string Symbol { get; set; }
	public string Exchange { get; set; }
	public DtcSecurityTypes SecurityType { get; set; }
	public string Description { get; set; }
	public decimal? MinPriceIncrement { get; set; }
	public decimal? CurrencyValuePerIncrement { get; set; }
	public string UnderlyingSymbol { get; set; }
	public decimal? StrikePrice { get; set; }
	public DtcPutCalls PutOrCall { get; set; }
	public DateTime? ExpirationDate { get; set; }
	public decimal? QuantityDivisor { get; set; }
	public bool IsMarketDepthSupported { get; set; }
	public string ExchangeSymbol { get; set; }
	public string Currency { get; set; }
	public decimal? ContractSize { get; set; }
	public decimal? OpenInterest { get; set; }
	public bool IsDelayed { get; set; }
	public long SecurityIdentifier { get; set; }
	public string ProductIdentifier { get; set; }
}

internal sealed class DtcHistoricalPriceRequest : DtcMessage
{
	public DtcHistoricalPriceRequest()
		: base(DtcMessageTypes.HistoricalPriceDataRequest)
	{
	}

	public int RequestId { get; set; }
	public string Symbol { get; set; }
	public string Exchange { get; set; }
	public int IntervalSeconds { get; set; }
	public DateTime? From { get; set; }
	public DateTime? To { get; set; }
	public uint MaxDays { get; set; }
	public bool IsDividendAdjusted { get; set; }
}

internal sealed class DtcHistoricalPriceHeader : DtcMessage
{
	public DtcHistoricalPriceHeader()
		: base(DtcMessageTypes.HistoricalPriceDataResponseHeader)
	{
	}

	public int RequestId { get; set; }
	public int IntervalSeconds { get; set; }
	public bool IsCompressed { get; set; }
	public bool IsEmpty { get; set; }
}

internal sealed class DtcHistoricalPriceRecord : DtcMessage
{
	public DtcHistoricalPriceRecord()
		: base(DtcMessageTypes.HistoricalPriceDataRecordResponse)
	{
	}

	public int RequestId { get; set; }
	public DateTime Time { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public long OpenInterestOrTrades { get; set; }
	public decimal BidVolume { get; set; }
	public decimal AskVolume { get; set; }
	public bool IsFinal { get; set; }
}

internal sealed class DtcHistoricalTickRecord : DtcMessage
{
	public DtcHistoricalTickRecord()
		: base(DtcMessageTypes.HistoricalPriceDataTickRecordResponse)
	{
	}

	public int RequestId { get; set; }
	public DateTime Time { get; set; }
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public DtcAtBidOrAsks AtBidOrAsk { get; set; }
	public bool IsFinal { get; set; }
}

internal sealed class DtcHistoricalPriceTrailer : DtcMessage
{
	public DtcHistoricalPriceTrailer()
		: base(DtcMessageTypes.HistoricalPriceDataResponseTrailer)
	{
	}

	public int RequestId { get; set; }
	public DateTime? LastTime { get; set; }
}

internal sealed class DtcSubmitOrder : DtcMessage
{
	public DtcSubmitOrder()
		: base(DtcMessageTypes.SubmitNewSingleOrder)
	{
	}

	public string Symbol { get; set; }
	public string Exchange { get; set; }
	public string TradeAccount { get; set; }
	public string ClientOrderId { get; set; }
	public DtcOrderTypes OrderType { get; set; }
	public DtcBuySells Side { get; set; }
	public decimal? Price1 { get; set; }
	public decimal? Price2 { get; set; }
	public decimal Quantity { get; set; }
	public DtcTimeInForces TimeInForce { get; set; }
	public DateTime? GoodTillTime { get; set; }
	public bool IsAutomated { get; set; }
	public bool IsParent { get; set; }
	public string FreeFormText { get; set; }
	public DtcOpenCloses OpenOrClose { get; set; }
	public decimal MaxShowQuantity { get; set; }
	public decimal IntendedPositionQuantity { get; set; }
}

internal sealed class DtcReplaceOrder : DtcMessage
{
	public DtcReplaceOrder()
		: base(DtcMessageTypes.CancelReplaceOrder)
	{
	}

	public string ServerOrderId { get; set; }
	public string ClientOrderId { get; set; }
	public decimal? Price1 { get; set; }
	public decimal? Price2 { get; set; }
	public decimal Quantity { get; set; }
	public bool IsPrice1Set { get; set; }
	public bool IsPrice2Set { get; set; }
	public DtcTimeInForces TimeInForce { get; set; }
	public DateTime? GoodTillTime { get; set; }
	public string TradeAccount { get; set; }
}

internal sealed class DtcCancelOrder : DtcMessage
{
	public DtcCancelOrder()
		: base(DtcMessageTypes.CancelOrder)
	{
	}

	public string ServerOrderId { get; set; }
	public string ClientOrderId { get; set; }
	public string TradeAccount { get; set; }
}

internal sealed class DtcOpenOrdersRequest : DtcMessage
{
	public DtcOpenOrdersRequest()
		: base(DtcMessageTypes.OpenOrdersRequest)
	{
	}

	public int RequestId { get; set; }
	public bool IsAllOrders { get; set; } = true;
	public string ServerOrderId { get; set; }
	public string TradeAccount { get; set; }
}

internal sealed class DtcHistoricalFillsRequest : DtcMessage
{
	public DtcHistoricalFillsRequest()
		: base(DtcMessageTypes.HistoricalOrderFillsRequest)
	{
	}

	public int RequestId { get; set; }
	public string ServerOrderId { get; set; }
	public int Days { get; set; }
	public string TradeAccount { get; set; }
	public DateTime? From { get; set; }
}

internal sealed class DtcCurrentPositionsRequest : DtcMessage
{
	public DtcCurrentPositionsRequest()
		: base(DtcMessageTypes.CurrentPositionsRequest)
	{
	}

	public int RequestId { get; set; }
	public string TradeAccount { get; set; }
}

internal sealed class DtcTradeAccountsRequest : DtcMessage
{
	public DtcTradeAccountsRequest()
		: base(DtcMessageTypes.TradeAccountsRequest)
	{
	}

	public int RequestId { get; set; }
}

internal sealed class DtcAccountBalanceRequest : DtcMessage
{
	public DtcAccountBalanceRequest()
		: base(DtcMessageTypes.AccountBalanceRequest)
	{
	}

	public int RequestId { get; set; }
	public string TradeAccount { get; set; }
}

internal sealed class DtcOrderUpdate : DtcMessage
{
	public DtcOrderUpdate()
		: base(DtcMessageTypes.OrderUpdate)
	{
	}

	public int RequestId { get; set; }
	public int TotalMessages { get; set; }
	public int MessageNumber { get; set; }
	public string Symbol { get; set; }
	public string Exchange { get; set; }
	public string PreviousServerOrderId { get; set; }
	public string ServerOrderId { get; set; }
	public string ClientOrderId { get; set; }
	public string ExchangeOrderId { get; set; }
	public DtcOrderStatuses Status { get; set; }
	public DtcOrderUpdateReasons Reason { get; set; }
	public DtcOrderTypes OrderType { get; set; }
	public DtcBuySells Side { get; set; }
	public decimal? Price1 { get; set; }
	public decimal? Price2 { get; set; }
	public DtcTimeInForces TimeInForce { get; set; }
	public DateTime? GoodTillTime { get; set; }
	public decimal? Quantity { get; set; }
	public decimal? FilledQuantity { get; set; }
	public decimal? RemainingQuantity { get; set; }
	public decimal? AverageFillPrice { get; set; }
	public decimal? LastFillPrice { get; set; }
	public DateTime? LastFillTime { get; set; }
	public decimal? LastFillQuantity { get; set; }
	public string LastFillExecutionId { get; set; }
	public string TradeAccount { get; set; }
	public string InfoText { get; set; }
	public bool IsNoOrders { get; set; }
	public DtcOpenCloses OpenOrClose { get; set; }
	public string FreeFormText { get; set; }
	public DateTime? OrderReceivedTime { get; set; }
	public DateTime? LatestTransactionTime { get; set; }
}

internal sealed class DtcHistoricalFill : DtcMessage
{
	public DtcHistoricalFill()
		: base(DtcMessageTypes.HistoricalOrderFillResponse)
	{
	}

	public int RequestId { get; set; }
	public int TotalMessages { get; set; }
	public int MessageNumber { get; set; }
	public string Symbol { get; set; }
	public string Exchange { get; set; }
	public string ServerOrderId { get; set; }
	public DtcBuySells Side { get; set; }
	public decimal Price { get; set; }
	public DateTime Time { get; set; }
	public decimal Quantity { get; set; }
	public string ExecutionId { get; set; }
	public string TradeAccount { get; set; }
	public bool IsNoFills { get; set; }
	public string InfoText { get; set; }
}

internal sealed class DtcPositionUpdate : DtcMessage
{
	public DtcPositionUpdate()
		: base(DtcMessageTypes.PositionUpdate)
	{
	}

	public int RequestId { get; set; }
	public int TotalMessages { get; set; }
	public int MessageNumber { get; set; }
	public string Symbol { get; set; }
	public string Exchange { get; set; }
	public decimal Quantity { get; set; }
	public decimal AveragePrice { get; set; }
	public string PositionIdentifier { get; set; }
	public string TradeAccount { get; set; }
	public bool IsNoPositions { get; set; }
	public bool IsUnsolicited { get; set; }
	public decimal MarginRequirement { get; set; }
	public DateTime? EntryTime { get; set; }
	public decimal OpenProfitLoss { get; set; }
}

internal sealed class DtcTradeAccount : DtcMessage
{
	public DtcTradeAccount()
		: base(DtcMessageTypes.TradeAccountResponse)
	{
	}

	public int TotalMessages { get; set; }
	public int MessageNumber { get; set; }
	public string Account { get; set; }
	public int RequestId { get; set; }
	public bool IsTradingDisabled { get; set; }
}

internal sealed class DtcAccountBalance : DtcMessage
{
	public DtcAccountBalance()
		: base(DtcMessageTypes.AccountBalanceUpdate)
	{
	}

	public int RequestId { get; set; }
	public decimal CashBalance { get; set; }
	public decimal AvailableFunds { get; set; }
	public string Currency { get; set; }
	public string TradeAccount { get; set; }
	public decimal SecuritiesValue { get; set; }
	public decimal MarginRequirement { get; set; }
	public int TotalMessages { get; set; }
	public int MessageNumber { get; set; }
	public bool IsNoBalances { get; set; }
	public bool IsUnsolicited { get; set; }
	public decimal OpenProfitLoss { get; set; }
	public decimal DailyProfitLoss { get; set; }
	public string InfoText { get; set; }
	public bool IsTradingDisabled { get; set; }
	public DateTime? TransactionTime { get; set; }
}

internal sealed class DtcReject : DtcMessage
{
	public DtcReject(DtcMessageTypes type)
		: base(type)
	{
	}

	public int RequestId { get; set; }
	public uint SymbolId { get; set; }
	public string Text { get; set; }
	public short ReasonCode { get; set; }
	public ushort RetrySeconds { get; set; }
}

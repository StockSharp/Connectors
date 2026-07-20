namespace StockSharp.Injective.Native.Model;

sealed class InjectiveSpotMarketsEnvelope
{
	public InjectiveSpotMarket[] Markets { get; set; }
}

sealed class InjectiveSpotMarket
{
	public string MarketId { get; set; }
	public string MarketStatus { get; set; }
	public string Ticker { get; set; }
	public string BaseDenom { get; set; }
	public InjectiveTokenMeta BaseTokenMeta { get; set; }
	public string QuoteDenom { get; set; }
	public InjectiveTokenMeta QuoteTokenMeta { get; set; }
	public string MakerFeeRate { get; set; }
	public string TakerFeeRate { get; set; }
	public string ServiceProviderFee { get; set; }
	public string MinPriceTickSize { get; set; }
	public string MinQuantityTickSize { get; set; }
	public string MinNotional { get; set; }
}

sealed class InjectiveDerivativeMarketsEnvelope
{
	public InjectiveDerivativeMarket[] Markets { get; set; }
}

sealed class InjectiveDerivativeMarket
{
	public string MarketId { get; set; }
	public string MarketStatus { get; set; }
	public string Ticker { get; set; }
	public string OracleBase { get; set; }
	public string OracleQuote { get; set; }
	public string OracleType { get; set; }
	public int OracleScaleFactor { get; set; }
	public string InitialMarginRatio { get; set; }
	public string MaintenanceMarginRatio { get; set; }
	public string QuoteDenom { get; set; }
	public InjectiveTokenMeta QuoteTokenMeta { get; set; }
	public string MakerFeeRate { get; set; }
	public string TakerFeeRate { get; set; }
	public string ServiceProviderFee { get; set; }
	public bool IsPerpetual { get; set; }
	public string MinPriceTickSize { get; set; }
	public string MinQuantityTickSize { get; set; }
	public InjectivePerpetualMarketInfo PerpetualMarketInfo { get; set; }
	public InjectivePerpetualMarketFunding PerpetualMarketFunding { get; set; }
	public InjectiveExpiryFuturesMarketInfo ExpiryFuturesMarketInfo { get; set; }
	public string MinNotional { get; set; }
	public string ReduceMarginRatio { get; set; }
}

sealed class InjectivePerpetualMarketInfo
{
	public string HourlyFundingRateCap { get; set; }
	public string HourlyInterestRate { get; set; }
	public long NextFundingTimestamp { get; set; }
	public long FundingInterval { get; set; }
}

sealed class InjectivePerpetualMarketFunding
{
	public string CumulativeFunding { get; set; }
	public string CumulativePrice { get; set; }
	public long LastTimestamp { get; set; }
	public string LastFundingRate { get; set; }
}

sealed class InjectiveExpiryFuturesMarketInfo
{
	public long ExpirationTimestamp { get; set; }
	public string SettlementPrice { get; set; }
}

sealed class InjectiveOrdersEnvelope
{
	public InjectiveOrder[] Orders { get; set; }
	public InjectivePaging Paging { get; set; }
}

sealed class InjectiveOrder
{
	public string OrderHash { get; set; }
	public string OrderSide { get; set; }
	public string Direction { get; set; }
	public string MarketId { get; set; }
	public string SubaccountId { get; set; }
	public bool IsReduceOnly { get; set; }
	public string Margin { get; set; }
	public string Price { get; set; }
	public string Quantity { get; set; }
	public string UnfilledQuantity { get; set; }
	public string FilledQuantity { get; set; }
	public string TriggerPrice { get; set; }
	public string FeeRecipient { get; set; }
	public string State { get; set; }
	public long CreatedAt { get; set; }
	public long UpdatedAt { get; set; }
	public long OrderNumber { get; set; }
	public string OrderType { get; set; }
	public bool IsConditional { get; set; }
	public ulong TriggerAt { get; set; }
	public string PlacedOrderHash { get; set; }
	public string ExecutionType { get; set; }
	public string TxHash { get; set; }
	public string Cid { get; set; }
	public bool IsActive { get; set; }
	public string AccountAddress { get; set; }
}

sealed class InjectivePositionsEnvelope
{
	public InjectivePosition[] Positions { get; set; }
	public InjectivePaging Paging { get; set; }
}

sealed class InjectivePosition
{
	public string Ticker { get; set; }
	public string MarketId { get; set; }
	public string SubaccountId { get; set; }
	public string Direction { get; set; }
	public string Quantity { get; set; }
	public string EntryPrice { get; set; }
	public string Margin { get; set; }
	public string LiquidationPrice { get; set; }
	public string MarkPrice { get; set; }
	public string AggregateReduceOnlyQuantity { get; set; }
	public long UpdatedAt { get; set; }
	public long CreatedAt { get; set; }
	public string Denom { get; set; }
	public string FundingLast { get; set; }
	public string FundingSum { get; set; }
	public string CumulativeFundingEntry { get; set; }
	public string EffectiveCumulativeFundingEntry { get; set; }
	public string Upnl { get; set; }
}

sealed class InjectivePortfolioEnvelope
{
	public InjectivePortfolio Portfolio { get; set; }
}

sealed class InjectivePortfolio
{
	public string AccountAddress { get; set; }
	public InjectiveCoin[] BankBalances { get; set; }
	public InjectiveSubaccountBalance[] Subaccounts { get; set; }
	[JsonProperty("positionsWithUPNL")]
	public InjectivePositionWithPnl[] PositionsWithUpnl { get; set; }
}

sealed class InjectiveCoin
{
	public string Denom { get; set; }
	public string Amount { get; set; }
	public string UsdValue { get; set; }
}

sealed class InjectiveSubaccountBalance
{
	public string SubaccountId { get; set; }
	public string Denom { get; set; }
	public InjectiveSubaccountDeposit Deposit { get; set; }
}

sealed class InjectiveSubaccountDeposit
{
	public string TotalBalance { get; set; }
	public string AvailableBalance { get; set; }
	public string TotalBalanceUsd { get; set; }
	public string AvailableBalanceUsd { get; set; }
}

sealed class InjectivePositionWithPnl
{
	public InjectivePosition Position { get; set; }

	[JsonProperty("unrealizedPNL")]
	public string UnrealizedPnl { get; set; }
}

sealed class InjectiveAccountEnvelope
{
	public InjectiveEthAccount Account { get; set; }
}

sealed class InjectiveEthAccount
{
	[JsonProperty("base_account")]
	public InjectiveBaseAccount BaseAccount { get; set; }
}

sealed class InjectiveBaseAccount
{
	public string Address { get; set; }

	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }

	public string Sequence { get; set; }
}

sealed class InjectiveBroadcastRequest
{
	[JsonProperty("tx_bytes")]
	public string TransactionBytes { get; set; }

	[JsonProperty("mode")]
	public string Mode { get; set; }
}

sealed class InjectiveBroadcastEnvelope
{
	[JsonProperty("tx_response")]
	public InjectiveTransactionResponse TransactionResponse { get; set; }
}

sealed class InjectiveTransactionResponse
{
	public string Height { get; set; }
	public string TxHash { get; set; }
	public string Codespace { get; set; }
	public uint Code { get; set; }
	public string Data { get; set; }

	[JsonProperty("raw_log")]
	public string RawLog { get; set; }

	public string Timestamp { get; set; }
}

sealed class InjectiveLatestBlockEnvelope
{
	public InjectiveBlock Block { get; set; }
}

sealed class InjectiveChainSocketRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("params")]
	public InjectiveChainSocketParameters Params { get; set; }
}

sealed class InjectiveChainSocketParameters
{
	[JsonProperty("query")]
	public string Query { get; set; }
}

sealed class InjectiveChainSocketEnvelope
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	public long? Id { get; set; }
	public InjectiveChainSocketResult Result { get; set; }
	public InjectiveChainSocketError Error { get; set; }
}

sealed class InjectiveChainSocketResult
{
	public string Query { get; set; }
	public InjectiveChainSocketData Data { get; set; }
}

sealed class InjectiveChainSocketData
{
	public string Type { get; set; }
	public InjectiveChainSocketValue Value { get; set; }
}

sealed class InjectiveChainSocketValue
{
	public InjectiveBlock Block { get; set; }
}

sealed class InjectiveChainSocketError
{
	public int Code { get; set; }
	public string Message { get; set; }
}

sealed class InjectiveBlock
{
	public InjectiveBlockHeader Header { get; set; }
}

sealed class InjectiveBlockHeader
{
	public string Height { get; set; }
	public string Time { get; set; }
}

sealed class InjectiveApiError
{
	public int Code { get; set; }
	public string Message { get; set; }
	public string Details { get; set; }
}

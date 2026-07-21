namespace StockSharp.SynFutures.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesApiResponse<T>
{
	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesSigningPayload
{
	[JsonProperty("uri", Order = 1)]
	public string Uri { get; init; }

	[JsonProperty("nonce", Order = 2)]
	public string Nonce { get; init; }

	[JsonProperty("ts", Order = 3)]
	public long Timestamp { get; init; }
}

readonly record struct SynFuturesQueryParameter(string Name, string Value);

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesToken
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("image")]
	public string Image { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesFunding
{
	[JsonProperty("long")]
	public string Long { get; set; }

	[JsonProperty("short")]
	public string Short { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesMarket
{
	[JsonProperty("instrumentAddr")]
	public string InstrumentAddress { get; set; }

	[JsonProperty("expiry")]
	public uint Expiry { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("baseToken")]
	public SynFuturesToken BaseToken { get; set; }

	[JsonProperty("quoteToken")]
	public SynFuturesToken QuoteToken { get; set; }

	[JsonProperty("fairPrice")]
	public string FairPrice { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("fairPriceChange24h")]
	public string PriceChange24Hours { get; set; }

	[JsonProperty("baseVolume24h")]
	public string BaseVolume24Hours { get; set; }

	[JsonProperty("quoteVolume24h")]
	public string QuoteVolume24Hours { get; set; }

	[JsonProperty("openInterests")]
	public string OpenInterest { get; set; }

	[JsonProperty("tvl")]
	public string TotalValueLocked { get; set; }

	[JsonProperty("volume24hUsd")]
	public string VolumeUsd24Hours { get; set; }

	[JsonProperty("tvlUsd")]
	public string TotalValueLockedUsd { get; set; }

	[JsonProperty("openInterestsUsd")]
	public string OpenInterestUsd { get; set; }

	[JsonProperty("longOi")]
	public string LongOpenInterest { get; set; }

	[JsonProperty("shortOi")]
	public string ShortOpenInterest { get; set; }

	[JsonProperty("periods1hFunding")]
	public SynFuturesFunding PeriodFunding { get; set; }

	[JsonProperty("last1hFunding")]
	public SynFuturesFunding LastFunding { get; set; }

	[JsonProperty("fundingRatePerHour")]
	public string FundingRatePerHour { get; set; }

	[JsonProperty("fullSymbol")]
	public string FullSymbol { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }

	[JsonProperty("marketType")]
	public string MarketType { get; set; }

	[JsonProperty("poolFee24h")]
	public string PoolFee24Hours { get; set; }

	[JsonProperty("maxLeverage")]
	public int MaximumLeverage { get; set; }

	[JsonProperty("spotPrice")]
	public string SpotPrice { get; set; }

	[JsonProperty("condition")]
	public int Condition { get; set; }

	[JsonProperty("ammStatus")]
	public int AmmStatus { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesBlockInfo
{
	[JsonProperty("height")]
	public long Height { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesDepthLevel
{
	[JsonProperty("tick")]
	public int Tick { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("baseSize")]
	public string BaseSize { get; set; }

	[JsonProperty("quoteSize")]
	public string QuoteSize { get; set; }

	[JsonProperty("baseSum")]
	public string BaseSum { get; set; }

	[JsonProperty("quoteSum")]
	public string QuoteSum { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesDepth
{
	[JsonProperty("blockInfo")]
	public SynFuturesBlockInfo BlockInfo { get; set; }

	[JsonProperty("bids")]
	public SynFuturesDepthLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public SynFuturesDepthLevel[] Asks { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesDepthSteps
{
	[JsonProperty("1")]
	public SynFuturesDepth Step1 { get; set; }

	[JsonProperty("10")]
	public SynFuturesDepth Step10 { get; set; }

	[JsonProperty("30")]
	public SynFuturesDepth Step30 { get; set; }

	[JsonProperty("100")]
	public SynFuturesDepth Step100 { get; set; }

	[JsonProperty("500")]
	public SynFuturesDepth Step500 { get; set; }

	public SynFuturesDepth SelectFinestCurrent()
		=> Step10 ?? Step1 ?? Step30 ?? Step100 ?? Step500;
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesCandle
{
	[JsonProperty("openTimestamp")]
	public long OpenTimestamp { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("baseVolume")]
	public decimal BaseVolume { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("closeTimestamp")]
	public long CloseTimestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesPortfolioData
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("portfolios")]
	public SynFuturesPortfolio[] Portfolios { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesPortfolio
{
	[JsonProperty("instrumentAddr")]
	public string InstrumentAddress { get; set; }

	[JsonProperty("traderAddr")]
	public string TraderAddress { get; set; }

	[JsonProperty("expiry")]
	public uint Expiry { get; set; }

	[JsonProperty("blockInfo")]
	public SynFuturesBlockInfo BlockInfo { get; set; }

	[JsonProperty("position")]
	public SynFuturesPosition Position { get; set; }

	[JsonProperty("orders")]
	public SynFuturesOpenOrder[] Orders { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesPosition
{
	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("entryNotional")]
	public string EntryNotional { get; set; }

	[JsonProperty("entrySocialLossIndex")]
	public string EntrySocialLossIndex { get; set; }

	[JsonProperty("entryFundingIndex")]
	public string EntryFundingIndex { get; set; }

	[JsonProperty("side")]
	public int Side { get; set; }

	[JsonProperty("entryPrice")]
	public string EntryPrice { get; set; }

	[JsonProperty("lastUpdateTime")]
	public long LastUpdateTime { get; set; }

	[JsonProperty("lastUpdateTxHash")]
	public string LastUpdateTransactionHash { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesOpenOrder
{
	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("taken")]
	public string Taken { get; set; }

	[JsonProperty("tick")]
	public int Tick { get; set; }

	[JsonProperty("nonce")]
	public uint Nonce { get; set; }

	[JsonProperty("side")]
	public int Side { get; set; }

	[JsonProperty("limitPrice")]
	public string LimitPrice { get; set; }

	[JsonProperty("oid")]
	public long OrderId { get; set; }

	[JsonProperty("lastUpdateTime")]
	public long LastUpdateTime { get; set; }

	[JsonProperty("lastUpdateTxHash")]
	public string LastUpdateTransactionHash { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesGateData
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("portfolios")]
	public SynFuturesGateBalance[] Portfolios { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesGateBalance
{
	[JsonProperty("quote")]
	public string QuoteAddress { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("reservedBalance")]
	public string ReservedBalance { get; set; }

	[JsonProperty("maxWithdrawable")]
	public string MaximumWithdrawable { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("currentPrice")]
	public decimal CurrentPrice { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesHistoryPage<T>
{
	[JsonProperty("list")]
	public T[] Items { get; set; }

	[JsonProperty("totalCount")]
	public long TotalCount { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("instrumentAddress")]
	public string InstrumentAddress { get; set; }

	[JsonProperty("expiry")]
	public uint Expiry { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("tradeFee")]
	public string TradeFee { get; set; }

	[JsonProperty("protocolFee")]
	public string ProtocolFee { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("txHash")]
	public string TransactionHash { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("baseToken")]
	public SynFuturesToken BaseToken { get; set; }

	[JsonProperty("quoteToken")]
	public SynFuturesToken QuoteToken { get; set; }

	[JsonProperty("typeString")]
	public string TypeName { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("trader")]
	public string Trader { get; set; }

	[JsonProperty("fairPrice")]
	public string FairPrice { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesOrderHistory
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("instrumentAddress")]
	public string InstrumentAddress { get; set; }

	[JsonProperty("expiry")]
	public uint Expiry { get; set; }

	[JsonProperty("placeTimestamp")]
	public long PlaceTimestamp { get; set; }

	[JsonProperty("placeTxHash")]
	public string PlaceTransactionHash { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("takenSize")]
	public string TakenSize { get; set; }

	[JsonProperty("takenBalance")]
	public string TakenBalance { get; set; }

	[JsonProperty("orderPrice")]
	public string OrderPrice { get; set; }

	[JsonProperty("feeRebate")]
	public string FeeRebate { get; set; }

	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("cancelTimestamp")]
	public long CancelTimestamp { get; set; }

	[JsonProperty("cancelTxHash")]
	public string CancelTransactionHash { get; set; }

	[JsonProperty("fillTimestamp")]
	public long FillTimestamp { get; set; }

	[JsonProperty("fillTxHash")]
	public string FillTransactionHash { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("baseToken")]
	public SynFuturesToken BaseToken { get; set; }

	[JsonProperty("quoteToken")]
	public SynFuturesToken QuoteToken { get; set; }

	[JsonProperty("typeString")]
	public string TypeName { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("tradeValue")]
	public string TradeValue { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesQuotation
{
	[JsonProperty("benchmark")]
	public string Benchmark { get; set; }

	[JsonProperty("sqrtFairPX96")]
	public string FairSqrtPriceX96 { get; set; }

	[JsonProperty("tick")]
	public string TickText { get; set; }

	[JsonProperty("mark")]
	public string Mark { get; set; }

	[JsonProperty("entryNotional")]
	public string EntryNotional { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("minAmount")]
	public string MinimumAmount { get; set; }

	[JsonProperty("sqrtPostFairPX96")]
	public string PostFairSqrtPriceX96 { get; set; }

	[JsonProperty("postTick")]
	public string PostTickText { get; set; }
}

namespace StockSharp.Bitmex.Native.Model;

class Symbol
{
	[JsonProperty("symbol")]
	public string Id { get; set; }

	[JsonProperty("rootSymbol")]
	public string RootSymbol { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("typ")]
	public string Typ { get; set; }

	[JsonProperty("listing")]
	public DateTime? Listing { get; set; }

	[JsonProperty("front")]
	public DateTime? Front { get; set; }

	[JsonProperty("expiry")]
	public DateTime? Expiry { get; set; }

	[JsonProperty("settle")]
	public DateTime? Settle { get; set; }

	[JsonProperty("relistInterval")]
	public DateTime? RelistInterval { get; set; }

	[JsonProperty("inverseLeg")]
	public string InverseLeg { get; set; }

	[JsonProperty("sellLeg")]
	public string SellLeg { get; set; }

	[JsonProperty("buyLeg")]
	public string BuyLeg { get; set; }

	[JsonProperty("optionStrikePcnt")]
	public double? OptionStrikePcnt { get; set; }

	[JsonProperty("optionStrikeRound")]
	public double? OptionStrikeRound { get; set; }

	[JsonProperty("optionStrikePrice")]
	public double? OptionStrikePrice { get; set; }

	[JsonProperty("optionMultiplier")]
	public double? OptionMultiplier { get; set; }

	[JsonProperty("positionCurrency")]
	public string PositionCurrency { get; set; }

	[JsonProperty("underlying")]
	public string Underlying { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("underlyingSymbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("reference")]
	public string Reference { get; set; }

	[JsonProperty("referenceSymbol")]
	public string ReferenceSymbol { get; set; }

	[JsonProperty("calcInterval")]
	public DateTime? CalcInterval { get; set; }

	[JsonProperty("publishInterval")]
	public DateTime? PublishInterval { get; set; }

	[JsonProperty("publishTime")]
	public DateTime? PublishTime { get; set; }

	[JsonProperty("maxOrderQty")]
	public double? MaxOrderQty { get; set; }

	[JsonProperty("maxPrice")]
	public double? MaxPrice { get; set; }

	[JsonProperty("lotSize")]
	public double? LotSize { get; set; }

	[JsonProperty("tickSize")]
	public double? TickSize { get; set; }

	[JsonProperty("multiplier")]
	public double? Multiplier { get; set; }

	[JsonProperty("settlCurrency")]
	public string SettlCurrency { get; set; }

	[JsonProperty("underlyingToPositionMultiplier")]
	public double? UnderlyingToPositionMultiplier { get; set; }

	[JsonProperty("underlyingToSettleMultiplier")]
	public double? UnderlyingToSettleMultiplier { get; set; }

	[JsonProperty("quoteToSettleMultiplier")]
	public double? QuoteToSettleMultiplier { get; set; }

	[JsonProperty("isQuanto")]
	public bool? IsQuanto { get; set; }

	[JsonProperty("isInverse")]
	public bool? IsInverse { get; set; }

	[JsonProperty("initMargin")]
	public double? InitMargin { get; set; }

	[JsonProperty("maintMargin")]
	public double? MaintMargin { get; set; }

	[JsonProperty("riskLimit")]
	public double? RiskLimit { get; set; }

	[JsonProperty("riskStep")]
	public double? RiskStep { get; set; }

	[JsonProperty("limit")]
	public object Limit { get; set; }

	[JsonProperty("capped")]
	public bool? Capped { get; set; }

	[JsonProperty("taxed")]
	public bool? Taxed { get; set; }

	[JsonProperty("deleverage")]
	public bool? Deleverage { get; set; }

	[JsonProperty("makerFee")]
	public double? MakerFee { get; set; }

	[JsonProperty("takerFee")]
	public double? TakerFee { get; set; }

	[JsonProperty("settlementFee")]
	public double? SettlementFee { get; set; }

	[JsonProperty("insuranceFee")]
	public double? InsuranceFee { get; set; }

	[JsonProperty("fundingBaseSymbol")]
	public string FundingBaseSymbol { get; set; }

	[JsonProperty("fundingQuoteSymbol")]
	public string FundingQuoteSymbol { get; set; }

	[JsonProperty("fundingPremiumSymbol")]
	public string FundingPremiumSymbol { get; set; }

	[JsonProperty("fundingTimestamp")]
	public DateTime? FundingTimestamp { get; set; }

	[JsonProperty("fundingInterval")]
	public DateTime? FundingInterval { get; set; }

	[JsonProperty("fundingRate")]
	public decimal? FundingRate { get; set; }

	[JsonProperty("indicativeFundingRate")]
	public decimal? IndicativeFundingRate { get; set; }

	[JsonProperty("rebalanceTimestamp")]
	public DateTime? RebalanceTimestamp { get; set; }

	[JsonProperty("rebalanceInterval")]
	public DateTime? RebalanceInterval { get; set; }

	[JsonProperty("openingTimestamp")]
	public DateTime? OpeningTimestamp { get; set; }

	[JsonProperty("closingTimestamp")]
	public DateTime? ClosingTimestamp { get; set; }

	[JsonProperty("sessionInterval")]
	public DateTime? SessionInterval { get; set; }

	[JsonProperty("prevClosePrice")]
	public double? PrevClosePrice { get; set; }

	[JsonProperty("limitDownPrice")]
	public double? LimitDownPrice { get; set; }

	[JsonProperty("limitUpPrice")]
	public double? LimitUpPrice { get; set; }

	[JsonProperty("bankruptLimitDownPrice")]
	public double? BankruptLimitDownPrice { get; set; }

	[JsonProperty("bankruptLimitUpPrice")]
	public double? BankruptLimitUpPrice { get; set; }

	[JsonProperty("prevTotalVolume")]
	public double? PrevTotalVolume { get; set; }

	[JsonProperty("totalVolume")]
	public double? TotalVolume { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("volume24h")]
	public double? Volume24H { get; set; }

	[JsonProperty("prevTotalTurnover")]
	public double? PrevTotalTurnover { get; set; }

	[JsonProperty("totalTurnover")]
	public double? TotalTurnover { get; set; }

	[JsonProperty("turnover")]
	public double? Turnover { get; set; }

	[JsonProperty("turnover24h")]
	public double? Turnover24H { get; set; }

	[JsonProperty("prevPrice24h")]
	public double? PrevPrice24H { get; set; }

	[JsonProperty("vwap")]
	public double? Vwap { get; set; }

	[JsonProperty("highPrice")]
	public double? HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public double? LowPrice { get; set; }

	[JsonProperty("lastPrice")]
	public double? LastPrice { get; set; }

	[JsonProperty("lastPriceProtected")]
	public double? LastPriceProtected { get; set; }

	[JsonProperty("lastTickDirection")]
	public string LastTickDirection { get; set; }

	[JsonProperty("lastChangePcnt")]
	public double? LastChangePcnt { get; set; }

	[JsonProperty("bidPrice")]
	public double? BidPrice { get; set; }

	[JsonProperty("midPrice")]
	public double? MidPrice { get; set; }

	[JsonProperty("askPrice")]
	public double? AskPrice { get; set; }

	[JsonProperty("impactBidPrice")]
	public double? ImpactBidPrice { get; set; }

	[JsonProperty("impactMidPrice")]
	public double? ImpactMidPrice { get; set; }

	[JsonProperty("impactAskPrice")]
	public double? ImpactAskPrice { get; set; }

	[JsonProperty("hasLiquidity")]
	public bool? HasLiquidity { get; set; }

	[JsonProperty("openInterest")]
	public double? OpenInterest { get; set; }

	[JsonProperty("openValue")]
	public double? OpenValue { get; set; }

	[JsonProperty("fairMethod")]
	public string FairMethod { get; set; }

	[JsonProperty("fairBasisRate")]
	public double? FairBasisRate { get; set; }

	[JsonProperty("fairBasis")]
	public double? FairBasis { get; set; }

	[JsonProperty("fairPrice")]
	public double? FairPrice { get; set; }

	[JsonProperty("markMethod")]
	public string MarkMethod { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("indicativeTaxRate")]
	public double? IndicativeTaxRate { get; set; }

	[JsonProperty("indicativeSettlePrice")]
	public double? IndicativeSettlePrice { get; set; }

	[JsonProperty("optionUnderlyingPrice")]
	public double? OptionUnderlyingPrice { get; set; }

	[JsonProperty("settledPrice")]
	public double? SettledPrice { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }
}
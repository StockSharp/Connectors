namespace StockSharp.Kucoin.Native.Futures.Model;

class Contract
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("rootSymbol")]
	public string RootSymbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("firstOpenDate")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime FirstOpenDate { get; set; }

	[JsonProperty("expireDate")]
	public object ExpireDate { get; set; }

	[JsonProperty("settleDate")]
	public object SettleDate { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("settleCurrency")]
	public string SettleCurrency { get; set; }

	[JsonProperty("maxOrderQty")]
	public double? MaxOrderQty { get; set; }

	[JsonProperty("maxPrice")]
	public double? MaxPrice { get; set; }

	[JsonProperty("lotSize")]
	public double? LotSize { get; set; }

	[JsonProperty("tickSize")]
	public double? TickSize { get; set; }

	[JsonProperty("indexPriceTickSize")]
	public double? IndexPriceTickSize { get; set; }

	[JsonProperty("multiplier")]
	public double? Multiplier { get; set; }

	[JsonProperty("initialMargin")]
	public double? InitialMargin { get; set; }

	[JsonProperty("maintainMargin")]
	public double? MaintainMargin { get; set; }

	[JsonProperty("maxRiskLimit")]
	public double? MaxRiskLimit { get; set; }

	[JsonProperty("minRiskLimit")]
	public double? MinRiskLimit { get; set; }

	[JsonProperty("riskStep")]
	public double? RiskStep { get; set; }

	[JsonProperty("makerFeeRate")]
	public double? MakerFeeRate { get; set; }

	[JsonProperty("takerFeeRate")]
	public double? TakerFeeRate { get; set; }

	[JsonProperty("takerFixFee")]
	public double? TakerFixFee { get; set; }

	[JsonProperty("makerFixFee")]
	public double? MakerFixFee { get; set; }

	[JsonProperty("settlementFee")]
	public double? SettlementFee { get; set; }

	[JsonProperty("isDeleverage")]
	public bool? IsDeleverage { get; set; }

	[JsonProperty("isQuanto")]
	public bool? IsQuanto { get; set; }

	[JsonProperty("isInverse")]
	public bool? IsInverse { get; set; }

	[JsonProperty("markMethod")]
	public string MarkMethod { get; set; }

	[JsonProperty("fairMethod")]
	public string FairMethod { get; set; }

	[JsonProperty("fundingBaseSymbol")]
	public string FundingBaseSymbol { get; set; }

	[JsonProperty("fundingQuoteSymbol")]
	public string FundingQuoteSymbol { get; set; }

	[JsonProperty("fundingRateSymbol")]
	public string FundingRateSymbol { get; set; }

	[JsonProperty("indexSymbol")]
	public string IndexSymbol { get; set; }

	[JsonProperty("settlementSymbol")]
	public string SettlementSymbol { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("fundingFeeRate")]
	public double? FundingFeeRate { get; set; }

	[JsonProperty("predictedFundingFeeRate")]
	public double? PredictedFundingFeeRate { get; set; }

	[JsonProperty("openInterest")]
	public int? OpenInterest { get; set; }

	[JsonProperty("turnoverOf24h")]
	public double? TurnoverOf24h { get; set; }

	[JsonProperty("volumeOf24h")]
	public double? VolumeOf24h { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("indexPrice")]
	public double? IndexPrice { get; set; }

	[JsonProperty("lastTradePrice")]
	public double? LastTradePrice { get; set; }

	[JsonProperty("nextFundingRateTime")]
	public int? NextFundingRateTime { get; set; }

	[JsonProperty("maxLeverage")]
	public int? MaxLeverage { get; set; }

	[JsonProperty("sourceExchanges")]
	public List<string> SourceExchanges { get; set; }

	[JsonProperty("premiumsSymbol1M")]
	public string PremiumsSymbol1M { get; set; }

	[JsonProperty("premiumsSymbol8H")]
	public string PremiumsSymbol8H { get; set; }

	[JsonProperty("fundingBaseSymbol1M")]
	public string FundingBaseSymbol1M { get; set; }

	[JsonProperty("fundingQuoteSymbol1M")]
	public string FundingQuoteSymbol1M { get; set; }

	[JsonProperty("lowPrice")]
	public double? LowPrice { get; set; }

	[JsonProperty("highPrice")]
	public double? HighPrice { get; set; }

	[JsonProperty("priceChgPct")]
	public double? PriceChgPct { get; set; }

	[JsonProperty("priceChg")]
	public double? PriceChg { get; set; }
}
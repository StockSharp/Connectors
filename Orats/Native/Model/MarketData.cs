namespace StockSharp.Orats.Native.Model;

sealed class OratsSnapshot
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("optionSymbol")]
	public string OptionSymbol { get; set; }

	[JsonProperty("tradeDate")]
	public string TradeDate { get; set; }

	[JsonProperty("expirDate")]
	public string ExpirationDate { get; set; }

	[JsonProperty("dte")]
	public int? DaysToExpiration { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("stockPrice")]
	public decimal? StockPrice { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("optValue")]
	public decimal? OptionValue { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("bidIv")]
	public decimal? BidIv { get; set; }

	[JsonProperty("midIv")]
	public decimal? MidIv { get; set; }

	[JsonProperty("askIv")]
	public decimal? AskIv { get; set; }

	[JsonProperty("smvVol")]
	public decimal? SmoothedVolatility { get; set; }

	[JsonProperty("residualRate")]
	public decimal? ResidualRate { get; set; }

	[JsonProperty("delta")]
	public decimal? Delta { get; set; }

	[JsonProperty("gamma")]
	public decimal? Gamma { get; set; }

	[JsonProperty("theta")]
	public decimal? Theta { get; set; }

	[JsonProperty("vega")]
	public decimal? Vega { get; set; }

	[JsonProperty("rho")]
	public decimal? Rho { get; set; }

	[JsonProperty("phi")]
	public decimal? Phi { get; set; }

	[JsonProperty("driftlessTheta")]
	public decimal? DriftlessTheta { get; set; }

	[JsonProperty("optSmvVol")]
	public decimal? OptionSmoothedVolatility { get; set; }

	[JsonProperty("extSmvVol")]
	public decimal? ExtendedSmoothedVolatility { get; set; }

	[JsonProperty("extOptValue")]
	public decimal? ExtendedOptionValue { get; set; }

	[JsonProperty("spotPrice")]
	public decimal? SpotPrice { get; set; }

	[JsonProperty("quoteDate")]
	public string QuoteDate { get; set; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; set; }

	[JsonProperty("expiryTod")]
	public string ExpirationTimeOfDay { get; set; }

	[JsonProperty("bid")]
	public decimal? StockBid { get; set; }

	[JsonProperty("ask")]
	public decimal? StockAsk { get; set; }
}

sealed class OratsStrike
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("tradeDate")]
	public string TradeDate { get; set; }

	[JsonProperty("expirDate")]
	public string ExpirationDate { get; set; }

	[JsonProperty("dte")]
	public int? DaysToExpiration { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("stockPrice")]
	public decimal? StockPrice { get; set; }

	[JsonProperty("callVolume")]
	public decimal? CallVolume { get; set; }

	[JsonProperty("callOpenInterest")]
	public decimal? CallOpenInterest { get; set; }

	[JsonProperty("callBidSize")]
	public decimal? CallBidSize { get; set; }

	[JsonProperty("callAskSize")]
	public decimal? CallAskSize { get; set; }

	[JsonProperty("putVolume")]
	public decimal? PutVolume { get; set; }

	[JsonProperty("putOpenInterest")]
	public decimal? PutOpenInterest { get; set; }

	[JsonProperty("putBidSize")]
	public decimal? PutBidSize { get; set; }

	[JsonProperty("putAskSize")]
	public decimal? PutAskSize { get; set; }

	[JsonProperty("callBidPrice")]
	public decimal? CallBidPrice { get; set; }

	[JsonProperty("callValue")]
	public decimal? CallValue { get; set; }

	[JsonProperty("callAskPrice")]
	public decimal? CallAskPrice { get; set; }

	[JsonProperty("putBidPrice")]
	public decimal? PutBidPrice { get; set; }

	[JsonProperty("putValue")]
	public decimal? PutValue { get; set; }

	[JsonProperty("putAskPrice")]
	public decimal? PutAskPrice { get; set; }

	[JsonProperty("callBidIv")]
	public decimal? CallBidIv { get; set; }

	[JsonProperty("callMidIv")]
	public decimal? CallMidIv { get; set; }

	[JsonProperty("callAskIv")]
	public decimal? CallAskIv { get; set; }

	[JsonProperty("smvVol")]
	public decimal? SmoothedVolatility { get; set; }

	[JsonProperty("putBidIv")]
	public decimal? PutBidIv { get; set; }

	[JsonProperty("putMidIv")]
	public decimal? PutMidIv { get; set; }

	[JsonProperty("putAskIv")]
	public decimal? PutAskIv { get; set; }

	[JsonProperty("residualRate")]
	public decimal? ResidualRate { get; set; }

	[JsonProperty("delta")]
	public decimal? Delta { get; set; }

	[JsonProperty("gamma")]
	public decimal? Gamma { get; set; }

	[JsonProperty("theta")]
	public decimal? Theta { get; set; }

	[JsonProperty("vega")]
	public decimal? Vega { get; set; }

	[JsonProperty("rho")]
	public decimal? Rho { get; set; }

	[JsonProperty("phi")]
	public decimal? Phi { get; set; }

	[JsonProperty("driftlessTheta")]
	public decimal? DriftlessTheta { get; set; }

	[JsonProperty("callSmvVol")]
	public decimal? CallSmoothedVolatility { get; set; }

	[JsonProperty("putSmvVol")]
	public decimal? PutSmoothedVolatility { get; set; }

	[JsonProperty("extSmvVol")]
	public decimal? ExtendedSmoothedVolatility { get; set; }

	[JsonProperty("extCallValue")]
	public decimal? ExtendedCallValue { get; set; }

	[JsonProperty("extPutValue")]
	public decimal? ExtendedPutValue { get; set; }

	[JsonProperty("spotPrice")]
	public decimal? SpotPrice { get; set; }

	[JsonProperty("quoteDate")]
	public string QuoteDate { get; set; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; set; }

	[JsonProperty("expiryTod")]
	public string ExpirationTimeOfDay { get; set; }
}

sealed class OratsDaily
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("tradeDate")]
	public string TradeDate { get; set; }

	[JsonProperty("clsPx")]
	public decimal? Close { get; set; }

	[JsonProperty("hiPx")]
	public decimal? High { get; set; }

	[JsonProperty("loPx")]
	public decimal? Low { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("stockVolume")]
	public decimal? Volume { get; set; }

	[JsonProperty("unadjClsPx")]
	public decimal? UnadjustedClose { get; set; }

	[JsonProperty("unadjHiPx")]
	public decimal? UnadjustedHigh { get; set; }

	[JsonProperty("unadjLoPx")]
	public decimal? UnadjustedLow { get; set; }

	[JsonProperty("unadjOpen")]
	public decimal? UnadjustedOpen { get; set; }

	[JsonProperty("unadjStockVolume")]
	public decimal? UnadjustedVolume { get; set; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; set; }
}

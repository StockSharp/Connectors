namespace StockSharp.Drift.Native.Model;

sealed class DriftMarketsResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }

	[JsonProperty("markets")]
	public DriftMarket[] Markets { get; init; }
}

sealed class DriftMarket
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("marketIndex")]
	public int MarketIndex { get; init; }

	[JsonProperty("marketType")]
	public DriftMarketTypes MarketType { get; init; }

	[JsonProperty("uiStatus")]
	public string UiStatus { get; init; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; init; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }

	[JsonProperty("precision")]
	public int Precision { get; init; }

	[JsonProperty("limits")]
	public DriftMarketLimits Limits { get; init; }

	[JsonProperty("fees")]
	public DriftMarketFees Fees { get; init; }

	[JsonProperty("oraclePrice")]
	public string OraclePrice { get; init; }

	[JsonProperty("takerFee")]
	public string TakerFee { get; init; }

	[JsonProperty("makerRebate")]
	public string MakerRebate { get; init; }

	[JsonProperty("makerFee")]
	public string MakerFee { get; init; }

	[JsonProperty("userFee")]
	public string UserFee { get; init; }

	[JsonProperty("oraclePriceSlot")]
	public long? OraclePriceSlot { get; init; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; init; }

	[JsonProperty("markPriceSlot")]
	public long? MarkPriceSlot { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("baseVolume")]
	public string BaseVolume { get; init; }

	[JsonProperty("quoteVolume")]
	public string QuoteVolume { get; init; }

	[JsonProperty("deposits")]
	public string Deposits { get; init; }

	[JsonProperty("borrows")]
	public string Borrows { get; init; }

	[JsonProperty("openInterest")]
	public DriftLongShortValue OpenInterest { get; init; }

	[JsonProperty("fundingRate")]
	public DriftLongShortValue FundingRate { get; init; }

	[JsonProperty("fundingRate24h")]
	public string FundingRate24Hours { get; init; }

	[JsonProperty("fundingRateUpdateTs")]
	public long? FundingRateUpdateTimestamp { get; init; }

	[JsonProperty("priceChange24h")]
	public string PriceChange24Hours { get; init; }

	[JsonProperty("priceChange24hPercent")]
	public string PriceChange24HoursPercent { get; init; }

	[JsonProperty("priceHigh")]
	public DriftOracleFillValue PriceHigh { get; init; }

	[JsonProperty("priceLow")]
	public DriftOracleFillValue PriceLow { get; init; }
}

sealed class DriftMarketLimits
{
	[JsonProperty("leverage")]
	public DriftMinimumMaximum Leverage { get; init; }

	[JsonProperty("amount")]
	public DriftMinimumMaximum Amount { get; init; }

	[JsonProperty("withdraw")]
	public DriftMinimumMaximum Withdraw { get; init; }

	[JsonProperty("deposit")]
	public DriftMinimumMaximum Deposit { get; init; }
}

sealed class DriftMinimumMaximum
{
	[JsonProperty("min")]
	public decimal? Minimum { get; init; }

	[JsonProperty("max")]
	public decimal? Maximum { get; init; }
}

sealed class DriftMarketFees
{
	[JsonProperty("maker")]
	public decimal? Maker { get; init; }

	[JsonProperty("taker")]
	public decimal? Taker { get; init; }
}

sealed class DriftLongShortValue
{
	[JsonProperty("long")]
	public string Long { get; init; }

	[JsonProperty("short")]
	public string Short { get; init; }
}

sealed class DriftOracleFillValue
{
	[JsonProperty("oracle")]
	public string Oracle { get; init; }

	[JsonProperty("fill")]
	public string Fill { get; init; }
}

sealed class DriftCandlesResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }

	[JsonProperty("records")]
	public DriftCandle[] Records { get; init; }
}

sealed class DriftCandle
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("resolution")]
	public string Resolution { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("fillOpen")]
	public decimal FillOpen { get; init; }

	[JsonProperty("fillHigh")]
	public decimal FillHigh { get; init; }

	[JsonProperty("fillClose")]
	public decimal FillClose { get; init; }

	[JsonProperty("fillLow")]
	public decimal FillLow { get; init; }

	[JsonProperty("oracleOpen")]
	public decimal OracleOpen { get; init; }

	[JsonProperty("oracleHigh")]
	public decimal OracleHigh { get; init; }

	[JsonProperty("oracleClose")]
	public decimal OracleClose { get; init; }

	[JsonProperty("oracleLow")]
	public decimal OracleLow { get; init; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; init; }

	[JsonProperty("baseVolume")]
	public decimal BaseVolume { get; init; }
}

sealed class DriftTrade
{
	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("txSig")]
	public string TransactionSignature { get; init; }

	[JsonProperty("txSigIndex")]
	public int TransactionIndex { get; init; }

	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("baseAssetAmountFilled")]
	public string BaseAssetAmountFilled { get; init; }

	[JsonProperty("quoteAssetAmountFilled")]
	public string QuoteAssetAmountFilled { get; init; }

	[JsonProperty("oraclePrice")]
	public string OraclePrice { get; init; }

	[JsonProperty("marketIndex")]
	public int MarketIndex { get; init; }

	[JsonProperty("marketType")]
	public DriftMarketTypes MarketType { get; init; }

	[JsonProperty("marketFilter")]
	public string MarketFilter { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("fillRecordId")]
	public string FillRecordId { get; init; }

	[JsonProperty("takerOrderDirection")]
	public string TakerOrderDirection { get; init; }

	[JsonProperty("makerOrderDirection")]
	public string MakerOrderDirection { get; init; }

	[JsonProperty("takerFee")]
	public string TakerFee { get; init; }

	[JsonProperty("makerFee")]
	public string MakerFee { get; init; }

	[JsonProperty("makerRebate")]
	public string MakerRebate { get; init; }

	[JsonProperty("taker")]
	public string Taker { get; init; }

	[JsonProperty("maker")]
	public string Maker { get; init; }

	[JsonProperty("user")]
	public string User { get; init; }

	[JsonProperty("takerOrderId")]
	public string TakerOrderId { get; init; }

	[JsonProperty("makerOrderId")]
	public string MakerOrderId { get; init; }

	[JsonProperty("action")]
	public string Action { get; init; }

	[JsonProperty("actionExplanation")]
	public string ActionExplanation { get; init; }
}

sealed class DriftDlobBook
{
	[JsonProperty("bids")]
	public DriftDlobLevel[] Bids { get; init; }

	[JsonProperty("asks")]
	public DriftDlobLevel[] Asks { get; init; }

	[JsonProperty("marketName")]
	public string MarketName { get; init; }

	[JsonProperty("marketType")]
	public DriftMarketTypes MarketType { get; init; }

	[JsonProperty("marketIndex")]
	public int MarketIndex { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; init; }

	[JsonProperty("bestBidPrice")]
	public string BestBidPrice { get; init; }

	[JsonProperty("bestAskPrice")]
	public string BestAskPrice { get; init; }

	[JsonProperty("oracle")]
	public string Oracle { get; init; }

	[JsonProperty("oracleData")]
	public DriftOracleData OracleData { get; init; }

	[JsonProperty("marketSlot")]
	public long MarketSlot { get; init; }
}

sealed class DriftDlobLevel
{
	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("sources")]
	public DriftDlobSources Sources { get; init; }
}

sealed class DriftDlobSources
{
	[JsonProperty("dlob")]
	public string Dlob { get; init; }

	[JsonProperty("vamm")]
	public string VirtualAmm { get; init; }

	[JsonProperty("indicative")]
	public string Indicative { get; init; }
}

sealed class DriftOracleData
{
	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("slot")]
	public string Slot { get; init; }

	[JsonProperty("confidence")]
	public string Confidence { get; init; }

	[JsonProperty("hasSufficientNumberOfDataPoints")]
	public bool IsSufficient { get; init; }

	[JsonProperty("twap")]
	public string Twap { get; init; }
}

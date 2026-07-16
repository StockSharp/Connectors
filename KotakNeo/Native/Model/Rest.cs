namespace StockSharp.KotakNeo.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoLoginRequest
{
	[JsonProperty("mobileNumber")]
	public string MobileNumber { get; set; }

	[JsonProperty("ucc")]
	public string UserCode { get; set; }

	[JsonProperty("totp")]
	public string Totp { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoMpinRequest
{
	[JsonProperty("mpin")]
	public string Mpin { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoLoginResponse
{
	[JsonProperty("data")]
	public KotakNeoSession Data { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public KotakNeoApiError[] Errors { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoSession
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("sid")]
	public string Sid { get; set; }

	[JsonProperty("rid")]
	public string Rid { get; set; }

	[JsonProperty("hsServerId")]
	public string ServerId { get; set; }

	[JsonProperty("dataCenter")]
	public string DataCenter { get; set; }

	[JsonProperty("baseUrl")]
	public string BaseUrl { get; set; }

	[JsonProperty("ucc")]
	public string UserCode { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoApiError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoResponse<T>
{
	[JsonProperty("stat")]
	public string Status { get; set; }

	[JsonProperty("stCode")]
	public int StatusCode { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errMsg")]
	public string ErrorMessage { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("nOrdNo")]
	public string OrderId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoScripFiles
{
	[JsonProperty("filesPaths")]
	public string[] FilePaths { get; set; }

	[JsonProperty("baseFolder")]
	public string BaseFolder { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoScripFilesResponse
{
	[JsonProperty("data")]
	public KotakNeoScripFiles Data { get; set; }

	[JsonProperty("filesPaths")]
	public string[] FilePaths { get; set; }

	[JsonProperty("baseFolder")]
	public string BaseFolder { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class KotakNeoNoRequest
{
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoNoData
{
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoLimitsRequest
{
	[JsonProperty("seg")]
	public string Segment { get; set; } = "ALL";

	[JsonProperty("exch")]
	public string Exchange { get; set; } = "ALL";

	[JsonProperty("prod")]
	public string Product { get; set; } = "ALL";
}

sealed class KotakNeoInstrument
{
	public string Token { get; set; }
	public string Group { get; set; }
	public string ExchangeSegment { get; set; }
	public string InstrumentType { get; set; }
	public string Symbol { get; set; }
	public string TradingSymbol { get; set; }
	public string OptionType { get; set; }
	public string Isin { get; set; }
	public string AssetCode { get; set; }
	public decimal? TickSize { get; set; }
	public decimal? LotSize { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public decimal? Multiplier { get; set; }
	public int Precision { get; set; }
	public decimal? StrikePrice { get; set; }
	public string Exchange { get; set; }
	public string InstrumentName { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoOrderRequest
{
	[JsonProperty("am")]
	public string AfterMarket { get; set; }

	[JsonProperty("dq")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("es")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("mp")]
	public decimal MarketProtection { get; set; }

	[JsonProperty("pc")]
	public string Product { get; set; }

	[JsonProperty("pf")]
	public string PortfolioFlag { get; set; } = "N";

	[JsonProperty("pr")]
	public decimal Price { get; set; }

	[JsonProperty("pt")]
	public string OrderType { get; set; }

	[JsonProperty("qt")]
	public long Quantity { get; set; }

	[JsonProperty("rt")]
	public string Validity { get; set; }

	[JsonProperty("tp")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("ts")]
	public string TradingSymbol { get; set; }

	[JsonProperty("tt")]
	public string TransactionType { get; set; }

	[JsonProperty("ig", NullValueHandling = NullValueHandling.Ignore)]
	public string Tag { get; set; }

	[JsonProperty("tk", NullValueHandling = NullValueHandling.Ignore)]
	public string Token { get; set; }

	[JsonProperty("sot", NullValueHandling = NullValueHandling.Ignore)]
	public string SquareOffType { get; set; }

	[JsonProperty("slt", NullValueHandling = NullValueHandling.Ignore)]
	public string StopLossType { get; set; }

	[JsonProperty("slv", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopLossValue { get; set; }

	[JsonProperty("sov", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? SquareOffValue { get; set; }

	[JsonProperty("lat", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? LastTradedPrice { get; set; }

	[JsonProperty("tlt", NullValueHandling = NullValueHandling.Ignore)]
	public string TrailingStopLoss { get; set; }

	[JsonProperty("tsv", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TrailingStopLossValue { get; set; }

	[JsonProperty("os")]
	public string Source { get; set; } = "NEOTRADEAPI";
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoModifyOrderRequest
{
	[JsonProperty("tk")]
	public string Token { get; set; }

	[JsonProperty("mp")]
	public decimal MarketProtection { get; set; }

	[JsonProperty("pc")]
	public string Product { get; set; }

	[JsonProperty("dd")]
	public string DateDays { get; set; } = "NA";

	[JsonProperty("dq")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("vd")]
	public string Validity { get; set; }

	[JsonProperty("ts")]
	public string TradingSymbol { get; set; }

	[JsonProperty("tt")]
	public string TransactionType { get; set; }

	[JsonProperty("pr")]
	public decimal Price { get; set; }

	[JsonProperty("pt")]
	public string OrderType { get; set; }

	[JsonProperty("fq")]
	public long FilledQuantity { get; set; }

	[JsonProperty("am")]
	public string AfterMarket { get; set; }

	[JsonProperty("tp")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("qt")]
	public long Quantity { get; set; }

	[JsonProperty("no")]
	public string OrderId { get; set; }

	[JsonProperty("es")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("os")]
	public string Source { get; set; } = "NEOTRADEAPI";
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoCancelOrderRequest
{
	[JsonProperty("on")]
	public string OrderId { get; set; }

	[JsonProperty("am")]
	public string AfterMarket { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class KotakNeoOrder
{
	[JsonProperty("nOrdNo")]
	public string OrderId { get; set; }

	[JsonProperty("exOrdId")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("exSeg")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("tok")]
	public string Token { get; set; }

	[JsonProperty("sym")]
	public string Symbol { get; set; }

	[JsonProperty("trdSym")]
	public string TradingSymbol { get; set; }

	[JsonProperty("prod")]
	public string Product { get; set; }

	[JsonProperty("prcTp")]
	public string PriceType { get; set; }

	[JsonProperty("trnsTp")]
	public string TransactionType { get; set; }

	[JsonProperty("vldt")]
	public string Validity { get; set; }

	[JsonProperty("ordSt")]
	public string OrderStatus { get; set; }

	[JsonProperty("stat")]
	public string Status { get; set; }

	[JsonProperty("prc")]
	public decimal? Price { get; set; }

	[JsonProperty("trgPrc")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("avgPrc")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("qty")]
	public decimal? Quantity { get; set; }

	[JsonProperty("fldQty")]
	public decimal? FilledQuantity { get; set; }

	[JsonProperty("unFldSz")]
	public decimal? UnfilledQuantity { get; set; }

	[JsonProperty("dscQty")]
	public decimal? DisclosedQuantity { get; set; }

	[JsonProperty("cnlQty")]
	public decimal? CancelledQuantity { get; set; }

	[JsonProperty("ordDtTm")]
	public string OrderTime { get; set; }

	[JsonProperty("exCfmTm")]
	public string ExchangeTime { get; set; }

	[JsonProperty("hsUpTm")]
	public string UpdateTime { get; set; }

	[JsonProperty("rejRsn")]
	public string RejectReason { get; set; }

	[JsonProperty("GuiOrdId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("it")]
	public string InstrumentType { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoTrade
{
	[JsonProperty("nOrdNo")]
	public string OrderId { get; set; }

	[JsonProperty("exOrdId")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("flId")]
	public string TradeId { get; set; }

	[JsonProperty("exSeg")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("tok")]
	public string Token { get; set; }

	[JsonProperty("sym")]
	public string Symbol { get; set; }

	[JsonProperty("trdSym")]
	public string TradingSymbol { get; set; }

	[JsonProperty("trnsTp")]
	public string TransactionType { get; set; }

	[JsonProperty("avgPrc")]
	public decimal? Price { get; set; }

	[JsonProperty("fldQty")]
	public decimal? Quantity { get; set; }

	[JsonProperty("flTm")]
	public string FillTime { get; set; }

	[JsonProperty("flDt")]
	public string FillDate { get; set; }

	[JsonProperty("exTm")]
	public string ExchangeTime { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoPosition
{
	[JsonProperty("exSeg")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("tok")]
	public string Token { get; set; }

	[JsonProperty("trdSym")]
	public string TradingSymbol { get; set; }

	[JsonProperty("prod")]
	public string Product { get; set; }

	[JsonProperty("netQty")]
	public decimal? NetQuantity { get; set; }

	[JsonProperty("cfBuyQty")]
	public decimal? CarryForwardBuyQuantity { get; set; }

	[JsonProperty("flBuyQty")]
	public decimal? FilledBuyQuantity { get; set; }

	[JsonProperty("cfSellQty")]
	public decimal? CarryForwardSellQuantity { get; set; }

	[JsonProperty("flSellQty")]
	public decimal? FilledSellQuantity { get; set; }

	[JsonProperty("buyAmt")]
	public decimal? BuyAmount { get; set; }

	[JsonProperty("cfBuyAmt")]
	public decimal? CarryForwardBuyAmount { get; set; }

	[JsonProperty("sellAmt")]
	public decimal? SellAmount { get; set; }

	[JsonProperty("cfSellAmt")]
	public decimal? CarryForwardSellAmount { get; set; }

	[JsonProperty("avgPrc")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("rpnl")]
	public decimal? RealizedProfit { get; set; }

	[JsonProperty("urmtom")]
	public decimal? UnrealizedProfit { get; set; }

	[JsonProperty("ltp")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("lotSz")]
	public decimal? LotSize { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoHolding
{
	[JsonProperty("displaySymbol")]
	public string DisplaySymbol { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("exchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("exchangeIdentifier")]
	public string ExchangeIdentifier { get; set; }

	[JsonProperty("instrumentToken")]
	public string InstrumentToken { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("closingPrice")]
	public decimal? ClosingPrice { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("sellableQuantity")]
	public decimal? SellableQuantity { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoLimits
{
	[JsonProperty("Net")]
	public decimal? Net { get; set; }

	[JsonProperty("NotionalCash")]
	public decimal? NotionalCash { get; set; }

	[JsonProperty("CollateralValue")]
	public decimal? CollateralValue { get; set; }

	[JsonProperty("MarginUsed")]
	public decimal? MarginUsed { get; set; }

	[JsonProperty("AmountUtilizedPrsnt")]
	public decimal? AmountUtilized { get; set; }
}

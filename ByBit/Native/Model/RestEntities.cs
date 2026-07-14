namespace StockSharp.ByBit.Native.Model;

class RestResponseResult
{
	[JsonProperty("list")]
	public JArray List { get; set; }

	[JsonProperty("nextPageCursor")]
	public string NextPageCursor { get; set; }

	[JsonProperty("category")]
	public string Category { get; set; }
}

class RestResponse
{
	[JsonProperty("retCode")]
	public int RetCode { get; set; }

	[JsonProperty("retMsg")]
	public string RetMsg { get; set; }

	[JsonProperty("retExtInfo")]
	public JToken ExtInfo { get; set; }

	[JsonProperty("result")]
	public JToken Result { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}

class Instrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("contractType")]
	public string ContractType { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("baseCoin")]
	public string BaseCoin { get; set; }

	[JsonProperty("quoteCoin")]
	public string QuoteCoin { get; set; }

	[JsonProperty("launchTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? LaunchTime { get; set; }

	[JsonProperty("deliveryTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? DeliveryTime { get; set; }

	[JsonProperty("deliveryFeeRate")]
	public double? DeliveryFeeRate { get; set; }

	[JsonProperty("priceScale")]
	public int? PriceScale { get; set; }

	[JsonProperty("leverageFilter")]
	public LeverageFilter LeverageFilter { get; set; }

	[JsonProperty("priceFilter")]
	public PriceFilter PriceFilter { get; set; }

	[JsonProperty("lotSizeFilter")]
	public LotSizeFilter LotSizeFilter { get; set; }

	[JsonProperty("unifiedMarginTrade")]
	public bool UnifiedMarginTrade { get; set; }

	[JsonProperty("fundingInterval")]
	public int FundingInterval { get; set; }

	[JsonProperty("settleCoin")]
	public string SettleCoin { get; set; }

	[JsonProperty("copyTrading")]
	public string CopyTrading { get; set; }

	[JsonProperty("upperFundingRate")]
	public double? UpperFundingRate { get; set; }

	[JsonProperty("lowerFundingRate")]
	public double? LowerFundingRate { get; set; }

	[JsonProperty("optionsType")]
	public string OptionsType { get; set; }
}

class LeverageFilter
{
	[JsonProperty("minLeverage")]
	public double? MinLeverage { get; set; }

	[JsonProperty("maxLeverage")]
	public double? MaxLeverage { get; set; }

	[JsonProperty("leverageStep")]
	public double? LeverageStep { get; set; }
}

class PriceFilter
{
	[JsonProperty("minPrice")]
	public double? MinPrice { get; set; }

	[JsonProperty("maxPrice")]
	public double? MaxPrice { get; set; }

	[JsonProperty("tickSize")]
	public double? TickSize { get; set; }
}

class LotSizeFilter
{
	[JsonProperty("basePrecision")]
	public double? BasePrecision { get; set; }

	[JsonProperty("quotePrecision")]
	public double? QuotePrecision { get; set; }

	[JsonProperty("maxOrderQty")]
	public double? MaxOrderQty { get; set; }

	[JsonProperty("maxMktOrderQty")]
	public double? MaxMktOrderQty { get; set; }

	[JsonProperty("minOrderQty")]
	public double? MinOrderQty { get; set; }

	[JsonProperty("qtyStep")]
	public double? QtyStep { get; set; }

	[JsonProperty("postOnlyMaxOrderQty")]
	public double? PostOnlyMaxOrderQty { get; set; }

	[JsonProperty("minNotionalValue")]
	public double? MinNotionalValue { get; set; }
}

class Trade
{
	[JsonProperty("execId")]
	public string TradeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("size")]
	public double Volume { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class Kline
{
	public long OpenTime { get; set; }
	public double Open { get; set; }
	public double High { get; set; }
	public double Low { get; set; }
	public double Close { get; set; }
	public double Volume { get; set; }
	public double Turnover { get; set; }
}

class Order
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderLinkId")]
	public string OrderLinkId { get; set; }

	[JsonProperty("blockTradeId")]
	public string BlockTradeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("isLeverage")]
	public int? IsLeverage { get; set; }

	[JsonProperty("positionIdx")]
	public int? PositionIdx { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("cancelType")]
	public string CancelType { get; set; }

	[JsonProperty("rejectReason")]
	public string RejectReason { get; set; }

	[JsonProperty("avgPrice")]
	public double? AvgPrice { get; set; }

	[JsonProperty("leavesQty")]
	public double? LeavesQty { get; set; }

	[JsonProperty("leavesValue")]
	public double? LeavesValue { get; set; }

	[JsonProperty("cumExecQty")]
	public double? CumExecQty { get; set; }

	[JsonProperty("cumExecValue")]
	public double? CumExecValue { get; set; }

	[JsonProperty("cumExecFee")]
	public double? CumExecFee { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("stopOrderType")]
	public string StopOrderType { get; set; }

	[JsonProperty("orderIv")]
	public double? OrderIv { get; set; }

	[JsonProperty("triggerPrice")]
	public double? TriggerPrice { get; set; }

	[JsonProperty("takeProfit")]
	public double? TakeProfit { get; set; }

	[JsonProperty("stopLoss")]
	public double? StopLoss { get; set; }

	[JsonProperty("tpTriggerBy")]
	public string TpTriggerBy { get; set; }

	[JsonProperty("slTriggerBy")]
	public string SlTriggerBy { get; set; }

	[JsonProperty("triggerDirection")]
	public int? TriggerDirection { get; set; }

	[JsonProperty("triggerBy")]
	public string TriggerBy { get; set; }

	[JsonProperty("lastPriceOnCreated")]
	public double? LastPriceOnCreated { get; set; }

	[JsonProperty("reduceOnly")]
	public bool? ReduceOnly { get; set; }

	[JsonProperty("closeOnTrigger")]
	public bool? CloseOnTrigger { get; set; }

	[JsonProperty("smpType")]
	public string SmpType { get; set; }

	[JsonProperty("smpGroup")]
	public int? SmpGroup { get; set; }

	[JsonProperty("smpOrderId")]
	public string SmpOrderId { get; set; }

	[JsonProperty("tpslMode")]
	public string TpslMode { get; set; }

	[JsonProperty("tpLimitPrice")]
	public double? TpLimitPrice { get; set; }

	[JsonProperty("slLimitPrice")]
	public double? SlLimitPrice { get; set; }

	[JsonProperty("placeType")]
	public string PlaceType { get; set; }

	[JsonProperty("createdTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedTime { get; set; }

	[JsonProperty("updatedTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdatedTime { get; set; }
}

class Position
{
	[JsonProperty("positionIdx")]
	public int PositionIdx { get; set; }

	[JsonProperty("riskId")]
	public int RiskId { get; set; }

	[JsonProperty("riskLimitValue")]
	public double? RiskLimitValue { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("avgPrice")]
	public double? AvgPrice { get; set; }

	[JsonProperty("positionValue")]
	public double? PositionValue { get; set; }

	[JsonProperty("tradeMode")]
	public int TradeMode { get; set; }

	[JsonProperty("positionStatus")]
	public string PositionStatus { get; set; }

	[JsonProperty("autoAddMargin")]
	public int AutoAddMargin { get; set; }

	[JsonProperty("adlRankIndicator")]
	public int AdlRankIndicator { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }

	[JsonProperty("positionBalance")]
	public double? PositionBalance { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("liqPrice")]
	public double? LiqPrice { get; set; }

	[JsonProperty("bustPrice")]
	public double? BustPrice { get; set; }

	[JsonProperty("positionMM")]
	public double? PositionMM { get; set; }

	[JsonProperty("positionIM")]
	public double? PositionIM { get; set; }

	[JsonProperty("tpslMode")]
	public string TpslMode { get; set; }

	[JsonProperty("takeProfit")]
	public double? TakeProfit { get; set; }

	[JsonProperty("stopLoss")]
	public double? StopLoss { get; set; }

	[JsonProperty("trailingStop")]
	public double? TrailingStop { get; set; }

	[JsonProperty("unrealisedPnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("curRealisedPnl")]
	public double? CurRealisedPnl { get; set; }

	[JsonProperty("cumRealisedPnl")]
	public double? CumRealisedPnl { get; set; }

	[JsonProperty("seq")]
	public long Seq { get; set; }

	[JsonProperty("isReduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("mmrSysUpdateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? MmrSysUpdateTime { get; set; }

	[JsonProperty("leverageSysUpdatedTime")]
	public string LeverageSysUpdatedTime { get; set; }

	[JsonProperty("sessionAvgPrice")]
	public double? SessionAvgPrice { get; set; }

	[JsonProperty("createdTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedTime { get; set; }

	[JsonProperty("updatedTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdatedTime { get; set; }
}

class WalletCoin
{
	[JsonProperty("availableToBorrow")]
	public double? AvailableToBorrow { get; set; }

	[JsonProperty("bonus")]
	public double? Bonus { get; set; }

	[JsonProperty("accruedInterest")]
	public double? AccruedInterest { get; set; }

	[JsonProperty("availableToWithdraw")]
	public double? AvailableToWithdraw { get; set; }

	[JsonProperty("totalOrderIM")]
	public double? TotalOrderIM { get; set; }

	[JsonProperty("equity")]
	public double? Equity { get; set; }

	[JsonProperty("totalPositionMM")]
	public double? TotalPositionMM { get; set; }

	[JsonProperty("usdValue")]
	public double? UsdValue { get; set; }

	[JsonProperty("spotHedgingQty")]
	public double? SpotHedgingQty { get; set; }

	[JsonProperty("unrealisedPnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("collateralSwitch")]
	public bool CollateralSwitch { get; set; }

	[JsonProperty("borrowAmount")]
	public double? BorrowAmount { get; set; }

	[JsonProperty("totalPositionIM")]
	public double? TotalPositionIM { get; set; }

	[JsonProperty("walletBalance")]
	public double? WalletBalance { get; set; }

	[JsonProperty("cumRealisedPnl")]
	public double? CumRealisedPnl { get; set; }

	[JsonProperty("locked")]
	public double? Locked { get; set; }

	[JsonProperty("marginCollateral")]
	public bool MarginCollateral { get; set; }

	[JsonProperty("coin")]
	public string Coin { get; set; }
}

class Wallet
{
	[JsonProperty("totalEquity")]
	public double? TotalEquity { get; set; }

	[JsonProperty("accountIMRate")]
	public double? AccountIMRate { get; set; }

	[JsonProperty("totalMarginBalance")]
	public double? TotalMarginBalance { get; set; }

	[JsonProperty("totalInitialMargin")]
	public double? TotalInitialMargin { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("totalAvailableBalance")]
	public double? TotalAvailableBalance { get; set; }

	[JsonProperty("accountMMRate")]
	public double? AccountMMRate { get; set; }

	[JsonProperty("totalPerpUPL")]
	public double? TotalPerpUPL { get; set; }

	[JsonProperty("totalWalletBalance")]
	public double? TotalWalletBalance { get; set; }

	[JsonProperty("accountLTV")]
	public double? AccountLTV { get; set; }

	[JsonProperty("totalMaintenanceMargin")]
	public double? TotalMaintenanceMargin { get; set; }

	[JsonProperty("coin")]
	public WalletCoin[] Coins { get; set; }
}

class OpenInterest
{
	[JsonProperty("openInterest")]
	public double Value { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}

class Volatility
{
	[JsonProperty("value")]
	public double Value { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}
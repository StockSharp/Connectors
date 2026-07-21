namespace StockSharp.GainsNetwork.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsTradingVariables
{
	[JsonProperty("lastRefreshed")]
	public string LastRefreshed { get; set; }

	[JsonProperty("refreshId")]
	public long RefreshId { get; set; }

	[JsonProperty("tradingState")]
	public GainsTradingStates TradingState { get; set; }

	[JsonProperty("pairs")]
	public GainsPair[] Pairs { get; set; }

	[JsonProperty("groups")]
	public GainsGroup[] Groups { get; set; }

	[JsonProperty("fees")]
	public GainsFee[] Fees { get; set; }

	[JsonProperty("pairInfos")]
	public GainsPairInfos PairInfos { get; set; }

	[JsonProperty("collaterals")]
	public GainsCollateral[] Collaterals { get; set; }

	[JsonProperty("currentBlock")]
	public long CurrentBlock { get; set; }

	[JsonProperty("currentL1Block")]
	public long CurrentLayer1Block { get; set; }

	[JsonProperty("isForexOpen")]
	public bool IsForexOpen { get; set; }

	[JsonProperty("isStocksOpen")]
	public bool IsStocksOpen { get; set; }

	[JsonProperty("isIndicesOpen")]
	public bool IsIndicesOpen { get; set; }

	[JsonProperty("isCommoditiesOpen")]
	public bool IsCommoditiesOpen { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsPair
{
	[JsonProperty("from")]
	public string From { get; set; }

	[JsonProperty("to")]
	public string To { get; set; }

	[JsonProperty("spreadP")]
	public string SpreadPercentage { get; set; }

	[JsonProperty("groupIndex")]
	public string GroupIndex { get; set; }

	[JsonProperty("feeIndex")]
	public string FeeIndex { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsGroup
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("minLeverage")]
	public string MinimumLeverage { get; set; }

	[JsonProperty("maxLeverage")]
	public string MaximumLeverage { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsFee
{
	[JsonProperty("totalPositionSizeFeeP")]
	public string TotalPositionSizeFeePercentage { get; set; }

	[JsonProperty("totalLiqCollateralFeeP")]
	public string TotalLiquidationFeePercentage { get; set; }

	[JsonProperty("oraclePositionSizeFeeP")]
	public string OracleFeePercentage { get; set; }

	[JsonProperty("minPositionSizeUsd")]
	public string MinimumPositionSizeUsd { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsPairInfos
{
	[JsonProperty("maxLeverages")]
	public string[] MaximumLeverages { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsCollateral
{
	[JsonProperty("collateralIndex")]
	public int CollateralIndex { get; set; }

	[JsonProperty("collateral")]
	public string Address { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("isActive")]
	public bool IsActive { get; set; }

	[JsonProperty("prices")]
	public GainsCollateralPrices Prices { get; set; }

	[JsonProperty("collateralConfig")]
	public GainsCollateralConfig Config { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsCollateralPrices
{
	[JsonProperty("collateralPriceUsd")]
	public decimal CollateralPriceUsd { get; set; }

	[JsonProperty("gnsPriceCollateral")]
	public decimal GnsPriceCollateral { get; set; }

	[JsonProperty("gnsPriceUsd")]
	public decimal GnsPriceUsd { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsCollateralConfig
{
	[JsonProperty("precision")]
	public string Precision { get; set; }

	[JsonProperty("precisionDelta")]
	public string PrecisionDelta { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsCharts
{
	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("opens")]
	public decimal?[] Opens { get; set; }

	[JsonProperty("highs")]
	public decimal?[] Highs { get; set; }

	[JsonProperty("lows")]
	public decimal?[] Lows { get; set; }

	[JsonProperty("closes")]
	public decimal?[] Closes { get; set; }

	[JsonProperty("indexPrices")]
	public decimal?[] IndexPrices { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsTradeContainer
{
	[JsonProperty("trade")]
	public GainsTrade Trade { get; set; }

	[JsonProperty("tradeInfo")]
	public GainsTradeInfo TradeInfo { get; set; }

	[JsonProperty("tradeFeesData")]
	public GainsTradeFeesData TradeFeesData { get; set; }

	[JsonProperty("uiRealizedPnlData")]
	public GainsRealizedPnlData RealizedPnlData { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsTrade
{
	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("index")]
	public string Index { get; set; }

	[JsonProperty("pairIndex")]
	public string PairIndex { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("long")]
	public bool IsLong { get; set; }

	[JsonProperty("isOpen")]
	public bool IsOpen { get; set; }

	[JsonProperty("collateralIndex")]
	public string CollateralIndex { get; set; }

	[JsonProperty("tradeType")]
	public GainsTradeTypes TradeType { get; set; }

	[JsonProperty("collateralAmount")]
	public string CollateralAmount { get; set; }

	[JsonProperty("openPrice")]
	public string OpenPrice { get; set; }

	[JsonProperty("tp")]
	public string TakeProfit { get; set; }

	[JsonProperty("sl")]
	public string StopLoss { get; set; }

	[JsonProperty("isCounterTrade")]
	public bool IsCounterTrade { get; set; }

	[JsonProperty("positionSizeToken")]
	public string PositionSizeToken { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsTradeInfo
{
	[JsonProperty("createdBlock")]
	public string CreatedBlock { get; set; }

	[JsonProperty("tpLastUpdatedBlock")]
	public string TakeProfitUpdatedBlock { get; set; }

	[JsonProperty("slLastUpdatedBlock")]
	public string StopLossUpdatedBlock { get; set; }

	[JsonProperty("maxSlippageP")]
	public string MaximumSlippagePercentage { get; set; }

	[JsonProperty("lastOiUpdateTs")]
	public long LastOpenInterestUpdateTime { get; set; }

	[JsonProperty("collateralPriceUsd")]
	public string CollateralPriceUsd { get; set; }

	[JsonProperty("contractsVersion")]
	public string ContractsVersion { get; set; }

	[JsonProperty("lastPosIncreaseBlock")]
	public string LastPositionIncreaseBlock { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsTradeFeesData
{
	[JsonProperty("realizedTradingFeesCollateral")]
	public string RealizedTradingFeesCollateral { get; set; }

	[JsonProperty("realizedPnlCollateral")]
	public string RealizedPnlCollateral { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsRealizedPnlData
{
	[JsonProperty("realizedTradingFeesCollateral")]
	public string RealizedTradingFeesCollateral { get; set; }

	[JsonProperty("realizedPnlPartialCloseCollateral")]
	public string RealizedPartialClosePnlCollateral { get; set; }

	[JsonProperty("pnlWithdrawnCollateral")]
	public string WithdrawnPnlCollateral { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsUserTradingVariables
{
	[JsonProperty("pendingMarketOrdersIds")]
	public long[] PendingMarketOrderIds { get; set; }

	[JsonProperty("collaterals")]
	public GainsUserCollateral[] Collaterals { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsUserCollateral
{
	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("allowance")]
	public string Allowance { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsHistoryResponse
{
	[JsonProperty("data")]
	public GainsHistoryItem[] Items { get; set; }

	[JsonProperty("pagination")]
	public GainsHistoryPagination Pagination { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsHistoryPagination
{
	[JsonProperty("hasMore")]
	public bool IsMoreAvailable { get; set; }

	[JsonProperty("nextCursor")]
	public long? NextCursor { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsHistoryItem
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("block")]
	public long Block { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("collateralPriceUsd")]
	public decimal? CollateralPriceUsd { get; set; }

	[JsonProperty("long")]
	public bool IsLong { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("leverage")]
	public decimal Leverage { get; set; }

	[JsonProperty("pnl")]
	public decimal? Pnl { get; set; }

	[JsonProperty("pnl_net")]
	public decimal? NetPnl { get; set; }

	[JsonProperty("tx")]
	public string TransactionHash { get; set; }

	[JsonProperty("tradeId")]
	public long? TradeId { get; set; }

	[JsonProperty("collateralIndex")]
	public int? CollateralIndex { get; set; }

	[JsonProperty("tradeIndex")]
	public int? TradeIndex { get; set; }

	[JsonProperty("collateralDelta")]
	public decimal? CollateralDelta { get; set; }

	[JsonProperty("leverageDelta")]
	public decimal? LeverageDelta { get; set; }

	[JsonProperty("marketPrice")]
	public decimal? MarketPrice { get; set; }

	[JsonProperty("isCounterTrade")]
	public bool IsCounterTrade { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GainsErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

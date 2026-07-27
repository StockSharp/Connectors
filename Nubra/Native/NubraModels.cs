namespace StockSharp.Nubra.Native;

sealed class NubraLoginResult
{
	public string SessionToken { get; init; }
	public string UserId { get; init; }
}

sealed class NubraUserInfo
{
	public string ClientCode { get; init; }
	public Uri UserWebSocketAddress { get; init; }
}

sealed class NubraInstrumentEnvelope
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("is_trading_on")]
	public bool IsTradingOn { get; set; }

	[JsonProperty("refdata")]
	public NubraInstrument[] Instruments { get; set; }
}

sealed class NubraInstrument
{
	[JsonProperty("ref_id")]
	public long RefId { get; set; }

	[JsonProperty("strike_price")]
	public long StrikePrice { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("token")]
	public long Token { get; set; }

	[JsonProperty("stock_name")]
	public string StockName { get; set; }

	[JsonProperty("series")]
	public string Series { get; set; }

	[JsonProperty("zanskar_name")]
	public string NubraName { get; set; }

	[JsonProperty("lot_size")]
	public decimal LotSize { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("expiry")]
	public long Expiry { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("derivative_type")]
	public string DerivativeType { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("asset_type")]
	public string AssetType { get; set; }

	[JsonProperty("tick_size")]
	public long TickSize { get; set; }

	[JsonProperty("underlying_prev_close")]
	public long PreviousClose { get; set; }
}

sealed class NubraDepthLevel
{
	[JsonProperty("p")]
	public long Price { get; set; }

	[JsonProperty("q")]
	public long Quantity { get; set; }

	[JsonProperty("o")]
	public long Orders { get; set; }
}

sealed class NubraMarketUpdate
{
	public long RefId { get; init; }
	public long Timestamp { get; init; }
	public long LastPrice { get; init; }
	public long LastQuantity { get; init; }
	public long Volume { get; init; }
	public NubraDepthLevel[] Bids { get; init; } = [];
	public NubraDepthLevel[] Asks { get; init; } = [];
}

sealed class NubraCandle
{
	public long Timestamp { get; init; }
	public long Open { get; init; }
	public long High { get; init; }
	public long Low { get; init; }
	public long Close { get; init; }
	public long Volume { get; init; }
}

sealed class NubraOrder
{
	[JsonProperty("intentOrderId")]
	public long IntentOrderId { get; set; }

	[JsonProperty("refId")]
	public long RefId { get; set; }

	[JsonProperty("refData")]
	public NubraOrderRefData RefData { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderQty")]
	public decimal OrderQuantity { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("filledQty")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("orderPrice")]
	public long OrderPrice { get; set; }

	[JsonProperty("entryPrice")]
	public long EntryPrice { get; set; }

	[JsonProperty("filledPrice")]
	public long FilledPrice { get; set; }

	[JsonProperty("ltp")]
	public long LastPrice { get; set; }

	[JsonProperty("deliveryType")]
	public string DeliveryType { get; set; }

	[JsonProperty("priceType")]
	public string PriceType { get; set; }

	[JsonProperty("validityType")]
	public string ValidityType { get; set; }

	[JsonProperty("executionMode")]
	public string ExecutionMode { get; set; }

	[JsonProperty("intentOrderType")]
	public string IntentOrderType { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }

	[JsonProperty("rejectionReason")]
	public string RejectionReason { get; set; }

	[JsonProperty("timestamps")]
	public NubraOrderTimestamps Timestamps { get; set; }

	public decimal EffectiveQuantity()
		=> OrderQuantity != 0 ? OrderQuantity : Quantity;

	public long EffectiveOrderPrice()
		=> OrderPrice != 0 ? OrderPrice : EntryPrice;
}

sealed class NubraOrderRefData
{
	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("derivativeType")]
	public string DerivativeType { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("lotSize")]
	public decimal LotSize { get; set; }

	[JsonProperty("tickSize")]
	public long TickSize { get; set; }
}

sealed class NubraOrderTimestamps
{
	[JsonProperty("intentCreatedAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("sentToColoAt")]
	public string SentAt { get; set; }

	[JsonProperty("filledAt")]
	public string FilledAt { get; set; }

	[JsonProperty("lastUpdatedAt")]
	public string UpdatedAt { get; set; }

	[JsonProperty("cancelledAt")]
	public string CancelledAt { get; set; }

	[JsonProperty("rejectedAt")]
	public string RejectedAt { get; set; }
}

sealed class NubraPositionEnvelope
{
	[JsonProperty("portfolio")]
	public NubraPositionPortfolio Portfolio { get; set; }
}

sealed class NubraPositionPortfolio
{
	[JsonProperty("clientCode")]
	public string ClientCode { get; set; }

	[JsonProperty("positions")]
	public NubraPosition[] Positions { get; set; }
}

sealed class NubraPosition
{
	[JsonProperty("refId")]
	public long RefId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("assetType")]
	public string AssetType { get; set; }

	[JsonProperty("deliveryType")]
	public string DeliveryType { get; set; }

	[JsonProperty("orderSide")]
	public string OrderSide { get; set; }

	[JsonProperty("netQuantity")]
	public decimal NetQuantity { get; set; }

	[JsonProperty("buyQuantity")]
	public decimal BuyQuantity { get; set; }

	[JsonProperty("sellQuantity")]
	public decimal SellQuantity { get; set; }

	[JsonProperty("lastTradedPrice")]
	public long LastPrice { get; set; }

	[JsonProperty("avgPrice")]
	public long AveragePrice { get; set; }

	[JsonProperty("avgBuyPrice")]
	public long AverageBuyPrice { get; set; }

	[JsonProperty("avgSellPrice")]
	public long AverageSellPrice { get; set; }

	[JsonProperty("pnl")]
	public long PnL { get; set; }
}

sealed class NubraHoldingEnvelope
{
	[JsonProperty("portfolio")]
	public NubraHoldingPortfolio Portfolio { get; set; }
}

sealed class NubraHoldingPortfolio
{
	[JsonProperty("clientCode")]
	public string ClientCode { get; set; }

	[JsonProperty("holdings")]
	public NubraHolding[] Holdings { get; set; }
}

sealed class NubraHolding
{
	[JsonProperty("refId")]
	public long RefId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("pledgedQty")]
	public decimal PledgedQuantity { get; set; }

	[JsonProperty("t1Qty")]
	public decimal T1Quantity { get; set; }

	[JsonProperty("avgPrice")]
	public long AveragePrice { get; set; }

	[JsonProperty("lastTradedPrice")]
	public long LastPrice { get; set; }

	[JsonProperty("netPnl")]
	public long PnL { get; set; }
}

sealed class NubraFundsEnvelope
{
	[JsonProperty("portFundsAndMargin")]
	public NubraFunds Funds { get; set; }
}

sealed class NubraFunds
{
	[JsonProperty("clientCode")]
	public string ClientCode { get; set; }

	[JsonProperty("startOfDayFunds")]
	public long StartOfDayFunds { get; set; }

	[JsonProperty("netTradingAmount")]
	public long NetTradingAmount { get; set; }

	[JsonProperty("netWithdrawalAmount")]
	public long NetWithdrawalAmount { get; set; }

	[JsonProperty("totalCollateral")]
	public long TotalCollateral { get; set; }

	[JsonProperty("netMarginAvailable")]
	public long AvailableMargin { get; set; }

	[JsonProperty("totalMarginBlocked")]
	public long BlockedMargin { get; set; }
}

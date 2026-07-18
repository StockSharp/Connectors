namespace StockSharp.WooX.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum WooXSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WooXOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,

	[EnumMember(Value = "POST_ONLY")]
	PostOnly,

	[EnumMember(Value = "ASK")]
	Ask,

	[EnumMember(Value = "BID")]
	Bid,

	[EnumMember(Value = "RPI")]
	RetailPriceImprovement,

	[EnumMember(Value = "LIQUIDATE")]
	Liquidate,

	[EnumMember(Value = "LIQUIDATE_BLP")]
	LiquidateBackstop,

	[EnumMember(Value = "ADL")]
	AutoDeleverage,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WooXOrderStatuses
{
	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "PARTIAL_FILLED")]
	PartialFilled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "CANCELLED")]
	Cancelled,

	[EnumMember(Value = "REJECTED")]
	Rejected,
}

enum WooXWsTopics
{
	Ticker,
	BestBidOffer,
	OrderBook,
	Trade,
	Candle,
	IndexPrice,
	MarkPrice,
	Balance,
	ExecutionReport,
	Position,
}

readonly record struct WooXParameter(string Name, string Value);

interface IWooXParameters
{
	WooXParameter[] GetParameters();
}

sealed class WooXEmptyParameters : IWooXParameters
{
	public static WooXEmptyParameters Instance { get; } = new();

	private WooXEmptyParameters()
	{
	}

	public WooXParameter[] GetParameters() => [];
}

class WooXResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("timestamp")]
	public decimal Timestamp { get; set; }
}

sealed class WooXDataResponse<TData> : WooXResponse
{
	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class WooXRowsResponse<TData> : WooXResponse
{
	[JsonProperty("rows")]
	public TData[] Rows { get; set; }

	[JsonProperty("meta")]
	public WooXPageMeta Meta { get; set; }
}

sealed class WooXPageMeta
{
	[JsonProperty("total")]
	public long Total { get; set; }

	[JsonProperty("records_per_page")]
	public int RecordsPerPage { get; set; }

	[JsonProperty("current_page")]
	public int CurrentPage { get; set; }
}

sealed class WooXHistoricalData<TData>
{
	[JsonProperty("rows")]
	public TData[] Rows { get; set; }

	[JsonProperty("meta")]
	public WooXPageMeta Meta { get; set; }
}

sealed class WooXSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("quote_min")]
	public decimal QuoteMinimum { get; set; }

	[JsonProperty("quote_max")]
	public decimal QuoteMaximum { get; set; }

	[JsonProperty("quote_tick")]
	public decimal QuoteTick { get; set; }

	[JsonProperty("base_min")]
	public decimal BaseMinimum { get; set; }

	[JsonProperty("base_max")]
	public decimal BaseMaximum { get; set; }

	[JsonProperty("base_tick")]
	public decimal BaseTick { get; set; }

	[JsonProperty("min_notional")]
	public decimal MinimumNotional { get; set; }

	[JsonProperty("is_trading")]
	public int IsTradingValue { get; set; }

	[JsonProperty("created_time")]
	public string CreatedTime { get; set; }

	[JsonProperty("updated_time")]
	public string UpdatedTime { get; set; }

	[JsonProperty("listing_time")]
	public string ListingTime { get; set; }

	[JsonProperty("base_asset_multiplier")]
	public decimal? BaseAssetMultiplier { get; set; }
}

sealed class WooXSymbolsResponse : WooXResponse
{
	[JsonProperty("rows")]
	public WooXSymbol[] Rows { get; set; }
}

sealed class WooXMarketTradesQuery : IWooXParameters
{
	public string Symbol { get; init; }
	public int Limit { get; init; }

	public WooXParameter[] GetParameters()
		=>
		[
			new("symbol", Symbol),
			new("limit", Limit.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class WooXHistoricalTradesQuery : IWooXParameters
{
	public string Symbol { get; init; }
	public long StartTime { get; init; }
	public int Page { get; init; }
	public int Size { get; init; }

	public WooXParameter[] GetParameters()
		=>
		[
			new("symbol", Symbol),
			new("start_time", StartTime.ToString(CultureInfo.InvariantCulture)),
			new("page", Page.ToString(CultureInfo.InvariantCulture)),
			new("size", Size.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class WooXMarketTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public WooXSides Side { get; set; }

	[JsonProperty("source")]
	public int Source { get; set; }

	[JsonProperty("executed_price")]
	public decimal Price { get; set; }

	[JsonProperty("executed_quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("executed_timestamp")]
	public string ExecutedTimestamp { get; set; }

	[JsonProperty("rpi")]
	public bool IsRetailPriceImprovement { get; set; }
}

sealed class WooXMarketTradesResponse : WooXResponse
{
	[JsonProperty("rows")]
	public WooXMarketTrade[] Rows { get; set; }
}

[JsonConverter(typeof(WooXPriceLevelConverter))]
sealed class WooXPriceLevel
{
	public decimal Price { get; set; }
	public decimal Quantity { get; set; }
}

sealed class WooXNamedPriceLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }
}

sealed class WooXOrderBookResponse : WooXResponse
{
	[JsonProperty("asks")]
	public WooXNamedPriceLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public WooXNamedPriceLevel[] Bids { get; set; }
}

sealed class WooXOrderBookQuery : IWooXParameters
{
	public int MaximumLevel { get; init; }

	public WooXParameter[] GetParameters()
		=> [new("max_level", MaximumLevel.ToString(CultureInfo.InvariantCulture))];
}

sealed class WooXKlinesQuery : IWooXParameters
{
	public string Symbol { get; init; }
	public string Interval { get; init; }
	public int Limit { get; init; }

	public WooXParameter[] GetParameters()
		=>
		[
			new("symbol", Symbol),
			new("type", Interval),
			new("limit", Limit.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class WooXHistoricalKlinesQuery : IWooXParameters
{
	public string Symbol { get; init; }
	public string Interval { get; init; }
	public long StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Page { get; init; }
	public int Size { get; init; }

	public WooXParameter[] GetParameters()
	{
		var result = new List<WooXParameter>
		{
			new("symbol", Symbol),
			new("type", Interval),
			new("start_time", StartTime.ToString(CultureInfo.InvariantCulture)),
			new("page", Page.ToString(CultureInfo.InvariantCulture)),
			new("size", Size.ToString(CultureInfo.InvariantCulture)),
		};
		if (EndTime is long endTime)
			result.Add(new("end_time", endTime.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class WooXCandle
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Interval { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("start_timestamp")]
	public long StartTimestamp { get; set; }

	[JsonProperty("end_timestamp")]
	public long EndTimestamp { get; set; }
}

sealed class WooXKlinesResponse : WooXResponse
{
	[JsonProperty("rows")]
	public WooXCandle[] Rows { get; set; }
}

sealed class WooXFuturesInfo
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("index_price")]
	public decimal IndexPrice { get; set; }

	[JsonProperty("mark_price")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("est_funding_rate")]
	public decimal EstimatedFundingRate { get; set; }

	[JsonProperty("last_funding_rate")]
	public decimal LastFundingRate { get; set; }

	[JsonProperty("next_funding_time")]
	public long NextFundingTime { get; set; }

	[JsonProperty("open_interest")]
	public decimal OpenInterest { get; set; }

	[JsonProperty("24h_open")]
	public decimal Open24Hours { get; set; }

	[JsonProperty("24h_close")]
	public decimal Close24Hours { get; set; }

	[JsonProperty("24h_high")]
	public decimal High24Hours { get; set; }

	[JsonProperty("24h_low")]
	public decimal Low24Hours { get; set; }

	[JsonProperty("24h_volume")]
	public decimal Volume24Hours { get; set; }

	[JsonProperty("24h_amount")]
	public decimal Amount24Hours { get; set; }
}

sealed class WooXFuturesResponse : WooXResponse
{
	[JsonProperty("rows")]
	public WooXFuturesInfo[] Rows { get; set; }
}

sealed class WooXPlaceOrderRequest : IWooXParameters
{
	public string Symbol { get; init; }
	public long ClientOrderId { get; init; }
	public WooXMarginModes? MarginMode { get; init; }
	public WooXOrderTypes OrderType { get; init; }
	public decimal? Price { get; init; }
	public decimal? Quantity { get; init; }
	public decimal? Amount { get; init; }
	public bool? IsReduceOnly { get; init; }
	public decimal? VisibleQuantity { get; init; }
	public WooXSides Side { get; init; }
	public WooXPositionSides? PositionSide { get; init; }

	public WooXParameter[] GetParameters()
	{
		var result = new List<WooXParameter>
		{
			new("symbol", Symbol),
			new("client_order_id", ClientOrderId.ToString(CultureInfo.InvariantCulture)),
			new("order_type", OrderType switch
			{
				WooXOrderTypes.Limit => "LIMIT",
				WooXOrderTypes.Market => "MARKET",
				WooXOrderTypes.ImmediateOrCancel => "IOC",
				WooXOrderTypes.FillOrKill => "FOK",
				WooXOrderTypes.PostOnly => "POST_ONLY",
				WooXOrderTypes.Ask => "ASK",
				WooXOrderTypes.Bid => "BID",
				WooXOrderTypes.RetailPriceImprovement => "RPI",
				_ => throw new ArgumentOutOfRangeException(nameof(OrderType), OrderType, null),
			}),
			new("side", Side == WooXSides.Buy ? "BUY" : "SELL"),
			new("order_tag", "StockSharp"),
		};
		if (MarginMode is WooXMarginModes marginMode)
			result.Add(new("margin_mode", marginMode == WooXMarginModes.Cross ? "CROSS" : "ISOLATED"));
		if (Price is decimal price)
			result.Add(new("order_price", price.ToWire()));
		if (Quantity is decimal quantity)
			result.Add(new("order_quantity", quantity.ToWire()));
		if (Amount is decimal amount)
			result.Add(new("order_amount", amount.ToWire()));
		if (IsReduceOnly is bool isReduceOnly)
			result.Add(new("reduce_only", isReduceOnly.ToWire()));
		if (VisibleQuantity is decimal visibleQuantity)
			result.Add(new("visible_quantity", visibleQuantity.ToWire()));
		if (PositionSide is WooXPositionSides positionSide && positionSide != WooXPositionSides.Both)
			result.Add(new("position_side", positionSide == WooXPositionSides.Long ? "LONG" : "SHORT"));
		return [.. result];
	}
}

sealed class WooXPlaceOrderResponse : WooXResponse
{
	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public long ClientOrderId { get; set; }

	[JsonProperty("order_type")]
	public WooXOrderTypes OrderType { get; set; }

	[JsonProperty("order_price")]
	public decimal Price { get; set; }

	[JsonProperty("order_quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("order_amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; set; }

}

sealed class WooXCancelOrderRequest : IWooXParameters
{
	public long OrderId { get; init; }
	public string Symbol { get; init; }

	public WooXParameter[] GetParameters()
		=>
		[
			new("order_id", OrderId.ToString(CultureInfo.InvariantCulture)),
			new("symbol", Symbol),
		];
}

sealed class WooXCancelSymbolRequest : IWooXParameters
{
	public string Symbol { get; init; }

	public WooXParameter[] GetParameters() => [new("symbol", Symbol)];
}

sealed class WooXOperationResponse : WooXResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class WooXEditOrderRequest
{
	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }
}

sealed class WooXEditOrderData
{
	[JsonProperty("success")]
	public bool IsSuccess { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class WooXOrdersQuery : IWooXParameters
{
	public string Symbol { get; init; }
	public string Status { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Page { get; init; }
	public int Size { get; init; }

	public WooXParameter[] GetParameters()
	{
		var result = new List<WooXParameter>
		{
			new("page", Page.ToString(CultureInfo.InvariantCulture)),
			new("size", Size.ToString(CultureInfo.InvariantCulture)),
		};
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		if (!Status.IsEmpty())
			result.Add(new("status", Status));
		if (StartTime is long startTime)
			result.Add(new("start_t", startTime.ToString(CultureInfo.InvariantCulture)));
		if (EndTime is long endTime)
			result.Add(new("end_t", endTime.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class WooXOrder
{
	[JsonProperty("created_time")]
	public string CreatedTime { get; set; }

	[JsonProperty("updated_time")]
	public string UpdatedTime { get; set; }

	[JsonProperty("side")]
	public WooXSides Side { get; set; }

	[JsonProperty("status")]
	public WooXOrderStatuses Status { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("client_order_id")]
	public long ClientOrderId { get; set; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("type")]
	public WooXOrderTypes Type { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("visible")]
	public decimal Visible { get; set; }

	[JsonProperty("executed")]
	public decimal Executed { get; set; }

	[JsonProperty("total_fee")]
	public decimal TotalFee { get; set; }

	[JsonProperty("fee_asset")]
	public string FeeAsset { get; set; }

	[JsonProperty("average_executed_price")]
	public decimal? AverageExecutedPrice { get; set; }

	[JsonProperty("realized_pnl")]
	public decimal? RealizedPnL { get; set; }

	[JsonProperty("position_side")]
	public WooXPositionSides? PositionSide { get; set; }

	[JsonProperty("margin_mode")]
	public WooXMarginModes? MarginMode { get; set; }

	[JsonProperty("leverage")]
	public int? Leverage { get; set; }
}

sealed class WooXTradeHistoryQuery : IWooXParameters
{
	public string Symbol { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Page { get; init; }
	public int Size { get; init; }

	public WooXParameter[] GetParameters()
	{
		var result = new List<WooXParameter>
		{
			new("page", Page.ToString(CultureInfo.InvariantCulture)),
			new("size", Size.ToString(CultureInfo.InvariantCulture)),
		};
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		if (StartTime is long startTime)
			result.Add(new("start_t", startTime.ToString(CultureInfo.InvariantCulture)));
		if (EndTime is long endTime)
			result.Add(new("end_t", endTime.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class WooXTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("executed_price")]
	public decimal Price { get; set; }

	[JsonProperty("executed_quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("is_maker")]
	public int IsMakerValue { get; set; }

	[JsonProperty("side")]
	public WooXSides Side { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("fee_asset")]
	public string FeeAsset { get; set; }

	[JsonProperty("executed_timestamp")]
	public string ExecutedTimestamp { get; set; }

	[JsonProperty("is_match_rpi")]
	public bool IsRetailPriceImprovement { get; set; }
}

sealed class WooXBalanceData
{
	[JsonProperty("holding")]
	public WooXBalance[] Holding { get; set; }

	[JsonProperty("userId")]
	public long UserId { get; set; }

	[JsonProperty("applicationId")]
	public string ApplicationId { get; set; }
}

sealed class WooXBalance
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("holding")]
	public decimal Holding { get; set; }

	[JsonProperty("frozen")]
	public decimal Frozen { get; set; }

	[JsonProperty("staked")]
	public decimal Staked { get; set; }

	[JsonProperty("unbonding")]
	public decimal Unbonding { get; set; }

	[JsonProperty("vault")]
	public decimal Vault { get; set; }

	[JsonProperty("interest")]
	public decimal Interest { get; set; }

	[JsonProperty("pendingShortQty")]
	public decimal PendingShortQuantity { get; set; }

	[JsonProperty("pendingLongQty")]
	public decimal PendingLongQuantity { get; set; }

	[JsonProperty("availableBalance")]
	public decimal? AvailableBalance { get; set; }

	[JsonProperty("averageOpenPrice")]
	public decimal AverageOpenPrice { get; set; }

	[JsonProperty("markPrice")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("pnl24H")]
	public decimal PnL24Hours { get; set; }

	[JsonProperty("fee24H")]
	public decimal Fee24Hours { get; set; }

	[JsonProperty("updatedTime")]
	public decimal UpdatedTime { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class WooXAccountInfo
{
	[JsonProperty("applicationId")]
	public string ApplicationId { get; set; }

	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("alias")]
	public string Alias { get; set; }

	[JsonProperty("accountMode")]
	public string AccountMode { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("totalCollateral")]
	public decimal TotalCollateral { get; set; }

	[JsonProperty("freeCollateral")]
	public decimal FreeCollateral { get; set; }

	[JsonProperty("totalAccountValue")]
	public decimal TotalAccountValue { get; set; }

	[JsonProperty("marginRatio")]
	public decimal MarginRatio { get; set; }
}

sealed class WooXPositionsData
{
	[JsonProperty("positions")]
	public WooXPosition[] Positions { get; set; }
}

sealed class WooXPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("holding")]
	public decimal Holding { get; set; }

	[JsonProperty("pendingLongQty")]
	public decimal PendingLongQuantity { get; set; }

	[JsonProperty("pendingShortQty")]
	public decimal PendingShortQuantity { get; set; }

	[JsonProperty("settlePrice")]
	public decimal SettlePrice { get; set; }

	[JsonProperty("averageOpenPrice")]
	public decimal AverageOpenPrice { get; set; }

	[JsonProperty("pnl24H")]
	public decimal PnL24Hours { get; set; }

	[JsonProperty("fee24H")]
	public decimal Fee24Hours { get; set; }

	[JsonProperty("markPrice")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("estLiqPrice")]
	public decimal EstimatedLiquidationPrice { get; set; }

	[JsonProperty("timestamp")]
	public decimal Timestamp { get; set; }

	[JsonProperty("positionSide")]
	public WooXPositionSides PositionSide { get; set; }

	[JsonProperty("marginMode")]
	public WooXMarginModes MarginMode { get; set; }

	[JsonProperty("isolatedMarginToken")]
	public string IsolatedMarginToken { get; set; }

	[JsonProperty("isolatedMarginAmount")]
	public decimal IsolatedMarginAmount { get; set; }

	[JsonProperty("isolatedFrozenLong")]
	public decimal IsolatedFrozenLong { get; set; }

	[JsonProperty("isolatedFrozenShort")]
	public decimal IsolatedFrozenShort { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }
}

sealed class WooXSetLeverageRequest : IWooXParameters
{
	public string Symbol { get; init; }
	public WooXMarginModes MarginMode { get; init; }
	public WooXPositionSides PositionSide { get; init; }
	public int Leverage { get; init; }

	public WooXParameter[] GetParameters()
		=>
		[
			new("symbol", Symbol),
			new("margin_mode", MarginMode == WooXMarginModes.Cross ? "CROSS" : "ISOLATED"),
			new("position_side", PositionSide switch
			{
				WooXPositionSides.Long => "LONG",
				WooXPositionSides.Short => "SHORT",
				_ => "BOTH",
			}),
			new("leverage", Leverage.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class WooXWsHeader
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class WooXWsEnvelope<TData>
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class WooXWsCommand
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("topic")]
	public string Topic { get; init; }

	[JsonProperty("event")]
	public string Event { get; init; }
}

sealed class WooXWsHeartbeat
{
	[JsonProperty("event")]
	public string Event { get; init; }
}

sealed class WooXWsAuth
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("event")]
	public string Event { get; init; } = "auth";

	[JsonProperty("params")]
	public WooXWsAuthParameters Parameters { get; init; }
}

sealed class WooXWsAuthParameters
{
	[JsonProperty("apikey")]
	public string ApiKey { get; init; }

	[JsonProperty("sign")]
	public string Signature { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }
}

sealed class WooXWsBook
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("asks")]
	public WooXPriceLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public WooXPriceLevel[] Bids { get; set; }
}

sealed class WooXWsTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("side")]
	public WooXSides Side { get; set; }

	[JsonProperty("source")]
	public int Source { get; set; }

	[JsonProperty("rpi")]
	public bool IsRetailPriceImprovement { get; set; }
}

sealed class WooXWsTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("count")]
	public long Count { get; set; }

	[JsonProperty("lastTs")]
	public long LastTimestamp { get; set; }
}

sealed class WooXWsBestBidOffer
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ask")]
	public decimal AskPrice { get; set; }

	[JsonProperty("askSize")]
	public decimal AskSize { get; set; }

	[JsonProperty("bid")]
	public decimal BidPrice { get; set; }

	[JsonProperty("bidSize")]
	public decimal BidSize { get; set; }
}

sealed class WooXWsReferencePrice
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }
}

sealed class WooXWsCandle
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Interval { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("startTime")]
	public long StartTime { get; set; }

	[JsonProperty("endTime")]
	public long EndTime { get; set; }
}

sealed class WooXWsBalanceData
{
	[JsonProperty("balances")]
	public WooXWsBalances Balances { get; set; }
}

[JsonConverter(typeof(WooXWsBalancesConverter))]
sealed class WooXWsBalances
{
	public WooXBalance[] Entries { get; set; }
}

sealed class WooXWsExecutionReport
{
	[JsonProperty("msgType")]
	public int MessageType { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("clientOrderId")]
	public long ClientOrderId { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("type")]
	public WooXOrderTypes Type { get; set; }

	[JsonProperty("side")]
	public WooXSides Side { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	[JsonProperty("executedPrice")]
	public decimal ExecutedPrice { get; set; }

	[JsonProperty("executedQuantity")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("feeAsset")]
	public string FeeAsset { get; set; }

	[JsonProperty("totalExecutedQuantity")]
	public decimal TotalExecutedQuantity { get; set; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("status")]
	public WooXOrderStatuses Status { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("totalFee")]
	public decimal TotalFee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("visible")]
	public decimal Visible { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("maker")]
	public bool IsMaker { get; set; }

	[JsonProperty("leverage")]
	public int? Leverage { get; set; }

	[JsonProperty("marginMode")]
	public WooXMarginModes? MarginMode { get; set; }

	[JsonProperty("positionSide")]
	public WooXPositionSides? PositionSide { get; set; }

	[JsonProperty("rpi")]
	public bool IsRetailPriceImprovement { get; set; }
}

sealed class WooXWsPositionData
{
	[JsonProperty("positions")]
	public WooXWsPositions Positions { get; set; }
}

[JsonConverter(typeof(WooXWsPositionsConverter))]
sealed class WooXWsPositions
{
	public WooXPosition[] Entries { get; set; }
}

sealed class WooXPriceLevelConverter : JsonConverter<WooXPriceLevel>
{
	public override WooXPriceLevel ReadJson(JsonReader reader, Type objectType,
		WooXPriceLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("WOO X price level must be an array.");
		if (!reader.Read() || reader.TokenType is not (JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException("WOO X price level has no price.");
		var price = Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType is not (JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException("WOO X price level has no quantity.");
		var quantity = Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("WOO X price level has unexpected fields.");
		return new() { Price = price, Quantity = quantity };
	}

	public override void WriteJson(JsonWriter writer, WooXPriceLevel value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Price);
		writer.WriteValue(value.Quantity);
		writer.WriteEndArray();
	}
}

sealed class WooXWsBalancesConverter : JsonConverter<WooXWsBalances>
{
	public override WooXWsBalances ReadJson(JsonReader reader, Type objectType,
		WooXWsBalances existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("WOO X balances must be an object.");
		var entries = new List<WooXBalance>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("WOO X balance token name is missing.");
			var token = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException("WOO X balance value is missing.");
			var balance = serializer.Deserialize<WooXBalance>(reader)
				?? throw new JsonSerializationException("WOO X balance value is empty.");
			balance.Token = token;
			entries.Add(balance);
		}
		return new() { Entries = [.. entries] };
	}

	public override void WriteJson(JsonWriter writer, WooXWsBalances value,
		JsonSerializer serializer)
	{
		writer.WriteStartObject();
		foreach (var balance in value?.Entries ?? [])
		{
			writer.WritePropertyName(balance.Token);
			serializer.Serialize(writer, balance);
		}
		writer.WriteEndObject();
	}
}

sealed class WooXWsPositionsConverter : JsonConverter<WooXWsPositions>
{
	public override WooXWsPositions ReadJson(JsonReader reader, Type objectType,
		WooXWsPositions existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("WOO X positions must be an object.");
		var entries = new List<WooXPosition>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("WOO X position symbol is missing.");
			var symbol = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException("WOO X position value is missing.");
			var position = serializer.Deserialize<WooXPosition>(reader)
				?? throw new JsonSerializationException("WOO X position value is empty.");
			position.Symbol = symbol;
			entries.Add(position);
		}
		return new() { Entries = [.. entries] };
	}

	public override void WriteJson(JsonWriter writer, WooXWsPositions value,
		JsonSerializer serializer)
	{
		writer.WriteStartObject();
		foreach (var position in value?.Entries ?? [])
		{
			writer.WritePropertyName(position.Symbol);
			serializer.Serialize(writer, position);
		}
		writer.WriteEndObject();
	}
}

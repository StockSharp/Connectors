namespace StockSharp.Synthetix.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixInfoRequest<T>
{
	[JsonProperty("params")]
	public T Parameters { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixTradeRequest<T>
{
	[JsonProperty("params")]
	public T Parameters { get; init; }

	[JsonProperty("nonce", DefaultValueHandling = DefaultValueHandling.Ignore)]
	public long? Nonce { get; init; }

	[JsonProperty("expiresAfter")]
	public long ExpiresAfter { get; init; }

	[JsonProperty("signature")]
	public SynthetixSignature Signature { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixApiResponse<T>
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("response")]
	public T Response { get; set; }

	[JsonProperty("error")]
	public SynthetixApiError Error { get; set; }

	[JsonProperty("request_id")]
	public string RequestId { get; set; }

	[JsonProperty("requestId")]
	public string RequestIdAlternative { get; set; }

	[JsonProperty("traceId")]
	public string TraceId { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixApiError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("retryable")]
	public bool IsRetryable { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSignature
{
	[JsonProperty("v")]
	public int V { get; init; }

	[JsonProperty("r")]
	public string R { get; init; }

	[JsonProperty("s")]
	public string S { get; init; }

	[JsonIgnore]
	public string Raw => R.IsEmpty() || S.IsEmpty()
		? null
		: R + S[2..] + V.ToString("x2", CultureInfo.InvariantCulture);
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixActionParameters
{
	[JsonProperty("action")]
	public string Action { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixMarketsParameters
{
	[JsonProperty("action")]
	public string Action { get; init; } = "getMarkets";

	[JsonProperty("activeOnly")]
	public bool IsActiveOnly { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSymbolParameters
{
	[JsonProperty("action")]
	public string Action { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixCandleParameters
{
	[JsonProperty("action")]
	public string Action { get; init; } = "getCandles";

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("interval")]
	public string Interval { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("startTime", DefaultValueHandling = DefaultValueHandling.Ignore)]
	public long? StartTime { get; init; }

	[JsonProperty("endTime", DefaultValueHandling = DefaultValueHandling.Ignore)]
	public long? EndTime { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixMarginTier
{
	[JsonProperty("minPositionSize")]
	public string MinimumPositionSize { get; set; }

	[JsonProperty("maxPositionSize")]
	public string MaximumPositionSize { get; set; }

	[JsonProperty("maxLeverage")]
	public decimal MaximumLeverage { get; set; }

	[JsonProperty("initialMarginRequirement")]
	public string InitialMarginRequirement { get; set; }

	[JsonProperty("maintenanceMarginRequirement")]
	public string MaintenanceMarginRequirement { get; set; }

	[JsonProperty("maintenanceDeductionValue")]
	public string MaintenanceDeductionValue { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixMarket
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("settleAsset")]
	public string SettlementAsset { get; set; }

	[JsonProperty("isOpen")]
	public bool IsOpen { get; set; }

	[JsonProperty("isCloseOnly")]
	public bool IsCloseOnly { get; set; }

	[JsonProperty("priceExponent")]
	public int PriceExponent { get; set; }

	[JsonProperty("quantityExponent")]
	public int QuantityExponent { get; set; }

	[JsonProperty("priceIncrement")]
	public string PriceIncrement { get; set; }

	[JsonProperty("minOrderSize")]
	public string MinimumOrderSize { get; set; }

	[JsonProperty("orderSizeIncrement")]
	public string OrderSizeIncrement { get; set; }

	[JsonProperty("contractSize")]
	public decimal ContractSize { get; set; }

	[JsonProperty("maxMarketOrderSize")]
	public string MaximumMarketOrderSize { get; set; }

	[JsonProperty("maxLimitOrderSize")]
	public string MaximumLimitOrderSize { get; set; }

	[JsonProperty("minOrderPrice")]
	public string MinimumOrderPrice { get; set; }

	[JsonProperty("minNotionalValue")]
	public string MinimumNotionalValue { get; set; }

	[JsonProperty("maintenanceMarginTiers")]
	public SynthetixMarginTier[] MaintenanceMarginTiers { get; set; }
}

[JsonConverter(typeof(SynthetixMarketPricesConverter))]
sealed class SynthetixMarketPrices
{
	public SynthetixMarketPrice[] Items { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixMarketPrice
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bestBid")]
	public string BestBid { get; set; }

	[JsonProperty("bestAsk")]
	public string BestAsk { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("indexPrice")]
	public string IndexPrice { get; set; }

	[JsonProperty("lastPrice")]
	public string LastPrice { get; set; }

	[JsonProperty("prevDayPrice")]
	public string PreviousDayPrice { get; set; }

	[JsonProperty("volume24h")]
	public string Volume24Hours { get; set; }

	[JsonProperty("quoteVolume24h")]
	public string QuoteVolume24Hours { get; set; }

	[JsonProperty("fundingRate")]
	public string FundingRate { get; set; }

	[JsonProperty("openInterest")]
	public string OpenInterest { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class SynthetixMarketPricesConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(SynthetixMarketPrices);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return new SynthetixMarketPrices { Items = [] };
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"Synthetix market prices must be a JSON object.");
		var items = new List<SynthetixMarketPrice>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"Synthetix market price key is missing.");
			var symbol = Convert.ToString(reader.Value,
				CultureInfo.InvariantCulture);
			if (!reader.Read())
				throw new JsonSerializationException(
					"Synthetix market price value is missing.");
			var item = serializer.Deserialize<SynthetixMarketPrice>(reader) ??
				throw new JsonSerializationException(
					"Synthetix market price is null.");
			if (item.Symbol.IsEmpty())
				item.Symbol = symbol;
			items.Add(item);
		}
		return new SynthetixMarketPrices { Items = [.. items] };
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

[JsonConverter(typeof(SynthetixRestPriceLevelConverter))]
sealed class SynthetixRestPriceLevel
{
	public string Price { get; init; }
	public string Quantity { get; init; }
}

sealed class SynthetixRestPriceLevelConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(SynthetixRestPriceLevel);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray || !reader.Read())
			throw new JsonSerializationException(
				"Synthetix order-book level must be an array.");
		var price = Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read())
			throw new JsonSerializationException(
				"Synthetix order-book quantity is missing.");
		var quantity = Convert.ToString(reader.Value,
			CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"Synthetix order-book level has an unexpected shape.");
		return new SynthetixRestPriceLevel
		{
			Price = price,
			Quantity = quantity,
		};
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixOrderBook
{
	[JsonProperty("bids")]
	public SynthetixRestPriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public SynthetixRestPriceLevel[] Asks { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixPublicTrades
{
	[JsonProperty("trades")]
	public SynthetixPublicTrade[] Trades { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixPublicTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixCandles
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("candles")]
	public SynthetixCandle[] Candles { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixCandle
{
	[JsonProperty("openTime")]
	public long OpenTime { get; set; }

	[JsonProperty("closeTime")]
	public long CloseTime { get; set; }

	[JsonProperty("openPrice")]
	public string OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public string HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public string LowPrice { get; set; }

	[JsonProperty("closePrice")]
	public string ClosePrice { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }

	[JsonProperty("quoteVolume")]
	public string QuoteVolume { get; set; }

	[JsonProperty("tradeCount")]
	public long TradeCount { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
class SynthetixSubAccountParameters
{
	[JsonProperty("action")]
	public string Action { get; init; }

	[JsonProperty("subAccountId")]
	public string SubAccountId { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixAccountPageParameters : SynthetixSubAccountParameters
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }

	[JsonProperty("startTime", DefaultValueHandling = DefaultValueHandling.Ignore)]
	public long? StartTime { get; init; }

	[JsonProperty("endTime", DefaultValueHandling = DefaultValueHandling.Ignore)]
	public long? EndTime { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("offset")]
	public int Offset { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSubAccount
{
	[JsonProperty("subAccountId")]
	public string SubAccountId { get; set; }

	[JsonProperty("masterAccountId")]
	public string MasterAccountId { get; set; }

	[JsonProperty("subAccountName")]
	public string Name { get; set; }

	[JsonProperty("collaterals")]
	public SynthetixCollateral[] Collaterals { get; set; }

	[JsonProperty("crossMarginSummary")]
	public SynthetixMarginSummary MarginSummary { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixCollateral
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("withdrawable")]
	public string Withdrawable { get; set; }

	[JsonProperty("pendingWithdraw")]
	public string PendingWithdraw { get; set; }

	[JsonProperty("adjustedCollateralValue")]
	public string AdjustedCollateralValue { get; set; }

	[JsonProperty("collateralValue")]
	public string CollateralValue { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("calculatedAt")]
	public long CalculatedAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixMarginSummary
{
	[JsonProperty("accountValue")]
	public string AccountValue { get; set; }

	[JsonProperty("availableMargin")]
	public string AvailableMargin { get; set; }

	[JsonProperty("totalUnrealizedPnl")]
	public string TotalUnrealizedPnl { get; set; }

	[JsonProperty("maintenanceMargin")]
	public string MaintenanceMargin { get; set; }

	[JsonProperty("initialMargin")]
	public string InitialMargin { get; set; }

	[JsonProperty("withdrawable")]
	public string Withdrawable { get; set; }

	[JsonProperty("adjustedAccountValue")]
	public string AdjustedAccountValue { get; set; }

	[JsonProperty("debt")]
	public string Debt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixPosition
{
	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("subAccountId")]
	public string SubAccountId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("entryPrice")]
	public string EntryPrice { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnl { get; set; }

	[JsonProperty("unrealizedPnl")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("usedMargin")]
	public string UsedMargin { get; set; }

	[JsonProperty("maintenanceMargin")]
	public string MaintenanceMargin { get; set; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("netFunding")]
	public string NetFunding { get; set; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; set; }

	[JsonProperty("createdAt")]
	public long CreatedAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixOrderIdentifier
{
	[JsonProperty("venueId")]
	public string VenueId { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixOrder
{
	[JsonProperty("order")]
	public SynthetixOrderIdentifier Order { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("triggerPriceType")]
	public string TriggerPriceType { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; set; }

	[JsonProperty("closePosition")]
	public bool IsClosePosition { get; set; }

	[JsonProperty("createdTime")]
	public long CreatedTime { get; set; }

	[JsonProperty("updatedTime")]
	public long UpdatedTime { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }

	[JsonProperty("filledQuantity")]
	public string FilledQuantity { get; set; }

	[JsonProperty("filledPrice")]
	public string FilledPrice { get; set; }

	[JsonProperty("expiresAt")]
	public long ExpiresAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixAccountTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("order")]
	public SynthetixOrderIdentifier Order { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnl { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeRate")]
	public string FeeRate { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("entryPrice")]
	public string EntryPrice { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("maker")]
	public bool IsMaker { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixAccountTrades
{
	[JsonProperty("trades")]
	public SynthetixAccountTrade[] Trades { get; set; }

	[JsonProperty("hasMore")]
	public bool IsMoreAvailable { get; set; }

	[JsonProperty("total")]
	public long Total { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixPlaceOrderParameters
{
	[JsonProperty("action")]
	public string Action { get; init; } = "placeOrders";

	[JsonProperty("subAccountId")]
	public string SubAccountId { get; init; }

	[JsonProperty("orders")]
	public SynthetixPlaceOrder[] Orders { get; init; }

	[JsonProperty("grouping")]
	public string Grouping { get; init; } = "na";

	[JsonProperty("source")]
	public string Source { get; init; } = "stocksharp";
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixPlaceOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("orderType")]
	public string OrderType { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; init; }

	[JsonProperty("isTriggerMarket")]
	public bool IsTriggerMarket { get; init; }

	[JsonProperty("closePosition")]
	public bool IsClosePosition { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("triggerPriceType")]
	public string TriggerPriceType { get; init; }

	[JsonProperty("expiresAt", DefaultValueHandling = DefaultValueHandling.Ignore)]
	public long? ExpiresAt { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixCancelOrdersParameters
{
	[JsonProperty("action")]
	public string Action { get; init; } = "cancelOrders";

	[JsonProperty("subAccountId")]
	public string SubAccountId { get; init; }

	[JsonProperty("orderIds")]
	public string[] OrderIds { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixCancelAllOrdersParameters
{
	[JsonProperty("action")]
	public string Action { get; init; } = "cancelAllOrders";

	[JsonProperty("subAccountId")]
	public string SubAccountId { get; init; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixModifyOrderParameters
{
	[JsonProperty("action")]
	public string Action { get; init; } = "modifyOrder";

	[JsonProperty("subAccountId")]
	public string SubAccountId { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixOperationResponse
{
	[JsonProperty("statuses")]
	public SynthetixOperationStatus[] Statuses { get; set; }

	[JsonProperty("order")]
	public SynthetixOrderIdentifier Order { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixOperationStatus
{
	[JsonProperty("resting")]
	public SynthetixOperationResult Resting { get; set; }

	[JsonProperty("filled")]
	public SynthetixOperationResult Filled { get; set; }

	[JsonProperty("canceled")]
	public SynthetixOperationResult Canceled { get; set; }

	[JsonProperty("cancelled")]
	public SynthetixOperationResult Cancelled { get; set; }

	[JsonProperty("modified")]
	public SynthetixOperationResult Modified { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	public SynthetixOperationResult Result => Resting ?? Filled ?? Canceled ??
		Cancelled ?? Modified;
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixOperationResult
{
	[JsonProperty("order")]
	public SynthetixOrderIdentifier Order { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("avgPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("totalSize")]
	public string TotalSize { get; set; }

	[JsonProperty("expiresAt")]
	public long ExpiresAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixCancelAllResult
{
	[JsonProperty("order")]
	public SynthetixOrderIdentifier Order { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

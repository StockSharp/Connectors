namespace StockSharp.Bitunix.Native.Model;

readonly record struct BitunixParameter(string Name, string Value);

interface IBitunixQuery
{
	IEnumerable<BitunixParameter> GetParameters();
}

sealed class BitunixEmptyQuery : IBitunixQuery
{
	public IEnumerable<BitunixParameter> GetParameters() => [];
}

sealed class BitunixSymbolsQuery : IBitunixQuery
{
	public string Symbols { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		if (!Symbols.IsEmpty())
			yield return new("symbols", Symbols);
	}
}

sealed class BitunixSymbolQuery : IBitunixQuery
{
	public string Symbol { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		if (!Symbol.IsEmpty())
			yield return new("symbol", Symbol);
	}
}

sealed class BitunixSpotDepthQuery : IBitunixQuery
{
	public string Symbol { get; init; }
	public string Precision { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		yield return new("precision", Precision);
		yield return new("symbol", Symbol);
	}
}

sealed class BitunixSpotKlineQuery : IBitunixQuery
{
	public string Symbol { get; init; }
	public string Interval { get; init; }
	public long? EndTime { get; init; }
	public int? Limit { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		if (EndTime is long endTime)
			yield return new("endTime", endTime.ToString(CultureInfo.InvariantCulture));
		yield return new("interval", Interval);
		if (Limit is int limit)
			yield return new("limit", limit.ToString(CultureInfo.InvariantCulture));
		yield return new("symbol", Symbol);
	}
}

sealed class BitunixFuturesDepthQuery : IBitunixQuery
{
	public string Symbol { get; init; }
	public string Limit { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		yield return new("limit", Limit);
		yield return new("symbol", Symbol);
	}
}

sealed class BitunixFuturesKlineQuery : IBitunixQuery
{
	public string Symbol { get; init; }
	public string Interval { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Limit { get; init; }
	public string Type { get; init; } = "LAST_PRICE";

	public IEnumerable<BitunixParameter> GetParameters()
	{
		if (EndTime is long endTime)
			yield return new("endTime", endTime.ToString(CultureInfo.InvariantCulture));
		yield return new("interval", Interval);
		yield return new("limit", Limit.ToString(CultureInfo.InvariantCulture));
		if (StartTime is long startTime)
			yield return new("startTime", startTime.ToString(CultureInfo.InvariantCulture));
		yield return new("symbol", Symbol);
		yield return new("type", Type);
	}
}

sealed class BitunixFuturesAccountQuery : IBitunixQuery
{
	public string MarginCoin { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		yield return new("marginCoin", MarginCoin);
	}
}

sealed class BitunixFuturesPositionsQuery : IBitunixQuery
{
	public string Symbol { get; init; }
	public string PositionId { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		if (!PositionId.IsEmpty())
			yield return new("positionId", PositionId);
		if (!Symbol.IsEmpty())
			yield return new("symbol", Symbol);
	}
}

sealed class BitunixFuturesOrdersQuery : IBitunixQuery
{
	public string MarginCoin { get; init; }
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string ClientId { get; init; }
	public string Status { get; init; }
	public string Type { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int? Skip { get; init; }
	public int? Limit { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		if (!ClientId.IsEmpty())
			yield return new("clientId", ClientId);
		if (EndTime is long endTime)
			yield return new("endTime", endTime.ToString(CultureInfo.InvariantCulture));
		if (Limit is int limit)
			yield return new("limit", limit.ToString(CultureInfo.InvariantCulture));
		if (!MarginCoin.IsEmpty())
			yield return new("marginCoin", MarginCoin);
		if (!OrderId.IsEmpty())
			yield return new("orderId", OrderId);
		if (Skip is int skip)
			yield return new("skip", skip.ToString(CultureInfo.InvariantCulture));
		if (StartTime is long startTime)
			yield return new("startTime", startTime.ToString(CultureInfo.InvariantCulture));
		if (!Status.IsEmpty())
			yield return new("status", Status);
		if (!Symbol.IsEmpty())
			yield return new("symbol", Symbol);
		if (!Type.IsEmpty())
			yield return new("type", Type);
	}
}

sealed class BitunixFuturesTradesQuery : IBitunixQuery
{
	public string MarginCoin { get; init; }
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string PositionId { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int? Skip { get; init; }
	public int? Limit { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		if (EndTime is long endTime)
			yield return new("endTime", endTime.ToString(CultureInfo.InvariantCulture));
		if (Limit is int limit)
			yield return new("limit", limit.ToString(CultureInfo.InvariantCulture));
		if (!MarginCoin.IsEmpty())
			yield return new("marginCoin", MarginCoin);
		if (!OrderId.IsEmpty())
			yield return new("orderId", OrderId);
		if (!PositionId.IsEmpty())
			yield return new("positionId", PositionId);
		if (Skip is int skip)
			yield return new("skip", skip.ToString(CultureInfo.InvariantCulture));
		if (StartTime is long startTime)
			yield return new("startTime", startTime.ToString(CultureInfo.InvariantCulture));
		if (!Symbol.IsEmpty())
			yield return new("symbol", Symbol);
	}
}

sealed class BitunixFuturesOrderDetailQuery : IBitunixQuery
{
	public string OrderId { get; init; }
	public string ClientId { get; init; }

	public IEnumerable<BitunixParameter> GetParameters()
	{
		if (!ClientId.IsEmpty())
			yield return new("clientId", ClientId);
		if (!OrderId.IsEmpty())
			yield return new("orderId", OrderId);
	}
}

sealed class BitunixSpotPlaceOrderRequest
{
	[JsonProperty("side")]
	public int Side { get; init; }

	[JsonProperty("type")]
	public int Type { get; init; }

	[JsonProperty("volume")]
	public string Volume { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class BitunixSpotCancelOrdersRequest
{
	[JsonProperty("orderIdList")]
	public BitunixSpotOrderReference[] OrderIdList { get; init; }
}

sealed class BitunixSpotOrderReference
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class BitunixSpotPendingOrdersRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class BitunixSpotOrderHistoryRequest
{
	[JsonProperty("page")]
	public int Page { get; init; } = 1;

	[JsonProperty("pageSize")]
	public int PageSize { get; init; } = 100;

	[JsonProperty("startTime")]
	public string StartTime { get; init; }

	[JsonProperty("endTime")]
	public string EndTime { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class BitunixSpotFillsRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class BitunixFuturesPlaceOrderRequest
{
	[JsonProperty("marginCoin")]
	public string MarginCoin { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("tradeSide")]
	public string TradeSide { get; init; }

	[JsonProperty("orderType")]
	public string OrderType { get; init; }

	[JsonProperty("positionId")]
	public string PositionId { get; init; }

	[JsonProperty("effect")]
	public string Effect { get; init; }

	[JsonProperty("clientId")]
	public string ClientId { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }
}

sealed class BitunixFuturesModifyOrderRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientId")]
	public string ClientId { get; init; }

	[JsonProperty("marginCoin")]
	public string MarginCoin { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }
}

sealed class BitunixFuturesCancelOrdersRequest
{
	[JsonProperty("marginCoin")]
	public string MarginCoin { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("orderList")]
	public BitunixFuturesOrderReference[] OrderList { get; init; }
}

sealed class BitunixFuturesOrderReference
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientId")]
	public string ClientId { get; init; }
}

sealed class BitunixFuturesCancelAllOrdersRequest
{
	[JsonProperty("marginCoin")]
	public string MarginCoin { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class BitunixFuturesChangeLeverageRequest
{
	[JsonProperty("marginCoin")]
	public string MarginCoin { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("leverage")]
	public int Leverage { get; init; }
}

sealed class BitunixFuturesChangeMarginModeRequest
{
	[JsonProperty("marginMode")]
	public string MarginMode { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("marginCoin")]
	public string MarginCoin { get; init; }
}

sealed class BitunixResponse<TData>
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class BitunixEmptyData
{
}

sealed class BitunixSpotPair
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("basePrecision")]
	public int BasePrecision { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("minPrice")]
	public decimal MinimumPrice { get; set; }

	[JsonProperty("minVolume")]
	public decimal MinimumVolume { get; set; }

	[JsonProperty("maxLimitOrderAmount")]
	public decimal MaximumLimitOrderAmount { get; set; }

	[JsonProperty("maxMarketOrderAmount")]
	public decimal MaximumMarketOrderAmount { get; set; }

	[JsonProperty("isOpen")]
	public int IsOpenValue { get; set; }

	[JsonProperty("precisions")]
	public string[] Precisions { get; set; }
}

sealed class BitunixSpotBookLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

sealed class BitunixSpotDepth
{
	[JsonProperty("asks")]
	public BitunixSpotBookLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public BitunixSpotBookLevel[] Bids { get; set; }

	[JsonProperty("ts")]
	public DateTimeOffset Time { get; set; }
}

sealed class BitunixSpotCandle
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("ts")]
	public DateTimeOffset Time { get; set; }
}

sealed class BitunixSpotOrderResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("side")]
	public int Side { get; set; }

	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("placeStatus")]
	public string PlaceStatus { get; set; }
}

sealed class BitunixSpotBalance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("balanceLocked")]
	public decimal Locked { get; set; }
}

sealed class BitunixSpotOrderPage
{
	[JsonProperty("total")]
	public long Total { get; set; }

	[JsonProperty("pageNum")]
	public int PageNumber { get; set; }

	[JsonProperty("pageSize")]
	public int PageSize { get; set; }

	[JsonProperty("data")]
	public BitunixSpotOrder[] Orders { get; set; }
}

sealed class BitunixSpotOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("dealAmount")]
	public decimal FilledAmount { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("dealVolume")]
	public decimal FilledVolume { get; set; }

	[JsonProperty("leftVolume")]
	public decimal RemainingVolume { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("ctime")]
	public DateTimeOffset CreateTime { get; set; }

	[JsonProperty("utime")]
	public DateTimeOffset UpdateTime { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("feeCoin")]
	public string FeeCoin { get; set; }
}

sealed class BitunixSpotFill
{
	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("feeCoin")]
	public string FeeCoin { get; set; }

	[JsonProperty("role")]
	public string Role { get; set; }

	[JsonProperty("ctime")]
	public DateTimeOffset Time { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }
}

sealed class BitunixFuturesProduct
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("minTradeVolume")]
	public decimal MinimumTradeVolume { get; set; }

	[JsonProperty("maxLimitOrderVolume")]
	public decimal MaximumLimitOrderVolume { get; set; }

	[JsonProperty("maxMarketOrderVolume")]
	public decimal MaximumMarketOrderVolume { get; set; }

	[JsonProperty("basePrecision")]
	public int BasePrecision { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("maxLeverage")]
	public int MaximumLeverage { get; set; }

	[JsonProperty("minLeverage")]
	public int MinimumLeverage { get; set; }

	[JsonProperty("defaultLeverage")]
	public int DefaultLeverage { get; set; }

	[JsonProperty("defaultMarginMode")]
	public string DefaultMarginMode { get; set; }

	[JsonProperty("symbolStatus")]
	public string Status { get; set; }

	[JsonProperty("isApiSupported")]
	public bool? IsApiSupported { get; set; }
}

sealed class BitunixFuturesTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("markPrice")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("lastPrice")]
	public decimal LastPrice { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("last")]
	public decimal Last { get; set; }

	[JsonProperty("quoteVol")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("baseVol")]
	public decimal BaseVolume { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }
}

[JsonConverter(typeof(BitunixBookLevelConverter))]
sealed class BitunixBookLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
}

sealed class BitunixBookLevelConverter : JsonConverter<BitunixBookLevel>
{
	public override BitunixBookLevel ReadJson(JsonReader reader, Type objectType,
		BitunixBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Bitunix book level must be an array.");
		var result = new BitunixBookLevel
		{
			Price = BitunixJson.ReadDecimal(reader, "book price"),
			Volume = BitunixJson.ReadDecimal(reader, "book volume"),
		};
		BitunixJson.SkipArrayTail(reader, "book level");
		return result;
	}

	public override void WriteJson(JsonWriter writer, BitunixBookLevel value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class BitunixFuturesDepth
{
	[JsonProperty("asks")]
	public BitunixBookLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public BitunixBookLevel[] Bids { get; set; }
}

sealed class BitunixFuturesCandle
{
	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("quoteVol")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("baseVol")]
	public decimal BaseVolume { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }
}

sealed class BitunixFuturesAccount
{
	[JsonProperty("marginCoin")]
	public string MarginCoin { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("frozen")]
	public decimal Frozen { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("transfer")]
	public decimal Transfer { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("crossUnrealizedPNL")]
	public decimal CrossUnrealizedPnl { get; set; }

	[JsonProperty("isolationUnrealizedPNL")]
	public decimal IsolationUnrealizedPnl { get; set; }

	[JsonProperty("bonus")]
	public decimal Bonus { get; set; }
}

sealed class BitunixFuturesPosition
{
	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("marginCoin")]
	public string MarginCoin { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("entryValue")]
	public decimal EntryValue { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("marginMode")]
	public string MarginMode { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("funding")]
	public decimal Funding { get; set; }

	[JsonProperty("realizedPNL")]
	public decimal RealizedPnl { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("unrealizedPNL")]
	public decimal UnrealizedPnl { get; set; }

	[JsonProperty("liqPrice")]
	public decimal LiquidationPrice { get; set; }

	[JsonProperty("avgOpenPrice")]
	public decimal AverageOpenPrice { get; set; }

	[JsonProperty("marginRate")]
	public decimal MarginRate { get; set; }

	[JsonProperty("ctime")]
	public long CreateTime { get; set; }

	[JsonProperty("mtime")]
	public long UpdateTime { get; set; }
}

sealed class BitunixFuturesOrderPage
{
	[JsonProperty("total")]
	public long Total { get; set; }

	[JsonProperty("orderList")]
	public BitunixFuturesOrder[] Orders { get; set; }
}

sealed class BitunixFuturesOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("marginCoin")]
	public string MarginCoin { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("tradeQty")]
	public decimal TradedQuantity { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("marginMode")]
	public string MarginMode { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("effect")]
	public string Effect { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("realizedPNL")]
	public decimal RealizedPnl { get; set; }

	[JsonProperty("ctime")]
	public long CreateTime { get; set; }

	[JsonProperty("mtime")]
	public long UpdateTime { get; set; }
}

sealed class BitunixFuturesTradePage
{
	[JsonProperty("total")]
	public long Total { get; set; }

	[JsonProperty("tradeList")]
	public BitunixFuturesTrade[] Trades { get; set; }
}

sealed class BitunixFuturesTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("marginCoin")]
	public string MarginCoin { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("realizedPNL")]
	public decimal RealizedPnl { get; set; }

	[JsonProperty("ctime")]
	public long CreateTime { get; set; }

	[JsonProperty("roleType")]
	public string RoleType { get; set; }
}

sealed class BitunixFuturesOrderIdResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }
}

sealed class BitunixFuturesOrderResult
{
	[JsonProperty("successList")]
	public BitunixFuturesOrderIdResult[] Successes { get; set; }

	[JsonProperty("failureList")]
	public BitunixFuturesOrderFailure[] Failures { get; set; }
}

sealed class BitunixFuturesOrderFailure
{
	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("errorMsg")]
	public string Message { get; set; }

	[JsonProperty("errorCode")]
	public int Code { get; set; }
}

sealed class BitunixWsCommand<TArgument>
{
	[JsonProperty("op")]
	public string Operation { get; init; }

	[JsonProperty("args")]
	public TArgument[] Arguments { get; init; }
}

sealed class BitunixWsPing
{
	[JsonProperty("op")]
	public string Operation { get; init; } = "ping";

	[JsonProperty("ping")]
	public long Time { get; init; }
}

sealed class BitunixWsSubscription
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("ch")]
	public string Channel { get; init; }
}

sealed class BitunixWsPrivateSubscription
{
	[JsonProperty("ch")]
	public string Channel { get; init; }
}

sealed class BitunixWsLogin
{
	[JsonProperty("apiKey")]
	public string ApiKey { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }

	[JsonProperty("sign")]
	public string Signature { get; init; }
}

sealed class BitunixWsHeader
{
	[JsonProperty("op")]
	public string Operation { get; set; }

	[JsonProperty("ch")]
	public string Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ts")]
	public long Time { get; set; }
}

sealed class BitunixWsOperation
{
	[JsonProperty("op")]
	public string Operation { get; set; }

	[JsonProperty("data")]
	public BitunixWsOperationData Data { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class BitunixWsOperationData
{
	[JsonProperty("result")]
	public bool? Result { get; set; }
}

sealed class BitunixWsEnvelope<TData>
{
	[JsonProperty("ch")]
	public string Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ts")]
	public long Time { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class BitunixWsTicker
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("la")]
	public decimal LastPrice { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("b")]
	public decimal BaseVolume { get; set; }

	[JsonProperty("q")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("r")]
	public decimal ChangePercent { get; set; }
}

sealed class BitunixWsPrice
{
	[JsonProperty("ip")]
	public decimal IndexPrice { get; set; }

	[JsonProperty("mp")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("fr")]
	public decimal FundingRate { get; set; }

	[JsonProperty("ft")]
	public DateTimeOffset FundingTime { get; set; }

	[JsonProperty("nft")]
	public DateTimeOffset NextFundingTime { get; set; }
}

sealed class BitunixWsDepth
{
	[JsonProperty("b")]
	public BitunixBookLevel[] Bids { get; set; }

	[JsonProperty("a")]
	public BitunixBookLevel[] Asks { get; set; }
}

sealed class BitunixWsTrade
{
	[JsonProperty("t")]
	[JsonConverter(typeof(BitunixDateTimeOffsetConverter))]
	public DateTimeOffset? Time { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("s")]
	public string Side { get; set; }
}

sealed class BitunixWsCandle
{
	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("b")]
	public decimal BaseVolume { get; set; }

	[JsonProperty("q")]
	public decimal QuoteVolume { get; set; }
}

sealed class BitunixWsBalance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("frozen")]
	public decimal Frozen { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("expMoney")]
	public decimal ExperienceMoney { get; set; }
}

sealed class BitunixWsPosition
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("positionType")]
	public string PositionType { get; set; }

	[JsonProperty("marginMode")]
	public string MarginMode { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("entryValue")]
	public decimal EntryValue { get; set; }

	[JsonProperty("ctime")]
	[JsonConverter(typeof(BitunixDateTimeOffsetConverter))]
	public DateTimeOffset? CreateTime { get; set; }

	[JsonProperty("mtime")]
	[JsonConverter(typeof(BitunixDateTimeOffsetConverter))]
	public DateTimeOffset? UpdateTime { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("realizedPNL")]
	public decimal RealizedPnl { get; set; }

	[JsonProperty("unrealizedPNL")]
	public decimal UnrealizedPnl { get; set; }

	[JsonProperty("funding")]
	public decimal Funding { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }
}

sealed class BitunixWsOrder
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("positionType")]
	public string PositionType { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("effect")]
	public string Effect { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("reductionOnly")]
	public bool IsReductionOnly { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("ctime")]
	[JsonConverter(typeof(BitunixDateTimeOffsetConverter))]
	public DateTimeOffset? CreateTime { get; set; }

	[JsonProperty("mtime")]
	[JsonConverter(typeof(BitunixDateTimeOffsetConverter))]
	public DateTimeOffset? UpdateTime { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }
}

sealed class BitunixDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
	public override DateTimeOffset? ReadJson(JsonReader reader, Type objectType,
		DateTimeOffset? existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType == JsonToken.Date)
		{
			return reader.Value switch
			{
				DateTimeOffset value => value.ToUniversalTime(),
				DateTime value => new DateTimeOffset(value.ToUniversalTime()),
				_ => throw new JsonSerializationException("Invalid Bitunix timestamp."),
			};
		}
		if (reader.TokenType is JsonToken.Integer or JsonToken.Float)
			return FromUnix(Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture));
		if (reader.TokenType == JsonToken.String)
		{
			var text = reader.Value?.ToString();
			if (text.IsEmpty())
				return null;
			if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
				return FromUnix(numeric);
			if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
				return time;
		}
		throw new JsonSerializationException("Invalid Bitunix timestamp.");
	}

	public override void WriteJson(JsonWriter writer, DateTimeOffset? value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;

	private static DateTimeOffset FromUnix(long value)
		=> value >= 100000000000L
			? DateTimeOffset.FromUnixTimeMilliseconds(value)
			: DateTimeOffset.FromUnixTimeSeconds(value);
}

static class BitunixJson
{
	public static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read())
			throw new JsonSerializationException($"Unexpected end of Bitunix {field}.");
		if (reader.TokenType is not (JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"Bitunix {field} must be numeric.");
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}

	public static void SkipArrayTail(JsonReader reader, string field)
	{
		while (reader.Read())
		{
			if (reader.TokenType == JsonToken.EndArray)
				return;
			reader.Skip();
		}
		throw new JsonSerializationException($"Unexpected end of Bitunix {field}.");
	}
}

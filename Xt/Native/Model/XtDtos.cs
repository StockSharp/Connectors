namespace StockSharp.Xt.Native.Model;

sealed class XtSpotResponse<TData>
{
	[JsonProperty("rc")]
	public int Code { get; set; }

	[JsonProperty("mc")]
	public string Message { get; set; }

	[JsonProperty("result")]
	public TData Result { get; set; }

	public bool IsSuccess => Code == 0;
}

sealed class XtFuturesResponse<TData>
{
	[JsonProperty("returnCode")]
	public int Code { get; set; }

	[JsonProperty("msgInfo")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public XtFuturesError Error { get; set; }

	[JsonProperty("result")]
	public TData Result { get; set; }

	public bool IsSuccess => Code == 0 && Error?.Code.IsEmpty() != false;
}

sealed class XtFuturesError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class XtEmptyResult
{
}

sealed class XtSpotSymbolsResult
{
	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("symbols")]
	public XtSymbol[] Symbols { get; set; }
}

sealed class XtSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("displayName")]
	public string Name { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("tradingEnabled")]
	public bool IsTradingEnabled { get; set; }

	[JsonProperty("openapiEnabled")]
	public bool IsOpenApiEnabled { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("baseCurrencyPrecision")]
	public int BasePrecision { get; set; }

	[JsonProperty("quoteCurrencyPrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("quantityPrecision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("orderTypes")]
	public string[] OrderTypes { get; set; }

	[JsonProperty("timeInForces")]
	public string[] TimeInForces { get; set; }

	[JsonProperty("filters")]
	public XtSymbolFilter[] Filters { get; set; }

	public bool IsEnabled => IsTradingEnabled && IsOpenApiEnabled && State.EqualsIgnoreCase("ONLINE");

	public string MinTradeSize => Filters?
		.FirstOrDefault(static filter => filter.Filter.EqualsIgnoreCase("QUANTITY"))?.Min;
}

sealed class XtSymbolFilter
{
	[JsonProperty("filter")]
	public string Filter { get; set; }

	[JsonProperty("min")]
	public string Min { get; set; }

	[JsonProperty("max")]
	public string Max { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }

	[JsonProperty("maxDeviation")]
	public string MaxDeviation { get; set; }

	[JsonProperty("avgPriceMins")]
	public int? AveragePriceMinutes { get; set; }
}

sealed class XtFuturesSymbolsResult
{
	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("symbols")]
	public XtFuturesSymbol[] Symbols { get; set; }
}

sealed class XtFuturesSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("contractType")]
	public string ContractType { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("baseCoin")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCoin")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("baseCoinPrecision")]
	public int BasePrecision { get; set; }

	[JsonProperty("pricePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("quantityPrecision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("contractSize")]
	public string ContractSize { get; set; }

	[JsonProperty("minStepPrice")]
	public string QuoteStep { get; set; }

	[JsonProperty("minQty")]
	public string MinSizeLimit { get; set; }

	[JsonProperty("maxNotional")]
	public string MaxNotional { get; set; }

	[JsonProperty("minNotional")]
	public string MinNotional { get; set; }

	[JsonProperty("supportOrderType")]
	public string SupportedOrderTypes { get; set; }

	[JsonProperty("supportTimeInForce")]
	public string SupportedTimeInForce { get; set; }

	[JsonProperty("state")]
	public int State { get; set; }

	[JsonProperty("tradeSwitch")]
	public bool IsTradingEnabled { get; set; }

	[JsonProperty("isOpenApi")]
	public bool IsOpenApiEnabled { get; set; }

	public string Name => Symbol;
	public string Status => State == 0 && IsTradingEnabled && IsOpenApiEnabled ? "TRADING" : "DISABLED";
	public string BaseStep => QuantityPrecision.PrecisionToStep().ToString(CultureInfo.InvariantCulture);
}

sealed class XtTicker
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("o")]
	public string Open { get; set; }

	[JsonProperty("c")]
	public string Close { get; set; }

	[JsonProperty("h")]
	public string High { get; set; }

	[JsonProperty("l")]
	public string Low { get; set; }

	[JsonProperty("q")]
	public string SpotVolume { get; set; }

	[JsonProperty("a")]
	public string FuturesVolume { get; set; }

	[JsonProperty("v")]
	public string Turnover { get; set; }

	[JsonProperty("i")]
	public string IndexPrice { get; set; }

	[JsonProperty("m")]
	public string MarkPrice { get; set; }

	[JsonProperty("bp")]
	public string BidPrice { get; set; }

	[JsonProperty("bq")]
	public string BidSize { get; set; }

	[JsonProperty("ap")]
	public string AskPrice { get; set; }

	[JsonProperty("aq")]
	public string AskSize { get; set; }

	public string Volume => SpotVolume.IsEmpty(FuturesVolume);
}

sealed class XtBookTicker
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("bp")]
	public string BidPrice { get; set; }

	[JsonProperty("bq")]
	public string BidSize { get; set; }

	[JsonProperty("ap")]
	public string AskPrice { get; set; }

	[JsonProperty("aq")]
	public string AskSize { get; set; }
}

sealed class XtMarketTrade
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("i")]
	public string TradeId { get; set; }

	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("q")]
	public string SpotSize { get; set; }

	[JsonProperty("a")]
	public string FuturesSize { get; set; }

	[JsonProperty("m")]
	public string FuturesSide { get; set; }

	[JsonProperty("b")]
	public bool? IsBuyerMaker { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	public string Size => SpotSize.IsEmpty(FuturesSize);
	public string Side => FuturesSide.IsEmpty()
		? IsBuyerMaker == true ? "SELL" : "BUY"
		: FuturesSide.EqualsIgnoreCase("BID") ? "BUY" : "SELL";
}

[JsonConverter(typeof(XtBookLevelConverter))]
sealed class XtBookLevel
{
	public string Price { get; set; }
	public string Size { get; set; }
}

sealed class XtBookLevelConverter : JsonConverter<XtBookLevel>
{
	public override XtBookLevel ReadJson(JsonReader reader, Type objectType,
		XtBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("XT.COM order-book level must be a JSON array.");

		var level = new XtBookLevel
		{
			Price = Read<string>(reader, serializer),
			Size = Read<string>(reader, serializer),
		};
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			reader.Skip();
		if (reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("XT.COM order-book level is incomplete.");
		return level;
	}

	private static T Read<T>(JsonReader reader, JsonSerializer serializer)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException("XT.COM order-book level is incomplete.");
		return serializer.Deserialize<T>(reader);
	}

	public override void WriteJson(JsonWriter writer, XtBookLevel value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class XtDepthData
{
	[JsonProperty("symbol")]
	public string SpotSymbol { get; set; }

	[JsonProperty("s")]
	public string FuturesSymbol { get; set; }

	[JsonProperty("timestamp")]
	public long SpotTime { get; set; }

	[JsonProperty("t")]
	public long FuturesTime { get; set; }

	[JsonProperty("lastUpdateId")]
	public long SpotUpdateId { get; set; }

	[JsonProperty("u")]
	public long FuturesUpdateId { get; set; }

	[JsonProperty("id")]
	public string FuturesStreamUpdateId { get; set; }

	[JsonProperty("bids")]
	public XtBookLevel[] SpotBids { get; set; }

	[JsonProperty("b")]
	public XtBookLevel[] FuturesBids { get; set; }

	[JsonProperty("asks")]
	public XtBookLevel[] SpotAsks { get; set; }

	[JsonProperty("a")]
	public XtBookLevel[] FuturesAsks { get; set; }

	public string Symbol => SpotSymbol.IsEmpty(FuturesSymbol);
	public long UpdateTime => SpotTime > 0 ? SpotTime : FuturesTime;
	public XtBookLevel[] Bids => SpotBids ?? FuturesBids;
	public XtBookLevel[] Asks => SpotAsks ?? FuturesAsks;
}

sealed class XtKline
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("o")]
	public string Open { get; set; }

	[JsonProperty("c")]
	public string Close { get; set; }

	[JsonProperty("h")]
	public string High { get; set; }

	[JsonProperty("l")]
	public string Low { get; set; }

	[JsonProperty("q")]
	public string SpotVolume { get; set; }

	[JsonProperty("a")]
	public string FuturesVolume { get; set; }

	[JsonProperty("v")]
	public string Turnover { get; set; }

	public string Volume => SpotVolume.IsEmpty(FuturesVolume);
}

sealed class XtSpotBalancesResult
{
	[JsonProperty("totalBtcAmount")]
	public string TotalBtcAmount { get; set; }

	[JsonProperty("assets")]
	public XtBalance[] Balances { get; set; }
}

sealed class XtBalance
{
	[JsonProperty("currency")]
	public string SpotCoin { get; set; }

	[JsonProperty("coin")]
	public string FuturesCoin { get; set; }

	[JsonProperty("availableAmount")]
	public string SpotAvailable { get; set; }

	[JsonProperty("walletBalance")]
	public string FuturesWallet { get; set; }

	[JsonProperty("frozenAmount")]
	public string SpotFrozen { get; set; }

	[JsonProperty("openOrderMarginFrozen")]
	public string FuturesFrozen { get; set; }

	[JsonProperty("totalAmount")]
	public string Total { get; set; }

	public string Coin => SpotCoin.IsEmpty(FuturesCoin);
	public string Free => SpotAvailable.IsEmpty(FuturesWallet);
	public string Frozen => SpotFrozen.IsEmpty(FuturesFrozen);
}

sealed class XtPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("positionType")]
	public string PositionType { get; set; }

	[JsonProperty("positionSize")]
	public string NetSize { get; set; }

	[JsonProperty("entryPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("floatingPL")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("breakPrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("calMarkPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("realizedProfit")]
	public string RealizedPnl { get; set; }

	[JsonProperty("availableCloseSize")]
	public string AvailableCloseSize { get; set; }

	[JsonProperty("updatedTime")]
	public long UpdateTime { get; set; }

	public string PositionId => $"{PositionType}:{PositionSide}";
	public string LongSize => PositionSide.EqualsIgnoreCase("LONG") ? NetSize : null;
	public string ShortSize => PositionSide.EqualsIgnoreCase("SHORT") ? NetSize : null;
}

sealed class XtOrderPage
{
	[JsonProperty("hasPrev")]
	public bool HasPrevious { get; set; }

	[JsonProperty("hasNext")]
	public bool HasNext { get; set; }

	[JsonProperty("items")]
	public XtOrder[] Items { get; set; }
}

sealed class XtFillPage
{
	[JsonProperty("items")]
	public XtFill[] Items { get; set; }

	[JsonProperty("page")]
	public int Page { get; set; }

	[JsonProperty("ps")]
	public int PageSize { get; set; }

	[JsonProperty("total")]
	public long Total { get; set; }
}

[JsonConverter(typeof(XtOrderResultConverter))]
sealed class XtOrderResult
{
	public long OrderId { get; set; }
	public string ClientOrderId { get; set; }
}

sealed class XtOrderResultConverter : JsonConverter<XtOrderResult>
{
	public override XtOrderResult ReadJson(JsonReader reader, Type objectType,
		XtOrderResult existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		var result = existingValue ?? new XtOrderResult();
		if (reader.TokenType is JsonToken.String or JsonToken.Integer)
		{
			long.TryParse(reader.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
				out var orderId);
			result.OrderId = orderId;
			return result;
		}
		if (reader.TokenType == JsonToken.Null)
			return result;
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("XT.COM order result has an invalid JSON shape.");

		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				continue;
			var name = reader.Value?.ToString();
			if (!reader.Read())
				break;
			if (name.EqualsIgnoreCase("orderId"))
			{
				long.TryParse(reader.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
					out var orderId);
				result.OrderId = orderId;
			}
			else if (name.EqualsIgnoreCase("clientOrderId"))
				result.ClientOrderId = reader.Value?.ToString();
			else
				reader.Skip();
		}
		return result;
	}

	public override void WriteJson(JsonWriter writer, XtOrderResult value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class XtOrder
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string SpotSide { get; set; }

	[JsonProperty("orderSide")]
	public string FuturesSide { get; set; }

	[JsonProperty("type")]
	public string SpotType { get; set; }

	[JsonProperty("orderType")]
	public string FuturesType { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("origQty")]
	public string OriginalSize { get; set; }

	[JsonProperty("executedQty")]
	public string FilledSize { get; set; }

	[JsonProperty("origQuoteQty")]
	public string OriginalAmount { get; set; }

	[JsonProperty("tradeQuote")]
	public string FilledAmount { get; set; }

	[JsonProperty("avgPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeCurrency")]
	public string SpotFeeCoin { get; set; }

	[JsonProperty("feeCoin")]
	public string FuturesFeeCoin { get; set; }

	[JsonProperty("state")]
	public string Status { get; set; }

	[JsonProperty("time")]
	public long SpotCreateTime { get; set; }

	[JsonProperty("createdTime")]
	public long FuturesCreateTime { get; set; }

	[JsonProperty("updatedTime")]
	public long UpdateTime { get; set; }

	public string Side => SpotSide.IsEmpty(FuturesSide);
	public string Type => SpotType.IsEmpty(FuturesType);
	public string Size => OriginalSize;
	public string FeeCoin => SpotFeeCoin.IsEmpty(FuturesFeeCoin);
	public long CreateTime => SpotCreateTime > 0 ? SpotCreateTime : FuturesCreateTime;
	public bool IsImmediateOrCancel => TimeInForce.EqualsIgnoreCase("IOC");
	public bool IsReduceOnly { get; set; }
}

sealed class XtFill
{
	[JsonProperty("tradeId")]
	public long SpotId { get; set; }

	[JsonProperty("execId")]
	public long FuturesId { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderSide")]
	public string SpotSide { get; set; }

	[JsonProperty("side")]
	public string SideAlias { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Size { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeCurrency")]
	public string SpotFeeCoin { get; set; }

	[JsonProperty("feeCoin")]
	public string FuturesFeeCoin { get; set; }

	[JsonProperty("time")]
	public long SpotTimestamp { get; set; }

	[JsonProperty("timestamp")]
	public long FuturesTimestamp { get; set; }

	public long Id => SpotId > 0 ? SpotId : FuturesId;
	public string Side => SpotSide.IsEmpty(SideAlias);
	public string FeeCoin => SpotFeeCoin.IsEmpty(FuturesFeeCoin);
	public long Timestamp => SpotTimestamp > 0 ? SpotTimestamp : FuturesTimestamp;
}

sealed class XtSpotOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; init; }

	[JsonProperty("bizType")]
	public string BusinessType { get; init; } = "SPOT";

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("quoteQty")]
	public string QuoteQuantity { get; init; }
}

sealed class XtFuturesOrderRequest
{
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("orderSide")]
	public string Side { get; init; }

	[JsonProperty("orderType")]
	public string Type { get; init; }

	[JsonProperty("origQty")]
	public string Quantity { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; init; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; init; }
}

sealed class XtFuturesSymbolRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class XtFuturesCancelRequest
{
	[JsonProperty("orderId")]
	public long OrderId { get; init; }
}

sealed class XtSpotCancelAllRequest
{
	[JsonProperty("bizType")]
	public string BusinessType { get; init; } = "SPOT";

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class XtWsTokenResult
{
	[JsonProperty("accessToken")]
	public string AccessToken { get; set; }

	[JsonProperty("refreshToken")]
	public string RefreshToken { get; set; }
}

[JsonConverter(typeof(XtListenKeyResultConverter))]
sealed class XtListenKeyResult
{
	public string ListenKey { get; set; }
}

sealed class XtListenKeyResultConverter : JsonConverter<XtListenKeyResult>
{
	public override XtListenKeyResult ReadJson(JsonReader reader, Type objectType,
		XtListenKeyResult existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		var result = existingValue ?? new XtListenKeyResult();
		if (reader.TokenType is JsonToken.String or JsonToken.Integer)
		{
			result.ListenKey = reader.Value?.ToString();
			return result;
		}
		if (reader.TokenType == JsonToken.Null)
			return result;
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("XT.COM listen-key result has an invalid JSON shape.");
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				continue;
			var name = reader.Value?.ToString();
			if (!reader.Read())
				break;
			if (name.EqualsIgnoreCase("listenKey") || name.EqualsIgnoreCase("accessToken"))
				result.ListenKey = reader.Value?.ToString();
			else
				reader.Skip();
		}
		return result;
	}

	public override void WriteJson(JsonWriter writer, XtListenKeyResult value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class XtWsResponse
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("event")]
	public string Event { get; set; }
}

sealed class XtWsSubscriptionCommand
{
	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public string[] Parameters { get; init; }

	[JsonProperty("listenKey")]
	public string ListenKey { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }
}

sealed class XtWsEnvelope<TData>
	where TData : class
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class XtSpotWsOrder
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long UpdateTime { get; set; }

	[JsonProperty("ct")]
	public long CreateTime { get; set; }

	[JsonProperty("i")]
	public long OrderId { get; set; }

	[JsonProperty("ci")]
	public string ClientOrderId { get; set; }

	[JsonProperty("st")]
	public string State { get; set; }

	[JsonProperty("sd")]
	public string Side { get; set; }

	[JsonProperty("tp")]
	public string Type { get; set; }

	[JsonProperty("oq")]
	public string OriginalQuantity { get; set; }

	[JsonProperty("oqq")]
	public string OriginalQuoteQuantity { get; set; }

	[JsonProperty("eq")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("ap")]
	public string AveragePrice { get; set; }

	[JsonProperty("f")]
	public string Fee { get; set; }
}

sealed class XtSpotWsBalance
{
	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("c")]
	public string Coin { get; set; }

	[JsonProperty("b")]
	public string Total { get; set; }

	[JsonProperty("f")]
	public string Frozen { get; set; }

	[JsonProperty("z")]
	public string BusinessType { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }
}

sealed class XtSpotWsFill
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("i")]
	public long TradeId { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("oi")]
	public long OrderId { get; set; }

	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("q")]
	public string Quantity { get; set; }

	[JsonProperty("b")]
	public bool IsBuyerMaker { get; set; }
}

sealed class XtFuturesWsOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("origQty")]
	public string OriginalQuantity { get; set; }

	[JsonProperty("avgPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("executedQty")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("orderSide")]
	public string Side { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("createdTime")]
	public long CreateTime { get; set; }

	[JsonProperty("orderType")]
	public string Type { get; set; }
}

sealed class XtFuturesWsFill
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderSide")]
	public string Side { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }
}

sealed class XtFuturesWsBalance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("walletBalance")]
	public string WalletBalance { get; set; }

	[JsonProperty("openOrderMarginFrozen")]
	public string Frozen { get; set; }
}

sealed class XtWsTradeMessage
{
	public string Symbol { get; init; }
	public XtMarketTrade[] Data { get; init; }
}

sealed class XtWsDepthMessage
{
	public string Symbol { get; init; }
	public long Timestamp { get; init; }
	public XtDepthData Data { get; init; }
}

sealed class XtWsIndexMessage
{
	public string Symbol { get; init; }
	public XtWsIndex[] Data { get; init; }
}

sealed class XtWsIndex
{
	public string Symbol { get; init; }
	public string IndexPrice { get; init; }
	public string MarkPrice { get; init; }
	public long UpdateTime { get; init; }
}

sealed class XtWsOrderMessage
{
	public string Symbol { get; init; }
	public long Timestamp { get; init; }
	public XtOrder Data { get; init; }
}

sealed class XtWsFillMessage
{
	public string Symbol { get; init; }
	public long Timestamp { get; init; }
	public XtFill Data { get; init; }
}

sealed class XtWsBalanceMessage
{
	public long Timestamp { get; init; }
	public XtWsBalanceData Data { get; init; }
}

sealed class XtWsBalanceData
{
	public string Type { get; init; }
	public string Symbol { get; init; }
	public XtBalance[] Balances { get; init; }
	public long Timestamp { get; init; }
}

sealed class XtWsPositionMessage
{
	public string Symbol { get; init; }
	public long Timestamp { get; init; }
	public XtPosition Data { get; init; }
}

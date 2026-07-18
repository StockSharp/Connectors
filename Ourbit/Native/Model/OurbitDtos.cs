namespace StockSharp.Ourbit.Native.Model;

readonly record struct OurbitParameter(string Name, string Value);

interface IOurbitParameters
{
	OurbitParameter[] GetParameters();
}

sealed class OurbitEmptyRequest : IOurbitParameters
{
	public OurbitParameter[] GetParameters() => [];
}

sealed class OurbitSymbolRequest : IOurbitParameters
{
	public string Symbol { get; init; }

	public OurbitParameter[] GetParameters() =>
		[new("symbol", Symbol)];
}

sealed class OurbitDepthRequest : IOurbitParameters
{
	public string Symbol { get; init; }
	public int Limit { get; init; }

	public OurbitParameter[] GetParameters() =>
	[
		new("symbol", Symbol),
		new("limit", Limit.ToString(CultureInfo.InvariantCulture)),
	];
}

sealed class OurbitSpotHistoryRequest : IOurbitParameters
{
	public string Symbol { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int? Limit { get; init; }
	public string Interval { get; init; }
	public string OrderId { get; init; }

	public OurbitParameter[] GetParameters() =>
	[
		new("symbol", Symbol),
		new("interval", Interval),
		new("orderId", OrderId),
		new("startTime", StartTime?.ToString(CultureInfo.InvariantCulture)),
		new("endTime", EndTime?.ToString(CultureInfo.InvariantCulture)),
		new("limit", Limit?.ToString(CultureInfo.InvariantCulture)),
	];
}

sealed class OurbitSpotOrderRequest : IOurbitParameters
{
	public string Symbol { get; init; }
	public string Side { get; init; }
	public string Type { get; init; }
	public string Quantity { get; init; }
	public string Price { get; init; }
	public string ClientOrderId { get; init; }

	public OurbitParameter[] GetParameters() =>
	[
		new("symbol", Symbol),
		new("side", Side),
		new("type", Type),
		new("quantity", Quantity),
		new("price", Price),
		new("newClientOrderId", ClientOrderId),
	];
}

sealed class OurbitSpotCancelRequest : IOurbitParameters
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }

	public OurbitParameter[] GetParameters() =>
	[
		new("symbol", Symbol),
		new("orderId", OrderId),
		new("origClientOrderId", ClientOrderId),
	];
}

sealed class OurbitSpotListenKeyRequest : IOurbitParameters
{
	public string ListenKey { get; init; }

	public OurbitParameter[] GetParameters() =>
		[new("listenKey", ListenKey)];
}

sealed class OurbitSpotExchangeInfo
{
	[JsonProperty("timezone")]
	public string TimeZone { get; set; }

	[JsonProperty("serverTime")]
	public long ServerTime { get; set; }

	[JsonProperty("symbols")]
	public OurbitSpotSymbol[] Symbols { get; set; }
}

sealed class OurbitSpotSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("baseAssetPrecision")]
	public int BaseAssetPrecision { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("quoteAssetPrecision")]
	public int QuoteAssetPrecision { get; set; }

	[JsonProperty("orderTypes")]
	public string[] OrderTypes { get; set; }

	[JsonProperty("isSpotTradingAllowed")]
	public bool? IsSpotTradingAllowed { get; set; }

	[JsonProperty("permissions")]
	public string[] Permissions { get; set; }

	[JsonProperty("maxQuoteAmount")]
	public string MaximumQuoteAmount { get; set; }
}

[JsonConverter(typeof(OurbitSpotBookLevelConverter))]
sealed class OurbitSpotBookLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
}

sealed class OurbitSpotBookLevelConverter : JsonConverter<OurbitSpotBookLevel>
{
	public override OurbitSpotBookLevel ReadJson(JsonReader reader, Type objectType,
		OurbitSpotBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Ourbit spot book level must be an array.");
		var result = new OurbitSpotBookLevel
		{
			Price = OurbitJson.ReadDecimal(reader, "spot book price"),
			Volume = OurbitJson.ReadDecimal(reader, "spot book volume"),
		};
		OurbitJson.SkipArrayTail(reader, "spot book level");
		return result;
	}

	public override void WriteJson(JsonWriter writer, OurbitSpotBookLevel value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class OurbitSpotDepth
{
	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonProperty("bids")]
	public OurbitSpotBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public OurbitSpotBookLevel[] Asks { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class OurbitSpotTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal Volume { get; set; }

	[JsonProperty("quoteQty")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; set; }

	[JsonProperty("tradeType")]
	public string TradeType { get; set; }
}

[JsonConverter(typeof(OurbitSpotKlineConverter))]
sealed class OurbitSpotKline
{
	public long OpenTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public long CloseTime { get; set; }
	public decimal QuoteVolume { get; set; }
}

sealed class OurbitSpotKlineConverter : JsonConverter<OurbitSpotKline>
{
	public override OurbitSpotKline ReadJson(JsonReader reader, Type objectType,
		OurbitSpotKline existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Ourbit spot candle must be an array.");
		var result = new OurbitSpotKline
		{
			OpenTime = OurbitJson.ReadInt64(reader, "spot candle open time"),
			Open = OurbitJson.ReadDecimal(reader, "spot candle open"),
			High = OurbitJson.ReadDecimal(reader, "spot candle high"),
			Low = OurbitJson.ReadDecimal(reader, "spot candle low"),
			Close = OurbitJson.ReadDecimal(reader, "spot candle close"),
			Volume = OurbitJson.ReadDecimal(reader, "spot candle volume"),
			CloseTime = OurbitJson.ReadInt64(reader, "spot candle close time"),
			QuoteVolume = OurbitJson.ReadDecimal(reader, "spot candle quote volume"),
		};
		OurbitJson.SkipArrayTail(reader, "spot candle");
		return result;
	}

	public override void WriteJson(JsonWriter writer, OurbitSpotKline value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class OurbitSpotTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("lastPrice")]
	public decimal LastPrice { get; set; }

	[JsonProperty("bidPrice")]
	public decimal BidPrice { get; set; }

	[JsonProperty("bidQty")]
	public decimal BidVolume { get; set; }

	[JsonProperty("askPrice")]
	public decimal AskPrice { get; set; }

	[JsonProperty("askQty")]
	public decimal AskVolume { get; set; }

	[JsonProperty("openPrice")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public decimal HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public decimal LowPrice { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("priceChange")]
	public decimal PriceChange { get; set; }

	[JsonProperty("priceChangePercent")]
	public decimal PriceChangePercent { get; set; }

	[JsonProperty("closeTime")]
	public long CloseTime { get; set; }
}

sealed class OurbitSpotBookTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bidPrice")]
	public decimal BidPrice { get; set; }

	[JsonProperty("bidQty")]
	public decimal BidVolume { get; set; }

	[JsonProperty("askPrice")]
	public decimal AskPrice { get; set; }

	[JsonProperty("askQty")]
	public decimal AskVolume { get; set; }
}

sealed class OurbitSpotOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("origQty")]
	public decimal OriginalVolume { get; set; }

	[JsonProperty("executedQty")]
	public decimal ExecutedVolume { get; set; }

	[JsonProperty("cummulativeQuoteQty")]
	public decimal CumulativeQuoteVolume { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class OurbitSpotOrderResult
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("transactTime")]
	public long TransactionTime { get; set; }
}

sealed class OurbitSpotAccount
{
	[JsonProperty("canTrade")]
	public bool IsTradingEnabled { get; set; }

	[JsonProperty("updateTime")]
	public long? UpdateTime { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("balances")]
	public OurbitSpotBalance[] Balances { get; set; }
}

sealed class OurbitSpotBalance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("free")]
	public decimal Available { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }
}

sealed class OurbitSpotFill
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal Volume { get; set; }

	[JsonProperty("commission")]
	public decimal Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("isBuyer")]
	public bool IsBuyer { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }
}

sealed class OurbitSpotListenKey
{
	[JsonProperty("listenKey")]
	public string Value { get; set; }
}

sealed class OurbitSpotError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class OurbitFuturesResponse<TData>
{
	[JsonProperty("success")]
	public bool IsSuccess { get; set; }

	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class OurbitFuturesDetailRequest : IOurbitParameters
{
	public string Symbol { get; init; }

	public OurbitParameter[] GetParameters() =>
	[
		new("client", "web"),
		new("symbol", Symbol),
	];
}

sealed class OurbitFuturesDepthRequest : IOurbitParameters
{
	public string Step { get; init; }

	public OurbitParameter[] GetParameters() => [new("step", Step)];
}

sealed class OurbitFuturesCandleRequest : IOurbitParameters
{
	public string Interval { get; init; }
	public long End { get; init; }

	public OurbitParameter[] GetParameters() =>
	[
		new("interval", Interval),
		new("end", End.ToString(CultureInfo.InvariantCulture)),
	];
}

sealed class OurbitFuturesHistoryRequest : IOurbitParameters
{
	public string Symbol { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int PageNumber { get; init; } = 1;
	public int PageSize { get; init; } = 100;

	public OurbitParameter[] GetParameters() =>
	[
		new("symbol", Symbol),
		new("start_time", StartTime?.ToString(CultureInfo.InvariantCulture)),
		new("end_time", EndTime?.ToString(CultureInfo.InvariantCulture)),
		new("page_num", PageNumber.ToString(CultureInfo.InvariantCulture)),
		new("page_size", PageSize.ToString(CultureInfo.InvariantCulture)),
	];
}

sealed class OurbitFuturesOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("vol")]
	public decimal Volume { get; init; }

	[JsonProperty("leverage")]
	public int Leverage { get; init; }

	[JsonProperty("side")]
	public int Side { get; init; }

	[JsonProperty("type")]
	public int Type { get; init; }

	[JsonProperty("openType")]
	public int OpenType { get; init; }

	[JsonProperty("externalOid")]
	public string ExternalOrderId { get; init; }
}

sealed class OurbitFuturesCancelRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }
}

sealed class OurbitFuturesCancelAllRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class OurbitFuturesProduct
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("dne")]
	public string EnglishName { get; set; }

	[JsonProperty("bc")]
	public string BaseCurrency { get; set; }

	[JsonProperty("qc")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("sc")]
	public string SettleCurrency { get; set; }

	[JsonProperty("cs")]
	public decimal ContractSize { get; set; }

	[JsonProperty("minL")]
	public int MinimumLeverage { get; set; }

	[JsonProperty("maxL")]
	public int MaximumLeverage { get; set; }

	[JsonProperty("ps")]
	public int PriceScale { get; set; }

	[JsonProperty("vs")]
	public int VolumeScale { get; set; }

	[JsonProperty("pu")]
	public decimal PriceUnit { get; set; }

	[JsonProperty("vu")]
	public decimal VolumeUnit { get; set; }

	[JsonProperty("minV")]
	public decimal MinimumVolume { get; set; }

	[JsonProperty("maxV")]
	public decimal MaximumVolume { get; set; }

	[JsonProperty("state")]
	public int State { get; set; }

	[JsonProperty("dsl")]
	public string[] DepthSteps { get; set; }

	[JsonProperty("type")]
	public int Type { get; set; }
}

sealed class OurbitFuturesTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("lastPrice")]
	public decimal LastPrice { get; set; }

	[JsonProperty("bid1")]
	public decimal BidPrice { get; set; }

	[JsonProperty("ask1")]
	public decimal AskPrice { get; set; }

	[JsonProperty("volume24")]
	public decimal Volume { get; set; }

	[JsonProperty("amount24")]
	public decimal Amount { get; set; }

	[JsonProperty("holdVol")]
	public decimal OpenInterest { get; set; }

	[JsonProperty("lower24Price")]
	public decimal LowPrice { get; set; }

	[JsonProperty("high24Price")]
	public decimal HighPrice { get; set; }

	[JsonProperty("riseFallRate")]
	public decimal ChangePercent { get; set; }

	[JsonProperty("riseFallValue")]
	public decimal Change { get; set; }

	[JsonProperty("indexPrice")]
	public decimal IndexPrice { get; set; }

	[JsonProperty("fairPrice")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("fundingRate")]
	public decimal FundingRate { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

[JsonConverter(typeof(OurbitFuturesBookLevelConverter))]
sealed class OurbitFuturesBookLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public int OrderCount { get; set; }
}

sealed class OurbitFuturesBookLevelConverter : JsonConverter<OurbitFuturesBookLevel>
{
	public override OurbitFuturesBookLevel ReadJson(JsonReader reader, Type objectType,
		OurbitFuturesBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Ourbit futures book level must be an array.");
		var result = new OurbitFuturesBookLevel
		{
			Price = OurbitJson.ReadDecimal(reader, "futures book price"),
			Volume = OurbitJson.ReadDecimal(reader, "futures book volume"),
		};
		if (!reader.Read())
			throw new JsonSerializationException("Unexpected end of Ourbit futures book level.");
		if (reader.TokenType != JsonToken.EndArray)
		{
			result.OrderCount = Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
			OurbitJson.SkipArrayTail(reader, "futures book level");
		}
		return result;
	}

	public override void WriteJson(JsonWriter writer, OurbitFuturesBookLevel value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class OurbitFuturesDepth
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("asks")]
	public OurbitFuturesBookLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public OurbitFuturesBookLevel[] Bids { get; set; }

	[JsonProperty("version")]
	public long Version { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class OurbitFuturesTrade
{
	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("T")]
	public int Side { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }
}

sealed class OurbitFuturesCandles
{
	[JsonProperty("time")]
	public long[] Times { get; set; }

	[JsonProperty("open")]
	public decimal[] Opens { get; set; }

	[JsonProperty("close")]
	public decimal[] Closes { get; set; }

	[JsonProperty("high")]
	public decimal[] Highs { get; set; }

	[JsonProperty("low")]
	public decimal[] Lows { get; set; }

	[JsonProperty("vol")]
	public decimal[] Volumes { get; set; }

	[JsonProperty("amount")]
	public decimal[] Amounts { get; set; }

	public IEnumerable<OurbitFuturesCandle> ToCandles()
	{
		var count = new[] { Times?.Length ?? 0, Opens?.Length ?? 0, Closes?.Length ?? 0,
			Highs?.Length ?? 0, Lows?.Length ?? 0, Volumes?.Length ?? 0 }.Min();
		for (var i = 0; i < count; i++)
			yield return new()
			{
				Time = Times[i],
				Open = Opens[i],
				Close = Closes[i],
				High = Highs[i],
				Low = Lows[i],
				Volume = Volumes[i],
				Amount = Amounts is { Length: > 0 } && i < Amounts.Length ? Amounts[i] : 0m,
			};
	}
}

sealed class OurbitFuturesCandle
{
	public long Time { get; set; }
	public decimal Open { get; set; }
	public decimal Close { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Volume { get; set; }
	public decimal Amount { get; set; }
}

sealed class OurbitFuturesBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("availableBalance")]
	public decimal AvailableBalance { get; set; }

	[JsonProperty("frozenBalance")]
	public decimal FrozenBalance { get; set; }

	[JsonProperty("cashBalance")]
	public decimal CashBalance { get; set; }

	[JsonProperty("equity")]
	public decimal Equity { get; set; }

	[JsonProperty("unrealized")]
	public decimal UnrealizedPnl { get; set; }
}

sealed class OurbitFuturesPosition
{
	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("positionType")]
	public int PositionType { get; set; }

	[JsonProperty("openType")]
	public int OpenType { get; set; }

	[JsonProperty("holdVol")]
	public decimal Volume { get; set; }

	[JsonProperty("holdAvgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("openAvgPrice")]
	public decimal OpenAveragePrice { get; set; }

	[JsonProperty("liquidatePrice")]
	public decimal LiquidationPrice { get; set; }

	[JsonProperty("realised")]
	public decimal RealizedPnl { get; set; }

	[JsonProperty("unrealised")]
	public decimal UnrealizedPnl { get; set; }

	[JsonProperty("leverage")]
	public decimal Leverage { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class OurbitFuturesOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("vol")]
	public decimal Volume { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("side")]
	public int Side { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("dealAvgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("dealVol")]
	public decimal FilledVolume { get; set; }

	[JsonProperty("takerFee")]
	public decimal TakerFee { get; set; }

	[JsonProperty("makerFee")]
	public decimal MakerFee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("openType")]
	public int OpenType { get; set; }

	[JsonProperty("state")]
	public int State { get; set; }

	[JsonProperty("externalOid")]
	public string ExternalOrderId { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class OurbitFuturesFill
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public int Side { get; set; }

	[JsonProperty("vol")]
	public decimal Volume { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("taker")]
	public bool IsTaker { get; set; }
}

sealed class OurbitSpotWsCommand
{
	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public string[] Parameters { get; init; }

	[JsonProperty("id")]
	public long? Id { get; init; }
}

sealed class OurbitSpotWsHeader
{
	[JsonProperty("c")]
	public string Channel { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("method")]
	public string Method { get; set; }
}

sealed class OurbitSpotWsEnvelope<TData>
{
	[JsonProperty("c")]
	public string Channel { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("d")]
	public TData Data { get; set; }
}

sealed class OurbitSpotWsBookTicker
{
	[JsonProperty("b")]
	public decimal BidPrice { get; set; }

	[JsonProperty("B")]
	public decimal BidVolume { get; set; }

	[JsonProperty("a")]
	public decimal AskPrice { get; set; }

	[JsonProperty("A")]
	public decimal AskVolume { get; set; }
}

sealed class OurbitSpotWsDepth
{
	[JsonProperty("bids")]
	public OurbitSpotWsBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public OurbitSpotWsBookLevel[] Asks { get; set; }

	[JsonProperty("r")]
	public long Revision { get; set; }
}

sealed class OurbitSpotWsBookLevel
{
	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }
}

sealed class OurbitSpotWsTrades
{
	[JsonProperty("deals")]
	public OurbitSpotWsTrade[] Trades { get; set; }
}

sealed class OurbitSpotWsTrade
{
	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("S")]
	public int Side { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }
}

sealed class OurbitSpotWsKlineContainer
{
	[JsonProperty("k")]
	public OurbitSpotWsKline Candle { get; set; }
}

sealed class OurbitSpotWsKline
{
	[JsonProperty("t")]
	public long OpenTime { get; set; }

	[JsonProperty("T")]
	public long CloseTime { get; set; }

	[JsonProperty("i")]
	public string Interval { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("a")]
	public decimal Amount { get; set; }
}

sealed class OurbitSpotWsPrivateAccount
{
	[JsonProperty("vcoinName")]
	public string Asset { get; set; }

	[JsonProperty("balanceAmount")]
	public decimal Balance { get; set; }

	[JsonProperty("frozenAmount")]
	public decimal Frozen { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }
}

sealed class OurbitSpotWsPrivateOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Volume { get; set; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("tradeType")]
	public int Side { get; set; }

	[JsonProperty("remainQuantity")]
	public decimal RemainingVolume { get; set; }

	[JsonProperty("cumulativeQuantity")]
	public decimal FilledVolume { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }
}

sealed class OurbitSpotWsPrivateFill
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Volume { get; set; }

	[JsonProperty("tradeType")]
	public int Side { get; set; }

	[JsonProperty("feeAmount")]
	public decimal Commission { get; set; }

	[JsonProperty("feeCurrency")]
	public string CommissionAsset { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }
}

sealed class OurbitFuturesWsCommand
{
	[JsonProperty("subscribe")]
	public bool? IsSubscribe { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("param")]
	public OurbitFuturesWsParameters Parameters { get; init; }
}

sealed class OurbitFuturesWsParameters
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("compress")]
	public bool? IsCompressed { get; init; }

	[JsonProperty("interval")]
	public string Interval { get; init; }

	[JsonProperty("apiKey")]
	public string ApiKey { get; init; }

	[JsonProperty("reqTime")]
	public string RequestTime { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("filters")]
	public OurbitFuturesWsFilter[] Filters { get; init; }
}

sealed class OurbitFuturesWsFilter
{
	[JsonProperty("filter")]
	public string Name { get; init; }
}

sealed class OurbitFuturesWsHeader
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ts")]
	public long Time { get; set; }
}

sealed class OurbitFuturesWsEnvelope<TData>
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ts")]
	public long Time { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class OurbitFuturesWsReply
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }

	[JsonProperty("ts")]
	public long Time { get; set; }
}

sealed class OurbitFuturesWsCandle
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("q")]
	public decimal Volume { get; set; }

	[JsonProperty("a")]
	public decimal Amount { get; set; }
}

static class OurbitJson
{
	public static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read())
			throw new JsonSerializationException($"Unexpected end of Ourbit {field}.");
		if (reader.TokenType is not (JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"Ourbit {field} must be numeric.");
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}

	public static long ReadInt64(JsonReader reader, string field)
	{
		if (!reader.Read())
			throw new JsonSerializationException($"Unexpected end of Ourbit {field}.");
		if (reader.TokenType is not (JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"Ourbit {field} must be an integer.");
		return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
	}

	public static void SkipArrayTail(JsonReader reader, string field)
	{
		while (reader.Read())
		{
			if (reader.TokenType == JsonToken.EndArray)
				return;
			reader.Skip();
		}
		throw new JsonSerializationException($"Unexpected end of Ourbit {field}.");
	}
}

namespace StockSharp.Pionex.Native.Model;

sealed class PionexResponse<TData>
	where TData : class
{
	[JsonProperty("result")]
	public bool IsSuccess { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class PionexEmptyData
{
}

sealed class PionexSymbolsData<TSymbol>
	where TSymbol : class
{
	[JsonProperty("symbols")]
	public TSymbol[] Symbols { get; set; }
}

sealed class PionexSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("basePrecision")]
	public int BasePrecision { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("amountPrecision")]
	public int AmountPrecision { get; set; }

	[JsonProperty("minNotional")]
	public string MinNotional { get; set; }

	[JsonProperty("minAmount")]
	public string MinAmount { get; set; }

	[JsonProperty("minTradeSize")]
	public string MinTradeSize { get; set; }

	[JsonProperty("maxTradeSize")]
	public string MaxTradeSize { get; set; }

	[JsonProperty("enable")]
	public bool IsEnabled { get; set; }
}

sealed class PionexFuturesSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("contractType")]
	public string ContractType { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("basePrecision")]
	public int BasePrecision { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("minNotional")]
	public string MinNotional { get; set; }

	[JsonProperty("baseStep")]
	public string BaseStep { get; set; }

	[JsonProperty("quoteStep")]
	public string QuoteStep { get; set; }

	[JsonProperty("minSizeLimit")]
	public string MinSizeLimit { get; set; }

	[JsonProperty("maxSizeLimit")]
	public string MaxSizeLimit { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class PionexTradesData
{
	[JsonProperty("trades")]
	public PionexMarketTrade[] Trades { get; set; }
}

sealed class PionexMarketTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class PionexTickersData
{
	[JsonProperty("tickers")]
	public PionexTicker[] Tickers { get; set; }
}

sealed class PionexTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("count")]
	public long Count { get; set; }
}

sealed class PionexBookTickersData
{
	[JsonProperty("tickers")]
	public PionexBookTicker[] Tickers { get; set; }
}

sealed class PionexBookTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bidPrice")]
	public string BidPrice { get; set; }

	[JsonProperty("bidSize")]
	public string BidSize { get; set; }

	[JsonProperty("askPrice")]
	public string AskPrice { get; set; }

	[JsonProperty("askSize")]
	public string AskSize { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

[JsonConverter(typeof(PionexBookLevelConverter))]
sealed class PionexBookLevel
{
	public string Price { get; set; }
	public string Size { get; set; }
}

sealed class PionexBookLevelConverter : JsonConverter<PionexBookLevel>
{
	public override PionexBookLevel ReadJson(JsonReader reader, Type objectType,
		PionexBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Pionex order-book level must be a JSON array.");

		var level = new PionexBookLevel
		{
			Price = Read<string>(reader, serializer),
			Size = Read<string>(reader, serializer),
		};
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Pionex order-book level has an invalid number of fields.");
		return level;
	}

	private static T Read<T>(JsonReader reader, JsonSerializer serializer)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException("Pionex order-book level is incomplete.");
		return serializer.Deserialize<T>(reader);
	}

	public override void WriteJson(JsonWriter writer, PionexBookLevel value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class PionexDepthData
{
	[JsonProperty("bids")]
	public PionexBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public PionexBookLevel[] Asks { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class PionexKlinesData
{
	[JsonProperty("klines")]
	public PionexKline[] Klines { get; set; }
}

sealed class PionexKline
{
	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }
}

class PionexBalancesData
{
	[JsonProperty("balances")]
	public PionexBalance[] Balances { get; set; }
}

sealed class PionexFuturesBalancesData : PionexBalancesData
{
	[JsonProperty("isolates")]
	public PionexIsolatedBalance[] Isolates { get; set; }
}

sealed class PionexBalance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("free")]
	public string Free { get; set; }

	[JsonProperty("frozen")]
	public string Frozen { get; set; }

	[JsonProperty("debts")]
	public string Debts { get; set; }
}

sealed class PionexIsolatedBalance
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("isolatedMode")]
	public string IsolatedMode { get; set; }

	[JsonProperty("balances")]
	public PionexBalance[] Balances { get; set; }
}

sealed class PionexPositionsData
{
	[JsonProperty("positions")]
	public PionexPosition[] Positions { get; set; }
}

sealed class PionexPosition
{
	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("isolatedMode")]
	public string IsolatedMode { get; set; }

	[JsonProperty("riskState")]
	public string RiskState { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("netSize")]
	public string NetSize { get; set; }

	[JsonProperty("avgPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("unrealizedPnL")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("sizeLong")]
	public string LongSize { get; set; }

	[JsonProperty("sizeShort")]
	public string ShortSize { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("initialMargin")]
	public string InitialMargin { get; set; }

	[JsonProperty("maintMargin")]
	public string MaintenanceMargin { get; set; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class PionexOrdersData
{
	[JsonProperty("orders")]
	public PionexOrder[] Orders { get; set; }
}

sealed class PionexFillsData
{
	[JsonProperty("fills")]
	public PionexFill[] Fills { get; set; }
}

sealed class PionexOrderResult
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }
}

sealed class PionexOrder
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("isolatedMode")]
	public string IsolatedMode { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("origSize")]
	public string OriginalSize { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("filledSize")]
	public string FilledSize { get; set; }

	[JsonProperty("filledAmount")]
	public string FilledAmount { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeCoin")]
	public string FeeCoin { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("IOC")]
	public bool IsImmediateOrCancel { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class PionexFill
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("role")]
	public string Role { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeCoin")]
	public string FeeCoin { get; set; }

	[JsonProperty("feeType")]
	public string FeeType { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class PionexSpotOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("IOC")]
	public bool? IsImmediateOrCancel { get; init; }
}

sealed class PionexFuturesOrderRequest
{
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }
}

sealed class PionexCancelOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("orderId")]
	public long OrderId { get; init; }
}

sealed class PionexCancelAllOrdersRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class PionexWsHeader
{
	[JsonProperty("op")]
	public string Operation { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("mcode")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class PionexWsSubscriptionCommand
{
	[JsonProperty("op")]
	public string Operation { get; init; }

	[JsonProperty("topic")]
	public string Topic { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("limit")]
	public int? Limit { get; init; }
}

sealed class PionexWsHeartbeat
{
	[JsonProperty("op")]
	public string Operation { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }
}

[JsonConverter(typeof(PionexWsAuthArgumentsConverter))]
sealed class PionexWsAuthArguments
{
	public string ApiKey { get; init; }
	public long Timestamp { get; init; }
	public string Signature { get; init; }
}

sealed class PionexWsAuthArgumentsConverter : JsonConverter<PionexWsAuthArguments>
{
	public override PionexWsAuthArguments ReadJson(JsonReader reader, Type objectType,
		PionexWsAuthArguments existingValue, bool hasExistingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, PionexWsAuthArguments value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		writer.WriteValue(value.ApiKey);
		writer.WriteValue(value.Timestamp);
		writer.WriteValue(value.Signature);
		writer.WriteEndArray();
	}
}

sealed class PionexWsAuthCommand
{
	[JsonProperty("op")]
	public string Operation { get; init; } = "auth";

	[JsonProperty("args")]
	public PionexWsAuthArguments Arguments { get; init; }
}

sealed class PionexWsTradeMessage
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public PionexWsTrade[] Data { get; set; }
}

sealed class PionexWsTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class PionexWsDepthMessage
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public PionexDepthData Data { get; set; }
}

sealed class PionexWsIndexMessage
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public PionexWsIndex[] Data { get; set; }
}

sealed class PionexWsIndex
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("indexPrice")]
	public string IndexPrice { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("nextFundingRate")]
	public string NextFundingRate { get; set; }

	[JsonProperty("nextFundingTime")]
	public long NextFundingTime { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class PionexWsOrderMessage
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public PionexOrder Data { get; set; }
}

sealed class PionexWsFillMessage
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public PionexFill Data { get; set; }
}

sealed class PionexWsBalanceMessage
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public PionexWsBalanceData Data { get; set; }
}

sealed class PionexWsBalanceData
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("balances")]
	public PionexBalance[] Balances { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class PionexWsPositionMessage
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public PionexPosition Data { get; set; }
}

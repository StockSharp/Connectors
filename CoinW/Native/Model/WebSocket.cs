namespace StockSharp.CoinW.Native.Model;

readonly record struct CoinWWsChannel(string Business, string Type, string PairCode, string Interval);

sealed class CoinWWsSubscriptionCommand
{
	[JsonProperty("event")]
	public string Event { get; init; }

	[JsonProperty("params")]
	public CoinWWsSubscriptionParameters Parameters { get; init; }
}

sealed class CoinWWsSubscriptionParameters
{
	[JsonProperty("biz")]
	public string Business { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("pairCode", NullValueHandling = NullValueHandling.Ignore)]
	public string PairCode { get; init; }

	[JsonProperty("interval", NullValueHandling = NullValueHandling.Ignore)]
	public string Interval { get; init; }
}

sealed class CoinWWsLoginCommand
{
	[JsonProperty("event")]
	public string Event { get; init; } = "login";

	[JsonProperty("params")]
	public CoinWWsLoginParameters Parameters { get; init; }
}

sealed class CoinWWsLoginParameters
{
	[JsonProperty("api_key")]
	public string ApiKey { get; init; }

	[JsonProperty("passphrase")]
	public string Passphrase { get; init; }
}

sealed class CoinWWsPingCommand
{
	[JsonProperty("event")]
	public string Event { get; init; } = "ping";
}

sealed class CoinWWsHeader
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("biz")]
	public string Business { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("pairCode")]
	public string PairCode { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("result")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class CoinWWsEnvelope<TData>
{
	[JsonProperty("biz")]
	public string Business { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("pairCode")]
	public string PairCode { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class CoinWWsStringEnvelope
{
	[JsonProperty("data")]
	public string Data { get; set; }
}

sealed class CoinWWsAcknowledgement
{
	[JsonProperty("result")]
	public bool IsSuccess { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class CoinWSpotWsTicker
{
	[JsonProperty("buy")]
	public string BidPrice { get; set; }

	[JsonProperty("sell")]
	public string AskPrice { get; set; }

	[JsonProperty("changePrice")]
	public string PriceChange { get; set; }

	[JsonProperty("changeRate")]
	public string PriceChangePercent { get; set; }

	[JsonProperty("high")]
	public string HighPrice { get; set; }

	[JsonProperty("last")]
	public string LastPrice { get; set; }

	[JsonProperty("low")]
	public string LowPrice { get; set; }

	[JsonProperty("open")]
	public string OpenPrice { get; set; }

	[JsonProperty("vol")]
	public string Volume { get; set; }

	[JsonProperty("volValue")]
	public string QuoteVolume { get; set; }
}

sealed class CoinWFuturesWsTicker
{
	[JsonProperty("changeRate")]
	public string PriceChangePercent { get; set; }

	[JsonProperty("high")]
	public string HighPrice { get; set; }

	[JsonProperty("last")]
	public string LastPrice { get; set; }

	[JsonProperty("low")]
	public string LowPrice { get; set; }

	[JsonProperty("open")]
	public string OpenPrice { get; set; }

	[JsonProperty("vol")]
	public string Volume { get; set; }

	[JsonProperty("volUsdt")]
	public string QuoteVolume { get; set; }
}

sealed class CoinWSpotWsDepth
{
	[JsonProperty("asks")]
	public CoinWBookLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public CoinWBookLevel[] Bids { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("seq")]
	public long Sequence { get; set; }

	[JsonProperty("startSeq")]
	public long FirstSequence { get; set; }

	[JsonProperty("endSeq")]
	public long LastSequence { get; set; }
}

sealed class CoinWFuturesWsDepth
{
	[JsonProperty("asks")]
	public CoinWBookLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public CoinWBookLevel[] Bids { get; set; }

	[JsonProperty("n")]
	public string NativeSymbol { get; set; }

	[JsonProperty("ts")]
	public long Time { get; set; }
}

sealed class CoinWSpotWsTrade
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("seq")]
	public string Id { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("size")]
	public string Volume { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }
}

sealed class CoinWFuturesWsTrade
{
	[JsonProperty("createdDate")]
	public long Time { get; set; }

	[JsonProperty("quantity")]
	public string Volume { get; set; }

	[JsonProperty("piece")]
	public string Contracts { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }
}

[JsonConverter(typeof(CoinWSpotWsCandleConverter))]
sealed class CoinWSpotWsCandle
{
	public long OpenTime { get; set; }
	public string OpenPrice { get; set; }
	public string ClosePrice { get; set; }
	public string HighPrice { get; set; }
	public string LowPrice { get; set; }
	public string Volume { get; set; }
	public string QuoteVolume { get; set; }
}

sealed class CoinWSpotWsCandleConverter : JsonConverter<CoinWSpotWsCandle>
{
	public override CoinWSpotWsCandle ReadJson(JsonReader reader, Type objectType,
		CoinWSpotWsCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("CoinW spot WebSocket candle must be an array.");
		var candle = new CoinWSpotWsCandle
		{
			OpenTime = CoinWJson.ReadInt64(reader, "spot candle time"),
			OpenPrice = CoinWJson.ReadWireString(reader, "spot candle open"),
			ClosePrice = CoinWJson.ReadWireString(reader, "spot candle close"),
			HighPrice = CoinWJson.ReadWireString(reader, "spot candle high"),
			LowPrice = CoinWJson.ReadWireString(reader, "spot candle low"),
			Volume = CoinWJson.ReadWireString(reader, "spot candle volume"),
			QuoteVolume = CoinWJson.ReadWireString(reader, "spot candle quote volume"),
		};
		CoinWJson.RequireArrayEnd(reader, "spot WebSocket candle");
		return candle;
	}

	public override void WriteJson(JsonWriter writer, CoinWSpotWsCandle value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

[JsonConverter(typeof(CoinWFuturesWsCandleConverter))]
sealed class CoinWFuturesWsCandle
{
	public long OpenTime { get; set; }
	public string OpenPrice { get; set; }
	public string HighPrice { get; set; }
	public string LowPrice { get; set; }
	public string ClosePrice { get; set; }
	public string Volume { get; set; }
}

sealed class CoinWFuturesWsCandleConverter : JsonConverter<CoinWFuturesWsCandle>
{
	public override CoinWFuturesWsCandle ReadJson(JsonReader reader, Type objectType,
		CoinWFuturesWsCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("CoinW futures WebSocket candle must be an array.");
		var candle = new CoinWFuturesWsCandle
		{
			OpenTime = CoinWJson.ReadInt64(reader, "futures candle time"),
			OpenPrice = CoinWJson.ReadWireString(reader, "futures candle open"),
			HighPrice = CoinWJson.ReadWireString(reader, "futures candle high"),
			LowPrice = CoinWJson.ReadWireString(reader, "futures candle low"),
			ClosePrice = CoinWJson.ReadWireString(reader, "futures candle close"),
			Volume = CoinWJson.ReadWireString(reader, "futures candle volume"),
		};
		CoinWJson.RequireArrayEnd(reader, "futures WebSocket candle");
		return candle;
	}

	public override void WriteJson(JsonWriter writer, CoinWFuturesWsCandle value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class CoinWSpotWsOrder
{
	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("dealFunds")]
	public string ExecutedValue { get; set; }

	[JsonProperty("type")]
	public string Status { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("product_id")]
	public string Symbol { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("size")]
	public string Volume { get; set; }

	[JsonProperty("remaining_size")]
	public string RemainingVolume { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("dealAvgPrice")]
	public string AveragePrice { get; set; }
}

sealed class CoinWSpotWsBalance
{
	[JsonProperty("available")]
	public string Available { get; set; }

	[JsonProperty("currency")]
	public string Asset { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("hold")]
	public string Held { get; set; }
}

sealed class CoinWFuturesWsOrder
{
	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("instrument")]
	public string NativeSymbol { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("quantityUnit")]
	public int QuantityUnit { get; set; }

	[JsonProperty("baseSize")]
	public string ContractSize { get; set; }

	[JsonProperty("totalPiece")]
	public string TotalContracts { get; set; }

	[JsonProperty("currentPiece")]
	public string CurrentContracts { get; set; }

	[JsonProperty("tradePiece")]
	public string ExecutedContracts { get; set; }

	[JsonProperty("orderPrice")]
	public string Price { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("originalType")]
	public string OriginalType { get; set; }

	[JsonProperty("posType")]
	public string PositionType { get; set; }

	[JsonProperty("positionModel")]
	public int PositionModel { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("createdDate")]
	public long CreatedTime { get; set; }

	[JsonProperty("updatedDate")]
	public long UpdatedTime { get; set; }

	[JsonProperty("thirdOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }
}

sealed class CoinWFuturesWsBalance
{
	[JsonProperty("available")]
	public string Available { get; set; }

	[JsonProperty("currency")]
	public string Asset { get; set; }

	[JsonProperty("margin")]
	public string Margin { get; set; }

	[JsonProperty("profitUnreal")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("freeze")]
	public string Frozen { get; set; }

	[JsonProperty("hold")]
	public string Held { get; set; }
}

sealed class CoinWFuturesWsPosition
{
	[JsonProperty("id")]
	public string PositionId { get; set; }

	[JsonProperty("instrument")]
	public string NativeSymbol { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("baseSize")]
	public string ContractSize { get; set; }

	[JsonProperty("currentPiece")]
	public string CurrentContracts { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("quantityUnit")]
	public int QuantityUnit { get; set; }

	[JsonProperty("openPrice")]
	public string OpenPrice { get; set; }

	[JsonProperty("indexPrice")]
	public string IndexPrice { get; set; }

	[JsonProperty("positionMargin")]
	public string Margin { get; set; }

	[JsonProperty("profitUnreal")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("positionModel")]
	public int PositionModel { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("createdDate")]
	public long CreatedTime { get; set; }

	[JsonProperty("updatedDate")]
	public long UpdatedTime { get; set; }
}

sealed class CoinWFuturesWsFill
{
	[JsonProperty("id")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("openId")]
	public string PositionId { get; set; }

	[JsonProperty("instrument")]
	public string NativeSymbol { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("realPrice")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("quantityUnit")]
	public int QuantityUnit { get; set; }

	[JsonProperty("baseSize")]
	public string ContractSize { get; set; }

	[JsonProperty("totalPiece")]
	public string Contracts { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("netProfit")]
	public string RealizedPnl { get; set; }

	[JsonProperty("createdDate")]
	public long Time { get; set; }
}

sealed class CoinWWsTickerUpdate
{
	public string PairCode { get; init; }
	public string LastPrice { get; init; }
	public string BidPrice { get; init; }
	public string AskPrice { get; init; }
	public string OpenPrice { get; init; }
	public string HighPrice { get; init; }
	public string LowPrice { get; init; }
	public string PriceChangePercent { get; init; }
	public string Volume { get; init; }
	public string QuoteVolume { get; init; }
}

sealed class CoinWWsDepthUpdate
{
	public string PairCode { get; init; }
	public CoinWBookLevel[] Bids { get; init; }
	public CoinWBookLevel[] Asks { get; init; }
	public long Time { get; init; }
	public long FirstSequence { get; init; }
	public long LastSequence { get; init; }
	public bool IsSnapshot { get; init; }
}

sealed class CoinWWsTradeUpdate
{
	public string PairCode { get; init; }
	public string Id { get; init; }
	public string Price { get; init; }
	public string Volume { get; init; }
	public string Side { get; init; }
	public long Time { get; init; }
}

sealed class CoinWWsCandleUpdate
{
	public string PairCode { get; init; }
	public string Interval { get; init; }
	public long OpenTime { get; init; }
	public string OpenPrice { get; init; }
	public string HighPrice { get; init; }
	public string LowPrice { get; init; }
	public string ClosePrice { get; init; }
	public string Volume { get; init; }
	public string QuoteVolume { get; init; }
}

sealed class CoinWWsBalanceUpdate
{
	public string Asset { get; init; }
	public string Available { get; init; }
	public string Held { get; init; }
	public string Margin { get; init; }
	public string UnrealizedPnl { get; init; }
	public long Time { get; init; }
}

sealed class CoinWWsOrderUpdate
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }
	public string Side { get; init; }
	public string Volume { get; init; }
	public string RemainingVolume { get; init; }
	public string ExecutedVolume { get; init; }
	public string ContractSize { get; init; }
	public string Contracts { get; init; }
	public string ExecutedContracts { get; init; }
	public int? QuantityUnit { get; init; }
	public string Price { get; init; }
	public string AveragePrice { get; init; }
	public string OrderType { get; init; }
	public string Status { get; init; }
	public string Fee { get; init; }
	public string PositionType { get; init; }
	public long Time { get; init; }
}

sealed class CoinWWsPositionUpdate
{
	public string Symbol { get; init; }
	public string PositionId { get; init; }
	public string Side { get; init; }
	public string Volume { get; init; }
	public string ContractSize { get; init; }
	public string Contracts { get; init; }
	public string OpenPrice { get; init; }
	public string IndexPrice { get; init; }
	public string Margin { get; init; }
	public string UnrealizedPnl { get; init; }
	public string Leverage { get; init; }
	public string Status { get; init; }
	public long Time { get; init; }
}

sealed class CoinWWsFillUpdate
{
	public string Symbol { get; init; }
	public string TradeId { get; init; }
	public string OrderId { get; init; }
	public string PositionId { get; init; }
	public string Side { get; init; }
	public string Price { get; init; }
	public string Volume { get; init; }
	public string ContractSize { get; init; }
	public string Contracts { get; init; }
	public string Fee { get; init; }
	public string RealizedPnl { get; init; }
	public long Time { get; init; }
}

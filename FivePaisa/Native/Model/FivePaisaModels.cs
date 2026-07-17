namespace StockSharp.FivePaisa.Native.Model;

internal sealed class FivePaisaRequest<T>
	where T : class
{
	[JsonProperty("head")]
	public FivePaisaRequestHead Head { get; set; }

	[JsonProperty("body")]
	public T Body { get; set; }
}

internal sealed class FivePaisaRequestHead
{
	[JsonProperty("key")]
	public string Key { get; set; }
}

internal sealed class FivePaisaResponse<T>
	where T : class
{
	[JsonProperty("head")]
	public FivePaisaResponseHead Head { get; set; }

	[JsonProperty("body")]
	public T Body { get; set; }
}

internal sealed class FivePaisaResponseHead
{
	[JsonProperty("responseCode")]
	public string ResponseCode { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("statusDescription")]
	public string StatusDescription { get; set; }
}

internal sealed class FivePaisaAccountRequest
{
	[JsonProperty("ClientCode")]
	public string ClientCode { get; set; }
}

internal sealed class FivePaisaInstrument
{
	public string Exchange { get; set; }
	public string ExchangeType { get; set; }
	public long ScripCode { get; set; }
	public string Name { get; set; }
	public DateTime? Expiry { get; set; }
	public string ScripType { get; set; }
	public decimal StrikeRate { get; set; }
	public string FullName { get; set; }
	public decimal TickSize { get; set; }
	public decimal LotSize { get; set; }
	public decimal QuantityLimit { get; set; }
	public decimal Multiplier { get; set; }
	public string SymbolRoot { get; set; }
	public string Isin { get; set; }
	public string ScripData { get; set; }
	public string Series { get; set; }
}

internal sealed class FivePaisaOrderRequest
{
	[JsonProperty("Exchange")]
	public string Exchange { get; set; }

	[JsonProperty("ExchangeType")]
	public string ExchangeType { get; set; }

	[JsonProperty("ScripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("ScripData")]
	public string ScripData { get; set; }

	[JsonProperty("Price")]
	public decimal Price { get; set; }

	[JsonProperty("OrderType")]
	public string OrderType { get; set; }

	[JsonProperty("Qty")]
	public long Quantity { get; set; }

	[JsonProperty("DisQty")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("StopLossPrice")]
	public decimal StopLossPrice { get; set; }

	[JsonProperty("IsIntraday")]
	public bool IsIntraday { get; set; }

	[JsonProperty("iOrderValidity")]
	public int OrderValidity { get; set; }

	[JsonProperty("AHPlaced")]
	public string AfterMarket { get; set; }

	[JsonProperty("RemoteOrderID")]
	public string RemoteOrderId { get; set; }

	[JsonProperty("AlgoID")]
	public long AlgoId { get; set; }
}

internal sealed class FivePaisaModifyOrderRequest
{
	[JsonProperty("ExchOrderID")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("Price")]
	public decimal Price { get; set; }

	[JsonProperty("Qty")]
	public long Quantity { get; set; }

	[JsonProperty("StopLossPrice")]
	public decimal StopLossPrice { get; set; }

	[JsonProperty("DisQty")]
	public long DisclosedQuantity { get; set; }
}

internal sealed class FivePaisaCancelOrderRequest
{
	[JsonProperty("ExchOrderID")]
	public string ExchangeOrderId { get; set; }
}

internal sealed class FivePaisaOrderResult
{
	[JsonProperty("BrokerOrderID")]
	public long BrokerOrderId { get; set; }

	[JsonProperty("ClientCode")]
	public string ClientCode { get; set; }

	[JsonProperty("Exch")]
	public string Exchange { get; set; }

	[JsonProperty("ExchType")]
	public string ExchangeType { get; set; }

	[JsonProperty("ExchOrderID")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("RemoteOrderID")]
	public string RemoteOrderId { get; set; }

	[JsonProperty("RMSResponseCode")]
	public int RmsResponseCode { get; set; }

	[JsonProperty("Message")]
	public string Message { get; set; }

	[JsonProperty("ScripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("Status")]
	public int Status { get; set; }

	[JsonProperty("Time")]
	public string Time { get; set; }
}

internal sealed class FivePaisaOrderBookBody
{
	[JsonProperty("Status")]
	public int Status { get; set; }

	[JsonProperty("Message")]
	public string Message { get; set; }

	[JsonProperty("OrderBookDetail")]
	public FivePaisaOrder[] Orders { get; set; }
}

internal sealed class FivePaisaOrder
{
	[JsonProperty("BrokerOrderId")]
	public long BrokerOrderId { get; set; }

	[JsonProperty("BrokerOrderTime")]
	public string BrokerOrderTime { get; set; }

	[JsonProperty("Exch")]
	public string Exchange { get; set; }

	[JsonProperty("ExchType")]
	public string ExchangeType { get; set; }

	[JsonProperty("ScripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("ScripName")]
	public string ScripName { get; set; }

	[JsonProperty("BuySell")]
	public string BuySell { get; set; }

	[JsonProperty("Qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("Rate")]
	public decimal Rate { get; set; }

	[JsonProperty("AtMarket")]
	public string AtMarket { get; set; }

	[JsonProperty("WithSL")]
	public string WithStopLoss { get; set; }

	[JsonProperty("SLTriggerRate")]
	public decimal StopLossTriggerRate { get; set; }

	[JsonProperty("ExchOrderID")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("ExchOrderTime")]
	public string ExchangeOrderTime { get; set; }

	[JsonProperty("OrderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("TradedQty")]
	public decimal TradedQuantity { get; set; }

	[JsonProperty("PendingQty")]
	public decimal PendingQuantity { get; set; }

	[JsonProperty("DelvIntra")]
	public string Product { get; set; }

	[JsonProperty("OrderValidity")]
	public int OrderValidity { get; set; }

	[JsonProperty("Reason")]
	public string Reason { get; set; }

	[JsonProperty("RemoteOrderID")]
	public string RemoteOrderId { get; set; }

	[JsonProperty("AveragePrice")]
	public decimal AveragePrice { get; set; }
}

internal sealed class FivePaisaTradeBookBody
{
	[JsonProperty("Status")]
	public int Status { get; set; }

	[JsonProperty("Message")]
	public string Message { get; set; }

	[JsonProperty("TradeBookDetail")]
	public FivePaisaTrade[] Trades { get; set; }
}

internal sealed class FivePaisaTrade
{
	[JsonProperty("Exch")]
	public string Exchange { get; set; }

	[JsonProperty("ExchType")]
	public string ExchangeType { get; set; }

	[JsonProperty("ScripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("ScripName")]
	public string ScripName { get; set; }

	[JsonProperty("BuySell")]
	public string BuySell { get; set; }

	[JsonProperty("Qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("Rate")]
	public decimal Rate { get; set; }

	[JsonProperty("ExchOrderID")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("ExchangeTradeID")]
	public string ExchangeTradeId { get; set; }

	[JsonProperty("ExchangeTradeTime")]
	public string ExchangeTradeTime { get; set; }

	[JsonProperty("RemoteOrderID")]
	public string RemoteOrderId { get; set; }

	[JsonProperty("DelvIntra")]
	public string Product { get; set; }
}

internal sealed class FivePaisaMarginBody
{
	[JsonProperty("ClientCode")]
	public string ClientCode { get; set; }

	[JsonProperty("EquityMargin")]
	public FivePaisaEquityMargin[] EquityMargins { get; set; }

	[JsonProperty("Status")]
	public int Status { get; set; }

	[JsonProperty("Message")]
	public string Message { get; set; }

	[JsonProperty("TimeStamp")]
	public string Timestamp { get; set; }
}

internal sealed class FivePaisaEquityMargin
{
	[JsonProperty("Ledgerbalance")]
	public decimal LedgerBalance { get; set; }

	[JsonProperty("MarginUtilized")]
	public decimal MarginUtilized { get; set; }

	[JsonProperty("NetAvailableMargin")]
	public decimal NetAvailableMargin { get; set; }

	[JsonProperty("GrossHoldingValue")]
	public decimal GrossHoldingValue { get; set; }

	[JsonProperty("TotalCollateralValue")]
	public decimal TotalCollateralValue { get; set; }
}

internal sealed class FivePaisaHoldingBody
{
	[JsonProperty("Status")]
	public int Status { get; set; }

	[JsonProperty("Message")]
	public string Message { get; set; }

	[JsonProperty("Data")]
	public FivePaisaHolding[] Holdings { get; set; }
}

internal sealed class FivePaisaHolding
{
	[JsonProperty("AvgRate")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("BseCode")]
	public long BseCode { get; set; }

	[JsonProperty("NseCode")]
	public long NseCode { get; set; }

	[JsonProperty("CurrentPrice")]
	public decimal CurrentPrice { get; set; }

	[JsonProperty("DPQty")]
	public decimal DepositoryQuantity { get; set; }

	[JsonProperty("PoolQty")]
	public decimal PoolQuantity { get; set; }

	[JsonProperty("Quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("Exch")]
	public string Exchange { get; set; }

	[JsonProperty("ExchType")]
	public string ExchangeType { get; set; }

	[JsonProperty("FullName")]
	public string FullName { get; set; }

	[JsonProperty("Symbol")]
	public string Symbol { get; set; }
}

internal sealed class FivePaisaPositionBody
{
	[JsonProperty("Status")]
	public int Status { get; set; }

	[JsonProperty("Message")]
	public string Message { get; set; }

	[JsonProperty("NetPositionDetail")]
	public FivePaisaPosition[] Positions { get; set; }
}

internal sealed class FivePaisaPosition
{
	[JsonProperty("Exch")]
	public string Exchange { get; set; }

	[JsonProperty("ExchType")]
	public string ExchangeType { get; set; }

	[JsonProperty("ScripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("ScripName")]
	public string ScripName { get; set; }

	[JsonProperty("NetQty")]
	public decimal NetQuantity { get; set; }

	[JsonProperty("BodQty")]
	public decimal BeginningQuantity { get; set; }

	[JsonProperty("AvgRate")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("LTP")]
	public decimal LastPrice { get; set; }

	[JsonProperty("BookedPL")]
	public decimal RealizedPnL { get; set; }

	[JsonProperty("MTOM")]
	public decimal UnrealizedPnL { get; set; }

	[JsonProperty("Multiplier")]
	public decimal Multiplier { get; set; }
}

internal sealed class FivePaisaCandleResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("data")]
	public FivePaisaCandleData Data { get; set; }
}

internal sealed class FivePaisaCandleData
{
	[JsonProperty("candles")]
	public FivePaisaCandle[] Candles { get; set; }
}

[JsonConverter(typeof(FivePaisaCandleConverter))]
internal sealed class FivePaisaCandle
{
	public string OpenTime { get; set; }
	public decimal OpenPrice { get; set; }
	public decimal HighPrice { get; set; }
	public decimal LowPrice { get; set; }
	public decimal ClosePrice { get; set; }
	public decimal Volume { get; set; }
}

internal sealed class FivePaisaCandleConverter : JsonConverter<FivePaisaCandle>
{
	public override FivePaisaCandle ReadJson(JsonReader reader, Type objectType,
		FivePaisaCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("5paisa candle must be a JSON array.");

		var candle = new FivePaisaCandle
		{
			OpenTime = Read<string>(reader, serializer),
			OpenPrice = Read<decimal>(reader, serializer),
			HighPrice = Read<decimal>(reader, serializer),
			LowPrice = Read<decimal>(reader, serializer),
			ClosePrice = Read<decimal>(reader, serializer),
			Volume = Read<decimal>(reader, serializer),
		};
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("5paisa candle has an unexpected field count.");
		return candle;
	}

	private static T Read<T>(JsonReader reader, JsonSerializer serializer)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException("5paisa candle is incomplete.");
		return serializer.Deserialize<T>(reader);
	}

	public override void WriteJson(JsonWriter writer, FivePaisaCandle value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

internal sealed class FivePaisaTokenPayload
{
	[JsonProperty("RedirectServer")]
	public string RedirectServer { get; set; }
}

internal sealed class FivePaisaFeedRequest
{
	[JsonProperty("Method")]
	public string Method { get; set; }

	[JsonProperty("Operation")]
	public string Operation { get; set; }

	[JsonProperty("ClientCode")]
	public string ClientCode { get; set; }

	[JsonProperty("MarketFeedData")]
	public FivePaisaFeedInstrument[] Instruments { get; set; }
}

internal sealed class FivePaisaFeedInstrument
{
	[JsonProperty("Exch")]
	public string Exchange { get; set; }

	[JsonProperty("ExchType")]
	public string ExchangeType { get; set; }

	[JsonProperty("ScripCode")]
	public long ScripCode { get; set; }
}

internal sealed class FivePaisaMarketUpdate
{
	[JsonProperty("Exch")]
	public string Exchange { get; set; }

	[JsonProperty("ExchType")]
	public string ExchangeType { get; set; }

	[JsonProperty("Token")]
	public long Token { get; set; }

	[JsonProperty("LastRate")]
	public decimal LastPrice { get; set; }

	[JsonProperty("LastQty")]
	public decimal LastVolume { get; set; }

	[JsonProperty("TotalQty")]
	public decimal TotalVolume { get; set; }

	[JsonProperty("High")]
	public decimal HighPrice { get; set; }

	[JsonProperty("Low")]
	public decimal LowPrice { get; set; }

	[JsonProperty("OpenRate")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("PClose")]
	public decimal PreviousClose { get; set; }

	[JsonProperty("AvgRate")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("BidQty")]
	public decimal BestBidVolume { get; set; }

	[JsonProperty("BidRate")]
	public decimal BestBidPrice { get; set; }

	[JsonProperty("OffQty")]
	public decimal BestAskVolume { get; set; }

	[JsonProperty("OffRate")]
	public decimal BestAskPrice { get; set; }

	[JsonProperty("TBidQ")]
	public decimal TotalBidVolume { get; set; }

	[JsonProperty("TOffQ")]
	public decimal TotalAskVolume { get; set; }

	[JsonProperty("TickDt")]
	public string TickTime { get; set; }

	[JsonProperty("Time")]
	public long Time { get; set; }

	[JsonProperty("ChgPcnt")]
	public decimal ChangePercent { get; set; }
}

internal sealed class FivePaisaOrderUpdate
{
	[JsonProperty("ReqType")]
	public string RequestType { get; set; }

	[JsonProperty("ReqStatus")]
	public int RequestStatus { get; set; }

	[JsonProperty("ClientCode")]
	public string ClientCode { get; set; }

	[JsonProperty("Exch")]
	public string Exchange { get; set; }

	[JsonProperty("ExchType")]
	public string ExchangeType { get; set; }

	[JsonProperty("ScripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("Symbol")]
	public string Symbol { get; set; }

	[JsonProperty("BrokerOrderID")]
	public long BrokerOrderId { get; set; }

	[JsonProperty("ExchOrderID")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("ExchOrderTime")]
	public string ExchangeOrderTime { get; set; }

	[JsonProperty("ExchTradeId")]
	public string ExchangeTradeId { get; set; }

	[JsonProperty("ExchTradeTime")]
	public string ExchangeTradeTime { get; set; }

	[JsonProperty("BuySell")]
	public string BuySell { get; set; }

	[JsonProperty("Qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("OrderQty")]
	public decimal OrderQuantity { get; set; }

	[JsonProperty("Price")]
	public decimal Price { get; set; }

	[JsonProperty("OrderPrice")]
	public decimal OrderPrice { get; set; }

	[JsonProperty("PendingQty")]
	public decimal PendingQuantity { get; set; }

	[JsonProperty("TradedQty")]
	public decimal TradedQuantity { get; set; }

	[JsonProperty("TotalTradedQty")]
	public decimal TotalTradedQuantity { get; set; }

	[JsonProperty("AtMarket")]
	public string AtMarket { get; set; }

	[JsonProperty("Product")]
	public string Product { get; set; }

	[JsonProperty("SLTriggerRate")]
	public decimal StopLossTriggerRate { get; set; }

	[JsonProperty("RemoteOrderId")]
	public string RemoteOrderId { get; set; }

	[JsonProperty("Status")]
	public string Status { get; set; }

	[JsonProperty("Remark")]
	public string Remark { get; set; }
}

internal sealed class FivePaisaDepthRequest
{
	[JsonProperty("Method")]
	public string Method { get; set; }

	[JsonProperty("Operation")]
	public string Operation { get; set; }

	[JsonProperty("instruments")]
	public string[] Instruments { get; set; }
}

internal sealed class FivePaisaDepthUpdate
{
	[JsonProperty("Exch")]
	public string Exchange { get; set; }

	[JsonProperty("ExchType")]
	public string ExchangeType { get; set; }

	[JsonProperty("Token")]
	public long Token { get; set; }

	[JsonProperty("ScripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("TBidQ")]
	public decimal TotalBidVolume { get; set; }

	[JsonProperty("TOffQ")]
	public decimal TotalAskVolume { get; set; }

	[JsonProperty("Details")]
	public FivePaisaDepthLevel[] Details { get; set; }

	[JsonProperty("TimeStamp")]
	public long Timestamp { get; set; }

	[JsonProperty("Time")]
	public string Time { get; set; }
}

internal sealed class FivePaisaDepthLevel
{
	[JsonProperty("Quantity")]
	public decimal Volume { get; set; }

	[JsonProperty("Price")]
	public decimal Price { get; set; }

	[JsonProperty("NumberOfOrders")]
	public int OrdersCount { get; set; }

	[JsonProperty("BbBuySellFlag")]
	public int BuySellFlag { get; set; }
}

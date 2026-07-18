namespace StockSharp.Weex.Native.Model;

sealed class WeexWsCommand
{
	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public string[] Parameters { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }
}

sealed class WeexWsPong
{
	[JsonProperty("method")]
	public string Method { get; init; } = "PONG";

	[JsonProperty("id")]
	public long Id { get; init; }
}

sealed class WeexWsHeader
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("event")]
	public string PublicControlEvent { get; set; }

	[JsonProperty("type")]
	public string PrivateControlEvent { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("result")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class WeexWsEnvelope<TData>
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("d")]
	public TData Data { get; set; }
}

sealed class WeexWsSpotTicker
{
	[JsonProperty("p")]
	public string PriceChange { get; set; }

	[JsonProperty("P")]
	public string PriceChangePercent { get; set; }

	[JsonProperty("w")]
	public string WeightedPrice { get; set; }

	[JsonProperty("c")]
	public string LastPrice { get; set; }

	[JsonProperty("Q")]
	public string LastVolume { get; set; }

	[JsonProperty("b")]
	public string BidPrice { get; set; }

	[JsonProperty("B")]
	public string BidVolume { get; set; }

	[JsonProperty("a")]
	public string AskPrice { get; set; }

	[JsonProperty("A")]
	public string AskVolume { get; set; }

	[JsonProperty("o")]
	public string OpenPrice { get; set; }

	[JsonProperty("h")]
	public string HighPrice { get; set; }

	[JsonProperty("l")]
	public string LowPrice { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }

	[JsonProperty("q")]
	public string QuoteVolume { get; set; }
}

sealed class WeexWsFuturesTicker
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("p")]
	public string PriceChange { get; set; }

	[JsonProperty("P")]
	public string PriceChangePercent { get; set; }

	[JsonProperty("w")]
	public string WeightedPrice { get; set; }

	[JsonProperty("c")]
	public string LastPrice { get; set; }

	[JsonProperty("o")]
	public string OpenPrice { get; set; }

	[JsonProperty("h")]
	public string HighPrice { get; set; }

	[JsonProperty("l")]
	public string LowPrice { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }

	[JsonProperty("q")]
	public string QuoteVolume { get; set; }

	[JsonProperty("m")]
	public string MarkPrice { get; set; }

	[JsonProperty("i")]
	public string IndexPrice { get; set; }
}

sealed class WeexWsDepth
{
	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("U")]
	public long FirstUpdateId { get; set; }

	[JsonProperty("u")]
	public long LastUpdateId { get; set; }

	[JsonProperty("l")]
	public int Level { get; set; }

	[JsonProperty("d")]
	public string DepthType { get; set; }

	[JsonProperty("b")]
	public WeexBookLevel[] Bids { get; set; }

	[JsonProperty("a")]
	public WeexBookLevel[] Asks { get; set; }
}

sealed class WeexWsTrade
{
	[JsonProperty("T")]
	public long Time { get; set; }

	[JsonProperty("t")]
	public string Id { get; set; }

	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("q")]
	public string Volume { get; set; }

	[JsonProperty("v")]
	public string Value { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }
}

sealed class WeexWsCandle
{
	[JsonProperty("t")]
	public long OpenTime { get; set; }

	[JsonProperty("T")]
	public long CloseTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("i")]
	public string Interval { get; set; }

	[JsonProperty("o")]
	public string OpenPrice { get; set; }

	[JsonProperty("c")]
	public string ClosePrice { get; set; }

	[JsonProperty("h")]
	public string HighPrice { get; set; }

	[JsonProperty("l")]
	public string LowPrice { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }

	[JsonProperty("q")]
	public string QuoteVolume { get; set; }

	[JsonProperty("n")]
	public long TradeCount { get; set; }
}

sealed class WeexWsAccountEntry
{
	[JsonProperty("coin")]
	public string Asset { get; set; }

	[JsonProperty("equity")]
	public string Equity { get; set; }

	[JsonProperty("available")]
	public string Available { get; set; }

	[JsonProperty("frozen")]
	public string Frozen { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("marginMode")]
	public string MarginMode { get; set; }
}

sealed class WeexWsOrder
{
	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderSide")]
	public WeexSides Side { get; set; }

	[JsonProperty("positionSide")]
	public WeexPositionSides? PositionSide { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("size")]
	public string Quantity { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("latestFillPrice")]
	public string LatestFillPrice { get; set; }

	[JsonProperty("cumFillSize")]
	public string CumulativeFillSize { get; set; }

	[JsonProperty("createdTime")]
	public long CreatedTime { get; set; }

	[JsonProperty("updatedTime")]
	public long UpdatedTime { get; set; }
}

sealed class WeexWsFill
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("coin")]
	public string Asset { get; set; }

	[JsonProperty("baseCoin")]
	public string BaseAsset { get; set; }

	[JsonProperty("quoteCoin")]
	public string QuoteAsset { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("positionSide")]
	public WeexPositionSides? PositionSide { get; set; }

	[JsonProperty("orderSide")]
	public WeexSides Side { get; set; }

	[JsonProperty("fillSize")]
	public string Quantity { get; set; }

	[JsonProperty("fillValue")]
	public string Value { get; set; }

	[JsonProperty("fillFee")]
	public string Fee { get; set; }

	[JsonProperty("realizePnl")]
	public string RealizedPnl { get; set; }

	[JsonProperty("createdTime")]
	public long CreatedTime { get; set; }
}

sealed class WeexWsPosition
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("coin")]
	public string Asset { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public WeexPositionSides Side { get; set; }

	[JsonProperty("marginMode")]
	public string MarginMode { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("openValue")]
	public string OpenValue { get; set; }

	[JsonProperty("isolatedMargin")]
	public string IsolatedMargin { get; set; }

	[JsonProperty("fundingFee")]
	public string FundingFee { get; set; }

	[JsonProperty("updatedTime")]
	public long UpdatedTime { get; set; }
}

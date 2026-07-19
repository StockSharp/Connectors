namespace StockSharp.Gemini.Native.Model;

sealed class GeminiWsHeader
{
	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("status")]
	public int? Status { get; set; }

	[JsonProperty("error")]
	public GeminiWsError Error { get; set; }

	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long? TradeId { get; set; }

	[JsonProperty("b")]
	public decimal? Bid { get; set; }

	[JsonProperty("B")]
	public decimal? BidSize { get; set; }
}

sealed class GeminiWsError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class GeminiWsResponse
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("error")]
	public GeminiWsError Error { get; set; }
}

sealed class GeminiWsSubscriptionRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public GeminiWsMethods Method { get; init; }

	[JsonProperty("params")]
	public string[] Streams { get; init; }
}

sealed class GeminiWsSimpleRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public GeminiWsMethods Method { get; init; }
}

sealed class GeminiWsPlaceOrderRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public GeminiWsMethods Method { get; init; } = GeminiWsMethods.PlaceOrder;

	[JsonProperty("params")]
	public GeminiWsPlaceOrderParams Params { get; init; }
}

sealed class GeminiWsPlaceOrderParams
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public GeminiWsSides Side { get; init; }

	[JsonProperty("type")]
	public GeminiWsOrderTypes OrderType { get; init; }

	[JsonProperty("timeInForce")]
	public GeminiWsTimeInForces TimeInForce { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }
}

sealed class GeminiWsCancelOrderRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public GeminiWsMethods Method { get; init; } = GeminiWsMethods.CancelOrder;

	[JsonProperty("params")]
	public GeminiWsCancelOrderParams Params { get; init; }
}

sealed class GeminiWsCancelOrderParams
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }
}

sealed class GeminiWsOrderUpdate
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public long EventTimeNanoseconds { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("i")]
	public long OrderId { get; set; }

	[JsonProperty("c")]
	public string ClientOrderId { get; set; }

	[JsonProperty("S")]
	public GeminiWsSides? Side { get; set; }

	[JsonProperty("o")]
	public GeminiWsOrderTypes? OrderType { get; set; }

	[JsonProperty("X")]
	public GeminiWsOrderStatuses Status { get; set; }

	[JsonProperty("p")]
	public decimal? Price { get; set; }

	[JsonProperty("P")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("q")]
	public decimal? OriginalQuantity { get; set; }

	[JsonProperty("z")]
	public decimal? RemainingQuantity { get; set; }

	[JsonProperty("Z")]
	public decimal? ExecutedQuantity { get; set; }

	[JsonProperty("L")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("t")]
	public long TradeId { get; set; }

	[JsonProperty("n")]
	public decimal? Fee { get; set; }

	[JsonProperty("m")]
	public bool? IsMaker { get; set; }

	[JsonProperty("r")]
	public string Reason { get; set; }

	[JsonProperty("T")]
	public long TransactionTimeNanoseconds { get; set; }
}

sealed class GeminiWsBalanceUpdate
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public long EventTimeNanoseconds { get; set; }

	[JsonProperty("u")]
	public long UpdateTimeNanoseconds { get; set; }

	[JsonProperty("B")]
	public GeminiWsBalance[] Balances { get; set; }
}

sealed class GeminiWsBalance
{
	[JsonProperty("a")]
	public string Asset { get; set; }

	[JsonProperty("f")]
	public decimal Available { get; set; }

	[JsonProperty("c")]
	public decimal Confirmed { get; set; }
}

sealed class GeminiWsPositionReport
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public long EventTimeNanoseconds { get; set; }

	[JsonProperty("u")]
	public long UpdateTimeNanoseconds { get; set; }

	[JsonProperty("A")]
	public long AccountId { get; set; }

	[JsonProperty("P")]
	public GeminiWsPosition[] Positions { get; set; }
}

sealed class GeminiWsPosition
{
	[JsonProperty("t")]
	public string ProductType { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("a")]
	public GeminiWsNamedAmount[] Amounts { get; set; }
}

sealed class GeminiWsNamedAmount
{
	[JsonProperty("t")]
	public string Name { get; set; }

	[JsonProperty("v")]
	public decimal Value { get; set; }

	[JsonProperty("c")]
	public string Asset { get; set; }
}

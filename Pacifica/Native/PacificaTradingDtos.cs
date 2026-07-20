namespace StockSharp.Pacifica.Native;

sealed class PacificaStopConfiguration
{
	[JsonProperty("stop_price")]
	public string StopPrice { get; init; }

	[JsonProperty("limit_price")]
	public string LimitPrice { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("trigger_price_type")]
	public global::StockSharp.Pacifica.PacificaTriggerPriceTypes
		TriggerPriceType { get; init; }
}

sealed class PacificaCreateOrderPayload
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("side")]
	public PacificaSides Side { get; init; }

	[JsonProperty("tif")]
	public PacificaTimeInForces TimeInForce { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("builder_code")]
	public string BuilderCode { get; init; }

	[JsonProperty("take_profit")]
	public PacificaStopConfiguration TakeProfit { get; init; }

	[JsonProperty("stop_loss")]
	public PacificaStopConfiguration StopLoss { get; init; }
}

sealed class PacificaCreateMarketOrderPayload
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("side")]
	public PacificaSides Side { get; init; }

	[JsonProperty("slippage_percent")]
	public string SlippagePercent { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("builder_code")]
	public string BuilderCode { get; init; }

	[JsonProperty("take_profit")]
	public PacificaStopConfiguration TakeProfit { get; init; }

	[JsonProperty("stop_loss")]
	public PacificaStopConfiguration StopLoss { get; init; }
}

sealed class PacificaEditOrderPayload
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("order_id")]
	public long? OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }
}

sealed class PacificaCancelOrderPayload
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("order_id")]
	public long? OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }
}

sealed class PacificaCancelAllOrdersPayload
{
	[JsonProperty("all_symbols")]
	public bool IsAllSymbols { get; init; }

	[JsonProperty("exclude_reduce_only")]
	public bool IsExcludeReduceOnly { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class PacificaSigningEnvelope<T>
{
	[JsonProperty("type")]
	public PacificaOperationTypes Type { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("expiry_window")]
	public long ExpiryWindow { get; init; }

	[JsonProperty("data")]
	public T Data { get; init; }
}

sealed class PacificaSignature
{
	public string Account { get; init; }
	public string AgentWallet { get; init; }
	public string Value { get; init; }
	public long Timestamp { get; init; }
	public long ExpiryWindow { get; init; }
}

abstract class PacificaSignedRequest
{
	[JsonProperty("account")]
	public string Account { get; init; }

	[JsonProperty("agent_wallet")]
	public string AgentWallet { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("expiry_window")]
	public long ExpiryWindow { get; init; }
}

sealed class PacificaSignedCreateOrder : PacificaSignedRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("side")]
	public PacificaSides Side { get; init; }

	[JsonProperty("tif")]
	public PacificaTimeInForces TimeInForce { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("builder_code")]
	public string BuilderCode { get; init; }

	[JsonProperty("take_profit")]
	public PacificaStopConfiguration TakeProfit { get; init; }

	[JsonProperty("stop_loss")]
	public PacificaStopConfiguration StopLoss { get; init; }
}

sealed class PacificaSignedCreateMarketOrder : PacificaSignedRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("side")]
	public PacificaSides Side { get; init; }

	[JsonProperty("slippage_percent")]
	public string SlippagePercent { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("builder_code")]
	public string BuilderCode { get; init; }

	[JsonProperty("take_profit")]
	public PacificaStopConfiguration TakeProfit { get; init; }

	[JsonProperty("stop_loss")]
	public PacificaStopConfiguration StopLoss { get; init; }
}

sealed class PacificaSignedEditOrder : PacificaSignedRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("order_id")]
	public long? OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }
}

sealed class PacificaSignedCancelOrder : PacificaSignedRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("order_id")]
	public long? OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }
}

sealed class PacificaSignedCancelAllOrders : PacificaSignedRequest
{
	[JsonProperty("all_symbols")]
	public bool IsAllSymbols { get; init; }

	[JsonProperty("exclude_reduce_only")]
	public bool IsExcludeReduceOnly { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class PacificaCreateOrderRequest
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("params")]
	public PacificaCreateOrderRequestParameters Parameters { get; init; }
}

sealed class PacificaCreateOrderRequestParameters
{
	[JsonProperty("create_order")]
	public PacificaSignedCreateOrder CreateOrder { get; init; }
}

sealed class PacificaCreateMarketOrderRequest
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("params")]
	public PacificaCreateMarketOrderRequestParameters Parameters { get; init; }
}

sealed class PacificaCreateMarketOrderRequestParameters
{
	[JsonProperty("create_market_order")]
	public PacificaSignedCreateMarketOrder CreateMarketOrder { get; init; }
}

sealed class PacificaEditOrderRequest
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("params")]
	public PacificaEditOrderRequestParameters Parameters { get; init; }
}

sealed class PacificaEditOrderRequestParameters
{
	[JsonProperty("edit_order")]
	public PacificaSignedEditOrder EditOrder { get; init; }
}

sealed class PacificaCancelOrderRequest
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("params")]
	public PacificaCancelOrderRequestParameters Parameters { get; init; }
}

sealed class PacificaCancelOrderRequestParameters
{
	[JsonProperty("cancel_order")]
	public PacificaSignedCancelOrder CancelOrder { get; init; }
}

sealed class PacificaCancelAllOrdersRequest
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("params")]
	public PacificaCancelAllOrdersRequestParameters Parameters { get; init; }
}

sealed class PacificaCancelAllOrdersRequestParameters
{
	[JsonProperty("cancel_all_orders")]
	public PacificaSignedCancelAllOrders CancelAllOrders { get; init; }
}

sealed class PacificaActionResponse
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("data")]
	public PacificaActionResult Data { get; init; }

	[JsonProperty("err")]
	public string Error { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("t")]
	public long Timestamp { get; init; }

	[JsonProperty("type")]
	public PacificaOperationTypes Type { get; init; }

	[JsonProperty("rl")]
	public PacificaRateLimit RateLimit { get; init; }
}

sealed class PacificaActionResult
{
	[JsonProperty("I")]
	public string ClientOrderId { get; init; }

	[JsonProperty("i")]
	public long? OrderId { get; init; }

	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("cancelled_count")]
	public int? CancelledCount { get; init; }
}

sealed class PacificaRateLimit
{
	[JsonProperty("r")]
	public long Remaining { get; init; }

	[JsonProperty("q")]
	public long Quota { get; init; }

	[JsonProperty("t")]
	public long ResetAfterMilliseconds { get; init; }
}

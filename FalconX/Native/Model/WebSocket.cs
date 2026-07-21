namespace StockSharp.FalconX.Native.Model;

sealed class FalconXSocketHeader
{
	[JsonProperty("event")]
	public FalconXSocketEvents Event { get; init; }

	[JsonProperty("status")]
	public FalconXSocketStatuses? Status { get; init; }

	[JsonProperty("request_id")]
	public string RequestId { get; init; }

	[JsonProperty("error")]
	public FalconXApiError Error { get; init; }
}

sealed class FalconXSocketResponse<TBody>
{
	[JsonProperty("event")]
	public FalconXSocketEvents Event { get; init; }

	[JsonProperty("status")]
	public FalconXSocketStatuses? Status { get; init; }

	[JsonProperty("request_id")]
	public string RequestId { get; init; }

	[JsonProperty("body")]
	public TBody Body { get; init; }

	[JsonProperty("error")]
	public FalconXApiError Error { get; init; }
}

sealed class FalconXSocketAuthenticationRequest
{
	[JsonProperty("action")]
	public FalconXSocketActions Action { get; init; } =
		FalconXSocketActions.Authenticate;

	[JsonProperty("api_key")]
	public string ApiKey { get; init; }

	[JsonProperty("passphrase")]
	public string Passphrase { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("request_id")]
	public string RequestId { get; init; }
}

sealed class FalconXSocketAuthenticationBody
{
	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("session_id")]
	public string SessionId { get; init; }
}

sealed class FalconXSocketMessageBody
{
	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class FalconXPriceQuantity
{
	[JsonProperty("token")]
	public string Token { get; init; }

	[JsonProperty("levels")]
	public decimal[] Levels { get; init; }
}

sealed class FalconXPriceSubscriptionRequest
{
	[JsonProperty("action")]
	public FalconXSocketActions Action { get; init; }

	[JsonProperty("base_token")]
	public string BaseToken { get; init; }

	[JsonProperty("quote_token")]
	public string QuoteToken { get; init; }

	[JsonProperty("quantity")]
	public FalconXPriceQuantity Quantity { get; init; }

	[JsonProperty("request_id")]
	public string RequestId { get; init; }
}

sealed class FalconXPriceUnsubscriptionRequest
{
	[JsonProperty("action")]
	public FalconXSocketActions Action { get; init; } =
		FalconXSocketActions.Unsubscribe;

	[JsonProperty("base_token")]
	public string BaseToken { get; init; }

	[JsonProperty("quote_token")]
	public string QuoteToken { get; init; }

	[JsonProperty("request_id")]
	public string RequestId { get; init; }
}

sealed class FalconXPriceTick
{
	[JsonProperty("base_token")]
	public string BaseToken { get; init; }

	[JsonProperty("quote_token")]
	public string QuoteToken { get; init; }

	[JsonProperty("quantity_token")]
	public string QuantityToken { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("buy_price")]
	public decimal? BuyPrice { get; init; }

	[JsonProperty("sell_price")]
	public decimal? SellPrice { get; init; }

	[JsonProperty("t_create")]
	public long Timestamp { get; init; }
}

sealed class FalconXSocketOrderRequest
{
	[JsonProperty("action")]
	public FalconXSocketActions Action { get; init; }

	[JsonProperty("request_id")]
	public string RequestId { get; init; }

	[JsonProperty("order_type")]
	public FalconXOrderTypes OrderType { get; init; }

	[JsonProperty("order_details")]
	public FalconXSocketOrderDetails OrderDetails { get; init; }
}

sealed class FalconXSocketOrderDetails
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("orig_client_order_id")]
	public string OriginalClientOrderId { get; init; }

	[JsonProperty("base_token")]
	public string BaseToken { get; init; }

	[JsonProperty("quote_token")]
	public string QuoteToken { get; init; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; init; }

	[JsonProperty("quantity_token")]
	public string QuantityToken { get; init; }

	[JsonProperty("limit_price")]
	public decimal? LimitPrice { get; init; }

	[JsonProperty("side")]
	public FalconXSides? Side { get; init; }

	[JsonProperty("tif")]
	public FalconXTimeInForces? TimeInForce { get; init; }

	[JsonProperty("expiry")]
	public string Expiry { get; init; }

	[JsonProperty("transaction_time_minutes")]
	public int? TransactionTimeMinutes { get; init; }

	[JsonProperty("number_transactions")]
	public int? TransactionsCount { get; init; }
}

sealed class FalconXSocketFill
{
	[JsonProperty("fx_quote_id")]
	public string QuoteId { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("quantity")]
	public FalconXQuantity Quantity { get; init; }

	[JsonProperty("t_execute")]
	public string ExecuteTime { get; init; }
}

sealed class FalconXSocketOrderBody
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("orig_client_order_id")]
	public string OriginalClientOrderId { get; init; }

	[JsonProperty("token_pair")]
	public FalconXTokenPair TokenPair { get; init; }

	[JsonProperty("quantity")]
	public FalconXQuantity Quantity { get; init; }

	[JsonProperty("side")]
	public FalconXSides? Side { get; init; }

	[JsonProperty("order_type")]
	public FalconXOrderTypes? OrderType { get; init; }

	[JsonProperty("limit_price")]
	public decimal? LimitPrice { get; init; }

	[JsonProperty("time_in_force")]
	public FalconXTimeInForces? TimeInForce { get; init; }

	[JsonProperty("expiry_time")]
	public string ExpiryTime { get; init; }

	[JsonProperty("order_status")]
	public FalconXOrderStatuses? OrderStatus { get; init; }

	[JsonProperty("number_transactions")]
	public int? TransactionsCount { get; init; }

	[JsonProperty("transaction_time_minutes")]
	public int? TransactionTimeMinutes { get; init; }

	[JsonProperty("executed_quantity")]
	public decimal? ExecutedQuantity { get; init; }

	[JsonProperty("executed_price")]
	public decimal? ExecutedPrice { get; init; }

	[JsonProperty("fills")]
	public FalconXSocketFill[] Fills { get; init; }

	[JsonProperty("error")]
	public FalconXApiError Error { get; init; }
}

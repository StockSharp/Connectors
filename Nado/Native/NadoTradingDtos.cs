namespace StockSharp.Nado.Native;

sealed class NadoSignedOrder
{
	[JsonProperty("sender")]
	public string Sender { get; init; }

	[JsonProperty("priceX18")]
	public string Price { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("expiration")]
	public string Expiration { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }

	[JsonProperty("appendix")]
	public string Appendix { get; init; }
}

sealed class NadoPlaceOrderPayload
{
	[JsonProperty("id")]
	public long? Id { get; init; }

	[JsonProperty("product_id")]
	public int ProductId { get; init; }

	[JsonProperty("order")]
	public NadoSignedOrder Order { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("spot_leverage")]
	public bool? IsSpotLeverage { get; init; }

	[JsonProperty("borrow_margin")]
	public bool? IsBorrowMargin { get; init; }
}

sealed class NadoPlaceOrderRequest
{
	[JsonProperty("place_order")]
	public NadoPlaceOrderPayload PlaceOrder { get; init; }
}

sealed class NadoCancelTransaction
{
	[JsonProperty("sender")]
	public string Sender { get; init; }

	[JsonProperty("productIds")]
	public int[] ProductIds { get; init; }

	[JsonProperty("digests")]
	public string[] Digests { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }
}

sealed class NadoCancelOrdersPayload
{
	[JsonProperty("tx")]
	public NadoCancelTransaction Transaction { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("required_unfilled_amount")]
	public string RequiredUnfilledAmount { get; init; }
}

sealed class NadoCancelOrdersRequest
{
	[JsonProperty("cancel_orders")]
	public NadoCancelOrdersPayload CancelOrders { get; init; }
}

sealed class NadoCancelProductsTransaction
{
	[JsonProperty("sender")]
	public string Sender { get; init; }

	[JsonProperty("productIds")]
	public int[] ProductIds { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }
}

sealed class NadoCancelProductOrdersPayload
{
	[JsonProperty("tx")]
	public NadoCancelProductsTransaction Transaction { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }
}

sealed class NadoCancelProductOrdersRequest
{
	[JsonProperty("cancel_product_orders")]
	public NadoCancelProductOrdersPayload CancelProductOrders { get; init; }
}

sealed class NadoCancelAndPlacePayload
{
	[JsonProperty("cancel_tx")]
	public NadoCancelTransaction CancelTransaction { get; init; }

	[JsonProperty("cancel_signature")]
	public string CancelSignature { get; init; }

	[JsonProperty("place_order")]
	public NadoPlaceOrderPayload PlaceOrder { get; init; }

	[JsonProperty("required_unfilled_amount")]
	public string RequiredUnfilledAmount { get; init; }

	[JsonProperty("place_requires_unfilled")]
	public bool? IsPlaceRequiresUnfilled { get; init; }
}

sealed class NadoCancelAndPlaceRequest
{
	[JsonProperty("cancel_and_place")]
	public NadoCancelAndPlacePayload CancelAndPlace { get; init; }
}

sealed class NadoPlacedOrder
{
	[JsonProperty("digest")]
	public string Digest { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}

sealed class NadoCancelledOrders
{
	[JsonProperty("cancelled_orders")]
	public NadoOrder[] Orders { get; set; }
}

sealed class NadoExecuteResponse<T>
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("signature")]
	public string Signature { get; set; }

	[JsonProperty("request_type")]
	public string RequestType { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_code")]
	public int? ErrorCode { get; set; }
}

sealed class NadoSocketHeader
{
	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("error")]
	public NadoSocketError Error { get; set; }
}

sealed class NadoSocketError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class NadoSocketResponse
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("result")]
	public NadoSocketResult Result { get; set; }

	[JsonProperty("error")]
	public NadoSocketError Error { get; set; }
}

sealed class NadoSocketResult
{
	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("server_time")]
	public string ServerTime { get; set; }

	[JsonProperty("client_time")]
	public string ClientTime { get; set; }
}

sealed class NadoStreamDefinition
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("product_id")]
	public int? ProductId { get; init; }

	[JsonProperty("granularity")]
	public int? Granularity { get; init; }

	[JsonProperty("subaccount")]
	public string Subaccount { get; init; }
}

sealed class NadoSubscriptionRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("stream")]
	public NadoStreamDefinition Stream { get; init; }
}

sealed class NadoPingRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; } = "ping";

	[JsonProperty("client_time")]
	public string ClientTime { get; init; }
}

abstract class NadoStreamEvent
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("product_id")]
	public int ProductId { get; set; }
}

sealed class NadoTradeEvent : NadoStreamEvent
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("taker_qty")]
	public string TakerQuantity { get; set; }

	[JsonProperty("maker_qty")]
	public string MakerQuantity { get; set; }

	[JsonProperty("is_taker_buyer")]
	public bool IsTakerBuyer { get; set; }
}

sealed class NadoBestBidOfferEvent : NadoStreamEvent
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("bid_price")]
	public string BidPrice { get; set; }

	[JsonProperty("bid_qty")]
	public string BidQuantity { get; set; }

	[JsonProperty("ask_price")]
	public string AskPrice { get; set; }

	[JsonProperty("ask_qty")]
	public string AskQuantity { get; set; }
}

sealed class NadoBookDepthEvent : NadoStreamEvent
{
	[JsonProperty("last_max_timestamp")]
	public string LastMaximumTimestamp { get; set; }

	[JsonProperty("min_timestamp")]
	public string MinimumTimestamp { get; set; }

	[JsonProperty("max_timestamp")]
	public string MaximumTimestamp { get; set; }

	[JsonProperty("bids")]
	public string[][] Bids { get; set; }

	[JsonProperty("asks")]
	public string[][] Asks { get; set; }
}

sealed class NadoCandleEvent : NadoStreamEvent
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("granularity")]
	public int Granularity { get; set; }

	[JsonProperty("open_x18")]
	public string Open { get; set; }

	[JsonProperty("high_x18")]
	public string High { get; set; }

	[JsonProperty("low_x18")]
	public string Low { get; set; }

	[JsonProperty("close_x18")]
	public string Close { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }
}

sealed class NadoFundingRateEvent : NadoStreamEvent
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("funding_rate_x18")]
	public string FundingRate { get; set; }

	[JsonProperty("update_time")]
	public string UpdateTime { get; set; }
}

sealed class NadoFillEvent : NadoStreamEvent
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("subaccount")]
	public string Subaccount { get; set; }

	[JsonProperty("order_digest")]
	public string OrderDigest { get; set; }

	[JsonProperty("filled_qty")]
	public string FilledQuantity { get; set; }

	[JsonProperty("remaining_qty")]
	public string RemainingQuantity { get; set; }

	[JsonProperty("original_qty")]
	public string OriginalQuantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("is_taker")]
	public bool IsTaker { get; set; }

	[JsonProperty("is_bid")]
	public bool IsBid { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("submission_idx")]
	public string SubmissionIndex { get; set; }

	[JsonProperty("appendix")]
	public string Appendix { get; set; }
}

sealed class NadoPositionChangeEvent : NadoStreamEvent
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("subaccount")]
	public string Subaccount { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("v_quote_amount")]
	public string QuoteAmount { get; set; }

	[JsonProperty("reason")]
	public NadoPositionChangeReasons Reason { get; set; }

	[JsonProperty("isolated")]
	public bool IsIsolated { get; set; }
}

sealed class NadoOrderUpdateEvent : NadoStreamEvent
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("digest")]
	public string Digest { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("reason")]
	public NadoOrderUpdateReasons Reason { get; set; }
}

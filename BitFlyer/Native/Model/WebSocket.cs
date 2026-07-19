namespace StockSharp.BitFlyer.Native.Model;

sealed class BitFlyerRpcCommand<TParameters>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";

	[JsonProperty("method")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerRpcMethods Method { get; init; }

	[JsonProperty("params")]
	public TParameters Parameters { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }
}

sealed class BitFlyerRpcChannelParameters
{
	[JsonProperty("channel")]
	public string Channel { get; init; }
}

sealed class BitFlyerRpcAuthParameters
{
	[JsonProperty("api_key")]
	public string ApiKey { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }
}

sealed class BitFlyerRpcHeader
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("result")]
	public bool? IsAccepted { get; set; }

	[JsonProperty("error")]
	public BitFlyerRpcError Error { get; set; }

	[JsonProperty("method")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerRpcMethods? Method { get; set; }

	[JsonProperty("params")]
	public BitFlyerRpcChannelHeader Parameters { get; set; }
}

sealed class BitFlyerRpcError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class BitFlyerRpcChannelHeader
{
	[JsonProperty("channel")]
	public string Channel { get; set; }
}

sealed class BitFlyerRpcChannelEnvelope<TMessage>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("method")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerRpcMethods Method { get; set; }

	[JsonProperty("params")]
	public BitFlyerRpcChannelPayload<TMessage> Parameters { get; set; }
}

sealed class BitFlyerRpcChannelPayload<TMessage>
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("message")]
	public TMessage Message { get; set; }
}

sealed class BitFlyerChildOrderEvent
{
	[JsonProperty("product_code")]
	public string ProductCode { get; set; }

	[JsonProperty("child_order_id")]
	public string OrderId { get; set; }

	[JsonProperty("child_order_acceptance_id")]
	public string AcceptanceId { get; set; }

	[JsonProperty("event_date")]
	public string EventDate { get; set; }

	[JsonProperty("event_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerChildEventTypes EventType { get; set; }

	[JsonProperty("child_order_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerChildOrderTypes? OrderType { get; set; }

	[JsonProperty("expire_date")]
	public string ExpireDate { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("exec_id")]
	public long? ExecutionId { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(BitFlyerNullableSideConverter))]
	public BitFlyerSides? Side { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("commission")]
	public decimal? Commission { get; set; }

	[JsonProperty("sfd")]
	public decimal? Sfd { get; set; }

	[JsonProperty("outstanding_size")]
	public decimal? OutstandingSize { get; set; }
}

sealed class BitFlyerParentOrderEvent
{
	[JsonProperty("product_code")]
	public string ProductCode { get; set; }

	[JsonProperty("parent_order_id")]
	public string OrderId { get; set; }

	[JsonProperty("parent_order_acceptance_id")]
	public string AcceptanceId { get; set; }

	[JsonProperty("event_date")]
	public string EventDate { get; set; }

	[JsonProperty("event_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerParentEventTypes EventType { get; set; }

	[JsonProperty("parent_order_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerParentOrderTypes? ParentOrderType { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("child_order_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerChildOrderTypes? ChildOrderType { get; set; }

	[JsonProperty("parameter_index")]
	public int? ParameterIndex { get; set; }

	[JsonProperty("child_order_acceptance_id")]
	public string ChildAcceptanceId { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(BitFlyerNullableSideConverter))]
	public BitFlyerSides? Side { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("expire_date")]
	public string ExpireDate { get; set; }
}

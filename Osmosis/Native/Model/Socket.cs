namespace StockSharp.Osmosis.Native.Model;

sealed class OsmosisSocketRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public OsmosisSocketParameters Parameters { get; init; }
}

sealed class OsmosisSocketParameters
{
	[JsonProperty("query")]
	public string Query { get; init; }
}

sealed class OsmosisSocketMessage
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long? Id { get; init; }

	[JsonProperty("result")]
	public OsmosisSocketResult Result { get; init; }

	[JsonProperty("error")]
	public OsmosisRpcError Error { get; init; }
}

sealed class OsmosisSocketResult
{
	[JsonProperty("query")]
	public string Query { get; init; }

	[JsonProperty("data")]
	public OsmosisSocketData Data { get; init; }
}

sealed class OsmosisSocketData
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("value")]
	public OsmosisSocketValue Value { get; init; }
}

sealed class OsmosisSocketValue
{
	[JsonProperty("TxResult")]
	public OsmosisSocketTransactionResult TransactionResult { get; init; }
}

sealed class OsmosisSocketTransactionResult
{
	[JsonProperty("height")]
	public string Height { get; init; }

	[JsonProperty("tx")]
	public string TransactionBytes { get; init; }

	[JsonProperty("result")]
	public OsmosisSocketExecutionResult Result { get; init; }
}

sealed class OsmosisSocketExecutionResult
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("events")]
	public OsmosisSocketEvent[] Events { get; init; }
}

sealed class OsmosisSocketEvent
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("attributes")]
	public OsmosisSocketAttribute[] Attributes { get; init; }
}

sealed class OsmosisSocketAttribute
{
	[JsonProperty("key")]
	public string Key { get; init; }

	[JsonProperty("value")]
	public string Value { get; init; }

	[JsonProperty("index")]
	public bool IsIndexed { get; init; }
}

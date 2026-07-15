namespace StockSharp.Robinhood.Native.Model;

sealed class McpRequest<TParams>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; } = "2.0";

	[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
	public long? Id { get; set; }

	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("params")]
	public TParams Params { get; set; }
}

sealed class McpResponse<TResult>
{
	[JsonProperty("result")]
	public TResult Result { get; set; }

	[JsonProperty("error")]
	public McpError Error { get; set; }
}

sealed class McpError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class McpInitializeParams
{
	[JsonProperty("protocolVersion")]
	public string ProtocolVersion { get; set; }

	[JsonProperty("capabilities")]
	public McpClientCapabilities Capabilities { get; set; } = new();

	[JsonProperty("clientInfo")]
	public McpClientInfo ClientInfo { get; set; }
}

sealed class McpClientCapabilities;

sealed class McpClientInfo
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }
}

sealed class McpInitializeResult
{
	[JsonProperty("protocolVersion")]
	public string ProtocolVersion { get; set; }
}

sealed class McpEmptyParams;

sealed class McpToolsResult
{
	[JsonProperty("tools")]
	public McpTool[] Tools { get; set; }
}

sealed class McpTool
{
	[JsonProperty("name")]
	public string Name { get; set; }
}

sealed class McpCallParams<TArguments>
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("arguments")]
	public TArguments Arguments { get; set; }
}

sealed class McpCallResult<TData>
{
	[JsonProperty("structuredContent")]
	public McpStructuredContent<TData> StructuredContent { get; set; }

	[JsonProperty("content")]
	public McpContent[] Content { get; set; }

	[JsonProperty("isError")]
	public bool IsError { get; set; }
}

sealed class McpStructuredContent<TData>
{
	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class McpContent
{
	[JsonProperty("type")]
	public McpContentType Type { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum McpContentType
{
	[EnumMember(Value = "text")]
	Text,
}

sealed class McpDataEnvelope<TData>
{
	[JsonProperty("data")]
	public TData Data { get; set; }
}

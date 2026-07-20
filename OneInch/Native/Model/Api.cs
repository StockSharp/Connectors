namespace StockSharp.OneInch.Native.Model;

sealed class OneInchTokenInfo
{
	[JsonProperty("address")]
	public string Address { get; init; }
	[JsonProperty("symbol")]
	public string Symbol { get; init; }
	[JsonProperty("name")]
	public string Name { get; init; }
	[JsonProperty("decimals")]
	public int Decimals { get; init; }
	[JsonProperty("logoURI")]
	public string LogoUri { get; init; }
	[JsonProperty("domainVersion")]
	public string DomainVersion { get; init; }
	[JsonProperty("eip2612")]
	public bool? IsEip2612 { get; init; }
	[JsonProperty("isFoT")]
	public bool? IsFeeOnTransfer { get; init; }
	[JsonProperty("tags")]
	public string[] Tags { get; init; }
}

sealed class OneInchQuoteResponse
{
	[JsonProperty("srcToken")]
	public OneInchTokenInfo SourceToken { get; init; }
	[JsonProperty("dstToken")]
	public OneInchTokenInfo DestinationToken { get; init; }
	[JsonProperty("dstAmount")]
	public string DestinationAmount { get; init; }
	[JsonProperty("gas")]
	public long? Gas { get; init; }
}

sealed class OneInchSwapResponse
{
	[JsonProperty("srcToken")]
	public OneInchTokenInfo SourceToken { get; init; }
	[JsonProperty("dstToken")]
	public OneInchTokenInfo DestinationToken { get; init; }
	[JsonProperty("dstAmount")]
	public string DestinationAmount { get; init; }
	[JsonProperty("tx")]
	public OneInchTransactionData Transaction { get; init; }
}

sealed class OneInchTransactionData
{
	[JsonProperty("from")]
	public string From { get; init; }
	[JsonProperty("to")]
	public string To { get; init; }
	[JsonProperty("data")]
	public string Data { get; init; }
	[JsonProperty("value")]
	public string Value { get; init; }
	[JsonProperty("gasPrice")]
	public string GasPrice { get; init; }
	[JsonProperty("gas")]
	public long Gas { get; init; }
	[JsonProperty("gasUsed")]
	public long? GasUsed { get; init; }
}

sealed class OneInchSpenderResponse
{
	[JsonProperty("address")]
	public string Address { get; init; }
}

sealed class OneInchApiError
{
	[JsonProperty("error")]
	public string Error { get; init; }
	[JsonProperty("description")]
	public string Description { get; init; }
	[JsonProperty("requestId")]
	public string RequestId { get; init; }
}

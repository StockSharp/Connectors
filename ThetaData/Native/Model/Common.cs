namespace StockSharp.ThetaData.Native.Model;

sealed class ThetaResponse<T>
{
	[JsonProperty("response")]
	public T[] Response { get; set; }
}

sealed class ThetaContractData<T>
{
	[JsonProperty("contract")]
	public ThetaContract Contract { get; set; }

	[JsonProperty("data")]
	public T[] Data { get; set; }
}

sealed class ThetaContract
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("root")]
	public string Root { get; set; }

	[JsonProperty("security_type")]
	public string SecurityType { get; set; }

	[JsonProperty("expiration")]
	public string Expiration { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("right")]
	public string Right { get; set; }
}

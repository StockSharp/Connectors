namespace StockSharp.Glassnode.Native.Model;

sealed class GlassnodeAssetsResponse
{
	[JsonProperty("data")]
	public GlassnodeAsset[] Data { get; set; }
}

sealed class GlassnodeAsset
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("external_ids")]
	public GlassnodeExternalIds ExternalIds { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("asset_type")]
	public GlassnodeAssetTypes Type { get; set; }

	[JsonProperty("blockchains")]
	public GlassnodeBlockchain[] Blockchains { get; set; }

	[JsonProperty("logo_url")]
	public string LogoUrl { get; set; }

	[JsonProperty("semantic_tags")]
	public string[] SemanticTags { get; set; }

	[JsonProperty("default_network")]
	public string DefaultNetwork { get; set; }
}

sealed class GlassnodeExternalIds
{
	[JsonProperty("ccdata")]
	public string CcData { get; set; }

	[JsonProperty("coinmarketcap")]
	public string CoinMarketCap { get; set; }

	[JsonProperty("coingecko")]
	public string CoinGecko { get; set; }
}

sealed class GlassnodeBlockchain
{
	[JsonProperty("blockchain")]
	public string Blockchain { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("decimals")]
	public int? Decimals { get; set; }

	[JsonProperty("on_chain_support")]
	public bool IsOnChainSupported { get; set; }
}

sealed class GlassnodeValuePoint
{
	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("v")]
	public decimal? Value { get; set; }
}

sealed class GlassnodeOhlcPoint
{
	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("o")]
	public GlassnodeOhlcValue Values { get; set; }

	[JsonProperty("v")]
	public GlassnodeOhlcValue AlternateValues { get; set; }

	public GlassnodeOhlcValue GetValues()
		=> Values ?? AlternateValues;
}

sealed class GlassnodeOhlcValue
{
	[JsonProperty("o")]
	public decimal? Open { get; set; }

	[JsonProperty("h")]
	public decimal? High { get; set; }

	[JsonProperty("l")]
	public decimal? Low { get; set; }

	[JsonProperty("c")]
	public decimal? Close { get; set; }

	[JsonIgnore]
	public bool IsComplete => Open is not null && High is not null &&
		Low is not null && Close is not null;
}

sealed class GlassnodeErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }

	[JsonProperty("status")]
	public GlassnodeErrorStatus Status { get; set; }
}

sealed class GlassnodeErrorStatus
{
	[JsonProperty("error_code")]
	public int? ErrorCode { get; set; }

	[JsonProperty("error_message")]
	public string ErrorMessage { get; set; }
}

namespace StockSharp.Hyperliquid.Native.Spot.Model;

class Meta
{
	[JsonProperty("universe")]
	public AssetInfo[] Universe { get; set; }

	[JsonProperty("tokens")]
	public TokenInfo[] Tokens { get; set; }
}

class AssetInfo
{
	[JsonProperty("tokens")]
	public int[] Tokens { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("index")]
	public int Index { get; set; }

	[JsonProperty("isCanonical")]
	public bool? IsCanonical { get; set; }
}

class TokenInfo
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("szDecimals")]
	public int SzDecimals { get; set; }

	[JsonProperty("weiDecimals")]
	public int? WeiDecimals { get; set; }

	[JsonProperty("index")]
	public int Index { get; set; }

	[JsonProperty("tokenId")]
	public string TokenId { get; set; }

	[JsonProperty("isCanonical")]
	public bool? IsCanonical { get; set; }

	[JsonProperty("evmContract")]
	public string EvmContract { get; set; }

	[JsonProperty("fullName")]
	public string FullName { get; set; }
}

class AssetCtx
{
	[JsonProperty("dayNtlVlm")]
	public string DayNtlVlm { get; set; }

	[JsonProperty("markPx")]
	public string MarkPx { get; set; }

	[JsonProperty("midPx")]
	public string MidPx { get; set; }

	[JsonProperty("prevDayPx")]
	public string PrevDayPx { get; set; }

	[JsonProperty("circulatingSupply")]
	public string CirculatingSupply { get; set; }

	[JsonProperty("coin")]
	public string Coin { get; set; }
}

class WsActiveAssetContext
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("ctx")]
	public AssetCtx Ctx { get; set; }
}

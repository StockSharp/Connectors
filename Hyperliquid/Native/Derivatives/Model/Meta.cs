namespace StockSharp.Hyperliquid.Native.Derivatives.Model;

class Meta
{
	[JsonProperty("universe")]
	public AssetInfo[] Universe { get; set; }

	[JsonProperty("collateralToken")]
	public int CollateralToken { get; set; }
}

class AssetInfo
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("szDecimals")]
	public int SzDecimals { get; set; }

	[JsonProperty("maxLeverage")]
	public int? MaxLeverage { get; set; }

	[JsonProperty("isDelisted")]
	public bool? IsDelisted { get; set; }
}

class AssetCtx
{
	[JsonProperty("funding")]
	public string Funding { get; set; }

	[JsonProperty("openInterest")]
	public string OpenInterest { get; set; }

	[JsonProperty("prevDayPx")]
	public string PrevDayPx { get; set; }

	[JsonProperty("dayNtlVlm")]
	public string DayNtlVlm { get; set; }

	[JsonProperty("premium")]
	public string Premium { get; set; }

	[JsonProperty("oraclePx")]
	public string OraclePx { get; set; }

	[JsonProperty("markPx")]
	public string MarkPx { get; set; }

	[JsonProperty("midPx")]
	public string MidPx { get; set; }

	[JsonProperty("impactPxs")]
	public string[] ImpactPxs { get; set; }

	[JsonProperty("dayBaseVlm")]
	public string DayBaseVlm { get; set; }
}

class WsActiveAssetContext
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("ctx")]
	public AssetCtx Ctx { get; set; }
}

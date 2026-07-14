namespace StockSharp.Hyperliquid.Native.Derivatives.Model;

class ClearinghouseState
{
	[JsonProperty("crossMarginSummary")]
	public MarginSummary CrossMarginSummary { get; set; }

	[JsonProperty("withdrawable")]
	public string Withdrawable { get; set; }

	[JsonProperty("assetPositions")]
	public AssetPosition[] AssetPositions { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }
}

class MarginSummary
{
	[JsonProperty("accountValue")]
	public string AccountValue { get; set; }

	[JsonProperty("totalNtlPos")]
	public string TotalNtlPos { get; set; }

	[JsonProperty("totalRawUsd")]
	public string TotalRawUsd { get; set; }

	[JsonProperty("totalMarginUsed")]
	public string TotalMarginUsed { get; set; }
}

class AssetPosition
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("position")]
	public PositionState Position { get; set; }
}

class PositionState
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("szi")]
	public string Szi { get; set; }

	[JsonProperty("entryPx")]
	public string EntryPx { get; set; }

	[JsonProperty("positionValue")]
	public string PositionValue { get; set; }

	[JsonProperty("unrealizedPnl")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("returnOnEquity")]
	public string ReturnOnEquity { get; set; }

	[JsonProperty("liquidationPx")]
	public string LiquidationPx { get; set; }

	[JsonProperty("marginUsed")]
	public string MarginUsed { get; set; }

	[JsonProperty("maxLeverage")]
	public decimal? MaxLeverage { get; set; }

	[JsonProperty("leverage")]
	public LeverageInfo Leverage { get; set; }
}

class LeverageInfo
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("value")]
	public decimal? Value { get; set; }
}

namespace StockSharp.Hyperliquid.Native.Common.Model;

class UserFill
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("px")]
	public string Px { get; set; }

	[JsonProperty("sz")]
	public string Sz { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("closedPnl")]
	public string ClosedPnl { get; set; }

	[JsonProperty("oid")]
	public long Oid { get; set; }

	[JsonProperty("tid")]
	public long Tid { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeToken")]
	public string FeeToken { get; set; }
}

class WsUserFills
{
	[JsonProperty("isSnapshot")]
	public bool IsSnapshot { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("fills")]
	public UserFill[] Fills { get; set; }
}

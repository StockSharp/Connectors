namespace StockSharp.Hyperliquid.Native.Common.Model;

class OpenOrder
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("limitPx")]
	public string LimitPx { get; set; }

	[JsonProperty("sz")]
	public string Sz { get; set; }

	[JsonProperty("oid")]
	public long Oid { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("origSz")]
	public string OrigSz { get; set; }

	[JsonProperty("reduceOnly")]
	public bool? ReduceOnly { get; set; }

	[JsonProperty("triggerPx")]
	public string TriggerPx { get; set; }

	[JsonProperty("isTrigger")]
	public bool? IsTrigger { get; set; }

	[JsonProperty("isPositionTpsl")]
	public bool? IsPositionTpSl { get; set; }

	[JsonProperty("orderType")]
	public object OrderType { get; set; }

	[JsonProperty("triggerCondition")]
	public string TriggerCondition { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("cloid")]
	public string Cloid { get; set; }
}

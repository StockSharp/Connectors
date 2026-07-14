namespace StockSharp.GateIO.Native.Futures.Model;

class Balance
{
	[JsonProperty("timestamp_ms")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Timestamp { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("change")]
	public double? Change { get; set; }

	[JsonProperty("total")]
	public double? Total { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("freeze")]
	public double? Freeze { get; set; }

	[JsonProperty("locked")]
	public double? Locked { get; set; }

	[JsonProperty("freeze_change")]
	public double? FreezeChange { get; set; }

	[JsonProperty("change_type")]
	public string ChangeType { get; set; }

	[JsonProperty("unrealised_pnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("position_margin")]
	public double? PositionMargin { get; set; }
}
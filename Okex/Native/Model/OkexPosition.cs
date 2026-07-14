namespace StockSharp.Okex.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OkexPosition
{
	[JsonProperty("instType")]
	public string InstType { get; set; }

	[JsonProperty("mgnMode")]
	public string MarginMode { get; set; }

	[JsonProperty("posId")]
	public string PosId { get; set; }

	[JsonProperty("posSide")]
	public string PosSide { get; set; }

	[JsonProperty("pos")]
	public decimal? Position { get; set; }

	[JsonProperty("posCcy")]
	public string PosCcy { get; set; }

	[JsonProperty("availPos")]
	public decimal? AvailPos { get; set; }

	[JsonProperty("avgPx")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("upl")]
	public decimal? UnrealizedPnL { get; set; }

	[JsonProperty("uplRatio")]
	public decimal? UplRatio { get; set; }

	[JsonProperty("instId")]
	public string InstrumentId { get; set; }

	[JsonProperty("lever")]
	public decimal? Leverage { get; set; }

	[JsonProperty("liqPx")]
	public decimal? EstimatedLiquidationPrice { get; set; }

	[JsonProperty("imr")]
	public decimal? Imr { get; set; }

	[JsonProperty("margin")]
	public decimal? Margin { get; set; }

	[JsonProperty("mgnRatio")]
	public decimal? MgnRatio { get; set; }

	[JsonProperty("mmr")]
	public decimal? Mmr { get; set; }

	[JsonProperty("liab")]
	public decimal? Liab { get; set; }

	[JsonProperty("liabCcy")]
	public string LiabCcy { get; set; }

	[JsonProperty("interest")]
	public decimal? Interest { get; set; }

	[JsonProperty("tradeId")]
	public long LastTradeId { get; set; }

	[JsonProperty("optVal")]
	public string OptVal { get; set; }

	[JsonProperty("notionalUsd")]
	public decimal? NotionalUsd { get; set; }

	/// <summary>
	/// auto decrease level (1 to 5)
	/// </summary>
	[JsonProperty("adl")]
	public int? Adl { get; set; }

	[JsonProperty("ccy")]
	public string Ccy { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("cTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? CreateAt { get; set; }

	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdatedAt { get; set; }

	public override string ToString() => $"{nameof(OkexPosition)} instId={InstrumentId} ({InstType}), mgn={MarginMode}, side={PosSide}, pos={Position}";
}
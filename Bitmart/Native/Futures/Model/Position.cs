namespace StockSharp.Bitmart.Native.Futures.Model;

class Position
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("current_fee")]
	public double? CurrentFee { get; set; }

	[JsonProperty("open_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime OpenTimestamp { get; set; }

	[JsonProperty("current_value")]
	public double? CurrentValue { get; set; }

	[JsonProperty("mark_price")]
	public double? MarkPrice { get; set; }

	[JsonProperty("position_value")]
	public double? PositionValue { get; set; }

	[JsonProperty("position_cross")]
	public double? PositionCross { get; set; }

	[JsonProperty("maintenance_margin")]
	public double? MaintenanceMargin { get; set; }

	[JsonProperty("close_vol")]
	public double? CloseVol { get; set; }

	[JsonProperty("close_avg_price")]
	public double? CloseAvgPrice { get; set; }

	[JsonProperty("open_avg_price")]
	public double? OpenAvgPrice { get; set; }

	[JsonProperty("entry_price")]
	public double? EntryPrice { get; set; }

	[JsonProperty("current_amount")]
	public double? CurrentAmount { get; set; }

	[JsonProperty("unrealized_value")]
	public double? UnrealizedValue { get; set; }

	[JsonProperty("realized_value")]
	public double? RealizedValue { get; set; }

	// position type
	// 1=long
	// 2=short
	[JsonProperty("position_type")]
	public int PositionType { get; set; }
}

class SocketPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("hold_volume")]
	public double? HoldVolume { get; set; }

	// position type
	// 1=long
	// 2=short
	[JsonProperty("position_type")]
	public int PositionType { get; set; }

	// Open position type
	// 1=isolated
	// 2=cross
	[JsonProperty("open_type")]
	public int OpenType { get; set; }

	[JsonProperty("frozen_volume")]
	public double? FrozenVolume { get; set; }

	[JsonProperty("close_volume")]
	public double? CloseVolume { get; set; }

	[JsonProperty("hold_avg_price")]
	public double? HoldAvgPrice { get; set; }

	[JsonProperty("close_avg_price")]
	public double? CloseAvgPrice { get; set; }

	[JsonProperty("open_avg_price")]
	public double? OpenAvgPrice { get; set; }

	[JsonProperty("liquidate_price")]
	public double? LiquidatePrice { get; set; }

	[JsonProperty("create_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("update_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }
}
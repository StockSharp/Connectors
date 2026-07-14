namespace StockSharp.Bitmex.Native.Model;

class Execution : Order
{
	[JsonProperty("execID")]
	public string ExecId { get; set; }

	[JsonProperty("lastQty")]
	public double? LastQty { get; set; }

	[JsonProperty("lastPx")]
	public double? LastPx { get; set; }

	[JsonProperty("underlyingLastPx")]
	public double? UnderlyingLastPx { get; set; }

	[JsonProperty("lastMkt")]
	public string LastMkt { get; set; }

	[JsonProperty("lastLiquidityInd")]
	public string LastLiquidityInd { get; set; }

	[JsonProperty("execType")]
	public string ExecType { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("tradePublishIndicator")]
	public string TradePublishIndicator { get; set; }

	[JsonProperty("trdMatchID")]
	public string TrdMatchId { get; set; }

	[JsonProperty("execCost")]
	public double? ExecCost { get; set; }

	[JsonProperty("execComm")]
	public double? ExecComm { get; set; }

	[JsonProperty("homeNotional")]
	public double? HomeNotional { get; set; }

	[JsonProperty("foreignNotional")]
	public double? ForeignNotional { get; set; }

	[JsonProperty("feeType")]
	public string FeeType { get; set; }
}
namespace StockSharp.Huobi.Native.Futures.Model;

class Position
{
	[JsonProperty("symbol")]
	public string Asset { get; set; }

	[JsonProperty("contract_code")]
	public string ContractCode { get; set; }

	[JsonProperty("contract_type")]
	public string ContractType { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("frozen")]
	public double? Frozen { get; set; }

	[JsonProperty("cost_open")]
	public double? CostOpen { get; set; }

	[JsonProperty("cost_hold")]
	public double? CostHold { get; set; }

	[JsonProperty("profit_unreal")]
	public double? ProfitUnreal { get; set; }

	[JsonProperty("profit_rate")]
	public double? ProfitRate { get; set; }

	[JsonProperty("profit")]
	public double? Profit { get; set; }

	[JsonProperty("position_margin")]
	public double? PositionMargin { get; set; }

	[JsonProperty("lever_rate")]
	public double? LeverRate { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }
}
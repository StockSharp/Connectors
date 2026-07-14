namespace StockSharp.Poloniex.Native.Model;

class PoloniexCurrency
{
	[JsonProperty("id")]
	public int Id { get; set; }

	[JsonProperty("maxDailyWithdrawal")]
	public double? MaxDailyWithdrawal { get; set; }

	[JsonProperty("txFee")]
	public double? TxFee { get; set; }

	[JsonProperty("minConf")]
	public int MinConf { get; set; }

	[JsonProperty("disabled")]
	public int Disabled { get; set; }
}
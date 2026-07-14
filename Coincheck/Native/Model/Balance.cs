namespace StockSharp.Coincheck.Native.Model;

class Balance
{
	[JsonProperty("jpy")]
	public double Jpy { get; set; }

	[JsonProperty("btc")]
	public double Btc { get; set; }

	[JsonProperty("jpy_reserved")]
	public double JpyReserved { get; set; }

	[JsonProperty("btc_reserved")]
	public double BtcReserved { get; set; }

	[JsonProperty("jpy_lend_in_use")]
	public double JpyLendInUse { get; set; }

	[JsonProperty("btc_lend_in_use")]
	public double BtcLendInUse { get; set; }

	[JsonProperty("jpy_lent")]
	public double JpyLent { get; set; }

	[JsonProperty("btc_lent")]
	public double BtcLent { get; set; }

	[JsonProperty("jpy_debt")]
	public double JpyDebt { get; set; }

	[JsonProperty("btc_debt")]
	public double BtcDebt { get; set; }
}
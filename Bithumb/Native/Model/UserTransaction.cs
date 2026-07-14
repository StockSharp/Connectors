namespace StockSharp.Bithumb.Native.Model;

class UserTransaction
{
	[JsonProperty("search")]
	public int Search { get; set; }

	[JsonProperty("transfer_date")]
	public long TransferDate { get; set; }

	[JsonProperty("units")]
	public string Units { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("btc1krw")]
	public decimal Btc1Krw { get; set; }

	[JsonProperty("eth1krw")]
	public decimal Eth1Krw { get; set; }

	[JsonProperty("dash1krw")]
	public decimal Dash1Krw { get; set; }

	[JsonProperty("ltc1krw")]
	public decimal Ltc1Krw { get; set; }

	[JsonProperty("etc1krw")]
	public decimal Etc1Krw { get; set; }

	[JsonProperty("xrp1krw")]
	public decimal Xrp1Krw { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("btc_remain")]
	public decimal BtcRemain { get; set; }

	[JsonProperty("eth_remain")]
	public decimal EthRemain { get; set; }

	[JsonProperty("dash_remain")]
	public decimal DashRemain { get; set; }

	[JsonProperty("ltc_remain")]
	public decimal LtcRemain { get; set; }

	[JsonProperty("etc_remain")]
	public decimal EtcRemain { get; set; }

	[JsonProperty("xrp_remain")]
	public decimal XrpRemain { get; set; }

	[JsonProperty("krw_remain")]
	public decimal KrwRemain { get; set; }
}
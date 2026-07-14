namespace StockSharp.DXtrade.Native.Model;

class AccountPortfolio
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("version")]
	public int Version { get; set; }

	[JsonProperty("balances")]
	public Balance[] Balances { get; set; }

	[JsonProperty("positions")]
	public Position[] Positions { get; set; }

	[JsonProperty("orders")]
	public Order[] Orders { get; set; }
}
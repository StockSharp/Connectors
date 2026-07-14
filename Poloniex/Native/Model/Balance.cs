namespace StockSharp.Poloniex.Native.Model;

class Balance
{
	[JsonProperty("available")]
	public double Available { get; set; }

	[JsonProperty("onOrders")]
	public double OnOrders { get; set; }

	[JsonProperty("btcValue")]
	public double BtcValue { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class SocketBalance
{
	public string EventType { get; set; }

	public int CurrencyId { get; set; }

	public string Wallet { get; set; }

	public double Amount { get; set; }
}
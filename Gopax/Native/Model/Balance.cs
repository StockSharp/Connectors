namespace StockSharp.Gopax.Native.Model;

class Balance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("avail")]
	public double Avail { get; set; }

	[JsonProperty("hold")]
	public double Hold { get; set; }

	[JsonProperty("pendingWithdrawal")]
	public int PendingWithdrawal { get; set; }
}
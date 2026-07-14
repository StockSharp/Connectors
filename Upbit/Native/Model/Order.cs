namespace StockSharp.Upbit.Native.Model;

class Order
{
	[JsonProperty("uuid")]
	public string Id { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("ord_type")]
	public string OrdType { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("avg_price")]
	public double? AvgPrice { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("created_at")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("remaining_volume")]
	public double? RemainingVolume { get; set; }

	[JsonProperty("reserved_fee")]
	public double? ReservedFee { get; set; }

	[JsonProperty("remaining_fee")]
	public double? RemainingFee { get; set; }

	[JsonProperty("paid_fee")]
	public double? PaidFee { get; set; }

	[JsonProperty("locked")]
	public double? Locked { get; set; }

	[JsonProperty("executed_volume")]
	public double? ExecutedVolume { get; set; }

	[JsonProperty("trades_count")]
	public int? TradesCount { get; set; }

	[JsonProperty("trades")]
	public IEnumerable<OwnTrade> Trades { get; set; }
}
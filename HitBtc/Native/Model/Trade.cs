namespace StockSharp.HitBtc.Native.Model;

class Trade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Time { get; set; }
}

class WsTrade
{
	[JsonProperty("i")]
	public long Id { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("s")]
	public string Side { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	public Trade ToTrade()
		=> new()
		{
			Id = Id,
			Price = Price,
			Quantity = Quantity,
			Side = Side,
			Time = Timestamp.FromHitBtcMilliseconds(),
		};
}

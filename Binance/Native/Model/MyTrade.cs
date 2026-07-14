namespace StockSharp.Binance.Native.Model;

class MyTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public long TradeId { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("orderListId")]
	public long OrderListId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal Qty { get; set; }

	[JsonProperty("commission")]
	public decimal Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	public Sides Direction { get; set; }

	[JsonProperty("side")]
	private string Side { set => Direction = value == "SELL" ? Sides.Sell : Sides.Buy; }

	[JsonProperty("isBuyer")]
	private bool IsBuyer { set => Direction = value ? Sides.Buy : Sides.Sell; }

	[JsonProperty("buyer")]
	private bool Buyer { set => IsBuyer = value; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }

	[JsonProperty("maker")]
	private bool Maker { set => IsMaker = value; }

	[JsonProperty("isBestMatch")]
	public bool IsBestMatch { get; set; }

	[JsonProperty("isIsolated")]
	public bool? IsIsolated { get; set; }
}

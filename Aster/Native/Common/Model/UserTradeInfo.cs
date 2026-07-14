namespace StockSharp.Aster.Native.Common.Model;

class UserTradeInfo
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public long? TradeId { get; set; }

	[JsonProperty("orderId")]
	public long? OrderId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("commission")]
	public string Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("isBuyer")]
	public bool? IsBuyer { get; set; }

	[JsonProperty("buyer")]
	public bool? Buyer { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	public Sides? GetSide()
	{
		if (!Side.IsEmpty())
			return Side.ToSide();

		if (IsBuyer is bool isBuyer)
			return isBuyer ? Sides.Buy : Sides.Sell;

		if (Buyer is bool buyer)
			return buyer ? Sides.Buy : Sides.Sell;

		return null;
	}
}

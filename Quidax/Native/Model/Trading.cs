namespace StockSharp.Quidax.Native.Model;

sealed class QuidaxMoney
{
	[JsonProperty("unit")]
	public string Unit { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }
}

sealed class QuidaxOrderMarket
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("base_unit")]
	public string BaseUnit { get; set; }

	[JsonProperty("quote_unit")]
	public string QuoteUnit { get; set; }
}

sealed class QuidaxOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("reference")]
	public string Reference { get; set; }

	[JsonProperty("market")]
	public QuidaxOrderMarket Market { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public QuidaxMoney Price { get; set; }

	[JsonProperty("volume")]
	public QuidaxMoney Volume { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("avg_price")]
	public QuidaxMoney AveragePrice { get; set; }

	[JsonProperty("origin_volume")]
	public QuidaxMoney OriginVolume { get; set; }

	[JsonProperty("executed_volume")]
	public QuidaxMoney ExecutedVolume { get; set; }

	[JsonProperty("trades_count")]
	public int TradesCount { get; set; }

	[JsonProperty("created_at")]
	public DateTime? CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public DateTime? UpdatedAt { get; set; }

	[JsonProperty("done_at")]
	public DateTime? DoneAt { get; set; }

	[JsonProperty("trades")]
	public QuidaxTrade[] Trades { get; set; }

	[JsonIgnore]
	public decimal OriginalVolume
		=> OriginVolume?.Amount ?? Volume?.Amount ?? 0;

	[JsonIgnore]
	public decimal RemainingVolume
		=> (OriginalVolume -
			(ExecutedVolume?.Amount ?? 0)).Max(0);
}

sealed class QuidaxWallet
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public decimal Available { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }

	[JsonProperty("staked")]
	public decimal Staked { get; set; }

	[JsonIgnore]
	public decimal Total => Available + Locked + Staked;
}

sealed class QuidaxPlaceOrderRequest
{
	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("ord_type")]
	public string OrderType { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("volume")]
	public string Volume { get; init; }
}

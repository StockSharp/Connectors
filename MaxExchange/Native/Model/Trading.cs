namespace StockSharp.MaxExchange.Native.Model;

sealed class MaxExchangeBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public decimal Amount { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }

	[JsonProperty("staked")]
	public decimal? Staked { get; set; }

	[JsonIgnore]
	public decimal Available
		=> (Amount - Locked).Max(0);
}

class MaxExchangeOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("wallet_type")]
	public string WalletType { get; set; }

	[JsonProperty("market")]
	public string Pair { get; set; }

	[JsonProperty("client_oid")]
	public string ClientOid { get; set; }

	[JsonProperty("group_id")]
	public long? GroupId { get; set; }

	[JsonProperty("side")]
	public string Action { get; set; }

	[JsonProperty("state")]
	public string Status { get; set; }

	[JsonProperty("ord_type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("avg_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("volume")]
	public decimal OriginalAmount { get; set; }

	[JsonProperty("remaining_volume")]
	public decimal RemainingAmount { get; set; }

	[JsonProperty("executed_volume")]
	public decimal ExecutedAmount { get; set; }

	[JsonProperty("trades_count")]
	public int TradesCount { get; set; }

	[JsonProperty("created_at")]
	public long CreatedTimestamp { get; set; }

	[JsonProperty("updated_at")]
	public long UpdatedTimestamp { get; set; }

	[JsonIgnore]
	public long Timestamp => UpdatedTimestamp;

	[JsonIgnore]
	public long? ClientId
		=> long.TryParse(ClientOid, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var value)
				? value
				: null;

	[JsonIgnore]
	public string TimeInForce
		=> Type.EqualsIgnoreCase("post_only")
			? "POST_ONLY"
			: Type.EqualsIgnoreCase("ioc_limit")
				? "IOC"
				: "GTC";

	[JsonIgnore]
	public string Condition => null;
}

sealed class MaxExchangePrivateTrade
{
	[JsonProperty("id")]
	public string TradeId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("wallet_type")]
	public string WalletType { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal BaseAmount { get; set; }

	[JsonProperty("funds")]
	public decimal QuoteAmount { get; set; }

	[JsonProperty("market")]
	public string Pair { get; set; }

	[JsonProperty("side")]
	public string Action { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("fee_currency")]
	public string FeeSymbol { get; set; }

	[JsonProperty("liquidity")]
	public string Liquidity { get; set; }

	[JsonProperty("created_at")]
	public long CreatedTimestamp { get; set; }

	[JsonIgnore]
	public long Timestamp => CreatedTimestamp;
}

sealed class MaxExchangePlaceOrderRequest
{
	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("volume")]
	public string Volume { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("client_oid")]
	public string ClientOid { get; init; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; init; }

	[JsonProperty("ord_type")]
	public string OrderType { get; init; }
}

sealed class MaxExchangePlaceOrderResult : MaxExchangeOrder
{
	[JsonIgnore]
	public string OrderId => Id;
}

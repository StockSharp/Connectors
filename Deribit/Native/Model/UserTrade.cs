namespace StockSharp.Deribit.Native.Model;

class UserTrade : Trade
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	///// <summary>
	///// "t" - if the subscriber is taker,
	///// "m" - the subscriber is maker,
	///// "" - if the trade is between other users (trade without subsriber's participation)
	///// can be used to quickly detect subscriber's trades between other user trades
	///// </summary>
	//[JsonProperty("me")]
	//public string Me { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("fee_currency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("state")]
	public string OrderState { get; set; }

	[JsonProperty("liquidity")]
	public string Liquidity { get; set; }

	[JsonProperty("label")]
	public string Label { get; set; }
}
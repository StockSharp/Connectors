namespace StockSharp.BitGo.Native.Model;

sealed class BitGoSocketRequest
{
	[JsonProperty("type")]
	public BitGoSocketActions Type { get; set; }

	[JsonProperty("channel")]
	public BitGoSocketChannels Channel { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("productId")]
	public string ProductId { get; set; }

	[JsonProperty("includeCumulative")]
	public bool? IsIncludeCumulative { get; set; }
}

sealed class BitGoSocketHeader
{
	[JsonProperty("type")]
	public BitGoSocketMessageTypes? Type { get; set; }

	[JsonProperty("channel")]
	public BitGoSocketChannels? Channel { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("errorName")]
	public string ErrorName { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class BitGoSubscriptionResponse
{
	[JsonProperty("type")]
	public BitGoSocketMessageTypes Type { get; set; }

	[JsonProperty("channel")]
	public BitGoSocketChannels Channel { get; set; }

	[JsonProperty("status")]
	public BitGoSubscriptionStatuses Status { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("productId")]
	public string ProductId { get; set; }

	[JsonProperty("session_id")]
	public string SessionId { get; set; }
}

[JsonConverter(typeof(BitGoBookLevelConverter))]
sealed class BitGoBookLevel
{
	public decimal Price { get; init; }
	public decimal Size { get; init; }
	public decimal? CumulativeSize { get; init; }
}

sealed class BitGoBookMessage
{
	[JsonProperty("channel")]
	public BitGoSocketChannels Channel { get; set; }

	[JsonProperty("type")]
	public BitGoSocketMessageTypes Type { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("bids")]
	public BitGoBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BitGoBookLevel[] Asks { get; set; }
}

sealed class BitGoOrderUpdate : BitGoOrder
{
	[JsonProperty("channel")]
	public BitGoSocketChannels Channel { get; set; }

	[JsonProperty("execType")]
	public BitGoExecutionTypes ExecType { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("fillQuantity")]
	public string FillQuantity { get; set; }

	[JsonProperty("fillPrice")]
	public string FillPrice { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }
}

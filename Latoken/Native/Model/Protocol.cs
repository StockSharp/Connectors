namespace StockSharp.LATOKEN.Native.Model;

sealed class LatokenUser
{
	[JsonProperty("id")]
	public string Id { get; set; }
}

sealed class LatokenOrderRequest
{
	[JsonProperty("baseCurrency", Order = 1)]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency", Order = 2)]
	public string QuoteCurrency { get; set; }

	[JsonProperty("side", Order = 3)]
	public string Side { get; set; }

	[JsonProperty("condition", Order = 4)]
	public string Condition { get; set; }

	[JsonProperty("type", Order = 5)]
	public string Type { get; set; }

	[JsonProperty("clientOrderId", Order = 6)]
	public string ClientOrderId { get; set; }

	[JsonProperty("price", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; set; }

	[JsonProperty("quantity", Order = 8)]
	public string Quantity { get; set; }

	[JsonProperty("timestamp", Order = 9)]
	public long Timestamp { get; set; }
}

sealed class LatokenOrderIdRequest
{
	[JsonProperty("id", Order = 1)]
	public string Id { get; set; }
}

sealed class LatokenOrderReply
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class LatokenWithdrawalRequest
{
	[JsonProperty("twoFaCode", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
	public string TwoFaCode { get; set; }

	[JsonProperty("currencyBinding", Order = 2)]
	public string CurrencyBinding { get; set; }

	[JsonProperty("amount", Order = 3)]
	public string Amount { get; set; }

	[JsonProperty("recipientAddress", Order = 4)]
	public string RecipientAddress { get; set; }

	[JsonProperty("memo", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
	public string Memo { get; set; }
}

sealed class LatokenWithdrawalReply
{
	[JsonProperty("withdrawalId")]
	public string WithdrawalId { get; set; }

	[JsonProperty("codeRequired")]
	public bool CodeRequired { get; set; }
}

sealed class LatokenSubscriptionMessage<T>
{
	[JsonProperty("payload")]
	public T Payload { get; set; }

	[JsonProperty("nonce")]
	public long? Nonce { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}

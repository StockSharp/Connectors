namespace StockSharp.CryptoCom.Native.Model;

sealed class CryptoComWsHeader : CryptoComResponseStatus
{
}

sealed class CryptoComWsRoutingEnvelope : CryptoComResponseStatus
{
	[JsonProperty("result")]
	public CryptoComWsRoutingResult Result { get; set; }
}

sealed class CryptoComWsRoutingResult
{
	[JsonProperty("instrument_name")]
	public string InstrumentName { get; set; }

	[JsonProperty("subscription")]
	public string Subscription { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }
}

sealed class CryptoComWsEnvelope<TItem> : CryptoComResponseStatus
{
	[JsonProperty("result")]
	public CryptoComWsResult<TItem> Result { get; set; }
}

sealed class CryptoComWsResult<TItem>
{
	[JsonProperty("instrument_name")]
	public string InstrumentName { get; set; }

	[JsonProperty("subscription")]
	public string Subscription { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("depth")]
	public int? Depth { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("data")]
	public TItem[] Data { get; set; }
}

sealed class CryptoComWsSubscriptionRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public CryptoComWsSubscriptionParams Parameters { get; init; }

	[JsonProperty("nonce")]
	public long Nonce { get; init; }
}

sealed class CryptoComWsSubscriptionParams
{
	[JsonProperty("channels")]
	public string[] Channels { get; init; }

	[JsonProperty("book_subscription_type")]
	public string BookSubscriptionType { get; init; }

	[JsonProperty("book_update_frequency")]
	public int? BookUpdateFrequency { get; init; }
}

sealed class CryptoComWsAuthRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("api_key")]
	public string ApiKey { get; init; }

	[JsonProperty("sig")]
	public string Signature { get; init; }

	[JsonProperty("nonce")]
	public long Nonce { get; init; }
}

sealed class CryptoComWsHeartbeatResponse
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }
}

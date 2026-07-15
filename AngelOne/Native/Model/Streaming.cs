namespace StockSharp.AngelOne.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneSubscriptionRequest
{
	[JsonProperty("correlationID")]
	public string CorrelationId { get; set; }

	[JsonProperty("action")]
	public int Action { get; set; }

	[JsonProperty("params")]
	public AngelOneSubscriptionParameters Parameters { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneSubscriptionParameters
{
	[JsonProperty("mode")]
	public int Mode { get; set; }

	[JsonProperty("tokenList")]
	public AngelOneTokenGroup[] TokenList { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneTokenGroup
{
	[JsonProperty("exchangeType")]
	public int ExchangeType { get; set; }

	[JsonProperty("tokens")]
	public string[] Tokens { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneStreamError
{
	[JsonProperty("correlationID")]
	public string CorrelationId { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }
}

sealed class AngelOneDepthLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public int OrdersCount { get; set; }
}

sealed class AngelOneMarketTick
{
	public AngelOneFeedModes Mode { get; set; }
	public AngelOneExchangeTypes ExchangeType { get; set; }
	public string Token { get; set; }
	public long SequenceNumber { get; set; }
	public DateTime ServerTime { get; set; }
	public decimal LastPrice { get; set; }
	public decimal LastVolume { get; set; }
	public decimal AveragePrice { get; set; }
	public decimal Volume { get; set; }
	public decimal TotalBuyVolume { get; set; }
	public decimal TotalSellVolume { get; set; }
	public decimal OpenPrice { get; set; }
	public decimal HighPrice { get; set; }
	public decimal LowPrice { get; set; }
	public decimal ClosePrice { get; set; }
	public DateTime? LastTradeTime { get; set; }
	public decimal OpenInterest { get; set; }
	public decimal UpperCircuit { get; set; }
	public decimal LowerCircuit { get; set; }
	public decimal YearHigh { get; set; }
	public decimal YearLow { get; set; }
	public AngelOneDepthLevel[] Bids { get; set; } = [];
	public AngelOneDepthLevel[] Asks { get; set; } = [];
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneOrderUpdate
{
	[JsonProperty("user-id")]
	public string UserId { get; set; }

	[JsonProperty("status-code")]
	public string StatusCode { get; set; }

	[JsonProperty("order-status")]
	public string OrderStatus { get; set; }

	[JsonProperty("error-message")]
	public string ErrorMessage { get; set; }

	[JsonProperty("orderData")]
	public AngelOneOrder Order { get; set; }
}

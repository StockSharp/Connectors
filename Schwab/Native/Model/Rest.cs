namespace StockSharp.Schwab.Native.Model;

sealed class UserPreferences
{
	[JsonProperty("streamerInfo")]
	public StreamerInfo[] StreamerInfo { get; set; }
}

sealed class StreamerInfo
{
	[JsonProperty("schwabClientCustomerId")]
	public string CustomerId { get; set; }

	[JsonProperty("schwabClientCorrelId")]
	public string CorrelId { get; set; }

	[JsonProperty("schwabClientChannel")]
	public string Channel { get; set; }

	[JsonProperty("schwabClientFunctionId")]
	public string FunctionId { get; set; }

	[JsonProperty("streamerSocketUrl")]
	public string SocketUrl { get; set; }
}

sealed class InstrumentLookupResponse
{
	[JsonProperty("instruments")]
	public SchwabInstrument[] Instruments { get; set; }
}

sealed class SchwabInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("assetType")]
	public SchwabAssetTypes? AssetType { get; set; }
}

sealed class CandleResponse
{
	[JsonProperty("candles")]
	public SchwabCandle[] Candles { get; set; }
}

sealed class SchwabCandle
{
	[JsonProperty("datetime")]
	public long Timestamp { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

sealed class AccountResponse
{
	[JsonProperty("securitiesAccount")]
	public SecuritiesAccount Account { get; set; }

	[JsonProperty("hashValue")]
	public string HashValue { get; set; }
}

sealed class SecuritiesAccount
{
	[JsonProperty("accountNumber")]
	public string AccountNumber { get; set; }

	[JsonProperty("hashValue")]
	public string HashValue { get; set; }

	[JsonProperty("currentBalances")]
	public AccountBalances Balances { get; set; }

	[JsonProperty("positions")]
	public AccountPosition[] Positions { get; set; }
}

sealed class AccountBalances
{
	[JsonProperty("cashBalance")]
	public decimal? CashBalance { get; set; }

	[JsonProperty("buyingPower")]
	public decimal? BuyingPower { get; set; }

	[JsonProperty("liquidationValue")]
	public decimal? LiquidationValue { get; set; }
}

sealed class AccountPosition
{
	[JsonProperty("instrument")]
	public OrderInstrument Instrument { get; set; }

	[JsonProperty("longQuantity")]
	public decimal? LongQuantity { get; set; }

	[JsonProperty("shortQuantity")]
	public decimal? ShortQuantity { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("marketValue")]
	public decimal? MarketValue { get; set; }
}

sealed class SchwabOrderRequest
{
	[JsonProperty("orderType")]
	public SchwabOrderTypes OrderType { get; set; }

	[JsonProperty("session")]
	public SchwabSessions Session { get; set; }

	[JsonProperty("duration")]
	public SchwabDurations Duration { get; set; }

	[JsonProperty("orderStrategyType")]
	public SchwabOrderStrategies StrategyType { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Price { get; set; }

	[JsonProperty("orderLegCollection")]
	public OrderLeg[] Legs { get; set; }
}

sealed class SchwabOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderType")]
	public SchwabOrderTypes? OrderType { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("remainingQuantity")]
	public decimal? RemainingQuantity { get; set; }

	[JsonProperty("status")]
	public SchwabOrderStatuses? Status { get; set; }

	[JsonProperty("enteredTime")]
	public DateTime? EnteredTime { get; set; }

	[JsonProperty("orderLegCollection")]
	public OrderLeg[] Legs { get; set; }
}

sealed class OrderLeg
{
	[JsonProperty("instruction")]
	public SchwabInstructions? Instruction { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("instrument")]
	public OrderInstrument Instrument { get; set; }
}

sealed class OrderInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("assetType")]
	public SchwabAssetTypes? AssetType { get; set; }
}

sealed class AccountActivityPayload
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("status")]
	public SchwabOrderStatuses? Status { get; set; }

	[JsonProperty("orderStatus")]
	public SchwabOrderStatuses? OrderStatus { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("order")]
	public AccountActivityPayload Order { get; set; }
}

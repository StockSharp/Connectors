namespace StockSharp.TastyTrade.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum TastyOrderTypes
{
	[EnumMember(Value = "Limit")]
	Limit,
	[EnumMember(Value = "Market")]
	Market,
	[EnumMember(Value = "Marketable Limit")]
	MarketableLimit,
	[EnumMember(Value = "Notional Market")]
	NotionalMarket,
	[EnumMember(Value = "Stop")]
	Stop,
	[EnumMember(Value = "Stop Limit")]
	StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TastyTimeInForces
{
	[EnumMember(Value = "Day")]
	Day,
	[EnumMember(Value = "Ext")]
	Extended,
	[EnumMember(Value = "Ext Overnight")]
	ExtendedOvernight,
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,
	[EnumMember(Value = "GTC Ext")]
	GoodTillCancelledExtended,
	[EnumMember(Value = "GTC Ext Overnight")]
	GoodTillCancelledExtendedOvernight,
	[EnumMember(Value = "GTD")]
	GoodTillDate,
	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TastyPriceEffects
{
	[EnumMember(Value = "Credit")]
	Credit,
	[EnumMember(Value = "Debit")]
	Debit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TastyLegActions
{
	[EnumMember(Value = "Allocate")]
	Allocate,
	[EnumMember(Value = "Buy")]
	Buy,
	[EnumMember(Value = "Buy to Close")]
	BuyToClose,
	[EnumMember(Value = "Buy to Open")]
	BuyToOpen,
	[EnumMember(Value = "Sell")]
	Sell,
	[EnumMember(Value = "Sell to Close")]
	SellToClose,
	[EnumMember(Value = "Sell to Open")]
	SellToOpen,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TastyOrderStatuses
{
	[EnumMember(Value = "Cancelled")]
	Cancelled,
	[EnumMember(Value = "Canceled")]
	Canceled,
	[EnumMember(Value = "Cancel Requested")]
	CancelRequested,
	[EnumMember(Value = "Contingent")]
	Contingent,
	[EnumMember(Value = "Expired")]
	Expired,
	[EnumMember(Value = "Filled")]
	Filled,
	[EnumMember(Value = "In Flight")]
	InFlight,
	[EnumMember(Value = "Live")]
	Live,
	[EnumMember(Value = "Received")]
	Received,
	[EnumMember(Value = "Replace Requested")]
	ReplaceRequested,
	[EnumMember(Value = "Rejected")]
	Rejected,
	[EnumMember(Value = "Removed")]
	Removed,
	[EnumMember(Value = "Partially Removed")]
	PartiallyRemoved,
	[EnumMember(Value = "Routed")]
	Routed,
	[EnumMember(Value = "Unknown")]
	Unknown,
}

sealed class TastyOrderRequest
{
	[JsonProperty("order-type")]
	public TastyOrderTypes OrderType { get; set; }

	[JsonProperty("time-in-force")]
	public TastyTimeInForces TimeInForce { get; set; }

	[JsonProperty("gtc-date", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? GoodTillDate { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Price { get; set; }

	[JsonProperty("price-effect", NullValueHandling = NullValueHandling.Ignore)]
	public TastyPriceEffects? PriceEffect { get; set; }

	[JsonProperty("stop-trigger", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopTrigger { get; set; }

	[JsonProperty("external-identifier", NullValueHandling = NullValueHandling.Ignore)]
	public string ExternalIdentifier { get; set; }

	[JsonProperty("automated-source")]
	public bool IsAutomatedSource { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("legs")]
	public TastyOrderRequestLeg[] Legs { get; set; }
}

sealed class TastyOrderReplaceRequest
{
	[JsonProperty("order-type")]
	public TastyOrderTypes OrderType { get; set; }

	[JsonProperty("time-in-force")]
	public TastyTimeInForces TimeInForce { get; set; }

	[JsonProperty("gtc-date", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? GoodTillDate { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Price { get; set; }

	[JsonProperty("price-effect", NullValueHandling = NullValueHandling.Ignore)]
	public TastyPriceEffects? PriceEffect { get; set; }

	[JsonProperty("stop-trigger", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopTrigger { get; set; }

	[JsonProperty("external-identifier", NullValueHandling = NullValueHandling.Ignore)]
	public string ExternalIdentifier { get; set; }

	[JsonProperty("automated-source")]
	public bool IsAutomatedSource { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }
}

sealed class TastyOrderRequestLeg
{
	[JsonProperty("action")]
	public TastyLegActions Action { get; set; }

	[JsonProperty("instrument-type")]
	public TastyInstrumentTypes InstrumentType { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class TastyPlacedOrder
{
	[JsonProperty("order")]
	public TastyOrder Order { get; set; }

	[JsonProperty("errors")]
	public TastyError[] Errors { get; set; }

	[JsonProperty("warnings")]
	public TastyOrderNotice[] Warnings { get; set; }
}

sealed class TastyOrderNotice
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class TastyOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("account-number")]
	public string AccountNumber { get; set; }

	[JsonProperty("order-type")]
	public TastyOrderTypes OrderType { get; set; }

	[JsonProperty("time-in-force")]
	public TastyTimeInForces TimeInForce { get; set; }

	[JsonProperty("gtc-date")]
	public DateTime? GoodTillDate { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("price-effect")]
	public TastyPriceEffects? PriceEffect { get; set; }

	[JsonProperty("stop-trigger")]
	public decimal? StopTrigger { get; set; }

	[JsonProperty("status")]
	public TastyOrderStatuses Status { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("received-at")]
	public DateTime? ReceivedAt { get; set; }

	[JsonProperty("live-at")]
	public DateTime? LiveAt { get; set; }

	[JsonProperty("terminal-at")]
	public DateTime? TerminalAt { get; set; }

	[JsonProperty("updated-at")]
	public DateTime? UpdatedAt { get; set; }

	[JsonProperty("reject-reason")]
	public string RejectReason { get; set; }

	[JsonProperty("external-identifier")]
	public string ExternalIdentifier { get; set; }

	[JsonProperty("legs")]
	public TastyOrderLeg[] Legs { get; set; }
}

sealed class TastyOrderLeg
{
	[JsonProperty("action")]
	public TastyLegActions Action { get; set; }

	[JsonProperty("instrument-type")]
	public TastyInstrumentTypes InstrumentType { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("remaining-quantity")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("fills")]
	public TastyFill[] Fills { get; set; }
}

sealed class TastyFill
{
	[JsonProperty("fill-id")]
	public string FillId { get; set; }

	[JsonProperty("fill-price")]
	public decimal FillPrice { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("filled-at")]
	public DateTime FilledAt { get; set; }

	[JsonProperty("destination-venue")]
	public string DestinationVenue { get; set; }
}

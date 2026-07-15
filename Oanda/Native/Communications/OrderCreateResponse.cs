namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OrderFillTransaction
{
	[JsonProperty("tradeOpened")]
	public TradeData TradeOpened { get; set; }

	[JsonProperty("tradeClosed")]
	public TradeData TradeClosed { get; set; }

	[JsonProperty("tradeReduced")]
	public TradeData TradeReduced { get; set; }

	//[JsonProperty("orderOpened")]
	//public Order OrderOpened { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OrderCreateResponse : OrderCancelResponse
{
	/// <summary>
	/// The Transaction that created the Order specified by the request.
	/// </summary>
	[JsonProperty("orderCreateTransaction")]
	public Transaction OrderCreateTransaction { get; set; }

	/// <summary>
	/// The Transaction that filled the newly created Order. Only provided when the Order was immediately filled.
	/// </summary>
	[JsonProperty("orderFillTransaction")]
	public Transaction OrderFillTransaction { get; set; }

	/// <summary>
	/// The Transaction that rejected the creation of the Order as requested.
	/// </summary>
	[JsonProperty("orderRejectTransaction")]
	public Transaction OrderRejectTransaction { get; set; }

	/// <summary>
	/// The Transaction that reissues the replacing Order. Only provided when the replacing Order was partially filled
	/// immediately and is configured to be reissued for its remaining units.
	/// </summary>
	[JsonProperty("orderReissueTransaction")]
	public Transaction OrderReissueTransaction { get; set; }

	/// <summary>
	/// The Transaction that rejects the reissue of the Order. Only provided when the replacing Order was paritially filled
	/// immediately and was configured to be reissued, however the reissue was rejected.
	/// </summary>
	[JsonProperty("orderReissueRejectTransaction")]
	public Transaction OrderReissueRejectTransaction { get; set; }
}
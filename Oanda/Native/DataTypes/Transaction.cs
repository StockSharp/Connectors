namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Transaction : Order
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("accountBalance")]
	public double? AccountBalance { get; set; }

	[JsonProperty("interest")]
	public double? Interest { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("stopLossPrice")]
	public double? StopLossPrice { get; set; }

	[JsonProperty("takeProfitPrice")]
	public double? TakeProfitPrice { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("tradeId")]
	public long? TradeId { get; set; }

	[JsonProperty("orderId")]
	public long? OrderId { get; set; }

	[JsonProperty("trailingStopLossDistance")]
	public double? TrailingStopLossDistance { get; set; }

	[JsonProperty("marginUsed")]
	public double? MarginUsed { get; set; }

	[JsonProperty("tradeOpened")]
	public TradeData TradeOpened { get; set; }

	[JsonProperty("tradeReduced")]
	public TradeData TradeReduced { get; set; }

	[JsonProperty("tradesClosed")]
	public TradeData[] TradesClosed { get; set; }

	[JsonProperty("batchID")]
	public string BatchId { get; set; }

	[JsonProperty("requestID")]
	public string RequestId { get; set; }

	[JsonProperty("partialFill")]
	public string PartialFill { get; set; }

	[JsonProperty("rejectReason")]
	public string RejectReason { get; set; }

	[JsonProperty("clientRequestID")]
	public string ClientRequestId { get; set; }

	/// <summary>
	/// The client Order ID of the Order filled (only provided if the client has assigned one).
	/// </summary>
	[JsonProperty("clientOrderID")]
	public string ClientOrderId { get; set; }

	/// <summary>
	/// The profit or loss incurred when the Order was filled.
	/// </summary>
	[JsonProperty("pl")]
	public double? PnL { get; set; }

	/// <summary>
	/// The commission charged in the Account’s home currency as a result of filling the Order.
	/// The commission is always represented as a positive quantity of the Account’s home currency,
	/// however it reduces the balance in the Account.
	/// </summary>
	[JsonProperty("commission")]
	public double? Commission { get; set; }
}
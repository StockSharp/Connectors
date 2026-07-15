namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Account
{
	/// <summary>
	/// The Account’s identifier.
	/// </summary>
	[JsonProperty("id")]
	public string Id { get; set; }

	/// <summary>
	/// ID of the user that created the Account.
	/// </summary>
	[JsonProperty("createdTime")]
	public string CreatedTime { get; set; }

	/// <summary>
	/// The home currency of the Account.
	/// </summary>
	[JsonProperty("currency")]
	public string Currency { get; set; }

	/// <summary>
	/// ID of the user that created the Account.
	/// </summary>
	[JsonProperty("createdByUserID")]
	public int CreatedByUserId { get; set; }

	/// <summary>
	/// Client-assigned alias for the Account. Only provided if the Account has an alias set.
	/// </summary>
	[JsonProperty("alias")]
	public string Alias { get; set; }

	/// <summary>
	/// Client-provided margin rate override for the Account. The effective
	/// margin rate of the Account is the lesser of this value and the OANDA
	/// margin rate for the Account’s division. This value is only provided if a
	/// margin rate override exists for the Account.
	/// </summary>
	[JsonProperty("marginRate")]
	public double MarginRate { get; set; }

	/// <summary>
	/// Flag indicating that the Account has hedging enabled.
	/// </summary>
	[JsonProperty("hedgingEnabled")]
	public bool HedgingEnabled { get; set; }

	/// <summary>
	/// The ID of the last Transaction created for the Account.
	/// </summary>
	[JsonProperty("lastTransactionID")]
	public long? LastTransactionId { get; set; }

	/// <summary>
	/// The current balance of the Account. Represented in the Account’s home currency.
	/// </summary>
	[JsonProperty("balance")]
	public double Balance { get; set; }

	/// <summary>
	/// The number of Trades currently open in the Account.
	/// </summary>
	[JsonProperty("openTradeCount")]
	public int OpenTradeCount { get; set; }

	/// <summary>
	/// The number of Positions currently open in the Account.
	/// </summary>
	[JsonProperty("openPositionCount")]
	public int OpenPositionCount { get; set; }

	/// <summary>
	/// The number of Orders currently pending in the Account.
	/// </summary>
	[JsonProperty("pendingOrderCount")]
	public int PendingOrderCount { get; set; }

	/// <summary>
	/// The total profit/loss realized over the lifetime of the Account.
	/// Represented in the Account’s home currency.
	/// </summary>
	[JsonProperty("pl")]
	public double PnL { get; set; }

	/// <summary>
	/// The total realized profit/loss for the Account since it was last reset by the client.
	/// Represented in the Account’s home currency.
	/// </summary>
	[JsonProperty("resettablePL")]
	public double ResettablePnL { get; set; }

	[JsonProperty("financing")]
	public string Financing { get; set; }

	[JsonProperty("commission")]
	public double Commission { get; set; }

	/// <summary>
	/// The details of the Orders currently pending in the Account.
	/// </summary>
	[JsonProperty("orders")]
	public IEnumerable<Order> Orders { get; set; }

	/// <summary>
	/// The details all Account Positions.
	/// </summary>
	[JsonProperty("positions")]
	public IEnumerable<Position> Positions { get; set; }

	/// <summary>
	/// The details of the Trades currently open in the Account.
	/// </summary>
	[JsonProperty("trades")]
	public IEnumerable<TradeData> Trades { get; set; }

	/// <summary>
	/// The total unrealized profit/loss for all Trades currently open in the Account.
	/// Represented in the Account’s home currency.
	/// </summary>
	[JsonProperty("unrealizedPL")]
	public double UnrealizedPnL { get; set; }

	/// <summary>
	/// The net asset value of the Account. Equal to Account balance + unrealizedPL.
	/// Represented in the Account’s home currency.
	/// </summary>
	[JsonProperty("NAV")]
	public double Nav { get; set; }

	/// <summary>
	/// Margin currently used for the Account. Represented in the Account’s home currency.
	/// </summary>
	[JsonProperty("marginUsed")]
	public double MarginUsed { get; set; }

	/// <summary>
	/// Margin available for Account. Represented in the Account’s home currency.
	/// </summary>
	[JsonProperty("marginAvailable")]
	public double MarginAvailable { get; set; }

	/// <summary>
	/// The value of the Account’s open positions represented in the Account’s home currency.
	/// </summary>
	[JsonProperty("positionValue")]
	public double PositionValue { get; set; }

	/// <summary>
	/// The Account’s margin closeout unrealized PL.
	/// </summary>
	[JsonProperty("marginCloseoutUnrealizedPL")]
	public double MarginCloseoutUnrealizedPnL { get; set; }

	/// <summary>
	/// The Account’s margin closeout NAV.
	/// </summary>
	[JsonProperty("marginCloseoutNAV")]
	public double MarginCloseoutNav { get; set; }

	/// <summary>
	/// The Account’s margin closeout margin used.
	/// </summary>
	[JsonProperty("marginCloseoutMarginUsed")]
	public double MarginCloseoutMarginUsed { get; set; }

	/// <summary>
	/// The value of the Account’s open positions as used for margin closeout calculations represented in the Account’s home currency.
	/// </summary>
	[JsonProperty("marginCloseoutPositionValue")]
	public double MarginCloseoutPositionValue { get; set; }

	/// <summary>
	/// The Account’s margin closeout percentage. When this value is 1.0 or above the Account is in a margin closeout situation.
	/// </summary>
	[JsonProperty("marginCloseoutPercent")]
	public double MarginCloseoutPercent { get; set; }

	/// <summary>
	/// The current WithdrawalLimit for the account which will be zero or a positive value indicating how much can be withdrawn from the account.
	/// </summary>
	[JsonProperty("withdrawalLimit")]
	public double WithdrawalLimit { get; set; }

	/// <summary>
	/// The Account’s margin call margin used.
	/// </summary>
	[JsonProperty("marginCallMarginUsed")]
	public double MarginCallMarginUsed { get; set; }

	/// <summary>
	/// The Account’s margin call percentage. When this value is 1.0 or above the Account is in a margin call situation.
	/// </summary>
	[JsonProperty("marginCallPercent")]
	public double MarginCallPercent { get; set; }
}
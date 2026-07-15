namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class PositionSide
{
	/// <summary>
	/// Number of units in the position (negative value indicates short position, positive indicates long position).
	/// </summary>
	[JsonProperty("units")]
	public double Units { get; set; }

	/// <summary>
	/// Profit/loss realized by the PositionSide over the lifetime of the Account.
	/// </summary>
	[JsonProperty("pl")]
	public double? PnL { get; set; }

	/// <summary>
	/// Profit/loss realized by the PositionSide since the Account’s resettablePL was last reset by the client.
	/// </summary>
	[JsonProperty("resettablePL")]
	public double? ResettablePnL { get; set; }

	[JsonProperty("financing")]
	public double? Financing { get; set; }

	/// <summary>
	/// Volume-weighted average of the underlying Trade open prices for the Position.
	/// </summary>
	[JsonProperty("averagePrice")]
	public double? AveragePrice { get; set; }

	/// <summary>
	/// The unrealized profit/loss of all open Trades that contribute to this PositionSide.
	/// </summary>
	[JsonProperty("unrealizedPL")]
	public double? UnrealizedPnL { get; set; }

	/// <summary>
	/// List of the open Trade IDs which contribute to the open Position.
	/// </summary>
	[JsonProperty("tradeIDs")]
	public IEnumerable<long> TradeIds { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Position
{
	/// <summary>
	/// The Position’s Instrument.
	/// </summary>
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	/// <summary>
	/// The details of the long side of the Position.
	/// </summary>
	[JsonProperty("long")]
	public PositionSide Long { get; set; }

	/// <summary>
	/// The details of the short side of the Position.
	/// </summary>
	[JsonProperty("short")]
	public PositionSide Short { get; set; }

	/// <summary>
	/// Profit/loss realized by the Position over the lifetime of the Account.
	/// </summary>
	[JsonProperty("pl")]
	public double? PnL { get; set; }

	/// <summary>
	/// Profit/loss realized by the Position since the Account’s resettablePL was last reset by the client.
	/// </summary>
	[JsonProperty("resettablePL")]
	public double? ResettablePnL { get; set; }

	[JsonProperty("financing")]
	public double? Financing { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	/// <summary>
	/// The unrealized profit/loss of all open Trades that contribute to this Position.
	/// </summary>
	[JsonProperty("unrealizedPL")]
	public double? UnrealizedPnL { get; set; }
}
namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Pricing
{
	[JsonProperty("time")]
	public string Time { get; set; }

	/// <summary>
	/// The list of prices and liquidity available on the Instrument’s bid side.
	/// It is possible for this list to be empty if there is no bid liquidity currently available
	/// for the Instrument in the Account.
	/// </summary>
	[JsonProperty("bids")]
	public IEnumerable<Quote> Bids { get; set; }

	/// <summary>
	/// The list of prices and liquidity available on the Instrument’s ask side. It is possible for
	/// this list to be empty if there is no ask liquidity currently available for the Instrument in the Account.
	/// </summary>
	[JsonProperty("asks")]
	public IEnumerable<Quote> Asks { get; set; }

	/// <summary>
	/// The closeout bid Price. This Price is used when a bid is required to closeout a Position (margin closeout or manual)
	/// yet there is no bid liquidity. The closeout bid is never used to open a new position.
	/// </summary>
	[JsonProperty("closeoutBid")]
	public double CloseoutBid { get; set; }

	/// <summary>
	/// The closeout ask Price. This Price is used when a ask is required to closeout a Position (margin closeout or manual)
	/// yet there is no ask liquidity. The closeout ask is never used to open a new position.
	/// </summary>
	[JsonProperty("closeoutAsk")]
	public double CloseoutAsk { get; set; }

	///// <summary>
	///// The status of the Price.
	///// Deprecated: Will be removed in a future API update.
	///// </summary>
	//[JsonProperty("status")]
	//public string Status { get; set; }

	/// <summary>
	/// Flag indicating if the Price is tradeable or not.
	/// </summary>
	[JsonProperty("tradeable")]
	public bool Tradeable { get; set; }

	/// <summary>
	/// The Price’s Instrument.
	/// </summary>
	[JsonProperty("instrument")]
	public string Instrument { get; set; }
}
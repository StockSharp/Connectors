namespace StockSharp.DukasCopyLive;

/// <summary>Dukascopy JForex native order commands.</summary>
[DataContract]
public enum DukasCopyLiveOrderCommands
{
	/// <summary>Derive the JForex command from the StockSharp side and order type.</summary>
	[EnumMember]
	Auto,

	/// <summary>Buy limit triggered by the bid side.</summary>
	[EnumMember]
	BuyLimitByBid,

	/// <summary>Sell limit triggered by the ask side.</summary>
	[EnumMember]
	SellLimitByAsk,

	/// <summary>Buy stop triggered by the bid side.</summary>
	[EnumMember]
	BuyStopByBid,

	/// <summary>Sell stop triggered by the ask side.</summary>
	[EnumMember]
	SellStopByAsk,

	/// <summary>Place a bid.</summary>
	[EnumMember]
	PlaceBid,

	/// <summary>Place an offer.</summary>
	[EnumMember]
	PlaceOffer,
}

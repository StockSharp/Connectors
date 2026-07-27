namespace StockSharp.HdfcSecurities;

/// <summary>HDFC Securities order products.</summary>
public enum HdfcProducts
{
	/// <summary>Cash-and-carry equity delivery.</summary>
	Delivery,

	/// <summary>Overnight derivatives position.</summary>
	Overnight,

	/// <summary>Intraday square-off.</summary>
	Intraday,

	/// <summary>Margin Trading Facility.</summary>
	Mtf,

	/// <summary>Sell pledged equity stock.</summary>
	CollateralSell,

	/// <summary>Same-day equity sale proceeds.</summary>
	Encash,
}

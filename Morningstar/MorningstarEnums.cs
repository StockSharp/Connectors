namespace StockSharp.Morningstar;

/// <summary>Morningstar Direct Web Services regions.</summary>
public enum MorningstarRegions
{
	/// <summary>Americas.</summary>
	Americas,

	/// <summary>Asia-Pacific.</summary>
	AsiaPacific,

	/// <summary>Europe, Middle East, and Africa.</summary>
	EuropeMiddleEastAfrica,
}

/// <summary>Morningstar entitled-universe sources.</summary>
public enum MorningstarInvestmentSources
{
	/// <summary>Equities, exchange-traded funds, indices, and related securities.</summary>
	Equities,

	/// <summary>Managed investments.</summary>
	ManagedInvestments,
}

/// <summary>Identifier supplied to Morningstar Time Series API.</summary>
public enum MorningstarIdentifierTypes
{
	/// <summary>Infer the identifier type from the value.</summary>
	Auto,

	/// <summary>Morningstar Performance ID.</summary>
	PerformanceId,

	/// <summary>Morningstar Security ID.</summary>
	SecurityId,

	/// <summary>ISIN.</summary>
	Isin,

	/// <summary>CUSIP.</summary>
	Cusip,

	/// <summary>Trading symbol.</summary>
	TradingSymbol,

	/// <summary>Morningstar ID (MSID).</summary>
	Msid,
}

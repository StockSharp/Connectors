namespace StockSharp.FactSet;

/// <summary>FactSet authentication schemes.</summary>
public enum FactSetAuthenticationModes
{
	/// <summary>USERNAME-SERIAL and API key over HTTP Basic authentication.</summary>
	ApiKey,

	/// <summary>OAuth 2.0 confidential client with a portal-issued JWK.</summary>
	OAuth,
}

/// <summary>FactSet equity price adjustments.</summary>
public enum FactSetPriceAdjustments
{
	/// <summary>Split adjusted.</summary>
	Split,

	/// <summary>Split and spinoff adjusted.</summary>
	Spinoff,

	/// <summary>Split, spinoff, and dividend adjusted.</summary>
	Dividend,

	/// <summary>Unadjusted.</summary>
	Unadjusted,
}

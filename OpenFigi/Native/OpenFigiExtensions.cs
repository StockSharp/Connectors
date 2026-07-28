namespace StockSharp.OpenFigi.Native;

static class OpenFigiExtensions
{
	public static SecurityMessage ToSecurityMessage(
		this OpenFigiInstrument instrument, long originalTransactionId,
		string identifierType = null, string identifierValue = null,
		string currency = null)
	{
		var id = new SecurityId
		{
			SecurityCode = instrument.Ticker
				.IsEmpty(instrument.SecurityDescription)
				.IsEmpty(instrument.Figi),
			BoardCode = BoardCodes.OpenFigi,
			Native = instrument.Figi,
			Bloomberg = instrument.Figi,
		};
		switch (identifierType)
		{
			case "ID_ISIN":
				id.Isin = identifierValue;
				break;
			case "ID_CUSIP":
				id.Cusip = identifierValue;
				break;
			case "ID_SEDOL":
				id.Sedol = identifierValue;
				break;
		}

		CurrencyTypes? currencyType = null;
		if (!currency.IsEmpty() &&
			Enum.TryParse<CurrencyTypes>(currency, true,
				out var parsedCurrency))
			currencyType = parsedCurrency;
		var className = new[]
		{
			instrument.MarketSector,
			instrument.SecurityType2.IsEmpty(instrument.SecurityType),
		}.Where(static value => !value.IsEmpty()).Join("/");
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = id,
			Name = instrument.Name,
			ShortName = instrument.SecurityDescription
				.IsEmpty(instrument.Ticker),
			Class = className,
			SecurityType = GetSecurityType(instrument.SecurityType2
				.IsEmpty(instrument.SecurityType)),
			Currency = currencyType,
		};
	}

	internal static SecurityTypes? GetSecurityType(string value)
	{
		if (value.IsEmpty())
			return null;
		var type = value.ToLowerInvariant();
		if (type.Contains("etf"))
			return SecurityTypes.Etf;
		if (type.Contains("mutual fund") ||
			type.Contains("closed-end fund") ||
			type.Contains("open-end fund") ||
			type == "fund")
			return SecurityTypes.Fund;
		if (type.Contains("option"))
			return SecurityTypes.Option;
		if (type.Contains("future"))
			return SecurityTypes.Future;
		if (type.Contains("index"))
			return SecurityTypes.Index;
		if (type.Contains("bond") ||
			type.Contains("note") ||
			type.Contains("municipal") ||
			type.Contains("government"))
			return SecurityTypes.Bond;
		if (type.Contains("currency") || type.Contains("forex"))
			return SecurityTypes.Currency;
		if (type.Contains("commodity"))
			return SecurityTypes.Commodity;
		if (type.Contains("warrant"))
			return SecurityTypes.Warrant;
		if (type.Contains("depositary receipt") ||
			type.Contains("american depositary"))
			return SecurityTypes.Adr;
		if (type.Contains("stock") || type.Contains("equity") ||
			type.Contains("common") || type.Contains("preferred"))
			return SecurityTypes.Stock;
		return null;
	}
}

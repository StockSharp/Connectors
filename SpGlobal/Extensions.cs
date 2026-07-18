namespace StockSharp.SpGlobal;

static class Extensions
{
	private const string _boardCode = "SPGCI";

	public static SecurityMessage ToSecurityMessage(this SpGlobalSymbol symbol,
		long originalTransactionId)
	{
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new SecurityId
			{
				SecurityCode = symbol.Symbol,
				BoardCode = _boardCode,
				Native = symbol.Symbol,
			},
			Name = symbol.Description.IsEmpty(symbol.Symbol),
			ShortName = symbol.Description.IsEmpty(symbol.Symbol),
			SecurityType = symbol.ContractType.ToSecurityType(),
			Class = new[] { symbol.Commodity, symbol.ContractType, symbol.MarketDataCategory }
				.Where(value => !value.IsEmpty()).Distinct(StringComparer.OrdinalIgnoreCase)
				.Join(" / "),
		};
		if (Enum.TryParse<CurrencyTypes>(symbol.Currency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static bool Matches(this SpGlobalSymbol symbol, string value)
	{
		if (value.IsEmpty())
			return true;
		return symbol.Symbol.EqualsIgnoreCase(value) ||
			symbol.Description.ContainsIgnoreCase(value) ||
			symbol.Commodity.ContainsIgnoreCase(value);
	}

	private static SecurityTypes ToSecurityType(this string contractType)
		=> contractType?.ToLowerInvariant() switch
		{
			"future" or "efp" => SecurityTypes.Future,
			"forward" => SecurityTypes.Forward,
			"swap" or "efs" => SecurityTypes.Swap,
			"cfd" => SecurityTypes.Cfd,
			"index" or "statistic" or "yield" => SecurityTypes.Index,
			_ => SecurityTypes.Commodity,
		};
}

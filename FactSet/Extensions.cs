namespace StockSharp.FactSet;

static class Extensions
{
	public static SecurityMessage ToSecurityMessage(this FactSetReference reference,
		long originalTransactionId)
	{
		var code = reference.GetRequestCode();
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = code,
				BoardCode = reference.GetBoardCode(),
			},
			Name = reference.Name,
			ShortName = reference.Name.IsEmpty(code),
			SecurityType = reference.ToSecurityType(),
			Class = reference.SecType,
		};
		if (Enum.TryParse<CurrencyTypes>(reference.Currency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static SecurityId ToFactSetSecurityId(this SecurityId securityId,
		FactSetReference reference)
	{
		if (securityId.SecurityCode.IsEmpty())
			securityId.SecurityCode = reference.GetRequestCode();
		if (securityId.BoardCode.IsEmpty())
			securityId.BoardCode = reference.GetBoardCode();
		return securityId;
	}

	public static string GetRequestCode(this FactSetReference reference)
		=> reference.RequestId.IsEmpty(reference.FsymId);

	public static bool Matches(this FactSetReference reference, string identifier)
		=> reference.RequestId.EqualsIgnoreCase(identifier) ||
			reference.FsymId.EqualsIgnoreCase(identifier);

	public static string GetBoardCode(this FactSetReference reference)
	{
		var exchange = reference.PrimaryExchange;
		if (exchange.IsEmpty())
			return "FACTSET";
		var code = new string(exchange.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
		return code.IsEmpty("FACTSET");
	}

	public static bool IsFixedIncome(this FactSetReference reference)
	{
		var value = $"{reference.SecType} {reference.SecTypeCode} {reference.SecTypeCodeDetail}";
		return value.ContainsIgnoreCase("bond") || value.ContainsIgnoreCase("debt") ||
			value.ContainsIgnoreCase("note") || value.ContainsIgnoreCase("treasury") ||
			value.ContainsIgnoreCase("municipal") || value.ContainsIgnoreCase("fixed income");
	}

	private static SecurityTypes? ToSecurityType(this FactSetReference reference)
	{
		if (reference.IsFixedIncome())
			return SecurityTypes.Bond;
		var value = $"{reference.SecType} {reference.SecTypeCodeDetail}";
		if (value.ContainsIgnoreCase("ETF") || value.ContainsIgnoreCase("exchange traded fund"))
			return SecurityTypes.Etf;
		if (value.ContainsIgnoreCase("ADR"))
			return SecurityTypes.Adr;
		if (value.ContainsIgnoreCase("GDR"))
			return SecurityTypes.Gdr;
		if (value.ContainsIgnoreCase("fund"))
			return SecurityTypes.Fund;
		if (value.ContainsIgnoreCase("index"))
			return SecurityTypes.Index;
		if (value.ContainsIgnoreCase("currency") || value.ContainsIgnoreCase("forex"))
			return SecurityTypes.Currency;
		if (value.ContainsIgnoreCase("stock") || value.ContainsIgnoreCase("equity") ||
			value.ContainsIgnoreCase("preferred"))
			return SecurityTypes.Stock;
		return null;
	}

	public static string ToNative(this FactSetPriceAdjustments adjustment)
		=> adjustment switch
		{
			FactSetPriceAdjustments.Split => "SPLIT",
			FactSetPriceAdjustments.Spinoff => "SPINOFF",
			FactSetPriceAdjustments.Dividend => "DIVADJ",
			FactSetPriceAdjustments.Unadjusted => "UNSPLIT",
			_ => throw new ArgumentOutOfRangeException(nameof(adjustment), adjustment, null),
		};
}

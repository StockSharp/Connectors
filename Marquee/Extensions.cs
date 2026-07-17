namespace StockSharp.Marquee;

static class Extensions
{
	public static SecurityMessage ToSecurityMessage(this MarqueeAsset asset, long originalTransactionId)
	{
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = asset.GetSecurityCode(),
				BoardCode = asset.GetBoardCode(),
				Sedol = asset.XRef?.Sedol,
				Cusip = asset.XRef?.Cusip,
				Isin = asset.XRef?.Isin,
				Ric = asset.XRef?.Ric,
				Bloomberg = asset.XRef?.Bbid,
			},
			Name = asset.Name,
			ShortName = asset.ShortName.IsEmpty(asset.GetSecurityCode()),
			SecurityType = asset.ToSecurityType(),
			Class = asset.Type.IsEmpty(asset.AssetClass),
		};

		if (Enum.TryParse<CurrencyTypes>(asset.Currency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	private static SecurityTypes? ToSecurityType(this MarqueeAsset asset)
	{
		var type = asset.Type ?? string.Empty;
		if (type.EqualsIgnoreCase("ETF"))
			return SecurityTypes.Etf;
		if (type.ContainsIgnoreCase("Stock"))
			return SecurityTypes.Stock;
		if (type.ContainsIgnoreCase("Index") || type.ContainsIgnoreCase("Basket"))
			return SecurityTypes.Index;
		if (type.ContainsIgnoreCase("Future") && !type.ContainsIgnoreCase("Option"))
			return SecurityTypes.Future;
		if (type.ContainsIgnoreCase("Option"))
			return SecurityTypes.Option;
		if (type.ContainsIgnoreCase("Bond") || type.EqualsIgnoreCase("Note") ||
			type.EqualsIgnoreCase("CD"))
			return SecurityTypes.Bond;
		if (type.EqualsIgnoreCase("Cross") || type.EqualsIgnoreCase("Currency"))
			return SecurityTypes.Currency;
		if (type.ContainsIgnoreCase("Fund"))
			return SecurityTypes.Fund;
		if (type.ContainsIgnoreCase("Coin") || type.ContainsIgnoreCase("Crypto") ||
			type.EqualsIgnoreCase("Token"))
			return SecurityTypes.CryptoCurrency;
		if (type.ContainsIgnoreCase("Commodity") || type.ContainsIgnoreCase("Precious Metal"))
			return SecurityTypes.Commodity;
		if (type.ContainsIgnoreCase("Forward"))
			return SecurityTypes.Forward;
		if (type.ContainsIgnoreCase("Swap"))
			return SecurityTypes.Swap;

		return asset.AssetClass?.ToUpperInvariant() switch
		{
			"EQUITY" => SecurityTypes.Stock,
			"LISTEDDERIVATIVE" => SecurityTypes.Future,
			"FX" => SecurityTypes.Currency,
			"DEBT" or "CREDIT" or "RATES" or "MORTGAGE" => SecurityTypes.Bond,
			"FUND" => SecurityTypes.Fund,
			"COMMOD" => SecurityTypes.Commodity,
			"DIGITAL ASSET" or "CRYPTOCURRENCY" => SecurityTypes.CryptoCurrency,
			_ => null,
		};
	}
}

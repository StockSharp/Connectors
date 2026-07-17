namespace StockSharp.JpmDataQuery;

static class Extensions
{
	public static SecurityMessage ToSecurityMessage(this JpmDataQueryInstrument instrument,
		long originalTransactionId, string groupId, SecurityTypes? securityType)
	{
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = instrument.InstrumentId,
				BoardCode = "JPMDQ",
				Cusip = instrument.Cusip,
				Isin = instrument.Isin,
			},
			Name = instrument.InstrumentName,
			ShortName = instrument.InstrumentName.IsEmpty(instrument.InstrumentId),
			SecurityType = securityType,
			Class = groupId,
		};

		if (Enum.TryParse<CurrencyTypes>(instrument.Currency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static bool Matches(this JpmDataQueryInstrument instrument, string identifier)
		=> instrument.InstrumentId.EqualsIgnoreCase(identifier) ||
			instrument.Isin.EqualsIgnoreCase(identifier) ||
			instrument.Cusip.EqualsIgnoreCase(identifier);

	public static bool Matches(this JpmDataQueryAttribute attribute, string identifier)
		=> attribute.AttributeId.EqualsIgnoreCase(identifier) ||
			attribute.AttributeName.EqualsIgnoreCase(identifier) ||
			attribute.Label.EqualsIgnoreCase(identifier);

	public static Level1Fields ToStockSharp(this JpmDataQueryValueFields field)
		=> field switch
		{
			JpmDataQueryValueFields.SpreadMiddle => Level1Fields.SpreadMiddle,
			JpmDataQueryValueFields.LastTradePrice => Level1Fields.LastTradePrice,
			JpmDataQueryValueFields.BestBidPrice => Level1Fields.BestBidPrice,
			JpmDataQueryValueFields.BestAskPrice => Level1Fields.BestAskPrice,
			JpmDataQueryValueFields.OpenPrice => Level1Fields.OpenPrice,
			JpmDataQueryValueFields.HighPrice => Level1Fields.HighPrice,
			JpmDataQueryValueFields.LowPrice => Level1Fields.LowPrice,
			JpmDataQueryValueFields.ClosePrice => Level1Fields.ClosePrice,
			JpmDataQueryValueFields.Volume => Level1Fields.Volume,
			JpmDataQueryValueFields.OpenInterest => Level1Fields.OpenInterest,
			JpmDataQueryValueFields.SettlementPrice => Level1Fields.SettlementPrice,
			_ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
		};
}

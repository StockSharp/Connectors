namespace StockSharp.Swissquote;

public partial class SwissquoteMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var accounts = await GetKnownAccounts(cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Exception firstError = null;

		foreach (var account in accounts.Where(IsSafekeepingAccount))
		{
			SwissquoteAccount positions;
			try
			{
				var date = GetSwissDate();
				positions = await GetRest().GetPositions(account.AccountIdentification,
					date, GetSwissOffset(date), cancellationToken);
			}
			catch (Exception ex)
			{
				firstError ??= ex;
				if (!SafekeepingAccountId.IsEmpty())
					throw;
				continue;
			}

			foreach (var position in positions.PositionList ?? [])
			{
				var security = ToSecurityMessage(position, lookupMsg.TransactionId);
				if (security == null || !security.IsMatch(lookupMsg, securityTypes))
					continue;
				var key = $"{security.SecurityId.SecurityCode}@{security.SecurityId.BoardCode}";
				if (!sent.Add(key))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
				break;
		}

		if (sent.Count == 0 && firstError != null)
			throw firstError;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async Task<SwissquoteAccountInformation[]> GetKnownAccounts(
		CancellationToken cancellationToken)
	{
		var configured = new List<SwissquoteAccountInformation>();
		if (!SafekeepingAccountId.IsEmpty())
		{
			configured.Add(new()
			{
				AccountIdentification = SafekeepingAccountId,
				AccountIdentificationType = "other",
				AccountType = "safekeepingAccount",
				AccountReferenceCurrency = AccountCurrency,
			});
		}
		if (!CashAccountId.IsEmpty() && !CashAccountId.EqualsIgnoreCase(SafekeepingAccountId))
		{
			configured.Add(new()
			{
				AccountIdentification = CashAccountId,
				AccountIdentificationType = "other",
				AccountType = "cashAccount",
				AccountReferenceCurrency = AccountCurrency,
			});
		}
		if (configured.Count > 0)
			return [.. configured];

		return [.. (await GetRest().GetCustomerAccounts(CustomerId, cancellationToken))
			.Where(customer => customer != null)
			.SelectMany(customer => customer.AccountOverview ?? [])
			.Where(account => account?.AccountIdentification.IsEmpty() == false)
			.GroupBy(account => account.AccountIdentification, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())];
	}

	private static bool IsSafekeepingAccount(SwissquoteAccountInformation account)
		=> account?.AccountType.EqualsIgnoreCase("cashAccount") != true;

	private static SecurityMessage ToSecurityMessage(SwissquotePosition position,
		long originalTransactionId)
	{
		var instrument = position?.FinancialInstrument;
		var identification = instrument?.FinancialInstrumentIdentification;
		if (identification?.Identification.IsEmpty() != false)
			return null;
		var securityType = instrument.ToSecurityType();
		var maturity = instrument.Dates?.FirstOrDefault(date =>
			date?.Type.EqualsIgnoreCase("expiryDate") == true ||
			date?.Type.EqualsIgnoreCase("maturityDate") == true)?.Date.ToDate();
		var strike = instrument.FinancialInstrumentPrices?.FirstOrDefault(price =>
			price?.Type.EqualsIgnoreCase("strikePrice") == true)?.Amount.ToDecimal();
		var multiplier = instrument.ContractSize.ToDecimal();

		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = identification.ToSecurityId(),
			SecurityType = securityType,
			Name = instrument.FinancialInstrumentName,
			ShortName = instrument.FinancialInstrumentName,
			Class = instrument.AssetClass,
			Currency = position.Currency.ToCurrency(),
			VolumeStep = 1,
			Multiplier = multiplier is > 0 ? multiplier : null,
			ExpiryDate = maturity,
			Strike = strike,
			OptionType = instrument.OptionDetails?.OptionType.ToOptionType(),
		};
	}
}

namespace StockSharp.Copper;

public partial class CopperMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
			!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Copper))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var currencies = await RestClient.GetCurrenciesAsync(cancellationToken);
		UpdateCurrencies(currencies);
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var toSkip = Math.Max(0, lookupMsg.Skip ?? 0);
		foreach (var currency in currencies
			.Where(currency => currency is not null && !currency.Currency.IsEmpty() &&
				(requestedCode.IsEmpty() ||
					currency.Currency.EqualsIgnoreCase(requestedCode)))
			.GroupBy(static currency => currency.Currency,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static currency => currency.Currency,
				StringComparer.OrdinalIgnoreCase))
		{
			var security = CreateSecurity(currency, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (toSkip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(CopperCurrency currency,
		long originalTransactionId)
	{
		ArgumentNullException.ThrowIfNull(currency);
		return new()
		{
			SecurityId = new()
			{
				SecurityCode = currency.Currency,
				BoardCode = BoardCodes.Copper,
				Native = currency.MainCurrency,
			},
			Name = currency.Name.IsEmpty() ? currency.Currency : currency.Name,
			ShortName = currency.Currency,
			SecurityType = currency.IsFiat
				? SecurityTypes.Currency
				: SecurityTypes.CryptoCurrency,
			VolumeStep = GetQuantityStep(currency.Decimals),
			OriginalTransactionId = originalTransactionId,
		};
	}

	private static decimal? GetQuantityStep(string decimals)
	{
		if (!int.TryParse(decimals, NumberStyles.None,
			CultureInfo.InvariantCulture, out var count) || count is < 0 or > 28)
			return null;
		var step = 1m;
		for (var index = 0; index < count; index++)
			step /= 10m;
		return step;
	}
}

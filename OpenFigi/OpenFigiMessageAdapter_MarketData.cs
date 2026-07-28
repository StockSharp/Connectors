namespace StockSharp.OpenFigi;

public partial class OpenFigiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.Skip is < 0)
			throw new ArgumentOutOfRangeException(nameof(lookupMsg.Skip));
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			return;
		}

		var request = CreateRequest(lookupMsg);
		IEnumerable<OpenFigiInstrument> instruments;
		if (request.Mapping is not null)
			instruments = await RestClient.MapAsync(request.Mapping,
				cancellationToken);
		else
			instruments = await RestClient.SearchAsync(
				request.Criteria, request.UseSearch, MaximumPages,
				MaximumResults, cancellationToken);

		var types = lookupMsg.GetSecurityTypes();
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? MaximumResults;
		foreach (var instrument in instruments
			.Where(static item => !item.Figi.IsEmpty())
			.GroupBy(static item => item.Figi,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First()))
		{
			var security = instrument.ToSecurityMessage(
				lookupMsg.TransactionId, request.IdentifierType,
				request.IdentifierValue, Currency);
			if (types.Count > 0 &&
				(security.SecurityType is null ||
					!types.Contains(security.SecurityType.Value)))
				continue;
			if (skip-- > 0)
				continue;
			if (lookupMsg.OnlySecurityId)
				security = new()
				{
					OriginalTransactionId = lookupMsg.TransactionId,
					SecurityId = security.SecurityId,
				};
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg,
			cancellationToken);
	}

	internal OpenFigiLookupRequest CreateRequest(
		SecurityLookupMessage message)
	{
		var identifier = GetIdentifier(message.SecurityId);
		var filters = CreateFilters();
		if (identifier.Type is not null)
		{
			filters["idType"] = identifier.Type;
			filters["idValue"] = identifier.Value;
			return new(filters, null, false, identifier.Type,
				identifier.Value);
		}

		var query = message.Name
			.IsEmpty(message.ShortName)?.Trim();
		if (!query.IsEmpty())
			filters["query"] = query;
		return new(null, filters, !query.IsEmpty(), null, null);
	}

	private static (string Type, string Value) GetIdentifier(
		SecurityId securityId)
	{
		var native = securityId.Native?.ToString()?.Trim();
		if (!native.IsEmpty())
		{
			var separator = native.IndexOf(':');
			if (separator > 0 && separator < native.Length - 1)
				return (native[..separator].ToUpperInvariant(),
					native[(separator + 1)..]);
			if (native.StartsWith("BBG",
				StringComparison.OrdinalIgnoreCase))
				return ("ID_BB_GLOBAL", native);
		}
		if (!securityId.Bloomberg.IsEmpty())
			return ("ID_BB_GLOBAL", securityId.Bloomberg.Trim());
		if (!securityId.Isin.IsEmpty())
			return ("ID_ISIN", securityId.Isin.Trim());
		if (!securityId.Cusip.IsEmpty())
			return ("ID_CUSIP", securityId.Cusip.Trim());
		if (!securityId.Sedol.IsEmpty())
			return ("ID_SEDOL", securityId.Sedol.Trim());
		if (!securityId.SecurityCode.IsEmpty())
			return ("TICKER", securityId.SecurityCode.Trim());
		return (null, null);
	}

	private JObject CreateFilters()
	{
		var result = new JObject();
		Add(result, "exchCode", ExchangeCode);
		Add(result, "micCode", MicCode);
		Add(result, "currency", Currency);
		Add(result, "marketSecDes", MarketSector);
		Add(result, "securityType2", SecurityType2);
		if (IncludeUnlistedEquities)
			result["includeUnlistedEquities"] = true;
		return result;
	}

	private static void Add(JObject target, string name,
		string value)
	{
		if (!value.IsEmpty())
			target[name] = value.Trim();
	}
}

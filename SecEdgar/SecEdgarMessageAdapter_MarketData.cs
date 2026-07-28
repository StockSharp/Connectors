namespace StockSharp.SecEdgar;

public partial class SecEdgarMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask MarketDataAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		if (mdMsg.DataType2 == SecEdgarDataTypes.CompanyFacts)
		{
			await OnCompanyFactsSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		await base.MarketDataAsync(mdMsg, cancellationToken);
	}

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
		var types = lookupMsg.GetSecurityTypes();
		if (types.Count > 0 &&
			!types.Any(type => type is SecurityTypes.Stock or
				SecurityTypes.Etf or SecurityTypes.Fund))
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			return;
		}
		var query = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode)
			.IsEmpty(lookupMsg.Name)
			.IsEmpty(lookupMsg.ShortName)?.Trim();
		var normalizedCik = query.NormalizeCik();
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var company in (await GetCompaniesAsync(
			cancellationToken))
			.Where(company => query.IsEmpty() ||
				!normalizedCik.IsEmpty() &&
					company.Cik.EqualsIgnoreCase(normalizedCik) ||
				company.Ticker.Contains(query,
					StringComparison.OrdinalIgnoreCase) ||
				company.Name?.Contains(query,
					StringComparison.OrdinalIgnoreCase) == true)
			.OrderBy(static company => company.Ticker,
				StringComparer.OrdinalIgnoreCase))
		{
			var security = company.ToSecurityMessage(
				lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, types))
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

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From > mdMsg.To)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From));
		var company = await ResolveCompanyAsync(mdMsg.SecurityId,
			cancellationToken);
		var submission = await RestClient.GetSubmissionAsync(
			company.Cik, cancellationToken);
		var filings = new List<SecEdgarFiling>();
		if (submission["filings"]?["recent"] is JObject recent)
			filings.AddRange(recent.ToFilings(company.Cik,
				company.Name));

		if (mdMsg.From is not null &&
			submission["filings"]?["files"] is JArray files)
		{
			foreach (var file in files.OfType<JObject>()
				.Where(file => Intersects(file, mdMsg.From, mdMsg.To))
				.Take(MaximumHistoricalFiles))
			{
				var name = file.Value<string>("name");
				if (name.IsEmpty())
					continue;
				var history = await RestClient.GetSubmissionFileAsync(
					name, cancellationToken);
				filings.AddRange(history.ToFilings(company.Cik,
					company.Name));
			}
		}

		var forms = Forms
			.Split(',', StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var maximum = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, int.MaxValue)
			: 1000;
		foreach (var filing in filings
			.Where(filing => forms.Count == 0 ||
				forms.Contains(filing.Form) ||
				filing.Form?.EndsWith("/A",
					StringComparison.OrdinalIgnoreCase) == true &&
				forms.Contains(filing.Form[..^2]))
			.Where(filing => mdMsg.From is null ||
				filing.FilingDate >=
					mdMsg.From.Value.ToUniversalTime())
			.Where(filing => mdMsg.To is null ||
				filing.FilingDate <=
					mdMsg.To.Value.ToUniversalTime())
			.GroupBy(static filing => filing.AccessionNumber,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static filing => filing.AcceptanceDateTime ??
				filing.FilingDate)
			.TakeLast(maximum))
			await SendOutMessageAsync(filing.ToNewsMessage(company,
				mdMsg.TransactionId, WebsiteEndpoint),
				cancellationToken);
		await CompleteSubscriptionAsync(mdMsg, cancellationToken);
	}

	private async ValueTask OnCompanyFactsSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From > mdMsg.To)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From));
		var company = await ResolveCompanyAsync(mdMsg.SecurityId,
			cancellationToken);
		var facts = (await RestClient.GetCompanyFactsAsync(company.Cik,
			cancellationToken)).ToFacts();
		var maximum = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, MaximumFacts)
			: MaximumFacts;
		foreach (var fact in facts
			.Where(fact => mdMsg.From is null ||
				fact.FiledDate >=
					mdMsg.From.Value.ToUniversalTime())
			.Where(fact => mdMsg.To is null ||
				fact.FiledDate <=
					mdMsg.To.Value.ToUniversalTime())
			.OrderBy(static fact => fact.FiledDate)
			.ThenBy(static fact => fact.Taxonomy,
				StringComparer.Ordinal)
			.ThenBy(static fact => fact.Concept,
				StringComparer.Ordinal)
			.TakeLast(maximum))
			await SendOutMessageAsync(new SecEdgarFactMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = company.ToSecurityId(),
				ServerTime = fact.FiledDate,
				Taxonomy = fact.Taxonomy,
				Concept = fact.Concept,
				Label = fact.Label,
				Description = fact.Description,
				Unit = fact.Unit,
				Value = fact.Value,
				NumericValue = fact.NumericValue,
				StartDate = fact.StartDate,
				EndDate = fact.EndDate,
				AccessionNumber = fact.AccessionNumber,
				FiscalYear = fact.FiscalYear,
				FiscalPeriod = fact.FiscalPeriod,
				Form = fact.Form,
				Frame = fact.Frame,
			}, cancellationToken);
		await CompleteSubscriptionAsync(mdMsg, cancellationToken);
	}

	private static bool Intersects(JObject file,
		DateTime? from, DateTime? to)
	{
		var start = file.Date("filingFrom");
		var end = file.Date("filingTo");
		return (from is null || end is null ||
				end >= from.Value.ToUniversalTime()) &&
			(to is null || start is null ||
				start <= to.Value.ToUniversalTime());
	}

	private async ValueTask CompleteSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message,
			cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}

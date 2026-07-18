namespace StockSharp.SpGlobal;

public partial class SpGlobalMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var identifier = lookupMsg.SecurityId.Native as string;
		var exactNative = !identifier.IsEmpty();
		if (identifier.IsEmpty())
			identifier = lookupMsg.SecurityId.SecurityCode;

		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var page = 1;
		while (left > 0)
		{
			var query = new SpGlobalSymbolQuery
			{
				Query = exactNative ? null : identifier,
				Symbol = exactNative ? identifier : null,
				Commodity = Commodity,
				ContractType = ContractType,
				MarketDataCategory = MarketDataCategory,
				AssessmentFrequency = AssessmentFrequency,
				Page = page,
			};
			var response = await SafeClient().GetSymbols(query, cancellationToken);
			foreach (var symbol in response.Results ?? [])
			{
				if (!symbol.Matches(identifier))
					continue;
				var security = symbol.ToSecurityMessage(lookupMsg.TransactionId);
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}

			var totalPages = response.Metadata?.GetTotalPages() ?? 1;
			if (page >= totalPages.Max(1))
				break;
			page++;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var symbol = (mdMsg.SecurityId.Native as string)
			.IsEmpty(mdMsg.SecurityId.SecurityCode)
			.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var isHistorical = mdMsg.From != null || mdMsg.To != null || mdMsg.Count != null;
		var query = new SpGlobalAssessmentQuery
		{
			Symbol = symbol,
			Bate = Bate,
			From = mdMsg.From?.ToUniversalTime().Date,
			To = mdMsg.To?.ToUniversalTime().Date,
		};

		var assessments = new List<SpGlobalAssessment>();
		var page = 1;
		do
		{
			query.Page = page;
			var response = isHistorical
				? await SafeClient().GetHistoricalAssessments(query, cancellationToken)
				: await SafeClient().GetCurrentAssessments(query, cancellationToken);
			foreach (var result in response.Results ?? [])
			{
				if (!result.Symbol.EqualsIgnoreCase(symbol))
					continue;
				assessments.AddRange((result.Data ?? []).Where(item => item.GetTime() != null));
			}
			var totalPages = response.Metadata?.GetTotalPages() ?? 1;
			if (page >= totalPages.Max(1))
				break;
			page++;
		}
		while (isHistorical);

		IEnumerable<SpGlobalAssessment> ordered = assessments
			.OrderBy(item => item.GetTime())
			.ThenBy(item => item.Bate, StringComparer.OrdinalIgnoreCase);
		if (mdMsg.Count is long count)
			ordered = ordered.TakeLast(checked((int)Math.Min(count.Max(0), int.MaxValue)));
		if (!isHistorical)
			ordered = ordered.TakeLast(1);
		var data = ordered.ToArray();
		if (data.Length == 0)
			throw new InvalidOperationException(
				$"S&P Global returned no entitled assessment values for '{symbol}'.");

		var securityId = mdMsg.SecurityId;
		if (securityId.SecurityCode.IsEmpty())
			securityId.SecurityCode = symbol;
		if (securityId.BoardCode.IsEmpty())
			securityId.BoardCode = _boardCode;
		securityId.Native ??= symbol;

		foreach (var assessment in data)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = assessment.GetTime().Value,
			}
			.TryAdd(Level1Fields.ClosePrice, assessment.Value)
			.TryAdd(Level1Fields.Change, assessment.Change?.DeltaPercent), cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}
}

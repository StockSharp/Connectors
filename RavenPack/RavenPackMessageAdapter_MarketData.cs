namespace StockSharp.RavenPack;

public partial class RavenPackMessageAdapter
{
	private readonly record struct HistoricalRecord(RavenPackAnalyticsRecord Value,
		DateTime Time);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var identifier = lookupMsg.SecurityId.ToIdentifier(lookupMsg.Name);
		var native = lookupMsg.SecurityId.Native as string;
		if (native.IsEmpty() && (identifier.IsEmpty() || identifier.HasWildcards()))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var resolved = await ResolveSecurity(lookupMsg.SecurityId, lookupMsg.Name,
			cancellationToken);
		if (resolved != null)
		{
			var security = CopySecurity(resolved.Security, lookupMsg.TransactionId,
				lookupMsg.OnlySecurityId);
			if (security.IsMatch(lookupMsg, lookupMsg.GetSecurityTypes()) &&
				(lookupMsg.Skip ?? 0) == 0)
			{
				await SendOutMessageAsync(security, cancellationToken);
			}
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			RemoveLiveSubscription(mdMsg.OriginalTransactionId);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		RavenPackResolvedSecurity resolved = null;
		if (HasSecurity(mdMsg.SecurityId))
		{
			resolved = await ResolveSecurity(mdMsg.SecurityId, null, cancellationToken)
				?? throw new InvalidOperationException(
					$"RavenPack could not map security '{mdMsg.SecurityId}'.");
		}

		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		if (from != null && to != null && from > to)
		{
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The RavenPack history start time is after its end time.");
		}

		var remaining = mdMsg.Count;
		if (from != null || to != null || mdMsg.IsHistoryOnly())
		{
			var queryTo = to ?? DateTime.UtcNow;
			var queryFrom = from ?? queryTo - DefaultHistoryLookback;
			var target = checked((int)Math.Min(remaining ?? MaxRecords, MaxRecords));
			var records = await GetHistory(queryFrom, queryTo, resolved?.EntityId,
				target, from == null, cancellationToken);
			foreach (var item in records)
			{
				await SendNews(mdMsg.TransactionId,
					resolved?.Security.SecurityId ?? default, item.Value, item.Time,
					cancellationToken);
				if (remaining is > 0 && --remaining == 0)
					break;
			}
		}

		if (mdMsg.IsHistoryOnly() || to != null || remaining == 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var subscription = new LiveNewsSubscription
		{
			TransactionId = mdMsg.TransactionId,
			EntityId = resolved?.EntityId,
			SecurityId = resolved?.Security.SecurityId ?? default,
			Remaining = remaining,
		};
		AddLiveSubscription(subscription);
		try
		{
			await EnsureFeed(cancellationToken);
		}
		catch
		{
			RemoveLiveSubscription(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async Task<RavenPackResolvedSecurity> ResolveSecurity(SecurityId requested,
		string name, CancellationToken cancellationToken)
	{
		var cached = GetCachedSecurity(requested);
		if (cached != null)
			return cached;

		RavenPackMappingCandidate candidate;
		RavenPackEntityReference reference;
		var native = (requested.Native as string)?.Trim();
		if (!native.IsEmpty())
		{
			reference = await SafeRest().GetEntityReference(native, cancellationToken);
			if (reference == null)
				return null;
			candidate = new()
			{
				EntityId = native,
			};
		}
		else
		{
			var identifier = requested.ToIdentifier(name);
			if (identifier.IsEmpty() || identifier.HasWildcards())
				return null;
			var response = await SafeRest().MapEntity(identifier, cancellationToken);
			var mapping = response?.Results?.FirstOrDefault();
			if (mapping == null || mapping.Errors?.Length > 0)
				return null;
			candidate = mapping.Candidates?.FirstOrDefault(value =>
				value?.EntityId.IsEmpty() == false);
			if (candidate == null)
				return null;
			reference = await SafeRest().GetEntityReference(candidate.EntityId,
				cancellationToken);
		}

		var security = candidate.ToSecurityMessage(reference, requested, 0);
		if (security == null)
			return null;
		var resolved = new RavenPackResolvedSecurity(candidate.EntityId, security);
		CacheSecurity(requested, resolved);
		return resolved;
	}

	private async Task<HistoricalRecord[]> GetHistory(DateTime from, DateTime to,
		string entityId, int target, bool takeLast,
		CancellationToken cancellationToken)
	{
		var query = new RavenPackJsonQueryRequest
		{
			StartDate = from.FormatApiTime(),
			EndDate = to.FormatApiTime(),
			TimeZone = "UTC",
			Frequency = "granular",
			Fields = Product.GetQueryFields(),
			Filters = RavenPackExtensions.ToEntityFilters(entityId),
		};
		var response = await SafeRest().GetRecords(DatasetId, query, cancellationToken);
		var records = new List<HistoricalRecord>();
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var record in response?.Records ?? [])
		{
			if (record == null || !record.MatchesEntity(entityId) ||
				!record.TryGetTimestamp(out var time) || time < from || time > to)
			{
				continue;
			}
			var key = record.GetEventKey();
			if (!key.IsEmpty() && !ids.Add(key))
				continue;
			records.Add(new(record, time));
		}
		var ordered = records.OrderBy(item => item.Time);
		return (takeLast ? ordered.TakeLast(target) : ordered.Take(target)).ToArray();
	}

	private async ValueTask SendNews(long transactionId, SecurityId requestedSecurityId,
		RavenPackAnalyticsRecord record, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		var documentId = record.GetDocumentId();
		string url = null;
		if (IsResolveDocumentUrls && !documentId.IsEmpty())
		{
			try
			{
				url = await SafeRest().GetDocumentUrl(documentId, cancellationToken);
			}
			catch (RavenPackApiException error) when (error.StatusCode is
				HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
			{
			}
		}

		var securityId = record.GetSecurityId(requestedSecurityId);
		await SendOutMessageAsync(new NewsMessage
		{
			OriginalTransactionId = transactionId,
			ServerTime = serverTime,
			Id = record.GetEventKey().IsEmpty(documentId),
			Headline = record.GetHeadline(),
			Story = record.GetStory(),
			Source = record.GetSource(),
			Url = url,
			BoardCode = RavenPackExtensions.BoardCode,
			SecurityId = securityId.SecurityCode.IsEmpty() ? null : securityId,
		}, cancellationToken);
	}

	private static SecurityMessage CopySecurity(SecurityMessage source,
		long transactionId, bool onlySecurityId)
	{
		if (onlySecurityId)
		{
			return new()
			{
				OriginalTransactionId = transactionId,
				SecurityId = source.SecurityId,
			};
		}
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = source.SecurityId,
			Name = source.Name,
			ShortName = source.ShortName,
			SecurityType = source.SecurityType,
			VolumeStep = source.VolumeStep,
		};
	}

	private static bool HasSecurity(SecurityId securityId)
		=> securityId.Native is string native && !native.IsEmpty() ||
			!securityId.SecurityCode.IsEmpty() || !securityId.Isin.IsEmpty() ||
			!securityId.Cusip.IsEmpty() || !securityId.Sedol.IsEmpty();

	private async ValueTask Complete(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}

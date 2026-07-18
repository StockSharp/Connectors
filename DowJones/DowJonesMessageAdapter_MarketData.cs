namespace StockSharp.DowJones;

public partial class DowJonesMessageAdapter
{
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

		var requestedSecurity = NormalizeSecurityId(mdMsg.SecurityId);
		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		if (from != null && to != null && from > to)
		{
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The Dow Jones history start time is after its end time.");
		}

		var remaining = mdMsg.Count;
		var liveCursor = DateTime.UtcNow;
		var remembered = new List<string>();
		if (from != null || to != null || mdMsg.IsHistoryOnly())
		{
			var queryTo = to ?? liveCursor;
			var queryFrom = from ?? queryTo - DefaultHistoryLookback;
			var target = checked((int)Math.Min(remaining ?? MaxNewsItems, MaxNewsItems));
			var articles = await SearchNews(requestedSecurity, queryFrom, queryTo,
				target, from == null, cancellationToken);
			foreach (var article in articles)
			{
				await SendNews(mdMsg.TransactionId, requestedSecurity, article,
					cancellationToken);
				remembered.Add(article.Key);
				if (remaining is > 0 && --remaining == 0)
					break;
			}
			liveCursor = queryTo;
		}

		if (mdMsg.IsHistoryOnly() || to != null || remaining == 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var subscription = new LiveNewsSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = requestedSecurity,
			CursorUtc = liveCursor,
			Remaining = remaining,
		};
		foreach (var key in remembered)
			subscription.TryRemember(key);
		AddLiveSubscription(subscription);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async Task<TimedArticle[]> SearchNews(SecurityId securityId,
		DateTime from, DateTime to, int target, bool takeLatest,
		CancellationToken cancellationToken)
	{
		if (target <= 0)
			return [];
		var query = securityId.BuildQuery(NewsQuery, from, to);
		var result = new List<TimedArticle>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var offset = 0;

		while (result.Count < target)
		{
			var limit = Math.Min(PageLimit, target - result.Count);
			var request = new DowJonesSearchRequest
			{
				Data = new()
				{
					Attributes = new()
					{
						Query = new()
						{
							SearchStrings = [new() { Value = query }],
						},
						Formatting = new()
						{
							SortOrder = takeLatest
								? "-PublicationDateChronological"
								: "PublicationDateChronological",
						},
						Navigation = new(),
						PageOffset = offset,
						PageLimit = limit,
					},
				},
			};
			var response = await SafeClient().Search(request, cancellationToken);
			var page = response?.Data ?? [];
			var before = result.Count;
			foreach (var resource in page)
			{
				if (resource == null || !resource.TryGetTime(out var time) ||
					time < from || time > to)
				{
					continue;
				}
				var key = resource.GetEventKey().IsEmpty(
					$"{time:O}|{resource.GetHeadline()}");
				if (seen.Add(key))
					result.Add(new(resource, time, key));
				if (result.Count == target)
					break;
			}

			if (page.Length < limit || result.Count == before)
				break;
			offset += page.Length;
			if (response?.Meta?.TotalCount is > 0 && offset >= response.Meta.TotalCount)
				break;
		}

		return [.. result.OrderBy(article => article.Time)];
	}

	private async ValueTask SendNews(long transactionId, SecurityId requestedSecurity,
		TimedArticle article, CancellationToken cancellationToken)
	{
		DowJonesContentResource full = null;
		if (IsFullTextEnabled && !article.Value.Id.IsEmpty())
		{
			try
			{
				full = (await SafeClient().GetArticle(article.Value.Id,
					cancellationToken))?.Data;
			}
			catch (DowJonesApiException error) when (error.StatusCode is
				HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
			{
			}
		}

		var source = full ?? article.Value;
		var securityId = source.GetSecurityId(requestedSecurity);
		if (securityId == default)
			securityId = article.Value.GetSecurityId(requestedSecurity);
		await SendOutMessageAsync(new NewsMessage
		{
			OriginalTransactionId = transactionId,
			ServerTime = article.Time,
			Id = article.Key,
			Headline = source.GetHeadline().IsEmpty(article.Value.GetHeadline()),
			Story = source.GetArticleText().IsEmpty(article.Value.GetSnippet()),
			Source = source.Meta?.Source?.Name
				.IsEmpty(article.Value.Meta?.Source?.Name).IsEmpty("Dow Jones Newswires"),
			Url = source.Attributes?.HostedUrl,
			Language = source.Meta?.Language?.Code
				.IsEmpty(article.Value.Meta?.Language?.Code),
			Priority = source.GetPriority() ?? article.Value.GetPriority(),
			BoardCode = DowJonesExtensions.BoardCode,
			SecurityId = HasSecurity(securityId) ? securityId : null,
		}, cancellationToken);
	}

	private static SecurityId NormalizeSecurityId(SecurityId securityId)
	{
		if (!securityId.SecurityCode.IsEmpty() || !securityId.Isin.IsEmpty())
			securityId.BoardCode = securityId.BoardCode.IsEmpty(DowJonesExtensions.BoardCode);
		return securityId;
	}

	private static bool HasSecurity(SecurityId securityId)
		=> !securityId.SecurityCode.IsEmpty() || !securityId.Isin.IsEmpty();

	private async ValueTask Complete(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}

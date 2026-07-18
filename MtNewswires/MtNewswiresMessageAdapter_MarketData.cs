namespace StockSharp.MtNewswires;

public partial class MtNewswiresMessageAdapter
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
		var symbol = requestedSecurity.NormalizeSymbol();
		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		if (from != null && to != null && from > to)
		{
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The MT Newswires history start time is after its end time.");
		}

		var remaining = mdMsg.Count;
		var liveCursor = DateTime.UtcNow;
		var remembered = new List<string>();
		if (from != null || to != null || mdMsg.IsHistoryOnly())
		{
			var queryTo = to ?? liveCursor;
			var queryFrom = from ?? queryTo - DefaultHistoryLookback;
			var target = checked((int)Math.Min(remaining ?? MaxNewsItems, MaxNewsItems));
			MtNewswiresArticle[] response;
			if (from == null && to == null)
			{
				response = await SafeClient().GetLatest(DataSource, DatasetId,
					symbol, target, cancellationToken);
			}
			else
			{
				response = await SafeClient().GetRange(DataSource, DatasetId,
					symbol, queryFrom, queryTo, cancellationToken);
			}

			var articles = Normalize(response, queryFrom, queryTo, target, from == null);
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
			Symbol = symbol,
			CursorUtc = liveCursor,
			Remaining = remaining,
		};
		foreach (var key in remembered)
			subscription.TryRemember(key);
		AddLiveSubscription(subscription);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private static TimedArticle[] Normalize(IEnumerable<MtNewswiresArticle> response,
		DateTime from, DateTime to, int target, bool takeLatest)
	{
		if (target <= 0)
			return [];
		var result = new List<TimedArticle>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var article in response ?? [])
		{
			if (article == null || !article.TryGetTime(out var time) ||
				time < from || time > to)
			{
				continue;
			}
			var key = article.GetEventKey(time);
			if (seen.Add(key))
				result.Add(new(article, time, key));
		}
		var ordered = result.OrderBy(article => article.Time);
		return (takeLatest ? ordered.TakeLast(target) : ordered.Take(target)).ToArray();
	}

	private ValueTask SendNews(long transactionId, SecurityId requestedSecurity,
		TimedArticle article, CancellationToken cancellationToken)
	{
		var securityId = article.Value.GetSecurityId(requestedSecurity);
		return SendOutMessageAsync(new NewsMessage
		{
			OriginalTransactionId = transactionId,
			ServerTime = article.Time,
			Id = article.Key,
			Headline = article.Value.Headline,
			Story = article.Value.Body,
			Source = "MT Newswires",
			BoardCode = MtNewswiresExtensions.BoardCode,
			SecurityId = securityId == default ? null : securityId,
		}, cancellationToken);
	}

	private static SecurityId NormalizeSecurityId(SecurityId securityId)
	{
		if (!securityId.SecurityCode.IsEmpty() || !securityId.Isin.IsEmpty())
		{
			securityId.BoardCode = securityId.BoardCode
				.IsEmpty(MtNewswiresExtensions.BoardCode);
		}
		return securityId;
	}

	private async ValueTask Complete(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}

namespace StockSharp.Finage;

public partial class FinageMessageAdapter
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

		var types = lookupMsg.GetSecurityTypes();
		if (types.Count > 0 &&
			!types.Contains(SecurityTypes.Currency))
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			return;
		}

		var query = (lookupMsg.SecurityId.Native?.ToString())
			.IsEmpty(lookupMsg.SecurityId.SecurityCode)
			.IsEmpty(lookupMsg.Name)
			.IsEmpty(lookupMsg.ShortName)?.Trim();
		var skip = lookupMsg.Skip ?? 0;
		var left = Math.Min(lookupMsg.Count ?? MaximumSecurities,
			MaximumSecurities);

		foreach (var instrument in (await GetInstrumentsAsync(
			query, cancellationToken))
			.Where(instrument => query.IsEmpty() ||
				instrument.Symbol.Contains(query,
					StringComparison.OrdinalIgnoreCase) ||
				instrument.Name?.Contains(query,
					StringComparison.OrdinalIgnoreCase) == true)
			.OrderBy(static instrument => instrument.Symbol,
				StringComparer.OrdinalIgnoreCase))
		{
			if (skip-- > 0)
				continue;

			var security = instrument.ToSecurityMessage(
				lookupMsg.TransactionId);
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

	internal async ValueTask<FinageInstrument[]>
		GetInstrumentsAsync(string search,
		CancellationToken cancellationToken)
	{
		if (!Symbols.IsEmpty())
			return Symbols.Split(',',
					StringSplitOptions.RemoveEmptyEntries |
						StringSplitOptions.TrimEntries)
				.Select(static symbol => symbol.ToInstrument())
				.DistinctBy(static instrument =>
					instrument.Symbol,
					StringComparer.OrdinalIgnoreCase)
				.Take(MaximumSecurities)
				.ToArray();

		return await RestClient.GetSymbolsAsync(search,
			MaximumSecurities, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			string removed;
			using (_subscriptionSync.EnterScope())
			{
				_subscriptions.TryGetValue(
					mdMsg.OriginalTransactionId, out removed);
				_subscriptions.Remove(mdMsg.OriginalTransactionId);
			}

			if (!removed.IsEmpty() && !HasSubscriptions(removed) &&
				_streamClient is not null)
				await _streamClient.UnsubscribeAsync([removed],
					cancellationToken);

			await SendSubscriptionResultAsync(mdMsg,
				cancellationToken);
			return;
		}

		if (mdMsg.Count is <= 0)
		{
			await CompleteSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Finage does not expose historical quote events " +
					"through Level1 subscriptions.");

		var instrument = ResolveInstrument(mdMsg.SecurityId);
		if (_restClient is not null)
		{
			var snapshot = await _restClient.GetQuoteAsync(
				instrument.Symbol, cancellationToken);
			await SendQuoteAsync(snapshot, mdMsg.TransactionId,
				cancellationToken);
		}

		if (mdMsg.IsHistoryOnly() || _streamClient is null)
		{
			await CompleteSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		var first = !HasSubscriptions(instrument.Symbol);
		using (_subscriptionSync.EnterScope())
			_subscriptions[mdMsg.TransactionId] =
				instrument.Symbol;

		try
		{
			if (first)
				await StreamClient.SubscribeAsync(
					[instrument.Symbol], cancellationToken);
		}
		catch
		{
			using (_subscriptionSync.EnterScope())
				_subscriptions.Remove(mdMsg.TransactionId);
			throw;
		}

		await SendSubscriptionResultAsync(mdMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
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

		var instrument = ResolveInstrument(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToFinageInterval();
		var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
		var maximum = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, int.MaxValue)
			: int.MaxValue;
		var from = (mdMsg.From ??
			to - timeFrame * Math.Min(maximum, 1000))
			.ToUniversalTime();

		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From));

		var bars = new List<FinageBar>();
		var maxRange = timeFrame * 40000;

		for (var cursor = from; cursor <= to;)
		{
			var end = cursor + maxRange;
			if (end > to)
				end = to;

			bars.AddRange(await RestClient.GetBarsAsync(
				instrument.Symbol, cursor, end, timeFrame,
				cancellationToken));

			if (end >= to)
				break;

			cursor = end + timeFrame;
		}

		foreach (var bar in bars
			.Where(bar => bar.Time >= from && bar.Time <= to)
			.GroupBy(static bar => bar.Time)
			.Select(static group => group.First())
			.OrderBy(static bar => bar.Time)
			.TakeLast(maximum))
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = instrument.ToSecurityId(),
				TypedArg = timeFrame,
				OpenTime = bar.Time,
				CloseTime = bar.Time + timeFrame,
				OpenPrice = bar.Open,
				HighPrice = bar.High,
				LowPrice = bar.Low,
				ClosePrice = bar.Close,
				TotalVolume = bar.Volume ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);

		await CompleteSubscriptionAsync(mdMsg, cancellationToken);
	}

	private async ValueTask OnStreamQuoteAsync(FinageQuote quote,
		CancellationToken cancellationToken)
	{
		long[] subscriptions;
		using (_subscriptionSync.EnterScope())
			subscriptions = _subscriptions
				.Where(pair => pair.Value.EqualsIgnoreCase(
					quote.Symbol))
				.Select(static pair => pair.Key)
				.ToArray();

		foreach (var transactionId in subscriptions)
			await SendQuoteAsync(quote, transactionId,
				cancellationToken);
	}

	private ValueTask SendQuoteAsync(FinageQuote quote,
		long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = quote.Symbol.ToInstrument()
				.ToSecurityId(),
			ServerTime = quote.Time,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.Bid, true)
		.TryAdd(Level1Fields.BestAskPrice, quote.Ask, true),
			cancellationToken);

	private async ValueTask CompleteSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message,
			cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}

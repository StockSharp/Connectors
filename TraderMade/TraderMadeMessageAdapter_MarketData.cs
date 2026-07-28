namespace StockSharp.TraderMade;

public partial class TraderMadeMessageAdapter
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
		var query = (lookupMsg.SecurityId.Native?.ToString())
			.IsEmpty(lookupMsg.SecurityId.SecurityCode)
			.IsEmpty(lookupMsg.Name)
			.IsEmpty(lookupMsg.ShortName)?.Trim();
		var skip = lookupMsg.Skip ?? 0;
		var left = Math.Min(lookupMsg.Count ?? MaximumSecurities,
			MaximumSecurities);
		foreach (var instrument in (await GetInstrumentsAsync(
			cancellationToken))
			.Where(instrument => query.IsEmpty() ||
				instrument.Symbol.Contains(query,
					StringComparison.OrdinalIgnoreCase) ||
				instrument.Name?.Contains(query,
					StringComparison.OrdinalIgnoreCase) == true)
			.Where(instrument => types.Count == 0 ||
				types.Contains(instrument.SecurityType))
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

	internal async ValueTask<TraderMadeInstrument[]>
		GetInstrumentsAsync(CancellationToken cancellationToken)
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
		var currencies = await RestClient.GetCurrenciesAsync(
			cancellationToken);
		var quoteCurrencies = QuoteCurrencies
			.Split(',',
				StringSplitOptions.RemoveEmptyEntries |
					StringSplitOptions.TrimEntries)
			.Select(static value => value.ToUpperInvariant())
			.Where(currencies.ContainsKey)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (quoteCurrencies.Length == 0)
			quoteCurrencies = [.. currencies.Keys];
		return currencies.Keys
			.SelectMany(@base => quoteCurrencies
				.Where(quote => !quote.EqualsIgnoreCase(@base))
				.Select(quote =>
				{
					var name = $"{currencies[@base]} / " +
						currencies[quote];
					return (@base + quote).ToInstrument(name);
				}))
			.Take(MaximumSecurities)
			.ToArray();
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> OnQuoteSubscriptionAsync(mdMsg, false,
			cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> OnQuoteSubscriptionAsync(mdMsg, true,
			cancellationToken);

	private async ValueTask OnQuoteSubscriptionAsync(
		MarketDataMessage mdMsg, bool depth,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		var subscriptions = depth
			? _depthSubscriptions
			: _level1Subscriptions;
		if (!mdMsg.IsSubscribe)
		{
			string removed;
			using (_subscriptionSync.EnterScope())
			{
				subscriptions.TryGetValue(
					mdMsg.OriginalTransactionId, out removed);
				subscriptions.Remove(mdMsg.OriginalTransactionId);
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
				"TraderMade does not expose historical quote events " +
					"through Level1 or market-depth subscriptions.");
		var instrument = ResolveInstrument(mdMsg.SecurityId);
		if (_restClient is not null)
		{
			var snapshot = (await _restClient.GetLiveAsync(
				[instrument.Symbol], cancellationToken))
				.FirstOrDefault();
			if (snapshot is not null)
				await SendQuoteAsync(snapshot, mdMsg.TransactionId,
					depth, cancellationToken);
		}
		if (mdMsg.IsHistoryOnly() || _streamClient is null)
		{
			await CompleteSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var first = !HasSubscriptions(instrument.Symbol);
		using (_subscriptionSync.EnterScope())
			subscriptions[mdMsg.TransactionId] =
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
				subscriptions.Remove(mdMsg.TransactionId);
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
		var interval = timeFrame.ToTraderMadeInterval();
		var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
		var maximum = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, int.MaxValue)
			: int.MaxValue;
		var from = (mdMsg.From ??
			to - timeFrame * Math.Min(maximum, 1000))
			.ToUniversalTime();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From));
		var bars = new List<TraderMadeBar>();
		for (var cursor = from; cursor <= to;)
		{
			var end = cursor + interval.MaxRange;
			if (end > to)
				end = to;
			bars.AddRange(await RestClient.GetBarsAsync(
				instrument.Symbol, cursor, end, timeFrame,
				Weekend, cancellationToken));
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
				State = CandleStates.Finished,
			}, cancellationToken);
		await CompleteSubscriptionAsync(mdMsg, cancellationToken);
	}

	private async ValueTask OnStreamQuoteAsync(
		TraderMadeQuote quote,
		CancellationToken cancellationToken)
	{
		long[] level1;
		long[] depth;
		using (_subscriptionSync.EnterScope())
		{
			level1 = _level1Subscriptions
				.Where(pair => pair.Value.EqualsIgnoreCase(
					quote.Symbol))
				.Select(static pair => pair.Key)
				.ToArray();
			depth = _depthSubscriptions
				.Where(pair => pair.Value.EqualsIgnoreCase(
					quote.Symbol))
				.Select(static pair => pair.Key)
				.ToArray();
		}
		foreach (var transactionId in level1)
			await SendQuoteAsync(quote, transactionId, false,
				cancellationToken);
		foreach (var transactionId in depth)
			await SendQuoteAsync(quote, transactionId, true,
				cancellationToken);
	}

	private ValueTask SendQuoteAsync(TraderMadeQuote quote,
		long transactionId, bool depth,
		CancellationToken cancellationToken)
	{
		var securityId = quote.Symbol.ToInstrument()
			.ToSecurityId();
		if (depth)
			return SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = transactionId,
				SecurityId = securityId,
				ServerTime = quote.Time,
				Bids = quote.Bids,
				Asks = quote.Asks,
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = quote.Time,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.Bid, true)
		.TryAdd(Level1Fields.BestAskPrice, quote.Ask, true)
		.TryAdd(Level1Fields.BestBidVolume,
			quote.BidVolume, true)
		.TryAdd(Level1Fields.BestAskVolume,
			quote.AskVolume, true)
		.TryAdd(Level1Fields.LastTradePrice,
			quote.Mid, true), cancellationToken);
	}

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

namespace StockSharp.TradeZero;

public partial class TradeZeroMessageAdapter
{
	private const string _boardCode = "TRADEZERO";

	private static SecurityId ToSecurityId(string symbol, string board = null)
		=> new() { SecurityCode = symbol, BoardCode = board.IsEmpty(_boardCode) };

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var symbol = lookupMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(lookupMsg.SecurityId.SecurityCode));
		var result = await _httpClient.Search(symbol, cancellationToken);
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var item in result?.Results ?? [])
		{
			var security = new SecurityMessage
			{
				SecurityId = ToSecurityId(item.Symbol),
				SecurityType = SecurityTypes.Stock,
				Name = item.Name,
				ShortName = item.Symbol,
				OriginalTransactionId = lookupMsg.TransactionId,
			};

			if (!security.IsMatch(lookupMsg, lookupMsg.GetSecurityTypes()))
				continue;

			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var quote = (await _httpClient.GetQuotes(mdMsg.SecurityId.SecurityCode, cancellationToken))?.FirstOrDefault();
		if (quote != null)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(Level1Fields.BestBidPrice, quote.Bid)
			.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, quote.Ask)
			.TryAdd(Level1Fields.BestAskVolume, quote.AskSize)
			.TryAdd(Level1Fields.LastTradePrice, quote.Last)
			.TryAdd(Level1Fields.LastTradeVolume, quote.LastSize)
			.TryAdd(Level1Fields.OpenPrice, quote.Open)
			.TryAdd(Level1Fields.HighPrice, quote.High)
			.TryAdd(Level1Fields.LowPrice, quote.Low)
			.TryAdd(Level1Fields.ClosePrice, quote.Close)
			.TryAdd(Level1Fields.SettlementPrice, quote.PrevClose)
			.TryAdd(Level1Fields.Volume, quote.Volume)
			.TryAdd(Level1Fields.Change, quote.Changed)
			.TryAdd(Level1Fields.VWAP, quote.Vwap), cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var dom = await _httpClient.GetDom(mdMsg.SecurityId.SecurityCode, cancellationToken);
		var levels = dom?.Levels ?? [];
		var maxDepth = mdMsg.MaxDepth ?? int.MaxValue;
		var bids = levels
			.Where(level => level.Bid is not null)
			.GroupBy(level => level.Bid.Value)
			.OrderByDescending(group => group.Key)
			.Take(maxDepth)
			.Select(group => new QuoteChange(group.Key, group.Sum(level => level.BidSize ?? 0)))
			.ToArray();
		var asks = levels
			.Where(level => level.Ask is not null)
			.GroupBy(level => level.Ask.Value)
			.OrderBy(group => group.Key)
			.Take(maxDepth)
			.Select(group => new QuoteChange(group.Key, group.Sum(level => level.AskSize ?? 0)))
			.ToArray();

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			ServerTime = DateTime.UtcNow,
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var timeFrame = mdMsg.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new ArgumentOutOfRangeException(nameof(mdMsg), timeFrame, "The requested time frame is not supported.");

		var interval = timeFrame.TotalDays >= 1 ? 0 : checked((long)timeFrame.TotalMilliseconds);
		var maxCandles = mdMsg.Count is long count ? checked((int)count.Min(5000).Max(1)) : 1000;
		var result = await _httpClient.GetBars(mdMsg.SecurityId.SecurityCode, interval, maxCandles, cancellationToken);

		foreach (var bar in result?.Bars ?? [])
		{
			var openTime = bar.Timestamp.FromUnix();
			if (mdMsg.From is DateTime from && openTime < from.ToUtc())
				continue;
			if (mdMsg.To is DateTime to && openTime > to.ToUtc())
				continue;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = openTime,
				OpenPrice = bar.Open,
				HighPrice = bar.High,
				LowPrice = bar.Low,
				ClosePrice = bar.Close,
				TotalVolume = bar.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}
}

namespace StockSharp.Tradovate;

public partial class TradovateMessageAdapter
{
	private const string _boardCode = "TRADOVATE";
	private readonly SynchronizedDictionary<long, SecurityId> _contracts = [];
	private readonly SynchronizedDictionary<string, long> _contractIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, SecurityId> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<long, SecurityId> _tickSubscriptions = [];
	private readonly SynchronizedDictionary<long, SecurityId> _depthSubscriptions = [];
	private readonly SynchronizedDictionary<long, (SecurityId securityId, TimeSpan timeFrame)> _candleSubscriptions = [];
	private readonly SynchronizedDictionary<long, long> _chartSubscriptions = [];

	private static SecurityId ToSecurityId(string symbol, string board = null)
		=> new() { SecurityCode = symbol, BoardCode = board.IsEmpty(_boardCode) };

	private async Task<(TradovateContract contract, SecurityId securityId)> EnsureContract(string symbol, CancellationToken cancellationToken)
	{
		if (_contractIds.TryGetValue(symbol, out var cachedId) && _contracts.TryGetValue(cachedId, out var cachedSecurityId))
			return (new TradovateContract { Id = cachedId, Name = symbol }, cachedSecurityId);

		var contract = await _httpClient.FindContract(symbol, cancellationToken)
			?? throw new InvalidOperationException($"Tradovate contract '{symbol}' was not found.");
		var securityId = ToSecurityId(contract.Name);
		_contractIds[contract.Name] = contract.Id;
		_contracts[contract.Id] = securityId;
		return (contract, securityId);
	}

	private async Task<SecurityId> GetSecurityId(long contractId, CancellationToken cancellationToken)
	{
		if (_contracts.TryGetValue(contractId, out var securityId))
			return securityId;

		var contract = await _httpClient.GetContract(contractId, cancellationToken)
			?? throw new InvalidOperationException($"Tradovate contract '{contractId}' was not found.");
		securityId = ToSecurityId(contract.Name);
		_contracts[contract.Id] = securityId;
		_contractIds[contract.Name] = contract.Id;
		return securityId;
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var left = lookupMsg.Count?.Min(1000) ?? 1000;
		var contracts = await _httpClient.SuggestContracts(lookupMsg.SecurityId.SecurityCode, (int)left, cancellationToken);

		foreach (var contract in contracts)
		{
			var maturity = await _httpClient.GetContractMaturity(contract.ContractMaturityId, cancellationToken);
			var product = await _httpClient.GetProduct(maturity.ProductId, cancellationToken);
			var exchange = await _httpClient.GetExchange(product.ExchangeId, cancellationToken);
			var securityId = ToSecurityId(contract.Name, exchange?.Name);

			_contracts[contract.Id] = securityId;
			_contractIds[contract.Name] = contract.Id;

			var security = new SecurityMessage
			{
				SecurityId = securityId,
				SecurityType = product.ProductType.ToSecurityType(),
				Name = product.Description,
				ShortName = product.Name,
				ExpiryDate = maturity.ExpirationDate.ToUtc(),
				PriceStep = product.TickSize,
				Multiplier = product.ValuePerPoint,
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
		var symbol = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.IsSubscribe)
		{
			await EnsureContract(symbol, cancellationToken);
			var isFirst = !HasQuoteSubscription(symbol);
			_level1Subscriptions[mdMsg.TransactionId] = mdMsg.SecurityId;
			if (isFirst && !mdMsg.IsHistoryOnly())
				await _marketSocket.SubscribeQuote(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
			if (!HasQuoteSubscription(symbol))
				await _marketSocket.UnsubscribeQuote(symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var symbol = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.IsSubscribe)
		{
			await EnsureContract(symbol, cancellationToken);
			var isFirst = !HasQuoteSubscription(symbol);
			_tickSubscriptions[mdMsg.TransactionId] = mdMsg.SecurityId;
			if (isFirst && !mdMsg.IsHistoryOnly())
				await _marketSocket.SubscribeQuote(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_tickSubscriptions.Remove(mdMsg.OriginalTransactionId);
			if (!HasQuoteSubscription(symbol))
				await _marketSocket.UnsubscribeQuote(symbol, cancellationToken);
		}
	}

	private bool HasQuoteSubscription(string symbol)
		=> _level1Subscriptions.Values.Concat(_tickSubscriptions.Values).Any(id => id.SecurityCode.EqualsIgnoreCase(symbol));

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var symbol = mdMsg.SecurityId.SecurityCode;

		if (mdMsg.IsSubscribe)
		{
			await EnsureContract(symbol, cancellationToken);
			var isFirst = !_depthSubscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(symbol));
			_depthSubscriptions[mdMsg.TransactionId] = mdMsg.SecurityId;
			if (isFirst && !mdMsg.IsHistoryOnly())
				await _marketSocket.SubscribeDom(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_depthSubscriptions.Remove(mdMsg.OriginalTransactionId);
			if (!_depthSubscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(symbol)))
				await _marketSocket.UnsubscribeDom(symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			var ids = _chartSubscriptions.Where(p => p.Value == mdMsg.OriginalTransactionId).Select(p => p.Key).ToArray();
			foreach (var id in ids)
				_chartSubscriptions.Remove(id);
			_candleSubscriptions.Remove(mdMsg.OriginalTransactionId);
			await _marketSocket.CancelChart(mdMsg.OriginalTransactionId, ids, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new ArgumentOutOfRangeException(nameof(mdMsg), timeFrame, "The requested time frame is not supported.");

		var (contract, securityId) = await EnsureContract(mdMsg.SecurityId.SecurityCode, cancellationToken);
		var isDaily = timeFrame.TotalDays >= 1;
		var request = new TradovateChartRequest
		{
			Symbol = contract.Name,
			ChartDescription = new()
			{
				UnderlyingType = isDaily ? TradovateChartTypes.DailyBar : TradovateChartTypes.MinuteBar,
				ElementSize = isDaily ? (int)timeFrame.TotalDays : (int)timeFrame.TotalMinutes,
				ElementSizeUnit = TradovateChartUnits.UnderlyingUnits,
				IsWithHistogram = false,
			},
			TimeRange = new()
			{
				ClosestTimestamp = mdMsg.To ?? DateTime.UtcNow,
				AsFarAsTimestamp = mdMsg.From,
				AsMuchAsElements = mdMsg.Count is long count ? (int)count.Min(int.MaxValue) : null,
			},
		};

		_candleSubscriptions[mdMsg.TransactionId] = (securityId, timeFrame);
		var (historicalId, realtimeId) = await _marketSocket.SubscribeChart(mdMsg.TransactionId, request, cancellationToken);
		if (historicalId is long history)
			_chartSubscriptions[history] = mdMsg.TransactionId;
		if (realtimeId is long realtime)
			_chartSubscriptions[realtime] = mdMsg.TransactionId;
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask OnQuoteReceived(TradovateQuote quote, CancellationToken cancellationToken)
	{
		var securityId = await GetSecurityId(quote.ContractId, cancellationToken);
		var entries = quote.Entries;
		if (entries == null)
			return;

		foreach (var pair in _level1Subscriptions.ToArray().Where(p => p.Value.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = pair.Key,
				SecurityId = pair.Value,
				ServerTime = quote.Timestamp.ToUtc(),
			}
			.TryAdd(Level1Fields.BestBidPrice, entries.Bid?.Price)
			.TryAdd(Level1Fields.BestBidVolume, entries.Bid?.Size)
			.TryAdd(Level1Fields.BestAskPrice, entries.Offer?.Price)
			.TryAdd(Level1Fields.BestAskVolume, entries.Offer?.Size)
			.TryAdd(Level1Fields.LastTradePrice, entries.Trade?.Price)
			.TryAdd(Level1Fields.LastTradeVolume, entries.Trade?.Size)
			.TryAdd(Level1Fields.Volume, entries.TotalTradeVolume?.Size)
			.TryAdd(Level1Fields.OpenPrice, entries.OpeningPrice?.Price)
			.TryAdd(Level1Fields.HighPrice, entries.HighPrice?.Price)
			.TryAdd(Level1Fields.LowPrice, entries.LowPrice?.Price)
			.TryAdd(Level1Fields.ClosePrice, entries.SettlementPrice?.Price)
			.TryAdd(Level1Fields.OpenInterest, entries.OpenInterest?.Size), cancellationToken);
		}

		if (entries.Trade?.Price is decimal tradePrice)
		{
			foreach (var pair in _tickSubscriptions.ToArray().Where(p => p.Value.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = pair.Key,
					SecurityId = pair.Value,
					ServerTime = quote.Timestamp.ToUtc(),
					TradePrice = tradePrice,
					TradeVolume = entries.Trade.Size,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask OnDomReceived(TradovateDom dom, CancellationToken cancellationToken)
	{
		var securityId = await GetSecurityId(dom.ContractId, cancellationToken);
		foreach (var pair in _depthSubscriptions.ToArray().Where(p => p.Value.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = pair.Key,
				SecurityId = pair.Value,
				ServerTime = dom.Timestamp.ToUtc(),
				Bids = [.. (dom.Bids ?? []).Where(l => l.Price is not null).Select(l => new QuoteChange(l.Price.Value, l.Size ?? 0))],
				Asks = [.. (dom.Offers ?? []).Where(l => l.Price is not null).Select(l => new QuoteChange(l.Price.Value, l.Size ?? 0))],
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		}
	}

	private async ValueTask OnChartReceived(TradovateChart chart, CancellationToken cancellationToken)
	{
		if (!_chartSubscriptions.TryGetValue(chart.Id, out var transactionId) || !_candleSubscriptions.TryGetValue(transactionId, out var subscription))
			return;

		foreach (var bar in chart.Bars ?? [])
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = transactionId,
				SecurityId = subscription.securityId,
				TypedArg = subscription.timeFrame,
				OpenTime = bar.Timestamp.ToUtc(),
				OpenPrice = bar.Open,
				HighPrice = bar.High,
				LowPrice = bar.Low,
				ClosePrice = bar.Close,
				TotalVolume = bar.UpVolume + bar.DownVolume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
	}
}

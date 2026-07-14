namespace StockSharp.Exmo;

partial class ExmoMessageAdapter
{
	private readonly Dictionary<SecurityId, int?> _orderBookSubscriptions = [];
	private readonly HashSet<SecurityId> _tradesSubscriptions = [];
	private readonly HashSet<SecurityId> _level1Subscriptions = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var pair in await _httpClient.GetSymbolsAsync(cancellationToken))
		{
			var symbol = pair.Value;

			var secMsg = new SecurityMessage
			{
				SecurityId = pair.Key.ToStockSharp(),
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityType = SecurityTypes.CryptoCurrency,
				//VolumeStep = symbol.MinQuantity,
				PriceStep = symbol.MinPrice,
				MinVolume = symbol.MinQuantity,
			};

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_level1Subscriptions.Add(mdMsg.SecurityId);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_level1Subscriptions.Remove(mdMsg.SecurityId);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currTime = CurrentTime;

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await ProcessOrderBookSubscriptionsAsync([mdMsg.SecurityId], mdMsg.MaxDepth, currTime, cancellationToken);
			_orderBookSubscriptions.Add(mdMsg.SecurityId, mdMsg.MaxDepth);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_orderBookSubscriptions.Remove(mdMsg.SecurityId);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await ProcessTicksSubscriptionsAsync([mdMsg.SecurityId], cancellationToken);
			_tradesSubscriptions.Add(mdMsg.SecurityId);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_tradesSubscriptions.Remove(mdMsg.SecurityId);
	}

	private async ValueTask ProcessSubscriptionsAsync(CancellationToken cancellationToken)
	{
		var currTime = CurrentTime;

		const int maxCount = 20;

		if (_orderBookSubscriptions.Count > 0)
		{
			foreach (var g in _orderBookSubscriptions.GroupBy(p => p.Value))
			{
				var depth = g.Key;

				foreach (var batch in g.Chunk(maxCount))
				{
					await ProcessOrderBookSubscriptionsAsync(batch.Select(p => p.Key), depth, currTime, cancellationToken);
				}
			}
		}

		if (_tradesSubscriptions.Count > 0)
		{
			foreach (var batch in _tradesSubscriptions.Chunk(maxCount))
			{
				await ProcessTicksSubscriptionsAsync(batch, cancellationToken);
			}
		}

		if (_level1Subscriptions.Count > 0)
		{
			var infos = await _httpClient.GetTickersAsync(cancellationToken);

			foreach (var pair in infos)
			{
				var info = pair.Value;

				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = pair.Key.ToStockSharp(),
					ServerTime = info.Updated,
				}
				.TryAdd(Level1Fields.HighPrice, info.High?.ToDecimal())
				.TryAdd(Level1Fields.LowPrice, info.Low?.ToDecimal())
				.TryAdd(Level1Fields.ClosePrice, info.LastTrade?.ToDecimal())
				.TryAdd(Level1Fields.Volume, info.Vol?.ToDecimal())
				.TryAdd(Level1Fields.VWAP, info.Avg?.ToDecimal())
				.TryAdd(Level1Fields.BestBidPrice, info.BuyPrice?.ToDecimal())
				.TryAdd(Level1Fields.BestAskPrice, info.SellPrice?.ToDecimal()), cancellationToken);
			}
		}
	}

	private async ValueTask ProcessOrderBookSubscriptionsAsync(IEnumerable<SecurityId> ids, int? depth, DateTime currTime, CancellationToken cancellationToken)
	{
		var books = await _httpClient.GetOrderBooksAsync(ids.Select(id => id.ToCurrency()), depth, cancellationToken);

		foreach (var pair in books)
		{
			var book = pair.Value;

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = pair.Key.ToStockSharp(),
				Bids = book.Bids.Select(e => new QuoteChange(e.Price, e.Quantity)).ToArray(),
				Asks = book.Asks.Select(e => new QuoteChange(e.Price, e.Quantity)).ToArray(),
				ServerTime = currTime,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessTicksSubscriptionsAsync(IEnumerable<SecurityId> ids, CancellationToken cancellationToken)
	{
		var trades = await _httpClient.GetTradesAsync(ids.Select(id => id.ToCurrency()), cancellationToken);

		foreach (var pair in trades)
		{
			var secId = pair.Key.ToStockSharp();

			foreach (var trade in pair.Value)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					SecurityId = secId,
					OriginSide = trade.Type.ToSide(),
					TradePrice = trade.Price,
					TradeVolume = trade.Quantity,
					ServerTime = trade.Time,
				}, cancellationToken);
			}
		}
	}
}

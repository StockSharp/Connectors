namespace StockSharp.Alpaca;

partial class AlpacaMessageAdapter
{
	private readonly SynchronizedSet<SecurityId> _cryptoSecIds = [];
	private readonly SynchronizedDictionary<string, SecurityId> _assetIds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedPairSet<(DataType, string), long> _mdTransIds = [];

	private async Task FillSecurities(CancellationToken cancellationToken)
	{
		foreach (var asset in await _tradingClient.GetAssets(cancellationToken))
		{
			var isCrypto = asset.IsCrypto();
			var secId = asset.ToSecId();

			if (isCrypto)
				_cryptoSecIds.Add(secId);

			_assetIds[asset.Id] = secId;
		}
	}

	private async Task<bool> EnsureIsCrypto(SecurityId requiredSecId, CancellationToken cancellationToken)
	{
		if (_cryptoSecIds.Count == 0)
			await FillSecurities(cancellationToken);

		return _cryptoSecIds.Contains(requiredSecId);
	}

	private async Task<SecurityId> EnsureGetSecId(string assetId, CancellationToken cancellationToken)
	{
		if (_assetIds.Count == 0)
			await FillSecurities(cancellationToken);

		return _assetIds.TryGetValue(assetId);
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var asset in await _tradingClient.GetAssets(cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var isCrypto = asset.IsCrypto();
			var secId = asset.ToSecId();

			var secMsg = new SecurityMessage
			{
				SecurityId = secId,
				Name = asset.Name,
				Shortable = asset.Shortable,
				Class = asset.Class,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = asset.PriceIncrement?.ToDecimal(),
				VolumeStep = asset.MinTradeIncrement?.ToDecimal(),
				MinVolume = asset.MinOrderSize?.ToDecimal(),
				SecurityType = isCrypto ? SecurityTypes.CryptoCurrency : SecurityTypes.Stock,
			};

			if (isCrypto)
				_cryptoSecIds.Add(secId);

			_assetIds[asset.Id] = secId;

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.SecurityCode;
		var transId = mdMsg.TransactionId;
		var isCrypto = await EnsureIsCrypto(secId, cancellationToken);

		SocketMarketDataClient socketClient = isCrypto ? _socketCryptoClient : _socketStockClient;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			RemoveTransId(transId);
			await socketClient.UnSubscribeOhlc(mdMsg.OriginalTransactionId, symbol, cancellationToken);
			return;
		}

		var tf = mdMsg.GetTimeFrame().ToNative();

		if (mdMsg.From is not null)
		{
			var from = mdMsg.From.Value;
			var to = mdMsg.To ?? CurrentTime;
			var left = mdMsg.Count ?? long.MaxValue;

			if (isCrypto)
			{
				await foreach (var c in _cryptoClient.GetOhlc(symbol, tf, from, to, null, CryptoLocation, cancellationToken).WithEnforcedCancellation(cancellationToken))
				{
					if (c.Time < from)
						continue;

					if (c.Time > to)
						break;

					await ProcessOhlcAsync(transId, c, cancellationToken);

					if (--left <= 0)
						break;
				}
			}
			else
			{
				await foreach (var c in _stockClient.GetOhlc(symbol, tf, from, to, null, StockFeed, cancellationToken).WithEnforcedCancellation(cancellationToken))
				{
					if (c.Time < from)
						continue;

					if (c.Time > to)
						break;

					await ProcessOhlcAsync(transId, c, cancellationToken);

					if (--left <= 0)
						break;
				}
			}
		}

		if (!mdMsg.IsHistoryOnly())
		{
			AddTransId(DataType.CandleTimeFrame, symbol, transId);
			await socketClient.SubscribeOhlc(mdMsg.TransactionId, symbol, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.SecurityCode;
		var transId = mdMsg.TransactionId;
		var isCrypto = await EnsureIsCrypto(secId, cancellationToken);

		SocketMarketDataClient socketClient = isCrypto ? _socketCryptoClient : _socketStockClient;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			RemoveTransId(transId);
			await socketClient.UnSubscribeTicks(mdMsg.OriginalTransactionId, symbol, cancellationToken);
			return;
		}

		if (mdMsg.From is not null)
		{
			var from = mdMsg.From.Value;
			var to = mdMsg.To ?? CurrentTime;
			var left = mdMsg.Count ?? long.MaxValue;

			if (isCrypto)
			{
				await foreach (var t in _cryptoClient.GetTicks(symbol, from, to, null, CryptoLocation, cancellationToken).WithEnforcedCancellation(cancellationToken))
				{
					if (t.Time < from)
						continue;

					if (t.Time > to)
						break;

					await ProcessTickAsync(mdMsg.TransactionId, t, cancellationToken);

					if (--left <= 0)
						break;
				}
			}
			else
			{
				await foreach (var t in _stockClient.GetTicks(symbol, from, to, null, StockFeed, cancellationToken).WithEnforcedCancellation(cancellationToken))
				{
					if (t.Time < from)
						continue;

					if (t.Time > to)
						break;

					await ProcessTickAsync(mdMsg.TransactionId, t, cancellationToken);

					if (--left <= 0)
						break;
				}
			}
		}

		if (!mdMsg.IsHistoryOnly())
		{
			AddTransId(DataType.Ticks, symbol, transId);
			await socketClient.SubscribeTicks(mdMsg.TransactionId, symbol, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.SecurityCode;
		var transId = mdMsg.TransactionId;
		var isCrypto = await EnsureIsCrypto(secId, cancellationToken);

		SocketMarketDataClient socketClient = isCrypto ? _socketCryptoClient : _socketStockClient;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			RemoveTransId(transId);
			await socketClient.UnSubscribeQuotes(mdMsg.OriginalTransactionId, symbol, cancellationToken);
			return;
		}

		if (mdMsg.From is not null)
		{
			var from = mdMsg.From.Value;
			var to = mdMsg.To ?? CurrentTime;
			var left = mdMsg.Count ?? long.MaxValue;

			if (isCrypto)
			{
				await foreach (var q in _cryptoClient.GetQuotes(symbol, from, to, null, CryptoLocation, cancellationToken))
				{
					if (q.Time < from)
						continue;

					if (q.Time > to)
						break;

					await ProcessQuoteAsync(mdMsg.TransactionId, q, cancellationToken);

					if (--left <= 0)
						break;
				}
			}
			else
			{
				await foreach (var q in _stockClient.GetQuotes(symbol, from, to, null, StockFeed, cancellationToken))
				{
					if (q.Time < from)
						continue;

					if (q.Time > to)
						break;

					await ProcessQuoteAsync(mdMsg.TransactionId, q, cancellationToken);

					if (--left <= 0)
						break;
				}
			}
		}

		if (!mdMsg.IsHistoryOnly())
		{
			AddTransId(DataType.Level1, symbol, transId);
			await socketClient.SubscribeQuotes(mdMsg.TransactionId, symbol, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.SecurityCode;
		var transId = mdMsg.TransactionId;
		var isCrypto = await EnsureIsCrypto(secId, cancellationToken);

		if (!isCrypto)
		{
			await SendSubscriptionNotSupportedAsync(transId, cancellationToken);
			return;
		}

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				AddTransId(DataType.MarketDepth, symbol, transId);
				await _socketCryptoClient.SubscribeOrderBook(mdMsg.TransactionId, symbol, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			RemoveTransId(transId);
			await _socketCryptoClient.UnSubscribeOrderBook(mdMsg.OriginalTransactionId, symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			RemoveTransId(transId);
			await _socketNewsClient.UnSubscribeNews(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		if (mdMsg.From is not null)
		{
			var from = mdMsg.From.Value;
			var to = mdMsg.To ?? CurrentTime;
			var left = mdMsg.Count ?? long.MaxValue;

			await foreach (var n in _newsClient.GetNews(string.Empty, from, to, default, true, cancellationToken))
			{
				if (n.CreatedAt < from)
					continue;

				if (n.CreatedAt > to)
					break;

				await ProcessNewsAsync(mdMsg.TransactionId, n, cancellationToken);

				if (--left <= 0)
					break;
			}
		}

		if (!mdMsg.IsHistoryOnly())
		{
			AddTransId(DataType.News, string.Empty, transId);
			await _socketNewsClient.SubscribeNews(mdMsg.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private ValueTask ProcessQuoteAsync(long transId, Quote quote, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			ServerTime = CurrentTime,
			OriginalTransactionId = transId,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, quote.BidSize?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, quote.AskSize?.ToDecimal())
		, cancellationToken);
	}

	private ValueTask OnQuoteReceived(string symbol, Quote quote, CancellationToken cancellationToken)
	{
		if (TryGetTransId(DataType.Level1, symbol, out var transId))
			return ProcessQuoteAsync(transId, quote, cancellationToken);

		return default;
	}

	private ValueTask ProcessOhlcAsync(long transId, Ohlc ohlc, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OpenPrice = (decimal)ohlc.Open,
			ClosePrice = (decimal)ohlc.Close,
			HighPrice = (decimal)ohlc.High,
			LowPrice = (decimal)ohlc.Low,
			TotalVolume = (decimal)ohlc.Volume,
			OpenTime = ohlc.Time,
			State = CandleStates.Finished,
			OriginalTransactionId = transId,
		}, cancellationToken);
	}

	private ValueTask OnOhlcReceived(string symbol, Ohlc ohlc, CancellationToken cancellationToken)
	{
		if (TryGetTransId(DataType.CandleTimeFrame, symbol, out var transId))
			return ProcessOhlcAsync(transId, ohlc, cancellationToken);

		return default;
	}

	private ValueTask ProcessTickAsync(long transId, Tick tick, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			ServerTime = tick.Time,
			OriginalTransactionId = transId,
			TradeId = tick.Id,
			TradePrice = tick.Price.ToDecimal(),
			TradeVolume = tick.Size.ToDecimal(),
			AveragePrice = tick.AvgPrice?.ToDecimal(),
			OriginSide = tick.Side?.ToSide(),
		}, cancellationToken);
	}

	private ValueTask OnTickReceived(string symbol, Tick tick, CancellationToken cancellationToken)
	{
		if (TryGetTransId(DataType.Ticks, symbol, out var transId))
			return ProcessTickAsync(transId, tick, cancellationToken);

		return default;
	}

	private ValueTask ProcessOrderBookAsync(long transId, OrderBook book, CancellationToken cancellationToken)
	{
		static QuoteChange ToQuote(OrderBookQuote q)
			=> new((decimal)q.Price, (decimal)q.Size);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transId,
			ServerTime = book.Time,
			Bids = book.Bids.Select(ToQuote).ToArray(),
			Asks = book.Asks.Select(ToQuote).ToArray(),
			State = book.IsReset == true ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
		}, cancellationToken);
	}

	private ValueTask OnOrderBookReceived(string symbol, OrderBook book, CancellationToken cancellationToken)
	{
		if (TryGetTransId(DataType.MarketDepth, symbol, out var transId))
			return ProcessOrderBookAsync(transId, book, cancellationToken);

		return default;
	}

	private ValueTask ProcessNewsAsync(long transId, News news, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new NewsMessage
		{
			ServerTime = news.CreatedAt,
			OriginalTransactionId = transId,
			Headline = news.Headline,
			Id = news.Id.ToString(),
			Story = news.Content,
			Source = news.Source,
			Url = news.Url,
		}, cancellationToken);
	}

	private ValueTask OnNewsReceived(News news, CancellationToken cancellationToken)
	{
		if (TryGetTransId(DataType.News, string.Empty, out var transId))
			return ProcessNewsAsync(transId, news, cancellationToken);

		return default;
	}

	private bool TryGetTransId(DataType dt, string symbol, out long transId)
		=> _mdTransIds.TryGetValue((dt, symbol.ToUpperInvariant()), out transId);

	private void AddTransId(DataType dt, string symbol, long transId)
		=> _mdTransIds[(dt, symbol.ToUpperInvariant())] = transId;

	private void RemoveTransId(long transId)
		=> _mdTransIds.RemoveByValue(transId);
}

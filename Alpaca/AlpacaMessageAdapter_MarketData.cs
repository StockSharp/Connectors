namespace StockSharp.Alpaca;

partial class AlpacaMessageAdapter
{
	private readonly SynchronizedSet<SecurityId> _cryptoSecIds = [];
	private readonly SynchronizedSet<SecurityId> _optionSecIds = [];
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

	/// <summary>
	/// Which market an instrument belongs to, and therefore which client answers for it.
	/// </summary>
	private async Task<SecurityTypes> EnsureKind(SecurityId requiredSecId, CancellationToken cancellationToken)
	{
		// An option is recognised by its board rather than by a lookup: the consolidated tape is the only
		// board options are quoted on here, and a caller can name a contract without having listed the
		// chain first.
		if (requiredSecId.BoardCode.EqualsIgnoreCase(BoardCodes.Opra) || _optionSecIds.Contains(requiredSecId))
			return SecurityTypes.Option;

		if (_cryptoSecIds.Count == 0)
			await FillSecurities(cancellationToken);

		return _cryptoSecIds.Contains(requiredSecId) ? SecurityTypes.CryptoCurrency : SecurityTypes.Stock;
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

		if (left > 0)
			left = await LookupOptionsAsync(lookupMsg, secTypes, left, cancellationToken);

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <summary>
	/// Adds option contracts to a lookup that asked for them.
	/// </summary>
	/// <remarks>
	/// Only when they were asked for by type. There are hundreds of thousands of listed contracts, and a
	/// caller that asked for everything wants the instruments it can hold a position in, not every
	/// strike of every expiry of every name.
	///
	/// The underlying is passed to the venue rather than filtered here, because the alternative is
	/// downloading the whole option universe to throw nearly all of it away.
	/// </remarks>
	private async Task<long> LookupOptionsAsync(SecurityLookupMessage lookupMsg, HashSet<SecurityTypes> secTypes, long left, CancellationToken cancellationToken)
	{
		if (!Sections.Contains(AlpacaSections.Option) || !secTypes.Contains(SecurityTypes.Option))
			return left;

		var underlying = lookupMsg.UnderlyingSecurityId.SecurityCode;

		if (underlying.IsEmpty())
			underlying = lookupMsg.SecurityId.SecurityCode.ToUnderlyingCode();

		// Left to itself the venue answers with the nearest expiry and nothing else - a hundred and sixty
		// contracts of one date, which reads like the whole chain and is not. The range is therefore always
		// stated: the exact date when the caller named one, and everything still listed when it did not.
		var expiryFrom = lookupMsg.ExpiryDate ?? CurrentTime.Date;
		var expiryTo = lookupMsg.ExpiryDate ?? expiryFrom.AddYears(_listedYears);

		await foreach (var contract in _tradingClient.GetOptionContracts(
			underlying, _activeContracts, lookupMsg.OptionType?.ToNative(), expiryFrom, expiryTo, cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var secMsg = contract.ToSecurityMessage();

			secMsg.OriginalTransactionId = lookupMsg.TransactionId;

			_optionSecIds.Add(secMsg.SecurityId);

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		return left;
	}

	private const string _activeContracts = "active";

	// Longer than any listed option runs, so the range never becomes the filter.
	private const int _listedYears = 3;

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.SecurityCode;
		var transId = mdMsg.TransactionId;
		var kind = await EnsureKind(secId, cancellationToken);

		SocketMarketDataClient socketClient = kind == SecurityTypes.CryptoCurrency ? _socketCryptoClient : _socketStockClient;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			RemoveTransId(transId);
			if (IsStreamOpen(socketClient))
				await socketClient.UnSubscribeOhlc(mdMsg.OriginalTransactionId, symbol, cancellationToken);
			return;
		}

		var tf = mdMsg.GetTimeFrame().ToNative();

		if (mdMsg.From is not null)
		{
			var from = mdMsg.From.Value;
			var to = mdMsg.To ?? CurrentTime;
			var left = mdMsg.Count ?? long.MaxValue;

			var candles = kind switch
			{
				SecurityTypes.CryptoCurrency => _cryptoClient.GetOhlc(symbol, tf, from, to, null, CryptoLocation, cancellationToken),
				SecurityTypes.Option => _optionClient.GetOhlc(symbol, tf, from, to, null, cancellationToken),
				_ => _stockClient.GetOhlc(symbol, tf, from, to, null, StockFeed, cancellationToken),
			};

			await foreach (var c in candles.WithEnforcedCancellation(cancellationToken))
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

		if (!mdMsg.IsHistoryOnly())
		{
			AddTransId(DataType.CandleTimeFrame, symbol, transId);
			await OpenStreamAsync(socketClient, cancellationToken);
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
		var kind = await EnsureKind(secId, cancellationToken);

		SocketMarketDataClient socketClient = kind == SecurityTypes.CryptoCurrency ? _socketCryptoClient : _socketStockClient;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			RemoveTransId(transId);
			if (IsStreamOpen(socketClient))
				await socketClient.UnSubscribeTicks(mdMsg.OriginalTransactionId, symbol, cancellationToken);
			return;
		}

		if (mdMsg.From is not null)
		{
			var from = mdMsg.From.Value;
			var to = mdMsg.To ?? CurrentTime;
			var left = mdMsg.Count ?? long.MaxValue;

			var ticks = kind switch
			{
				SecurityTypes.CryptoCurrency => _cryptoClient.GetTicks(symbol, from, to, null, CryptoLocation, cancellationToken),
				SecurityTypes.Option => _optionClient.GetTicks(symbol, from, to, null, cancellationToken),
				_ => _stockClient.GetTicks(symbol, from, to, null, StockFeed, cancellationToken),
			};

			await foreach (var t in ticks.WithEnforcedCancellation(cancellationToken))
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

		if (!mdMsg.IsHistoryOnly())
		{
			AddTransId(DataType.Ticks, symbol, transId);
			await OpenStreamAsync(socketClient, cancellationToken);
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
		var kind = await EnsureKind(secId, cancellationToken);

		SocketMarketDataClient socketClient = kind == SecurityTypes.CryptoCurrency ? _socketCryptoClient : _socketStockClient;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			RemoveTransId(transId);

			// An option was never put on a socket, so there is nothing to take off one.
			if (kind != SecurityTypes.Option && IsStreamOpen(socketClient))
				await socketClient.UnSubscribeQuotes(mdMsg.OriginalTransactionId, symbol, cancellationToken);

			return;
		}

		if (mdMsg.From is not null)
		{
			var from = mdMsg.From.Value;
			var to = mdMsg.To ?? CurrentTime;
			var left = mdMsg.Count ?? long.MaxValue;

			// An option has no quote history at this venue - the only quote it has is the current one - so
			// there is nothing to replay and saying so beats returning an empty range that reads like a
			// contract nobody has ever quoted.
			var quotes = kind switch
			{
				SecurityTypes.CryptoCurrency => _cryptoClient.GetQuotes(symbol, from, to, null, CryptoLocation, cancellationToken),
				SecurityTypes.Option => null,
				_ => _stockClient.GetQuotes(symbol, from, to, null, StockFeed, cancellationToken),
			};

			if (quotes is null)
				this.AddWarningLog("{0}: this venue publishes no option quote history, only the current quote.", secId);

			await foreach (var q in quotes ?? AsyncEnumerable.Empty<Quote>())
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

		if (!mdMsg.IsHistoryOnly())
		{
			// Options do not stream from this venue - the option feed speaks a format nothing here reads -
			// so the subscription answers with the quote the contract shows at the moment it is made and
			// finishes. A caller wanting a later quote asks again, which is the truth of what is available
			// rather than a subscription that looks live and never updates.
			if (kind == SecurityTypes.Option)
			{
				var quotes = await _optionClient.GetLatestQuotes([symbol], OptionFeed, cancellationToken);

				if (quotes.TryGetValue(symbol, out var latest))
					await ProcessQuoteAsync(transId, latest, cancellationToken);
				else
					this.AddWarningLog("{0}: the venue shows no quote for this contract.", secId);
			}
			else
			{
				AddTransId(DataType.Level1, symbol, transId);
				await OpenStreamAsync(socketClient, cancellationToken);
				await socketClient.SubscribeQuotes(mdMsg.TransactionId, symbol, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.SecurityCode;
		var transId = mdMsg.TransactionId;
		var kind = await EnsureKind(secId, cancellationToken);

		// Only the crypto feed publishes a book here.
		if (kind != SecurityTypes.CryptoCurrency)
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
				await OpenStreamAsync(_socketCryptoClient, cancellationToken);
				await _socketCryptoClient.SubscribeOrderBook(mdMsg.TransactionId, symbol, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			RemoveTransId(transId);
			if (IsStreamOpen(_socketCryptoClient))
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
			if (IsStreamOpen(_socketNewsClient))
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
			await OpenStreamAsync(_socketNewsClient, cancellationToken);
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

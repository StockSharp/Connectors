namespace StockSharp.cTrader;

public partial class cTraderMessageAdapter
{
	private readonly SynchronizedDictionary<long, (bool isFirstTime, Dictionary<ulong, (Sides side, ulong price)> quotes)> _quotes = new();
	private readonly SynchronizedDictionary<long, Dictionary<DataType, long>> _symbolSubscriptions = new();
	private readonly SynchronizedDictionary<long, List<(long time, long price)>> _histLevel1 = new();
	private readonly SynchronizedDictionary<long, List<long>> _pendingInstruments = new();
	private readonly SynchronizedDictionary<long, (string name, string desc)> _secNameMap = new();

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (_pendingInstruments.TryGetAndRemove(lookupMsg.TransactionId, out var list))
		{
			var request = new ProtoOASymbolByIdReq
			{
				CtidTraderAccountId = _accountId,
			};

			request.SymbolId.AddRange(list);

			await _client.SendMessage(request, lookupMsg.TransactionId.To<string>(), cancellationToken);
		}
		else
		{
			var request = new ProtoOASymbolsListReq
			{
				CtidTraderAccountId = _accountId,
				IncludeArchivedSymbols = lookupMsg.IncludeExpired,
			};

			_subscriptions[lookupMsg.TransactionId] = (lookupMsg.TypedClone(), true);
			await _client.SendMessage(request, lookupMsg.TransactionId.To<string>(), cancellationToken);
		}
	}

	private async Task OnSymbolsListResponse(long transId, ProtoOASymbolsListRes msg)
	{
		if (!_subscriptions.TryGetValue(transId, out var t))
			return;

		var list = new List<long>();

		foreach (var symbol in msg.Symbol)
		{
			list.Add(symbol.SymbolId);
			_secNameMap[symbol.SymbolId] = (symbol.SymbolName, symbol.Description);
		}

		_pendingInstruments[transId] = list;

		var lookupMsg = (SecurityLookupMessage)t.subscription;
		lookupMsg.LoopBack(this);
		await SendOutMessageAsync(lookupMsg, CancellationToken.None);
	}

	private async Task OnSymbolByIdResponse(long transId, ProtoOASymbolByIdRes msg)
	{
		if (!_subscriptions.TryGetAndRemove(transId, out var t))
			return;

		var lookupMsg = (SecurityLookupMessage)t.subscription;
		var left = lookupMsg.Count ?? long.MaxValue;
		var secTypes = lookupMsg.GetSecurityTypes();

		foreach (var symbol in msg.Symbol)
		{
			if (!_secNameMap.TryGetValue(symbol.SymbolId, out var t1))
				continue;

			var secMsg = new SecurityMessage
			{
				SecurityId = new()
				{
					SecurityCode = t1.name,
					BoardCode = BoardCodes.cTrader,
					NativeAsInt = symbol.SymbolId,
				},
				Name = t1.desc,
				OriginalTransactionId = transId,
				Multiplier = symbol.HasLotSize ? symbol.LotSize.FromMonetary() : null,
				MaxVolume = symbol.HasMaxVolume ? symbol.MaxVolume.FromMonetary() : null,
				MinVolume = symbol.HasMinVolume ? symbol.MinVolume.FromMonetary() : null,
				PriceStep = symbol.HasStepVolume ? symbol.StepVolume.FromMonetary() : null,
				Decimals = symbol.HasDigits ? symbol.Digits : null,
				Shortable = symbol.HasEnableShortSelling ? symbol.EnableShortSelling : null,
			};

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, CancellationToken.None);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(transId, CancellationToken.None);
	}

	private void OnSymbolChangedEvent(ProtoOASymbolChangedEvent msg)
	{
	}

	private void OnSymbolsForConversionResponse(ProtoOASymbolsForConversionRes msg)
	{
	}

	private void OnSymbolCategoryResponse(ProtoOASymbolCategoryListRes msg)
	{
	}

	private void OnAssetClassListResponse(ProtoOAAssetClassListRes msg)
	{

	}

	private void OnAssetListResponse(ProtoOAAssetListRes msg)
	{

	}

	private async Task OnDepthEvent(ProtoOADepthEvent msg)
	{
		if (!_quotes.TryGetValue((long)msg.SymbolId, out var t))
			return;

		var quotes = t.quotes;

		if (t.isFirstTime)
			_quotes[(long)msg.SymbolId] = new(false, quotes);

		var bids = new List<QuoteChange>();
		var asks = new List<QuoteChange>();

		var prices = new HashSet<ulong>();

		foreach (var quote in msg.NewQuotes)
		{
			if (quote.HasBid)
			{
				prices.Add(quote.Bid);

				var qc = new QuoteChange(quote.Bid.FromMonetary(), quote.Size.FromMonetary());

				if (quote.HasId)
					quotes[quote.Id] = (Sides.Buy, quote.Bid);

				bids.Add(qc);
			}
			else if (quote.HasAsk)
			{
				prices.Add(quote.Ask);

				var qc = new QuoteChange(quote.Ask.FromMonetary(), quote.Size.FromMonetary());

				if (quote.HasId)
					quotes[quote.Id] = (Sides.Sell, quote.Ask);

				asks.Add(qc);
			}
		}

		foreach (var q in msg.DeletedQuotes)
		{
			if (quotes.TryGetAndRemove(q, out var quote))
			{
				var price = quote.price;

				if (prices.Contains(price))
					continue;

				if (quote.side == Sides.Buy)
					bids.Add(new(price, 0));
				else
					asks.Add(new(price, 0));
			}
		}

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = new() { NativeAsInt = (long)msg.SymbolId },
			State = t.isFirstTime ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
			ServerTime = DateTime.UtcNow,
			Bids = bids.OrderByDescending(q => q.Price).ToArray(),
			Asks = asks.OrderBy(q => q.Price).ToArray(),
		}, CancellationToken.None);
	}

	private async Task OnSubscribeDepthQuotesResponse(long transId, ProtoOASubscribeDepthQuotesRes msg)
	{
		await SendSubscriptionReplyAsync(transId, CancellationToken.None);
		await SendSubscriptionOnlineAsync(transId, CancellationToken.None);
	}

	private async Task OnUnsubscribeDepthQuotesResponse(long transId, ProtoOAUnsubscribeDepthQuotesRes msg)
	{
		await SendSubscriptionReplyAsync(transId, CancellationToken.None);
	}

	private async Task OnSubscribeSpotsResponse(long transId, ProtoOASubscribeSpotsRes msg)
	{
		// response was sent previously in OnGetTickdataResponse
		if (_subscriptions.ContainsKey(transId))
			return;

		await SendSubscriptionReplyAsync(transId, CancellationToken.None);
		await SendSubscriptionOnlineAsync(transId, CancellationToken.None);
	}

	private async Task OnUnsubscribeSpotsResponse(long transId, ProtoOAUnsubscribeSpotsRes msg)
	{
		await SendSubscriptionReplyAsync(transId, CancellationToken.None);
	}

	private async Task OnSpotEvent(ProtoOASpotEvent msg)
	{
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = new() { NativeAsInt = msg.SymbolId },
			ServerTime = msg.HasTimestamp ? msg.Timestamp.FromUnix(false) : DateTime.UtcNow,
		}
		.TryAdd(Level1Fields.BestBidPrice, msg.Bid.FromMonetary())
		.TryAdd(Level1Fields.BestAskPrice, msg.Ask.FromMonetary())
		.TryAdd(Level1Fields.ClosePrice, msg.SessionClose.FromMonetary())
		, CancellationToken.None);

		if (_symbolSubscriptions.TryGetValue(msg.SymbolId, out var candlesSubId))
		{
			foreach (var bar in msg.Trendbar)
			{
				if (!bar.HasPeriod || !candlesSubId.TryGetValue(bar.Period.FromNative().TimeFrame(), out var transId))
					continue;

				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OpenTime = DateTime.UnixEpoch.AddMinutes(bar.UtcTimestampInMinutes),
					TotalVolume = bar.Volume.FromMonetary(),
					OpenPrice = (bar.Low + (long)bar.DeltaOpen).FromMonetary(),
					HighPrice = (bar.Low + (long)bar.DeltaHigh).FromMonetary(),
					LowPrice = bar.Low.FromMonetary(),
					ClosePrice = (bar.Low + (long)bar.DeltaClose).FromMonetary(),
					OriginalTransactionId = transId,
					State = CandleStates.Active,
				}, CancellationToken.None);
			}
		}
	}

	private async Task OnGetTrendbarsResponse(long transId, ProtoOAGetTrendbarsRes msg)
	{
		if (!_subscriptions.TryGetValue(transId, out var t))
			return;

		if (t.isFirstTime)
		{
			await SendSubscriptionReplyAsync(transId, CancellationToken.None);

			_subscriptions[transId] = (t.subscription, false);
		}

		var mdMsg = (MarketDataMessage)t.subscription;
		var left = mdMsg.Count ?? long.MaxValue;

		foreach (var bar in msg.Trendbar)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OpenTime = DateTime.UnixEpoch.AddMinutes(bar.UtcTimestampInMinutes),
				TotalVolume = bar.Volume.FromMonetary(),
				OpenPrice = (bar.Low + (long)bar.DeltaOpen).FromMonetary(),
				HighPrice = (bar.Low + (long)bar.DeltaHigh).FromMonetary(),
				LowPrice = bar.Low.FromMonetary(),
				ClosePrice = (bar.Low + (long)bar.DeltaClose).FromMonetary(),
				OriginalTransactionId = transId,
				State = CandleStates.Finished,
			}, CancellationToken.None);

			if (--left <= 0)
				break;
		}

		if (mdMsg.Count is not null)
			mdMsg.Count = left;

		// TODO
		//if (msg.HasMore)
		//	return;

		if (mdMsg.IsHistoryOnly())
		{
			_subscriptions.Remove(transId);
			await SendSubscriptionFinishedAsync(transId, CancellationToken.None);
		}
		else
		{
			mdMsg.From = null;
			mdMsg.LoopBack(this);
			await SendOutMessageAsync(mdMsg, CancellationToken.None);
		}
	}

	private async Task OnGetTickdataResponse(long transId, ProtoOAGetTickDataRes msg)
	{
		if (!_subscriptions.TryGetValue(transId, out var t))
			return;

		if (t.isFirstTime)
		{
			await SendSubscriptionReplyAsync(transId, CancellationToken.None);

			_subscriptions[transId] = (t.subscription, false);
		}

		var mdMsg = (MarketDataMessage)t.subscription;
		var field = mdMsg.Fields?.FirstOrDefault() ?? Level1Fields.BestBidPrice;
		var left = mdMsg.Count ?? long.MaxValue;

		if (!_histLevel1.TryGetValue(transId, out var list))
			return;

		long? prevTime = null;
		long? prevPrice = null;

		foreach (var tick in msg.TickData)
		{
			var time = tick.Timestamp + (prevTime ?? 0);
			var price = tick.Tick + (prevPrice ?? 0);

			list.Add((time, price));

			prevTime = time;
			prevPrice = price;

			if (--left <= 0)
				break;
		}

		if (mdMsg.Count is not null)
			mdMsg.Count = left;

		// TODO
		//if (msg.HasMore)
		//	return;

		_histLevel1.Remove(transId);

		foreach (var (time, price) in list.OrderBy(t => t.time))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = transId,
				ServerTime = time.FromUnix(false),
			}
			.TryAdd(field, price.FromMonetary()), CancellationToken.None);
		}

		if (mdMsg.IsHistoryOnly())
		{
			_subscriptions.Remove(transId);
			await SendSubscriptionFinishedAsync(transId, CancellationToken.None);
		}
		else
		{
			mdMsg.From = null;
			mdMsg.LoopBack(this);
			await SendOutMessageAsync(mdMsg, CancellationToken.None);
		}
	}

	private async Task OnSubscribeLiveTrendbarResponse(long transId, ProtoOASubscribeLiveTrendbarRes msg)
	{
		// response was sent previously in OnGetTrendbarsResponse
		if (_subscriptions.ContainsKey(transId))
			return;

		await SendSubscriptionReplyAsync(transId, CancellationToken.None);
		await SendSubscriptionOnlineAsync(transId, CancellationToken.None);
	}

	private async Task OnUnsubscribeLiveTrendbarResponse(long transId, ProtoOAUnsubscribeLiveTrendbarRes msg)
	{
		await SendSubscriptionReplyAsync(transId, CancellationToken.None);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var period = mdMsg.GetTimeFrame().ToNative();
		var symbol = mdMsg.SecurityId.NativeAsInt;

		if (!mdMsg.IsSubscribe)
		{
			_symbolSubscriptions.TryGetValue(symbol)?.Remove(mdMsg.DataType2);

			await _client.SendMessage(new ProtoOAUnsubscribeLiveTrendbarReq
			{
				CtidTraderAccountId = _accountId,
				SymbolId = symbol,
				Period = period,
			}, transId.To<string>(), cancellationToken);
		}
		else
		{
			if (mdMsg.From is not null)
			{
				var req = new ProtoOAGetTrendbarsReq
				{
					CtidTraderAccountId = _accountId,
					SymbolId = symbol,
					Period = period,
					FromTimestamp = (long)mdMsg.From.Value.ToUnix(false),
					ToTimestamp = (long)(mdMsg.To ?? DateTime.UtcNow).ToUnix(false),
				};

				if (mdMsg.Count != null)
					req.Count = (uint)mdMsg.Count.Value;

				_subscriptions[transId] = (mdMsg.TypedClone(), true);
				await _client.SendMessage(req, transId.To<string>(), cancellationToken);
				return;
			}

			var symbolSubscriptions = _symbolSubscriptions.SafeAdd(symbol);

			if (symbolSubscriptions.TryAdd(DataType.Level1, transId))
			{
				await _client.SendMessage(new ProtoOASubscribeSpotsReq
				{
					CtidTraderAccountId = _accountId,
					SymbolId = { symbol },
				}, transId.To<string>(), cancellationToken);
			}

			symbolSubscriptions[mdMsg.DataType2] = transId;

			await _client.SendMessage(new ProtoOASubscribeLiveTrendbarReq
			{
				CtidTraderAccountId = _accountId,
				SymbolId = symbol,
				Period = period,
			}, transId.To<string>(), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var symbol = mdMsg.SecurityId.NativeAsInt;

		if (!mdMsg.IsSubscribe)
		{
			_quotes.Remove(symbol);
			_symbolSubscriptions.TryGetValue(symbol)?.Remove(mdMsg.DataType2);

			await _client.SendMessage(new ProtoOAUnsubscribeDepthQuotesReq
			{
				CtidTraderAccountId = _accountId,
				SymbolId = { symbol },
			}, transId.To<string>(), cancellationToken);
		}
		else
		{
			_quotes.Add(symbol, new(true, new()));
			_symbolSubscriptions.SafeAdd(symbol)[mdMsg.DataType2] = transId;

			await _client.SendMessage(new ProtoOASubscribeDepthQuotesReq
			{
				CtidTraderAccountId = _accountId,
				SymbolId = { symbol },
			}, transId.To<string>(), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var symbol = mdMsg.SecurityId.NativeAsInt;

		if (!mdMsg.IsSubscribe)
		{
			_symbolSubscriptions.TryGetValue(symbol)?.Remove(mdMsg.DataType2);

			await _client.SendMessage(new ProtoOAUnsubscribeSpotsReq
			{
				CtidTraderAccountId = _accountId,
				SymbolId = { symbol },
			}, transId.To<string>(), cancellationToken);
		}
		else
		{
			if (mdMsg.From is not null)
			{
				var field = mdMsg.Fields?.FirstOrDefault() ?? Level1Fields.BestBidPrice;

				var req = new ProtoOAGetTickDataReq
				{
					CtidTraderAccountId = _accountId,
					SymbolId = symbol,
					FromTimestamp = (long)mdMsg.From.Value.ToUnix(false),
					ToTimestamp = (long)(mdMsg.To ?? DateTime.UtcNow).ToUnix(false),
					Type = field == Level1Fields.BestAskPrice ? ProtoOAQuoteType.Ask : ProtoOAQuoteType.Bid,
				};

				_subscriptions[transId] = (mdMsg.TypedClone(), true);
				_histLevel1[transId] = new();
				await _client.SendMessage(req, transId.To<string>(), cancellationToken);
				return;
			}

			_symbolSubscriptions.SafeAdd(symbol)[mdMsg.DataType2] = transId;
			await _client.SendMessage(new ProtoOASubscribeSpotsReq
			{
				CtidTraderAccountId = _accountId,
				SymbolId = { symbol },
			}, transId.To<string>(), cancellationToken);
		}
	}
}
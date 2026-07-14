namespace StockSharp.ByBit;

public partial class ByBitMessageAdapter
{
	private readonly SynchronizedPairSet<(SecurityId secId, MessageTypes msgType, object arg), long> _subscriptionIds = new();
	private readonly SynchronizedDictionary<long, long> _orderBookSeq = new();
	private readonly SynchronizedDictionary<long, (PublicSocketClient client, string symbol, int depth, string interval)> _subscriptions = new();

	private PublicSocketClient GetClient(SecurityId secId)
	{
		var section = secId.ToSection();

		var client = section switch
		{
			ByBitSections.Spot => _spotClient,
			ByBitSections.Linear => _linearClient,
			ByBitSections.Inverse => _inverseClient,
			ByBitSections.Options => _optionsClient,
			_ => throw new InvalidOperationException(section.ToString()),
		};

		return client ?? throw new InvalidOperationException("client is null");
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		var sections = new Dictionary<ByBitSections, SecurityTypes>
		{
			{ ByBitSections.Spot, SecurityTypes.CryptoCurrency },
			{ ByBitSections.Linear, SecurityTypes.Future },
			{ ByBitSections.Inverse, SecurityTypes.Future },
			{ ByBitSections.Options, SecurityTypes.Option },
		};

		// ByBit api required exact name, so like isn't really supported
		//var symbolLike = lookupMsg.SecurityId.ToNative();
		var baseCoin = lookupMsg.UnderlyingSecurityId.ToNative();

		foreach (var (section, secType) in sections)
		{
			if (secTypes.Count > 0 && !secTypes.Contains(secType))
				continue;

			await foreach (var instrument in _httpClient.GetInstruments(section.ToNative(), default, baseCoin, lookupMsg.IncludeExpired ? "Closed" : default, cancellationToken).WithEnforcedCancellation(cancellationToken))
			{
				var sizeFilter = instrument.LotSizeFilter;

				var coin = instrument.QuoteCoin;

				if (coin == "BIT")
					coin = null;

				var secMsg = new SecurityMessage
				{
					SecurityId = instrument.Symbol.ToStockSharp(section),
					SecurityType = secType,
					PriceStep = instrument.PriceFilter?.TickSize?.ToDecimal(),
					VolumeStep = (sizeFilter?.QtyStep ?? sizeFilter.BasePrecision)?.ToDecimal(),
					Decimals = instrument.PriceScale,
					MinVolume = sizeFilter?.MinOrderQty?.ToDecimal(),
					MaxVolume = sizeFilter?.MaxOrderQty?.ToDecimal(),
					Currency = coin.FromMicexCurrencyName(ex => this.AddDebugLog(ex.ToString())),
					IssueDate = instrument.LaunchTime,
					SettlementDate = instrument.DeliveryTime,
					OriginalTransactionId = lookupMsg.TransactionId,
				}.TryFillUnderlyingId(instrument.BaseCoin?.ToUpperInvariant());

				if (secType == SecurityTypes.Option)
				{
					var parts = instrument.Symbol.SplitBySep("-");
					secMsg.OptionType = instrument.OptionsType.ToOptionType();
					secMsg.Strike = (parts.Length == 4 ? parts[^2] : parts[^3]).To<decimal>();
				}

				if (!secMsg.IsMatch(lookupMsg, secTypes))
					continue;

				await SendOutMessageAsync(secMsg, cancellationToken);

				if (--left <= 0)
					break;
			}

			if (left <= 0)
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
			var secId = mdMsg.SecurityId;
			var client = GetClient(secId);
			var symbol = secId.ToNative();

			var field = mdMsg.Fields?.FirstOrDefault();

			if (field == Level1Fields.OpenInterest)
			{
				var now = DateTime.UtcNow;

				var from = (long)(mdMsg.From ?? now.Date).ToUnix(false);
				var to = (long)(mdMsg.To ?? now).ToUnix(false);
				var left = mdMsg.Count ?? long.MaxValue;

				await foreach (var oi in _httpClient.GetOpenInterest(secId.ToCategory(), symbol, "5min", default, from, to, cancellationToken).WithEnforcedCancellation(cancellationToken))
				{
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						OriginalTransactionId = mdMsg.TransactionId,
						ServerTime = oi.Time,
					}.TryAdd(field.Value, oi.Value.ToDecimal()), cancellationToken);

					if (--left <= 0)
						break;
				}

				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			}
			else if (field == Level1Fields.HistoricalVolatility)
			{
				var now = DateTime.UtcNow;

				var from = (long)(mdMsg.From ?? now.Date).ToUnix(false);
				var to = (long)(mdMsg.To ?? now).ToUnix(false);
				var left = mdMsg.Count ?? long.MaxValue;

				await foreach (var hv in _httpClient.GetHistoricalVolatility(secId.ToCategory(), mdMsg.UnderlyingSecurityId.ToNative(), default, from, to, cancellationToken).WithEnforcedCancellation(cancellationToken))
				{
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						OriginalTransactionId = mdMsg.TransactionId,
						ServerTime = hv.Time,
					}.TryAdd(field.Value, hv.Value.ToDecimal()), cancellationToken);

					if (--left <= 0)
						break;
				}

				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			}
			else
			{
				_subscriptionIds[(secId, MessageTypes.Level1Change, default)] = mdMsg.TransactionId;
				_subscriptions.Add(mdMsg.TransactionId, (client, symbol, default, default));
				await client.SubscribeTicker(mdMsg.TransactionId, symbol, cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
		}
		else
		{
			_subscriptionIds.RemoveByValue(mdMsg.OriginalTransactionId);
			var (client, symbol, _, _) = _subscriptions[mdMsg.OriginalTransactionId];
			await client.UnsubscribeTicker(mdMsg.OriginalTransactionId, symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var secId = mdMsg.SecurityId;
			var client = GetClient(secId);
			var symbol = secId.ToNative();
			var depth = mdMsg.MaxDepth ?? 50;
			_subscriptionIds[(secId, MessageTypes.QuoteChange, depth)] = mdMsg.TransactionId;
			_subscriptions.Add(mdMsg.TransactionId, (client, symbol, depth, default));
			await client.SubscribeOrderBook(mdMsg.TransactionId, symbol, depth, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_subscriptionIds.RemoveByValue(mdMsg.OriginalTransactionId);
			var (client, symbol, depth, _) = _subscriptions[mdMsg.OriginalTransactionId];
			await client.UnsubscribeOrderBook(mdMsg.OriginalTransactionId, symbol, depth, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var secId = mdMsg.SecurityId;
			var symbol = secId.ToNative();

			if (mdMsg.From is not null)
			{
				var from = mdMsg.From.Value;
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var section = secId.ToSection();

				var trades = await _httpClient.GetTrades(section.ToNative(), symbol, section == ByBitSections.Spot ? 60 : 1000, cancellationToken).ToArrayAsync(cancellationToken);

				foreach (var trade in trades.OrderBy(t => t.Time))
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (trade.Time < from)
						continue;

					if (trade.Time > to)
						break;

					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						SecurityId = trade.Symbol.ToStockSharp(section),
						TradeStringId = trade.TradeId,
						TradePrice = trade.Price.ToDecimal(),
						TradeVolume = trade.Volume.ToDecimal(),
						ServerTime = trade.Time,
						OriginSide = trade.Side.ToSide(),
						OriginalTransactionId = mdMsg.TransactionId,
					}, cancellationToken);

					if (--left <= 0)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				var client = GetClient(secId);
				_subscriptionIds[(secId, MessageTypes.Execution, default)] = mdMsg.TransactionId;
				_subscriptions.Add(mdMsg.TransactionId, (client, symbol, default, default));
				await client.SubscribeTrades(mdMsg.TransactionId, symbol, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_subscriptionIds.RemoveByValue(mdMsg.OriginalTransactionId);
			var (client, symbol, _, _) = _subscriptions[mdMsg.OriginalTransactionId];
			await client.UnsubscribeTrades(mdMsg.OriginalTransactionId, symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var secId = mdMsg.SecurityId;
			var symbol = secId.ToNative();
			var tf = mdMsg.GetTimeFrame();
			var interval = tf.ToNative();

			if (mdMsg.From is not null)
			{
				var category = secId.ToCategory();

				var from = (long)mdMsg.From.Value.ToUnix(false);
				var to = (long)(mdMsg.To ?? DateTime.UtcNow).ToUnix(false);
				var left = mdMsg.Count ?? long.MaxValue;
				var tfMls = (long)tf.TotalMilliseconds;

				while (from < to)
				{
					var candles = await _httpClient.GetKlines(category, symbol, interval, 1000, from, default, cancellationToken).ToArrayAsync(cancellationToken);
					var needBreak = true;

					foreach (var ohlc in candles.OrderBy(c => c.OpenTime).Where(c => c.OpenTime >= from).ToArray())
					{
						if (ohlc.OpenTime > to)
						{
							needBreak = true;
							break;
						}

						await SendOutMessageAsync(new TimeFrameCandleMessage
						{
							OpenPrice = ohlc.Open.ToDecimal() ?? 0,
							HighPrice = ohlc.High.ToDecimal() ?? 0,
							LowPrice = ohlc.Low.ToDecimal() ?? 0,
							ClosePrice = ohlc.Close.ToDecimal() ?? 0,
							TotalVolume = ohlc.Volume.ToDecimal() ?? 0,
							OpenTime = ohlc.OpenTime.FromUnix(false),
							OriginalTransactionId = mdMsg.TransactionId,
							State = CandleStates.Finished,
						}, cancellationToken);

						if (--left <= 0)
						{
							needBreak = true;
							break;
						}

						from = ohlc.OpenTime + tfMls;
						needBreak = false;
					}

					if (needBreak)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				var client = GetClient(secId);
				_subscriptionIds[(secId, MessageTypes.CandleTimeFrame, tf)] = mdMsg.TransactionId;
				_subscriptions.Add(mdMsg.TransactionId, (client, symbol, default, interval));
				await client.SubscribeKlines(mdMsg.TransactionId, symbol, interval, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_subscriptionIds.RemoveByValue(mdMsg.OriginalTransactionId);
			var (client, symbol, _, interval) = _subscriptions[mdMsg.OriginalTransactionId];
			await client.UnsubscribeKlines(mdMsg.OriginalTransactionId, symbol, interval, cancellationToken);
		}
	}

	private async ValueTask SessionOnTradesReceived(ByBitSections section, string symbol, IEnumerable<WebSocketTrade> trades, CancellationToken cancellationToken)
	{
		var secId = symbol.ToStockSharp(section);

		if (!_subscriptionIds.TryGetValue((secId, MessageTypes.Execution, default), out var transId))
			return;

		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = trade.Symbol.ToStockSharp(section),
				TradeStringId = trade.TradeId,
				TradePrice = trade.Price.ToDecimal(),
				TradeVolume = trade.Volume.ToDecimal(),
				ServerTime = trade.Time,
				OriginSide = trade.Side.ToSide(),
				IsUpTick = trade.PriceChange.ToUpTick(),
				OriginalTransactionId = transId,
			}, cancellationToken);
		}
	}

	private async ValueTask SessionOnKlinesReceived(ByBitSections section, string symbol, string interval, IEnumerable<WebSocketKline> klines, CancellationToken cancellationToken)
	{
		var secId = symbol.ToStockSharp(section);
		var tf = interval.ToTimeFrame();

		if (!_subscriptionIds.TryGetValue((secId, MessageTypes.CandleTimeFrame, tf), out var transId))
			return;

		foreach (var kline in klines)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OpenPrice = kline.Open.ToDecimal() ?? 0,
				HighPrice = kline.High.ToDecimal() ?? 0,
				LowPrice = kline.Low.ToDecimal() ?? 0,
				ClosePrice = kline.Close.ToDecimal() ?? 0,
				TotalVolume = kline.Volume.ToDecimal() ?? 0,
				OpenTime = kline.Start,
				CloseTime = kline.End,
				OriginalTransactionId = transId,
				State = kline.Confirm ? CandleStates.Finished : CandleStates.Active,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderBookDelta(ByBitSections section, string symbol, int depth, WebSocketOrderBookDelta delta, CancellationToken cancellationToken)
	{
		var secId = symbol.ToStockSharp(section);

		if (!_subscriptionIds.TryGetValue((secId, MessageTypes.QuoteChange, depth), out var transId))
			return default;

		if (!_orderBookSeq.TryGetValue(transId, out var lastSeq) || lastSeq >= delta.Sequence)
			return default;

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
			OriginalTransactionId = transId,
			State = QuoteChangeStates.Increment,
			SeqNum = delta.Sequence,
			Bids = delta.Bids.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray(),
			Asks = delta.Asks.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray(),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookSnapshot(ByBitSections section, string symbol, int depth, WebSocketOrderBookSnapshot snapshot, CancellationToken cancellationToken)
	{
		var secId = symbol.ToStockSharp(section);

		if (!_subscriptionIds.TryGetValue((secId, MessageTypes.QuoteChange, depth), out var transId))
			return default;

		// you'll receive "u"=1, which is a snapshot data due to the restart of the service.
		// So please overwrite your local orderbook
		if (snapshot.UpdateId == 1)
		{
			_orderBookSeq.Remove(transId);
			return default;
		}

		_orderBookSeq[transId] = snapshot.Sequence;

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
			OriginalTransactionId = transId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = snapshot.Sequence,
			Bids = snapshot.Bids.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray(),
			Asks = snapshot.Asks.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray(),
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerReceived(ByBitSections section, WebSocketTicker ticker, CancellationToken cancellationToken)
	{
		var secId = ticker.Symbol.ToStockSharp(section);

		if (!_subscriptionIds.TryGetValue((secId, MessageTypes.Level1Change, default), out var transId))
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transId,
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(Level1Fields.Delta, ticker.Delta?.ToDecimal())
		.TryAdd(Level1Fields.Gamma, ticker.Gamma?.ToDecimal())
		.TryAdd(Level1Fields.Theta, ticker.Theta?.ToDecimal())
		.TryAdd(Level1Fields.Vega, ticker.Vega?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume24h?.ToDecimal())
		.TryAdd(Level1Fields.Turnover, ticker.Turnover24h?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice24h?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice24h?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, (ticker.AskPrice ?? ticker.Ask1Price)?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, (ticker.AskSize ?? ticker.Ask1Size)?.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, (ticker.BidPrice ?? ticker.Bid1Price)?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, (ticker.BidSize ?? ticker.Bid1Size)?.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice?.ToDecimal())
		.TryAdd(Level1Fields.LastTradeUpDown, ticker.TickDirection?.ToUpTick())
		.TryAdd(Level1Fields.Change, ticker.Change24h?.ToDecimal())
		.TryAdd(Level1Fields.Index, ticker.IndexPrice?.ToDecimal())
		.TryAdd(Level1Fields.ImpliedVolatility, ticker.MarkPriceIv?.ToDecimal())
		.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest?.ToDecimal())
		.TryAdd(Level1Fields.UnderlyingPrice, ticker.UnderlyingPrice?.ToDecimal())
		, cancellationToken);
	}
}

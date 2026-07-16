namespace StockSharp.TigerBrokers;

public partial class TigerBrokersMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var requestedTypes = lookupMsg.GetSecurityTypes().ToHashSet();
		var allTypes = requestedTypes.Count == 0;
		var left = lookupMsg.Count ?? long.MaxValue;

		if (allTypes || requestedTypes.Contains(SecurityTypes.Stock) || requestedTypes.Contains(SecurityTypes.Fund))
		{
			var markets = lookupMsg.SecurityId.BoardCode.IsEmpty()
				? new[] { Market.US, Market.HK, Market.SG, Market.AU, Market.CN }
				: [lookupMsg.SecurityId.BoardCode.ToMarket()];
			foreach (var market in markets)
			{
				foreach (var stock in await _client.GetStocks(market, cancellationToken))
				{
					var instrument = new TigerInstrument
					{
						Symbol = stock.Symbol,
						SubscriptionSymbol = stock.Symbol,
						Name = stock.Name,
						Market = market,
						SecurityType = SecType.STK,
					};
					if (await SendSecurity(instrument, lookupMsg, requestedTypes, cancellationToken) && --left <= 0)
						break;
				}
				if (left <= 0)
					break;
			}
		}

		if (left > 0 && (allTypes || requestedTypes.Contains(SecurityTypes.Future)))
		{
			var code = lookupMsg.SecurityId.SecurityCode;
			if (!code.IsEmpty())
			{
				var future = await _client.GetFuture(code, cancellationToken);
				if (future != null)
					await SendSecurity(future.ToInstrument(), lookupMsg, requestedTypes, cancellationToken);
			}
			else
			{
				foreach (var exchange in await _client.GetFutureExchanges(cancellationToken))
				{
					foreach (var future in await _client.GetFutures(exchange.Code, cancellationToken))
					{
						if (await SendSecurity(future.ToInstrument(), lookupMsg, requestedTypes, cancellationToken) && --left <= 0)
							break;
					}
					if (left <= 0)
						break;
				}
			}
		}

		if (left > 0 && (allTypes || requestedTypes.Contains(SecurityTypes.Option)) &&
			!lookupMsg.UnderlyingSecurityId.SecurityCode.IsEmpty())
		{
			var underlying = lookupMsg.UnderlyingSecurityId.SecurityCode;
			var market = lookupMsg.UnderlyingSecurityId.BoardCode.ToMarket();
			foreach (var expiration in await _client.GetOptionExpirations(underlying, market, cancellationToken))
			{
				foreach (var expiry in expiration.Timestamps ?? [])
				{
					foreach (var chain in await _client.GetOptionChain(underlying, market, expiry, cancellationToken))
					{
						foreach (var group in chain.Items ?? [])
						{
							foreach (var option in new[] { group.Call, group.Put }.WhereNotNull())
							{
								var instrument = option.ToInstrument(underlying, market, chain.Expiry > 0 ? chain.Expiry : expiry);
								if (await SendSecurity(instrument, lookupMsg, requestedTypes, cancellationToken) && --left <= 0)
									break;
							}
							if (left <= 0)
								break;
						}
						if (left <= 0)
							break;
					}
					if (left <= 0)
						break;
				}
				if (left <= 0)
					break;
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var instrument = ResolveInstrument(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();

		if (!mdMsg.IsSubscribe)
		{
			await RemoveSubscription(mdMsg.OriginalTransactionId);
			return;
		}

		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
			await SendHistoricalCandles(mdMsg, instrument, timeFrame, cancellationToken);

		if (!mdMsg.IsHistoryOnly())
		{
			if (timeFrame != TimeSpan.FromMinutes(1) || instrument.SecurityType != SecType.STK)
				throw new InvalidOperationException("Tiger OpenAPI live candle push supports one-minute stock bars only.");
			var subscription = new TigerMarketSubscription
			{
				TransactionId = mdMsg.TransactionId,
				DataType = timeFrame.TimeFrame(),
				SecurityId = mdMsg.SecurityId,
				Instrument = instrument,
				TimeFrame = timeFrame,
				FeedType = TigerFeedTypes.Kline,
			};
			var hasFeed = HasFeed(subscription.FeedType, instrument.SubscriptionSymbol);
			_marketSubscriptions[mdMsg.TransactionId] = subscription;
			if (!hasFeed)
				await _client.SetSubscription(subscription.FeedType, instrument.SubscriptionSymbol, true);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask ProcessSubscription(MarketDataMessage mdMsg, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveSubscription(mdMsg.OriginalTransactionId);
			return;
		}

		var instrument = ResolveInstrument(mdMsg.SecurityId);
		var feedType = dataType switch
		{
			_ when dataType == DataType.Ticks => TigerFeedTypes.TradeTick,
			_ when dataType == DataType.MarketDepth && instrument.SecurityType == SecType.STK => TigerFeedTypes.Depth,
			_ => instrument.ToFeedType(),
		};
		if (dataType == DataType.Ticks && instrument.SecurityType != SecType.STK)
			throw new InvalidOperationException("Tiger OpenAPI tick push is available for stocks only.");

		var subscription = new TigerMarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			DataType = dataType,
			SecurityId = mdMsg.SecurityId,
			Instrument = instrument,
			FeedType = feedType,
		};
		var hasFeed = HasFeed(feedType, instrument.SubscriptionSymbol);
		_marketSubscriptions[mdMsg.TransactionId] = subscription;
		if (!hasFeed)
			await _client.SetSubscription(feedType, instrument.SubscriptionSymbol, true);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask RemoveSubscription(long transactionId)
	{
		if (!_marketSubscriptions.Remove(transactionId, out var subscription))
			return;
		if (!HasFeed(subscription.FeedType, subscription.Instrument.SubscriptionSymbol))
			await _client.SetSubscription(subscription.FeedType, subscription.Instrument.SubscriptionSymbol, false);
	}

	private bool HasFeed(TigerFeedTypes feedType, string symbol)
		=> _marketSubscriptions.Values.Any(s => s.FeedType == feedType && s.Instrument.SubscriptionSymbol.EqualsIgnoreCase(symbol));

	private async ValueTask<bool> SendSecurity(TigerInstrument instrument, SecurityLookupMessage lookupMsg,
		HashSet<SecurityTypes> securityTypes, CancellationToken cancellationToken)
	{
		if (instrument == null || instrument.SubscriptionSymbol.IsEmpty())
			return false;
		var securityId = instrument.ToSecurityId();
		var security = new SecurityMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			SecurityId = securityId,
			SecurityType = instrument.SecurityType.ToSecurityType(),
			Name = instrument.Name,
			ShortName = instrument.Symbol,
			Currency = instrument.Currency.ToCurrency(),
			ExpiryDate = instrument.ExpiryDate,
			Strike = instrument.Strike,
			OptionType = instrument.Right.EqualsIgnoreCase("CALL") ? OptionTypes.Call :
				instrument.Right.EqualsIgnoreCase("PUT") ? OptionTypes.Put : null,
			Multiplier = instrument.Multiplier,
			PriceStep = instrument.PriceStep,
		};
		if (security.SecurityType == SecurityTypes.Option)
			security.UnderlyingSecurityId = new() { SecurityCode = instrument.Symbol, BoardCode = instrument.Market.ToBoardCode() };
		if (!security.IsMatch(lookupMsg, securityTypes))
			return false;

		_instruments[instrument.SubscriptionSymbol] = instrument;
		_instruments[securityId.ToStringId()] = instrument;
		await SendOutMessageAsync(security, cancellationToken);
		return true;
	}

	private TigerInstrument ResolveInstrument(SecurityId securityId)
	{
		var native = securityId.Native as string;
		if (!native.IsEmpty() && _instruments.TryGetValue(native, out var instrument))
			return instrument;
		if (_instruments.TryGetValue(securityId.ToStringId(), out instrument))
			return instrument;
		return securityId.ToInstrument();
	}

	private IEnumerable<TigerMarketSubscription> FindSubscriptions(string symbol, DataType dataType)
		=> _marketSubscriptions.Values.Where(s => s.DataType == dataType &&
			(s.Instrument.SubscriptionSymbol.EqualsIgnoreCase(symbol) || s.Instrument.Symbol.EqualsIgnoreCase(symbol)));

	private async ValueTask OnQuoteReceived(QuoteBasicData data, CancellationToken cancellationToken)
	{
		var symbol = data.Identifier.IsEmpty(data.Symbol);
		foreach (var subscription in FindSubscriptions(symbol, DataType.Level1).ToArray())
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = (data.HasServerTimestamp ? (long)data.ServerTimestamp : (long)data.Timestamp).FromUnixMilliseconds(),
			}
			.TryAdd(Level1Fields.LastTradePrice, data.HasLatestPrice ? (decimal)data.LatestPrice : null)
			.TryAdd(Level1Fields.LastTradeTime, data.HasLatestPriceTimestamp ? ((long)data.LatestPriceTimestamp).FromUnixMilliseconds() : null)
			.TryAdd(Level1Fields.AveragePrice, data.HasAvgPrice ? (decimal)data.AvgPrice : null)
			.TryAdd(Level1Fields.ClosePrice, data.HasPreClose ? (decimal)data.PreClose : null)
			.TryAdd(Level1Fields.OpenPrice, data.HasOpen ? (decimal)data.Open : null)
			.TryAdd(Level1Fields.HighPrice, data.HasHigh ? (decimal)data.High : null)
			.TryAdd(Level1Fields.LowPrice, data.HasLow ? (decimal)data.Low : null)
			.TryAdd(Level1Fields.Volume, data.HasVolume ? data.Volume : null)
			.TryAdd(Level1Fields.Turnover, data.HasAmount ? (decimal)data.Amount : null)
			.TryAdd(Level1Fields.OpenInterest, data.HasOpenInt ? data.OpenInt : null)
			.TryAdd(Level1Fields.PriceStep, data.HasMinTick ? (decimal)data.MinTick : null), cancellationToken);
		}
	}

	private async ValueTask OnBboReceived(QuoteBBOData data, CancellationToken cancellationToken)
	{
		foreach (var subscription in FindSubscriptions(data.Symbol, DataType.Level1).ToArray())
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = ((long)data.Timestamp).FromUnixMilliseconds(),
			}
			.TryAdd(Level1Fields.BestBidPrice, (decimal)data.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, data.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, (decimal)data.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, data.AskSize), cancellationToken);
		}

		foreach (var subscription in FindSubscriptions(data.Symbol, DataType.MarketDepth)
			.Where(s => s.Instrument.SecurityType != SecType.STK).ToArray())
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = ((long)data.Timestamp).FromUnixMilliseconds(),
				Bids = data.BidPrice > 0 ? [new((decimal)data.BidPrice, data.BidSize)] : [],
				Asks = data.AskPrice > 0 ? [new((decimal)data.AskPrice, data.AskSize)] : [],
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		}
	}

	private async ValueTask OnDepthReceived(QuoteDepthData data, CancellationToken cancellationToken)
	{
		foreach (var subscription in FindSubscriptions(data.Symbol, DataType.MarketDepth).ToArray())
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = ((long)data.Timestamp).FromUnixMilliseconds(),
				Bids = data.Bid.ToQuotes(true),
				Asks = data.Ask.ToQuotes(false),
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		}
	}

	private async ValueTask OnTradeTickReceived(TradeTick data, CancellationToken cancellationToken)
	{
		foreach (var subscription in FindSubscriptions(data.Symbol, DataType.Ticks).ToArray())
			foreach (var tick in data.Ticks ?? [])
				await SendTick(subscription, tick.Time, (decimal)tick.Price, tick.Volume, tick.Sn, tick.TickType, cancellationToken);
	}

	private async ValueTask OnFullTickReceived(TickData data, CancellationToken cancellationToken)
	{
		foreach (var subscription in FindSubscriptions(data.Symbol, DataType.Ticks).ToArray())
			foreach (var tick in data.Ticks)
				await SendTick(subscription, tick.Time, (decimal)tick.Price, tick.Volume, tick.Sn, tick.Type, cancellationToken);
	}

	private ValueTask SendTick(TigerMarketSubscription subscription, long time, decimal price, decimal volume, long id,
		string tickType, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TradeId = id,
			TradePrice = price,
			TradeVolume = volume,
			ServerTime = time.FromUnixMilliseconds(),
			OriginSide = tickType switch { "+" => Sides.Buy, "-" => Sides.Sell, _ => null },
		}, cancellationToken);

	private async ValueTask OnKlineReceived(KlineData data, CancellationToken cancellationToken)
	{
		foreach (var subscription in FindSubscriptions(data.Symbol, TimeSpan.FromMinutes(1).TimeFrame()).ToArray())
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				TypedArg = TimeSpan.FromMinutes(1),
				OpenTime = data.Time.FromUnixMilliseconds(),
				OpenPrice = (decimal)data.Open,
				HighPrice = (decimal)data.High,
				LowPrice = (decimal)data.Low,
				ClosePrice = (decimal)data.Close,
				TotalVolume = data.Volume,
				TotalTicks = data.Count,
				State = CandleStates.Active,
			}, cancellationToken);
		}
	}

	private async ValueTask SendHistoricalCandles(MarketDataMessage mdMsg, TigerInstrument instrument, TimeSpan timeFrame,
		CancellationToken cancellationToken)
	{
		var from = mdMsg.From?.ToUniversalTime() ?? DateTimeOffset.UtcNow.AddYears(-1);
		var to = mdMsg.To?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
		var limit = (int)Math.Min(mdMsg.Count ?? 300, 300);
		IEnumerable<(long time, decimal open, decimal high, decimal low, decimal close, decimal volume)> candles;

		if (instrument.SecurityType is SecType.OPT or SecType.FOP)
		{
			var response = await _client.GetOptionCandles(instrument, timeFrame.ToOptionPeriod(), from.ToUnixTimeMilliseconds(),
				to.ToUnixTimeMilliseconds(), limit, cancellationToken);
			candles = (response.Data?.SelectMany(i => i.Items ?? []) ?? [])
				.Select(c => (c.Time, (decimal)c.Open, (decimal)c.High, (decimal)c.Low, (decimal)c.Close, (decimal)c.Volume));
		}
		else if (instrument.SecurityType == SecType.FUT)
		{
			var response = await _client.GetFutureCandles(instrument.SubscriptionSymbol, timeFrame.ToFuturePeriod(),
				from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds(), limit, cancellationToken);
			candles = (response.Data?.SelectMany(i => i.Items ?? []) ?? [])
				.Select(c => (c.Time, c.Open, c.High, c.Low, c.Close, (decimal)c.Volume));
		}
		else
		{
			var response = await _client.GetStockCandles(instrument.SubscriptionSymbol, timeFrame.ToStockPeriod(),
				from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds(), limit, cancellationToken);
			candles = (response.Data?.SelectMany(i => i.Items ?? []) ?? [])
				.Select(c => (c.Time, (decimal)c.Open, (decimal)c.High, (decimal)c.Low, (decimal)c.Close, (decimal)c.Volume));
		}

		foreach (var candle in candles.OrderBy(c => c.time))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = candle.time.FromUnixMilliseconds(),
				OpenPrice = candle.open,
				HighPrice = candle.high,
				LowPrice = candle.low,
				ClosePrice = candle.close,
				TotalVolume = candle.volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
	}
}

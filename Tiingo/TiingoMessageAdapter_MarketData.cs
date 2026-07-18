namespace StockSharp.Tiingo;

public partial class TiingoMessageAdapter
{
	private readonly record struct SelectedCandle(TiingoCandle Value, DateTime OpenTime);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var board = lookupMsg.SecurityId.BoardCode;
		var native = lookupMsg.SecurityId.Native as string;
		var value = native.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name);
		if (TiingoSecurityKey.TryParse(native, out var nativeKey))
			value = nativeKey.Ticker;
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async Task Emit(SecurityMessage security)
		{
			if (security == null || left <= 0 ||
				!security.IsMatch(lookupMsg, securityTypes) ||
				!sent.Add(security.SecurityId.Native as string))
			{
				return;
			}
			if (skip > 0)
			{
				skip--;
				return;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}

		var noBoard = board.IsEmpty();
		var noTypes = securityTypes.Count == 0;
		bool Requested(TiingoMarkets market)
		{
			if (!noBoard)
				return board.EqualsIgnoreCase(market.ToBoard());
			if (noTypes)
				return true;
			return market switch
			{
				TiingoMarkets.Forex => securityTypes.Contains(SecurityTypes.Currency),
				TiingoMarkets.Crypto => securityTypes.Contains(SecurityTypes.CryptoCurrency),
				_ => securityTypes.Any(type => type is not SecurityTypes.Currency and
					not SecurityTypes.CryptoCurrency),
			};
		}

		if (Requested(TiingoMarkets.Stocks) && left > 0)
		{
			if (value.IsEmpty())
			{
				foreach (var item in await SafeRest().GetSupportedTickers(cancellationToken) ?? [])
				{
					await Emit(item?.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}
			else
			{
				var matches = await SafeRest().Search(value, cancellationToken) ?? [];
				foreach (var item in matches.Where(item => item?.IsActive != false &&
					item.Matches(value)))
				{
					await Emit(item.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
				if (matches.Length == 0 && left > 0)
				{
					try
					{
						await Emit((await SafeRest().GetStockMetadata(value, cancellationToken))
							?.ToSecurityMessage(lookupMsg.TransactionId));
					}
					catch (TiingoApiException error) when (error.IsLookupMiss)
					{
					}
				}
			}
		}

		if (Requested(TiingoMarkets.Forex) && left > 0)
		{
			try
			{
				foreach (var item in await SafeRest().GetForexQuotes(
					value.IsEmpty() ? null : Extensions.NormalizeTicker(value),
					cancellationToken) ?? [])
				{
					if (!item.Matches(value))
						continue;
					await Emit(item.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}
			catch (TiingoApiException error) when (!value.IsEmpty() && error.IsLookupMiss)
			{
			}
		}

		if (Requested(TiingoMarkets.Crypto) && left > 0)
		{
			try
			{
				foreach (var item in await SafeRest().GetCryptoMetadata(
					value.IsEmpty() ? null : Extensions.NormalizeTicker(value),
					cancellationToken) ?? [])
				{
					if (!item.Matches(value))
						continue;
					await Emit(item.ToSecurityMessage(CryptoExchange,
						lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}
			catch (TiingoApiException error) when (!value.IsEmpty() && error.IsLookupMiss)
			{
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscription(mdMsg.OriginalTransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (mdMsg.From != null || mdMsg.To != null)
			throw new NotSupportedException("Tiingo does not expose historical Level1 events.");

		var key = mdMsg.SecurityId.GetTiingoKey(CryptoExchange);
		var securityId = mdMsg.SecurityId.NormalizeTiingo(key);
		var remaining = mdMsg.Count;
		var snapshotSent = await SendLevel1Snapshot(mdMsg.TransactionId, securityId, key,
			cancellationToken);
		if (snapshotSent && remaining is > 0)
			remaining--;
		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscription(mdMsg, securityId, key, remaining, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscription(mdMsg.OriginalTransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = mdMsg.SecurityId.GetTiingoKey(CryptoExchange);
		if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
			throw new NotSupportedException("Tiingo does not expose historical trade events.");
		if (key.Market == TiingoMarkets.Forex)
			throw new NotSupportedException("Tiingo forex streaming contains quotes, not trades.");
		if (key.Market == TiingoMarkets.Stocks &&
			EquityStreamingMode == TiingoEquityStreamingModes.ReferencePrice)
		{
			throw new NotSupportedException(
				"Equity ticks require an IEX TOPS stream mode and the corresponding entitlement.");
		}

		var securityId = mdMsg.SecurityId.NormalizeTiingo(key);
		await AddLiveSubscription(mdMsg, securityId, key, mdMsg.Count, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (!Extensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException($"Tiingo does not support {timeFrame} candles.");
		var key = mdMsg.SecurityId.GetTiingoKey(CryptoExchange);
		var securityId = mdMsg.SecurityId.NormalizeTiingo(key);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = (mdMsg.From ?? Extensions.EstimateFrom(to, timeFrame, mdMsg.Count)).ToUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The candle-history start time is after its end time.");

		var left = mdMsg.Count ?? long.MaxValue;
		var emitted = new HashSet<long>();
		var current = from;
		while (current <= to && left > 0)
		{
			var window = timeFrame.GetHistoryWindow();
			var chunkTo = to - current > window ? current + window : to;
			TiingoCandle[] values;
			switch (key.Market)
			{
				case TiingoMarkets.Stocks:
					values = await SafeRest().GetStockCandles(key.Ticker, timeFrame,
						current, chunkTo, IsAfterHours, IsForceFill, cancellationToken) ?? [];
					break;
				case TiingoMarkets.Forex:
					values = await SafeRest().GetForexCandles(key.Ticker, timeFrame,
						current, chunkTo, cancellationToken) ?? [];
					break;
				case TiingoMarkets.Crypto:
					values = (await SafeRest().GetCryptoCandles(key.Ticker, timeFrame,
						current, chunkTo, cancellationToken) ?? [])
						.Where(item => item?.Ticker.EqualsIgnoreCase(key.Ticker) == true)
						.SelectMany(item => item.PriceData ?? []).ToArray();
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(key.Market), key.Market, null);
			}

			var parsed = values.Where(value => value?.Date.IsEmpty() == false &&
				Extensions.TryParseUtc(value.Date, out _))
				.Select(value => new SelectedCandle(value, Extensions.ParseUtc(value.Date)))
				.OrderBy(item => item.OpenTime).ToArray();
			foreach (var item in parsed)
			{
				if (item.OpenTime < from || item.OpenTime > to ||
					!emitted.Add(item.OpenTime.Ticks))
				{
					continue;
				}
				var useAdjusted = key.Market == TiingoMarkets.Stocks &&
					timeFrame >= TimeSpan.FromDays(1) &&
					PriceAdjustment == TiingoPriceAdjustments.Adjusted;
				var open = useAdjusted ? item.Value.AdjustedOpen ?? item.Value.Open : item.Value.Open;
				var high = useAdjusted ? item.Value.AdjustedHigh ?? item.Value.High : item.Value.High;
				var low = useAdjusted ? item.Value.AdjustedLow ?? item.Value.Low : item.Value.Low;
				var close = useAdjusted ? item.Value.AdjustedClose ?? item.Value.Close : item.Value.Close;
				var volume = useAdjusted ? item.Value.AdjustedVolume ?? item.Value.Volume :
					item.Value.Volume;
				if (open == null || high == null || low == null || close == null)
					continue;

				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					DataType = mdMsg.DataType2,
					OpenTime = item.OpenTime,
					CloseTime = Extensions.GetCandleCloseTime(item.OpenTime, timeFrame),
					OpenPrice = open.Value,
					HighPrice = high.Value,
					LowPrice = low.Value,
					ClosePrice = close.Value,
					TotalVolume = volume.GetValueOrDefault(),
					State = CandleStates.Finished,
				}, cancellationToken);
				if (--left <= 0)
					break;
			}

			if (chunkTo >= to || left <= 0)
				break;
			current = chunkTo;
			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var hasSecurity = !(mdMsg.SecurityId.Native as string)
			.IsEmpty(mdMsg.SecurityId.SecurityCode).IsEmpty();
		var key = hasSecurity ? mdMsg.SecurityId.GetTiingoKey(CryptoExchange) : default;
		var securityId = hasSecurity ? mdMsg.SecurityId.NormalizeTiingo(key) : default;
		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		if (from != null && to != null && from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The news-history start time is after its end time.");

		var target = Math.Min(mdMsg.Count ?? 100, int.MaxValue);
		var news = new List<TiingoNewsItem>();
		var ids = new HashSet<long>();
		for (var offset = 0; news.Count < target;)
		{
			var limit = checked((int)Math.Min(1000, target - news.Count));
			var page = await SafeRest().GetNews(hasSecurity ? key.Ticker : null,
				from, to, limit, offset, cancellationToken) ?? [];
			foreach (var item in page)
			{
				if (item != null && (item.Id == null || ids.Add(item.Id.Value)))
					news.Add(item);
			}
			if (page.Length < limit)
				break;
			offset = checked(offset + page.Length);
			await IterationInterval.Delay(cancellationToken);
		}

		foreach (var item in news.Select(item => new
		{
			Value = item,
			Time = Extensions.TryParseUtc(item.PublishedDate, out var published) ? published :
				Extensions.TryParseUtc(item.CrawlDate, out var crawled) ? crawled : (DateTime?)null,
		}).Where(item => item.Time != null).OrderBy(item => item.Time).Take(checked((int)target)))
		{
			await SendOutMessageAsync(new NewsMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				ServerTime = item.Time.Value,
				Id = item.Value.Id?.ToString(CultureInfo.InvariantCulture),
				Headline = item.Value.Title,
				Story = item.Value.Description,
				Source = item.Value.Source,
				Url = item.Value.Url,
				SecurityId = securityId,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<bool> SendLevel1Snapshot(long transactionId, SecurityId securityId,
		TiingoSecurityKey key, CancellationToken cancellationToken)
	{
		Level1ChangeMessage message = null;
		switch (key.Market)
		{
			case TiingoMarkets.Stocks:
			{
				var quote = (await SafeRest().GetStockQuote(key.Ticker, cancellationToken) ?? [])
					.FirstOrDefault(item => item?.Ticker.EqualsIgnoreCase(key.Ticker) == true);
				var time = LatestTime(quote?.Timestamp, quote?.QuoteTimestamp,
					quote?.LastSaleTimestamp);
				if (quote == null || time == null)
					return false;
				message = new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					ServerTime = time.Value,
				}
				.TryAdd(Level1Fields.OpenPrice, Positive(quote.Open))
				.TryAdd(Level1Fields.HighPrice, Positive(quote.High))
				.TryAdd(Level1Fields.LowPrice, Positive(quote.Low))
				.TryAdd(Level1Fields.SettlementPrice, Positive(quote.PreviousClose))
				.TryAdd(Level1Fields.Volume, Positive(quote.Volume))
				.TryAdd(Level1Fields.TheorPrice, Positive(quote.TiingoLast));
				if (EquityStreamingMode != TiingoEquityStreamingModes.ReferencePrice)
				{
					message
						.TryAdd(Level1Fields.LastTradePrice, Positive(quote.Last))
						.TryAdd(Level1Fields.LastTradeVolume, Positive(quote.LastSize))
						.TryAdd(Level1Fields.LastTradeTime, Positive(quote.Last) != null ?
							LatestTime(quote.LastSaleTimestamp, quote.Timestamp) : null)
						.TryAdd(Level1Fields.BestBidPrice, Positive(quote.BidPrice))
						.TryAdd(Level1Fields.BestBidVolume, Positive(quote.BidSize))
						.TryAdd(Level1Fields.BestBidTime, Positive(quote.BidPrice) != null ?
							LatestTime(quote.QuoteTimestamp, quote.Timestamp) : null)
						.TryAdd(Level1Fields.BestAskPrice, Positive(quote.AskPrice))
						.TryAdd(Level1Fields.BestAskVolume, Positive(quote.AskSize))
						.TryAdd(Level1Fields.BestAskTime, Positive(quote.AskPrice) != null ?
							LatestTime(quote.QuoteTimestamp, quote.Timestamp) : null);
				}
				break;
			}
			case TiingoMarkets.Forex:
			{
				var quote = (await SafeRest().GetForexQuotes(key.Ticker, cancellationToken) ?? [])
					.FirstOrDefault(item => item?.Ticker.EqualsIgnoreCase(key.Ticker) == true);
				if (quote == null || !Extensions.TryParseUtc(quote.QuoteTimestamp, out var time))
					return false;
				message = new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					ServerTime = time,
				}
				.TryAdd(Level1Fields.TheorPrice, Positive(quote.MidPrice))
				.TryAdd(Level1Fields.BestBidPrice, Positive(quote.BidPrice))
				.TryAdd(Level1Fields.BestBidVolume, Positive(quote.BidSize))
				.TryAdd(Level1Fields.BestBidTime, Positive(quote.BidPrice) != null ? time : null)
				.TryAdd(Level1Fields.BestAskPrice, Positive(quote.AskPrice))
				.TryAdd(Level1Fields.BestAskVolume, Positive(quote.AskSize))
				.TryAdd(Level1Fields.BestAskTime, Positive(quote.AskPrice) != null ? time : null);
				break;
			}
			case TiingoMarkets.Crypto:
			{
				var values = (await SafeRest().GetCryptoCandles(key.Ticker,
					TimeSpan.FromMinutes(1), null, null, cancellationToken) ?? [])
					.Where(item => item?.Ticker.EqualsIgnoreCase(key.Ticker) == true)
					.SelectMany(item => item.PriceData ?? [])
					.Where(item => item?.Date.IsEmpty() == false &&
						Extensions.TryParseUtc(item.Date, out _))
					.OrderBy(item => Extensions.ParseUtc(item.Date)).ToArray();
				var value = values.LastOrDefault();
				if (value == null || !Extensions.TryParseUtc(value.Date, out var time))
					return false;
				message = new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					ServerTime = time,
				}.TryAdd(Level1Fields.TheorPrice, Positive(value.Close));
				break;
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(key.Market), key.Market, null);
		}

		if (message.Changes.Count == 0)
			return false;
		await SendOutMessageAsync(message, cancellationToken);
		return true;
	}

	private async ValueTask OnStreamData(TiingoMarkets market, TiingoStreamData data,
		CancellationToken cancellationToken)
	{
		if (data?.Ticker.IsEmpty() != false)
			return;
		LiveSubscription[] subscriptions;
		lock (_liveSync)
		{
			subscriptions = _liveSubscriptions.Values.Where(subscription =>
				MatchesStreamData(subscription.Key, market, data)).ToArray();
		}

		var emitted = new List<LiveSubscription>();
		foreach (var subscription in subscriptions)
		{
			if (subscription.DataType == DataType.Level1)
			{
				var message = ToLevel1Message(subscription, data);
				if (message == null || message.Changes.Count == 0)
					continue;
				await SendOutMessageAsync(message, cancellationToken);
			}
			else
			{
				if (data.Kind is not TiingoStreamDataKinds.IexTrade and
					not TiingoStreamDataKinds.CryptoTrade || Positive(data.LastPrice) == null)
				{
					continue;
				}
				await SendOutMessageAsync(new ExecutionMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					DataTypeEx = DataType.Ticks,
					ServerTime = data.Time,
					TradePrice = data.LastPrice,
					TradeVolume = Positive(data.LastSize),
				}, cancellationToken);
			}
			emitted.Add(subscription);
		}

		var finished = new List<LiveSubscription>();
		var unsubscribe = new List<TiingoSecurityKey>();
		lock (_liveSync)
		{
			foreach (var subscription in emitted)
			{
				if (!_liveSubscriptions.TryGetValue(subscription.TransactionId, out var current) ||
					!ReferenceEquals(subscription, current))
				{
					continue;
				}
				if (current.Remaining is > 0 && --current.Remaining == 0)
				{
					_liveSubscriptions.Remove(current.TransactionId);
					finished.Add(current);
				}
			}
			foreach (var key in finished.Select(item => item.Key))
			{
				if (!_liveSubscriptions.Values.Any(item => SameStreamIdentity(item.Key, key)) &&
					!unsubscribe.Any(item => SameStreamIdentity(item, key)))
				{
					unsubscribe.Add(key);
				}
			}
		}

		foreach (var subscription in finished)
			await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
		foreach (var key in unsubscribe)
		{
			var stream = GetExistingStream(key.Market);
			if (stream != null)
				await stream.Unsubscribe(key.Ticker, cancellationToken);
			RetireUnusedStream(key.Market);
		}
	}

	private static Level1ChangeMessage ToLevel1Message(LiveSubscription subscription,
		TiingoStreamData data)
	{
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = data.Time,
		};
		switch (data.Kind)
		{
			case TiingoStreamDataKinds.ReferencePrice:
				message.TryAdd(Level1Fields.TheorPrice, Positive(data.ReferencePrice));
				break;
			case TiingoStreamDataKinds.EquityLiquidity:
				message
					.TryAdd(Level1Fields.TheorPrice, Positive(data.ReferencePrice))
					.TryAdd(Level1Fields.BestBidPrice, Positive(data.BidPrice))
					.TryAdd(Level1Fields.BestBidVolume, Positive(data.BidSize))
					.TryAdd(Level1Fields.BestBidTime, Positive(data.BidPrice) != null ? data.Time : null)
					.TryAdd(Level1Fields.BestAskPrice, Positive(data.AskPrice))
					.TryAdd(Level1Fields.BestAskVolume, Positive(data.AskSize))
					.TryAdd(Level1Fields.BestAskTime, Positive(data.AskPrice) != null ? data.Time : null);
				break;
			case TiingoStreamDataKinds.IexQuote:
			case TiingoStreamDataKinds.ForexQuote:
			case TiingoStreamDataKinds.CryptoQuote:
				message
					.TryAdd(Level1Fields.TheorPrice, Positive(data.MidPrice))
					.TryAdd(Level1Fields.BestBidPrice, Positive(data.BidPrice))
					.TryAdd(Level1Fields.BestBidVolume, Positive(data.BidSize))
					.TryAdd(Level1Fields.BestBidTime, Positive(data.BidPrice) != null ? data.Time : null)
					.TryAdd(Level1Fields.BestAskPrice, Positive(data.AskPrice))
					.TryAdd(Level1Fields.BestAskVolume, Positive(data.AskSize))
					.TryAdd(Level1Fields.BestAskTime, Positive(data.AskPrice) != null ? data.Time : null)
					.TryAdd(Level1Fields.State, data.IsHalted == null ? null :
						data.IsHalted.Value ? SecurityStates.Stoped : SecurityStates.Trading);
				break;
			case TiingoStreamDataKinds.IexTrade:
			case TiingoStreamDataKinds.CryptoTrade:
				message
					.TryAdd(Level1Fields.LastTradePrice, Positive(data.LastPrice))
					.TryAdd(Level1Fields.LastTradeVolume, Positive(data.LastSize))
					.TryAdd(Level1Fields.LastTradeTime, Positive(data.LastPrice) != null ? data.Time : null)
					.TryAdd(Level1Fields.State, data.IsHalted == null ? null :
						data.IsHalted.Value ? SecurityStates.Stoped : SecurityStates.Trading);
				break;
			case TiingoStreamDataKinds.IexBreak:
				message.TryAdd(Level1Fields.State, data.IsHalted == null ? null :
					data.IsHalted.Value ? SecurityStates.Stoped : SecurityStates.Trading);
				break;
		}
		return message;
	}

	private async Task AddLiveSubscription(MarketDataMessage mdMsg, SecurityId securityId,
		TiingoSecurityKey key, long? remaining, CancellationToken cancellationToken)
	{
		var subscription = new LiveSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			Key = key,
			DataType = mdMsg.DataType2,
			Remaining = remaining,
		};
		var isFirst = false;
		lock (_liveSync)
		{
			if (_liveSubscriptions.ContainsKey(mdMsg.TransactionId))
				throw new InvalidOperationException($"Tiingo subscription {mdMsg.TransactionId} already exists.");
			isFirst = !_liveSubscriptions.Values.Any(item => SameStreamIdentity(item.Key, key));
			_liveSubscriptions.Add(mdMsg.TransactionId, subscription);
		}

		try
		{
			if (isFirst)
				await GetOrCreateStream(key.Market).Subscribe(key.Ticker, cancellationToken);
		}
		catch
		{
			lock (_liveSync)
				_liveSubscriptions.Remove(mdMsg.TransactionId);
			await DisposeUnusedStream(key.Market);
			throw;
		}
	}

	private async Task RemoveLiveSubscription(long transactionId,
		CancellationToken cancellationToken)
	{
		LiveSubscription removed;
		var unsubscribe = false;
		lock (_liveSync)
		{
			if (!_liveSubscriptions.Remove(transactionId, out removed))
				return;
			unsubscribe = !_liveSubscriptions.Values.Any(item => SameStreamIdentity(item.Key,
				removed.Key));
		}
		if (unsubscribe)
		{
			var stream = GetExistingStream(removed.Key.Market);
			if (stream != null)
				await stream.Unsubscribe(removed.Key.Ticker, cancellationToken);
		}
		await DisposeUnusedStream(removed.Key.Market);
	}

	private TiingoWebSocketClient GetExistingStream(TiingoMarkets market)
	{
		lock (_streamSync)
			return GetStream(market);
	}

	private void RetireUnusedStream(TiingoMarkets market)
	{
		TiingoWebSocketClient stream;
		lock (_liveSync)
		{
			if (_liveSubscriptions.Values.Any(item => item.Key.Market == market))
				return;
			lock (_streamSync)
			{
				stream = GetStream(market);
				SetStream(market, null);
			}
		}
		if (stream == null)
			return;
		stream.DataReceived -= OnStreamData;
		stream.Error -= SendOutErrorAsync;
		stream.RequestStop();
		_ = FinishRetiredStream(stream);
	}

	private static async Task FinishRetiredStream(TiingoWebSocketClient stream)
	{
		try
		{
			await stream.Disconnect();
		}
		finally
		{
			stream.Dispose();
		}
	}

	private static bool SameStreamIdentity(TiingoSecurityKey first, TiingoSecurityKey second)
		=> first.Market == second.Market && (first.Market == TiingoMarkets.Crypto ||
			first.Ticker.EqualsIgnoreCase(second.Ticker));

	private static bool MatchesStreamData(TiingoSecurityKey key, TiingoMarkets market,
		TiingoStreamData data)
		=> key.Market == market && key.Ticker.EqualsIgnoreCase(data.Ticker) &&
			(market != TiingoMarkets.Crypto || key.Exchange.IsEmpty() ||
				key.Exchange.EqualsIgnoreCase(data.Exchange));

	private static DateTime? LatestTime(params string[] values)
		=> values.Where(value => Extensions.TryParseUtc(value, out _))
			.Select(Extensions.ParseUtc).Cast<DateTime?>().OrderBy(value => value).LastOrDefault();

	private static decimal? Positive(decimal? value) => value is > 0 ? value : null;
}

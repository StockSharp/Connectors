namespace StockSharp.OpenMarkets;

partial class OpenMarketsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var types = message.GetSecurityTypes();
		var exchange = message.SecurityId.BoardCode.IsEmpty(DefaultExchange);
		var today = CurrentTime.ToExchangeTime().Date;
		var securities = await _client.GetSecurities(exchange, cancellationToken) ?? [];
		var selected = securities.Where(security =>
			(message.SecurityId.SecurityCode.IsEmpty() ||
				security.SecurityCode.ContainsIgnoreCase(message.SecurityId.SecurityCode)) &&
			(message.Name.IsEmpty() || security.SecurityDescription.ContainsIgnoreCase(message.Name)) &&
			(message.ShortName.IsEmpty() || security.IssuerName.ContainsIgnoreCase(message.ShortName)) &&
			(types.Count == 0 || security.SecurityType.ToSecurityType() is SecurityTypes type &&
				types.Contains(type)) &&
			(message.IncludeExpired || security.LastListedDate == null ||
				security.LastListedDate.Value.Date >= today))
			.ToArray();

		var information = new List<OpenMarketsSecurityInformation>();
		foreach (var batch in selected.Chunk(1000))
		{
			information.AddRange(await _client.GetSecurityInformation(batch.Select(security =>
				security.SecurityCode.ToSecurityId(security.Exchange)
					.ToNativeSecurity(DataSource, DefaultExchange)).ToArray(), cancellationToken) ?? []);
		}
		var informationBySecurity = information
			.Where(item => item != null && !item.SecurityCode.IsEmpty())
			.GroupBy(item => GetSecurityKey(item.SecurityCode, item.Exchange),
				StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.First(),
				StringComparer.OrdinalIgnoreCase);

		var results = new List<SecurityMessage>(selected.Length);
		foreach (var security in selected)
		{
			cancellationToken.ThrowIfCancellationRequested();
			informationBySecurity.TryGetValue(
				GetSecurityKey(security.SecurityCode, security.Exchange), out var info);
			SetMultiplier(security.SecurityCode, security.Exchange, info?.PriceMultiplier);
			var multiplier = GetMultiplier(security.SecurityCode.ToSecurityId(security.Exchange));
			var securityId = security.SecurityCode.ToSecurityId(security.Exchange);
			securityId.Isin = security.Isin;
			securityId.Sedol = security.Sedol;
			securityId.Cusip = security.Cusip;

			var result = new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = securityId,
				Name = security.SecurityDescription.IsEmpty(security.IssuerName),
				ShortName = security.IssuerName,
				Class = security.SecurityType,
				SecurityType = security.SecurityType.ToSecurityType(),
				Currency = info?.CurrencyCode.To<CurrencyTypes?>(),
				PriceStep = info?.MinimumPriceStep.FromNativePrice(multiplier),
				VolumeStep = info?.LotSize,
				MinVolume = info?.LotSize,
				Multiplier = info?.SharesPerContract,
				IssueDate = security.FirstListedDate?.ToUtc(),
				ExpiryDate = (info?.ExerciseDate ?? info?.MaturityDate)?.ToUtc(),
				Strike = info?.ExercisePrice.FromNativePrice(multiplier),
				OptionType = info?.CallOrPut?.EqualsIgnoreCase("Call") == true
					? OptionTypes.Call
					: info?.CallOrPut?.EqualsIgnoreCase("Put") == true ? OptionTypes.Put : null,
				UnderlyingSecurityId = info?.UnderlyingInstrumentCode.IsEmpty() != false
					? default
					: info.UnderlyingInstrumentCode.ToSecurityId(
						info.UnderlyingInstrumentExchange.IsEmpty(security.Exchange)),
			};
			results.Add(result);
		}

		IEnumerable<SecurityMessage> filtered = results.Where(result => result.IsMatch(message, types));
		var skip = Math.Max(0, message.Skip ?? 0);
		if (skip > 0)
			filtered = filtered.Skip(skip > int.MaxValue ? int.MaxValue : (int)skip);
		if (message.Count != null)
		{
			var count = Math.Max(0, message.Count.Value);
			filtered = filtered.Take(count > int.MaxValue ? int.MaxValue : (int)count);
		}

		foreach (var result in filtered)
			await SendOutMessageAsync(result, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			_level1Subscriptions.Remove(message.OriginalTransactionId);
			return;
		}

		await EnsureMultiplier(message.SecurityId, cancellationToken);
		var native = message.SecurityId.ToNativeSecurity(DataSource, DefaultExchange);
		var response = await _client.GetQuotes([native], cancellationToken);
		foreach (var quote in response?.Quotes ?? [])
			await ProcessQuote(message.TransactionId, message.SecurityId, quote, cancellationToken);

		if (!message.IsHistoryOnly())
		{
			_level1Subscriptions[message.TransactionId] = message.SecurityId;
			await _streams.EnsureMarketSubscriptions(DataSource, true, false, cancellationToken);
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			_tickSubscriptions.Remove(message.OriginalTransactionId);
			return;
		}

		await EnsureMultiplier(message.SecurityId, cancellationToken);
		if (message.From != null)
			await SendHistoricalTrades(message, cancellationToken);

		if (!message.IsHistoryOnly())
		{
			_tickSubscriptions[message.TransactionId] = message.SecurityId;
			await _streams.EnsureMarketSubscriptions(DataSource, false, true, cancellationToken);
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			_depthSubscriptions.Remove(message.OriginalTransactionId);
			return;
		}

		await EnsureMultiplier(message.SecurityId, cancellationToken);
		var subscription = new DepthSubscription
		{
			SecurityId = message.SecurityId,
			Depth = Math.Clamp(message.MaxDepth ?? 10, 1, 100),
		};
		await RefreshDepth(message.TransactionId, subscription, cancellationToken);
		if (!message.IsHistoryOnly())
			_depthSubscriptions[message.TransactionId] = subscription;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var timeFrame = message.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"OpenMarkets does not support this time-series interval.");

		await EnsureMultiplier(message.SecurityId, cancellationToken);
		var multiplier = GetMultiplier(message.SecurityId);
		var native = message.SecurityId.ToNativeSecurity(DataSource, DefaultExchange);
		var to = (message.To ?? CurrentTime).ToExchangeTime();
		var from = message.From?.ToExchangeTime() ?? (timeFrame >= TimeSpan.FromDays(1)
			? to.AddYears(-1)
			: to.AddDays(-1));
		var left = message.Count ?? long.MaxValue;
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(message.From), message.From,
				"OpenMarkets history start time cannot be after the end time.");
		if (left <= 0)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		if (timeFrame >= TimeSpan.FromDays(1))
		{
			var frequency = timeFrame == TimeSpan.FromDays(7) ? "Weekly" : "Daily";
			foreach (var candle in (await _client.GetTimeSeries(native, frequency, from, to,
				cancellationToken) ?? []).OrderBy(item => item.TimeSeriesDate))
			{
				if (candle.TimeSeriesDate is not DateTime time)
					continue;
				await SendCandle(message.TransactionId, message.SecurityId, timeFrame,
					time.ToExchangeUtc(), candle.OpenPrice, candle.HighPrice, candle.LowPrice,
					candle.ClosePrice, candle.TotalVolume, multiplier, cancellationToken);
				if (--left <= 0)
					break;
			}
		}
		else
		{
			var interval = checked((int)timeFrame.TotalMinutes);
			var maxDays = interval < 10 ? 7 : interval <= 30 ? 30 : 60;
			for (var cursor = from; cursor <= to && left > 0;)
			{
				var rangeEnd = cursor.AddDays(maxDays).AddTicks(-1);
				if (rangeEnd > to)
					rangeEnd = to;
				foreach (var candle in (await _client.GetIntradayTimeSeries(native, interval,
					cursor, rangeEnd, cancellationToken) ?? [])
					.OrderBy(item => item.TimeSeriesDateTime))
				{
					if (candle.TimeSeriesDateTime is not DateTime time)
						continue;
					await SendCandle(message.TransactionId, message.SecurityId, timeFrame,
						time.ToExchangeUtc(), candle.OpenPrice, candle.HighPrice, candle.LowPrice,
						candle.ClosePrice, candle.TotalVolume, multiplier, cancellationToken);
					if (--left <= 0)
						break;
				}
				if (rangeEnd >= to)
					break;
				cursor = rangeEnd.AddTicks(1);
			}
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async Task EnsureMultiplier(SecurityId securityId, CancellationToken cancellationToken)
	{
		var key = GetSecurityKey(securityId.SecurityCode,
			securityId.BoardCode.IsEmpty(DefaultExchange));
		if (_priceMultipliers.ContainsKey(key))
			return;
		var native = securityId.ToNativeSecurity(DataSource, DefaultExchange);
		var info = (await _client.GetSecurityInformation([native], cancellationToken) ?? [])
			.FirstOrDefault();
		SetMultiplier(securityId.SecurityCode, securityId.BoardCode.IsEmpty(DefaultExchange),
			info?.PriceMultiplier);
	}

	private async Task SendHistoricalTrades(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		var native = message.SecurityId.ToNativeSecurity(DataSource, DefaultExchange);
		var from = message.From.Value.ToExchangeTime();
		var to = (message.To ?? CurrentTime).ToExchangeTime();
		var left = message.Count ?? long.MaxValue;
		for (var cursor = from; cursor <= to && left > 0; cursor = cursor.Date.AddDays(1))
		{
			var dayEnd = cursor.Date.AddDays(1).AddTicks(-1);
			var rangeEnd = dayEnd < to ? dayEnd : to;
			foreach (var trade in (await _client.GetMarketTrades(native, cursor, rangeEnd,
				cancellationToken) ?? []).OrderBy(item => item.TradeGmtDateTime ?? item.TradeDateTime))
			{
				await ProcessMarketTrade(message.TransactionId, message.SecurityId, trade,
					cancellationToken);
				if (--left <= 0)
					break;
			}
		}
	}

	private ValueTask SendCandle(long transactionId, SecurityId securityId, TimeSpan timeFrame,
		DateTime openTime, decimal? open, decimal? high, decimal? low, decimal close,
		decimal? volume, decimal multiplier, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = open.FromNativePrice(multiplier) ?? close * multiplier,
			HighPrice = high.FromNativePrice(multiplier) ?? close * multiplier,
			LowPrice = low.FromNativePrice(multiplier) ?? close * multiplier,
			ClosePrice = close * multiplier,
			TotalVolume = volume ?? 0,
			State = CandleStates.Finished,
		}, cancellationToken);

	private async ValueTask ProcessStreamQuotes(OpenMarketsStreamQuote[] quotes,
		CancellationToken cancellationToken)
	{
		foreach (var quote in quotes ?? [])
		{
			SetMultiplier(quote.SecurityCode, quote.Exchange, quote.PriceMultiplier);
			var subscriptions = _level1Subscriptions.Where(pair =>
				Matches(pair.Value, quote.SecurityCode, quote.Exchange)).ToArray();
			var dto = new OpenMarketsQuote
			{
				SecurityCode = quote.SecurityCode,
				Exchange = quote.Exchange,
				DataSource = quote.DataSource,
				AskCount = quote.AskCount,
				AskPrice = quote.AskPrice,
				AskVolume = quote.AskVolume,
				BidCount = quote.BidCount,
				BidPrice = quote.BidPrice,
				BidVolume = quote.BidVolume,
				TotalVolume = quote.TotalVolume,
				TotalValue = quote.TotalValue,
				HighPrice = quote.HighPrice,
				LastPrice = quote.LastPrice,
				LowPrice = quote.LowPrice,
				MatchPrice = quote.MatchPrice,
				MatchVolume = quote.MatchVolume,
				MarketValue = quote.MarketValue,
				MarketVolume = quote.MarketVolume,
				Movement = quote.Movement,
				OpenPrice = quote.OpenPrice,
				TradingStatus = quote.TradingStatus,
				TradeCount = quote.TradeCount,
				TradeDateTime = quote.TradeDateTime,
				UpdateDateTime = quote.UpdateDateTime,
				PreviousClosePrice = quote.PreviousClosePrice,
				PriceMultiplier = quote.PriceMultiplier,
				MarketVwap = quote.MarketVwap,
			};
			foreach (var subscription in subscriptions)
				await ProcessQuote(subscription.Key, subscription.Value, dto, cancellationToken);
		}
	}

	private async ValueTask ProcessStreamMarketTrades(OpenMarketsStreamMarketTrade[] trades,
		CancellationToken cancellationToken)
	{
		foreach (var trade in trades ?? [])
		{
			var key = $"{trade.DataSource}:{trade.Exchange}:{trade.SecurityCode}:{trade.TradeNumber}";
			if (!_reportedMarketTrades.TryAdd(key))
				continue;
			var subscriptions = _tickSubscriptions.Where(pair =>
				Matches(pair.Value, trade.SecurityCode, trade.Exchange)).ToArray();
			foreach (var subscription in subscriptions)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = subscription.Key,
					SecurityId = subscription.Value,
					TradeId = trade.TradeNumber,
					TradePrice = trade.Price.FromNativePrice(GetMultiplier(subscription.Value)),
					TradeVolume = trade.TradeVolume,
					ServerTime = trade.TradeDateTime?.ToExchangeUtc() ?? CurrentTime,
				}, cancellationToken);
			}
		}
	}

	private ValueTask ProcessQuote(long transactionId, SecurityId securityId, OpenMarketsQuote quote,
		CancellationToken cancellationToken)
	{
		SetMultiplier(quote.SecurityCode, quote.Exchange, quote.PriceMultiplier);
		var multiplier = GetMultiplier(securityId);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = (quote.UpdateDateTime ?? quote.TradeDateTime)?.ToExchangeUtc() ?? CurrentTime,
		}
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice.FromNativePrice(multiplier))
		.TryAdd(Level1Fields.BestAskVolume, quote.AskVolume)
		.TryAdd(Level1Fields.AsksCount, quote.AskCount)
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice.FromNativePrice(multiplier))
		.TryAdd(Level1Fields.BestBidVolume, quote.BidVolume)
		.TryAdd(Level1Fields.BidsCount, quote.BidCount)
		.TryAdd(Level1Fields.LastTradePrice, quote.LastPrice.FromNativePrice(multiplier))
		.TryAdd(Level1Fields.LastTradeTime, quote.TradeDateTime?.ToExchangeUtc())
		.TryAdd(Level1Fields.OpenPrice, quote.OpenPrice.FromNativePrice(multiplier))
		.TryAdd(Level1Fields.HighPrice, quote.HighPrice.FromNativePrice(multiplier))
		.TryAdd(Level1Fields.LowPrice, quote.LowPrice.FromNativePrice(multiplier))
		.TryAdd(Level1Fields.ClosePrice, quote.PreviousClosePrice.FromNativePrice(multiplier))
		.TryAdd(Level1Fields.Change, quote.Movement.FromNativePrice(multiplier))
		.TryAdd(Level1Fields.Volume, quote.TotalVolume)
		.TryAdd(Level1Fields.Turnover, quote.TotalValue)
		.TryAdd(Level1Fields.VWAP, quote.MarketVwap.FromNativePrice(multiplier))
		.TryAdd(Level1Fields.TradesCount, quote.TradeCount), cancellationToken);
	}

	private ValueTask ProcessMarketTrade(long transactionId, SecurityId securityId,
		OpenMarketsMarketTrade trade, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			TradeId = trade.TradeNumber,
			TradePrice = trade.TradePrice.FromNativePrice(GetMultiplier(securityId)),
			TradeVolume = trade.TradeVolume,
			ServerTime = trade.TradeGmtDateTime?.ToUtc() ??
				trade.TradeDateTime?.ToExchangeUtc() ?? CurrentTime,
		}, cancellationToken);

	private async Task RefreshDepth(long transactionId, DepthSubscription subscription,
		CancellationToken cancellationToken)
	{
		var securityId = subscription.SecurityId;
		var native = securityId.ToNativeSecurity(DataSource, DefaultExchange);
		var multiplier = GetMultiplier(securityId);
		var depth = await _client.GetDepth(native, subscription.Depth, cancellationToken) ?? [];
		var bids = depth.Where(level => level.BidPrice != null && level.BidVolume != null)
			.OrderByDescending(level => level.BidPrice)
			.Take(subscription.Depth)
			.Select(level => new QuoteChange(level.BidPrice.Value * multiplier,
				level.BidVolume.Value)
			{
				OrdersCount = level.BidOrderCount is decimal count
					? (int)Math.Min(int.MaxValue, count)
					: null,
			})
			.ToArray();
		var asks = depth.Where(level => level.AskPrice != null && level.AskVolume != null)
			.OrderBy(level => level.AskPrice)
			.Take(subscription.Depth)
			.Select(level => new QuoteChange(level.AskPrice.Value * multiplier,
				level.AskVolume.Value)
			{
				OrdersCount = level.AskOrderCount is decimal count
					? (int)Math.Min(int.MaxValue, count)
					: null,
			})
			.ToArray();
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = CurrentTime,
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private bool Matches(SecurityId securityId, string code, string exchange)
		=> securityId.SecurityCode.EqualsIgnoreCase(code) &&
			securityId.BoardCode.IsEmpty(DefaultExchange).EqualsIgnoreCase(
				exchange.IsEmpty(DefaultExchange));
}

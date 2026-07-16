namespace StockSharp.IG;

using StockSharp.IG.Native;

public partial class IgMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var count = (int)Math.Clamp(lookupMsg.Count ?? 100, 1, 1000);
		var nativeEpic = lookupMsg.SecurityId.Native as string;
		if (!nativeEpic.IsEmpty())
		{
			var details = await GetMarket(nativeEpic, cancellationToken);
			await SendSecurity(details.Instrument.Epic, details.Instrument.Name, details.Instrument.Type,
				details.Instrument.Expiry, details, lookupMsg, securityTypes, cancellationToken);
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var query = lookupMsg.SecurityId.SecurityCode;
		if (query.IsEmpty())
			throw new InvalidOperationException("IG security lookup requires an EPIC or a search term.");
		var summaries = (await _rest.SearchMarkets(query, cancellationToken)).Markets ?? [];
		var sent = 0;
		foreach (var summary in summaries)
		{
			if (sent >= count)
				break;
			IgMarketDetails details = null;
			if (summary.Epic.EqualsIgnoreCase(query))
				details = await GetMarket(summary.Epic, cancellationToken);
			if (await SendSecurity(summary.Epic, summary.Name, summary.InstrumentType, summary.Expiry,
				details, lookupMsg, securityTypes, cancellationToken))
				sent++;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessPriceSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessPriceSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var old))
				_stream.UnsubscribeTicks(old.Epic);
			return;
		}
		if (mdMsg.IsHistoryOnly())
			throw new NotSupportedException("IG does not expose historical tick trades through the public REST API.");
		var epic = mdMsg.SecurityId.ToEpic();
		_marketSubscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Epic = epic,
			DataType = DataType.Ticks,
		};
		try
		{
			_stream.SubscribeTicks(epic);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_candleSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var old))
				_stream.UnsubscribeCandles(old.Epic, old.TimeFrame);
			return;
		}
		var epic = mdMsg.SecurityId.ToEpic();
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToResolution();

		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
			await SendHistoricalCandles(mdMsg, epic, timeFrame, cancellationToken);
		if (!mdMsg.IsHistoryOnly())
		{
			_ = timeFrame.ToLightstreamerScale();
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				TransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				Epic = epic,
				TimeFrame = timeFrame,
			};
			try
			{
				_stream.SubscribeCandles(epic, timeFrame);
			}
			catch
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				throw;
			}
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask ProcessPriceSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var old))
				_stream.UnsubscribePrice(old.Epic);
			return;
		}
		var epic = mdMsg.SecurityId.ToEpic();
		if (mdMsg.IsHistoryOnly())
		{
			if (dataType == DataType.MarketDepth)
				throw new NotSupportedException("IG REST snapshots do not include the streamed tier sizes.");
			var details = await GetMarket(epic, cancellationToken);
			await SendMarketSnapshot(mdMsg.TransactionId, mdMsg.SecurityId, details, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		_marketSubscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Epic = epic,
			DataType = dataType,
		};
		try
		{
			_stream.SubscribePrice(epic);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
	}

	private async ValueTask<bool> SendSecurity(string epic, string name, string instrumentType, string expiry,
		IgMarketDetails details, SecurityLookupMessage lookupMsg, HashSet<SecurityTypes> securityTypes,
		CancellationToken cancellationToken)
	{
		if (epic.IsEmpty())
			return false;
		var instrument = details?.Instrument;
		var security = new SecurityMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			SecurityId = epic.ToSecurityId(),
			SecurityType = (instrument?.Type).IsEmpty(instrumentType).ToSecurityType(),
			Name = (instrument?.Name).IsEmpty(name),
			ShortName = epic,
			Currency = Enum.TryParse<CurrencyTypes>(instrument?.Currencies?.FirstOrDefault(c => c.IsDefault)?.Code
				?? instrument?.Currencies?.FirstOrDefault()?.Code, true, out var currency) ? currency : null,
			VolumeStep = details?.DealingRules?.MinDealSize?.Value ?? instrument?.LotSize,
			PriceStep = details?.DealingRules?.MinStepDistance?.Unit.EqualsIgnoreCase("POINTS") == true
				? details.DealingRules.MinStepDistance.Value : null,
			Multiplier = instrument?.ContractSize.ToDecimalInvariant() ?? instrument?.LotSize,
		};
		if (!security.IsMatch(lookupMsg, securityTypes))
			return false;
		await SendOutMessageAsync(security, cancellationToken);
		if (details?.Snapshot != null)
			await SendMarketSnapshot(lookupMsg.TransactionId, security.SecurityId, details, cancellationToken);
		return true;
	}

	private async Task<IgMarketDetails> GetMarket(string epic, CancellationToken cancellationToken)
	{
		if (_markets.TryGetValue(epic, out var details))
			return details;
		details = await _rest.GetMarket(epic, cancellationToken);
		if (details?.Instrument?.Epic.IsEmpty() != false)
			throw new InvalidOperationException($"IG market '{epic}' returned no instrument details.");
		_markets[epic] = details;
		return details;
	}

	private ValueTask SendMarketSnapshot(long transactionId, SecurityId securityId, IgMarketDetails details,
		CancellationToken cancellationToken)
	{
		var snapshot = details.Snapshot;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = snapshot.UpdateTimeUtc.ParseIgTime()?.UtcDateTime ?? DateTime.UtcNow,
		}
		.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
		.TryAdd(Level1Fields.BestAskPrice, snapshot.Offer)
		.TryAdd(Level1Fields.HighPrice, snapshot.High)
		.TryAdd(Level1Fields.LowPrice, snapshot.Low)
		.TryAdd(Level1Fields.Change, snapshot.NetChange)
		.TryAdd(Level1Fields.State, snapshot.MarketStatus.ToSecurityState()), cancellationToken);
	}

	private async ValueTask SendHistoricalCandles(MarketDataMessage mdMsg, string epic, TimeSpan timeFrame,
		CancellationToken cancellationToken)
	{
		var count = (int)Math.Clamp(mdMsg.Count ?? 1000, 1, 10000);
		var to = mdMsg.To is null ? DateTimeOffset.UtcNow : new(mdMsg.To.Value.ToUniversalTime());
		var from = mdMsg.From is null
			? to - TimeSpan.FromTicks(checked(timeFrame.Ticks * count))
			: new DateTimeOffset(mdMsg.From.Value.ToUniversalTime());
		var candles = new SortedDictionary<DateTimeOffset, IgPriceSnapshot>();
		var page = 1;
		while (candles.Count < count)
		{
			var response = await _rest.GetPrices(epic, timeFrame.ToResolution(), from, to, page,
				Math.Min(500, count - candles.Count), cancellationToken);
			foreach (var candle in response.Prices ?? [])
			{
				var time = candle.SnapshotTimeUtc.ParseIgTime() ?? candle.SnapshotTime.ParseIgTime();
				if (time is { } value && value >= from && value <= to)
					candles[value] = candle;
			}
			var totalPages = response.Metadata?.PageData?.TotalPages ?? 1;
			if (page >= totalPages || response.Prices?.Length is null or 0)
				break;
			page++;
		}
		foreach (var pair in candles.Take(count))
		{
			var candle = pair.Value;
			var open = candle.Open?.Mid;
			var high = candle.High?.Mid;
			var low = candle.Low?.Mid;
			var close = candle.Close?.Mid;
			if (open == null || high == null || low == null || close == null)
				continue;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = pair.Key.UtcDateTime,
				OpenPrice = open.Value,
				HighPrice = high.Value,
				LowPrice = low.Value,
				ClosePrice = close.Value,
				TotalVolume = candle.Volume ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
	}

	private void OnMarketReceived(IgMarketUpdate update)
		=> RunStreamHandler(ProcessMarket(update, CancellationToken.None));

	private async ValueTask ProcessMarket(IgMarketUpdate update, CancellationToken cancellationToken)
	{
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(s =>
			s.Epic.EqualsIgnoreCase(update.Epic) && s.DataType != DataType.Ticks))
		{
			if (subscription.DataType == DataType.Level1)
			{
				var bid = update.Bids?.FirstOrDefault();
				var ask = update.Asks?.FirstOrDefault();
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = update.Time.UtcDateTime,
				}
				.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
				.TryAdd(Level1Fields.BestBidVolume, bid?.Volume)
				.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
				.TryAdd(Level1Fields.BestAskVolume, ask?.Volume)
				.TryAdd(Level1Fields.OpenPrice, update.MidOpen)
				.TryAdd(Level1Fields.HighPrice, update.High)
				.TryAdd(Level1Fields.LowPrice, update.Low)
				.TryAdd(Level1Fields.State, update.MarketState.ToSecurityState()), cancellationToken);
			}
			else
			{
				await SendOutMessageAsync(new QuoteChangeMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = update.Time.UtcDateTime,
					Bids = [.. (update.Bids ?? []).Select(level => new QuoteChange(level.Price, level.Volume ?? 0))],
					Asks = [.. (update.Asks ?? []).Select(level => new QuoteChange(level.Price, level.Volume ?? 0))],
					State = QuoteChangeStates.SnapshotComplete,
				}, cancellationToken);
			}
		}
	}

	private void OnTickReceived(IgTickUpdate update)
		=> RunStreamHandler(ProcessTick(update, CancellationToken.None));

	private async ValueTask ProcessTick(IgTickUpdate update, CancellationToken cancellationToken)
	{
		var price = update.Last ?? (update.Bid is { } bid && update.Offer is { } offer ? (bid + offer) / 2 : update.Bid ?? update.Offer);
		if (price == null)
			return;
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(s =>
			s.Epic.EqualsIgnoreCase(update.Epic) && s.DataType == DataType.Ticks))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				TradeStringId = $"{update.Epic}:{update.Time.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}",
				TradePrice = price.Value,
				TradeVolume = update.LastVolume ?? update.TotalVolume ?? 0,
				ServerTime = update.Time.UtcDateTime,
			}, cancellationToken);
		}
	}

	private void OnCandleReceived(IgCandleUpdate update)
		=> RunStreamHandler(ProcessCandle(update, CancellationToken.None));

	private async ValueTask ProcessCandle(IgCandleUpdate update, CancellationToken cancellationToken)
	{
		if (update.Open == null || update.High == null || update.Low == null || update.Close == null)
			return;
		foreach (var subscription in _candleSubscriptions.CachedValues.Where(s =>
			s.Epic.EqualsIgnoreCase(update.Epic) && s.TimeFrame == update.TimeFrame))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				TypedArg = subscription.TimeFrame,
				OpenTime = update.Time.UtcDateTime,
				OpenPrice = update.Open.Value,
				HighPrice = update.High.Value,
				LowPrice = update.Low.Value,
				ClosePrice = update.Close.Value,
				TotalVolume = update.Volume ?? 0,
				State = update.IsFinished ? CandleStates.Finished : CandleStates.Active,
			}, cancellationToken);
		}
	}
}

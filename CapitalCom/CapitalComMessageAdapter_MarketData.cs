namespace StockSharp.CapitalCom;

public partial class CapitalComMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var count = (int)Math.Clamp(lookupMsg.Count ?? 10000, 1, 10000);
		var nativeEpic = lookupMsg.SecurityId.Native as string;

		if (!nativeEpic.IsEmpty())
		{
			var details = await GetMarket(nativeEpic, cancellationToken);
			await SendSecurity(details.Instrument.Epic, details.Instrument.Symbol,
				details.Instrument.Name, details.Instrument.Type, details, lookupMsg,
				securityTypes, cancellationToken);
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var query = lookupMsg.SecurityId.SecurityCode;
		var response = await _rest.GetMarkets(query, cancellationToken);
		var sent = 0;
		foreach (var summary in response.Markets ?? [])
		{
			if (sent >= count)
				break;

			CapitalComMarketDetails details = null;
			if (!query.IsEmpty() && summary.Epic.EqualsIgnoreCase(query))
				details = await GetMarket(summary.Epic, cancellationToken);

			if (await SendSecurity(summary.Epic, summary.Symbol, summary.InstrumentName,
				summary.InstrumentType, details, lookupMsg, securityTypes, cancellationToken,
				summary))
				sent++;
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
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var old) &&
				!_marketSubscriptions.CachedValues.Any(s => s.Epic.EqualsIgnoreCase(old.Epic)))
				await _stream.UnsubscribeMarket(old.Epic, cancellationToken);
			return;
		}

		var epic = mdMsg.SecurityId.ToEpic();
		if (mdMsg.IsHistoryOnly())
		{
			var details = await GetMarket(epic, cancellationToken);
			await SendMarketSnapshot(mdMsg.TransactionId, mdMsg.SecurityId, details, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var isFirst = !_marketSubscriptions.CachedValues.Any(s => s.Epic.EqualsIgnoreCase(epic));
		_marketSubscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Epic = epic,
		};

		try
		{
			if (isFirst)
				await _stream.SubscribeMarket(epic, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_candleSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var old))
			{
				_lastLiveCandles.Remove(old.TransactionId);
				if (!_candleSubscriptions.CachedValues.Any(s => s.Epic.EqualsIgnoreCase(old.Epic) &&
					s.TimeFrame == old.TimeFrame))
				{
					await _stream.UnsubscribeCandles(old.Epic, old.TimeFrame.ToResolution(),
						cancellationToken);
				}
			}
			return;
		}

		var epic = mdMsg.SecurityId.ToEpic();
		var timeFrame = mdMsg.GetTimeFrame();
		var resolution = timeFrame.ToResolution();

		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
			await SendHistoricalCandles(mdMsg, epic, timeFrame, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		if (!mdMsg.IsHistoryOnly())
		{
			var isFirst = !_candleSubscriptions.CachedValues.Any(s =>
				s.Epic.EqualsIgnoreCase(epic) && s.TimeFrame == timeFrame);
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				TransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				Epic = epic,
				TimeFrame = timeFrame,
			};

			try
			{
				if (isFirst)
					await _stream.SubscribeCandles(epic, resolution, cancellationToken);
			}
			catch
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				throw;
			}
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask<bool> SendSecurity(string epic, string symbol, string name,
		string instrumentType, CapitalComMarketDetails details, SecurityLookupMessage lookupMsg,
		HashSet<SecurityTypes> securityTypes, CancellationToken cancellationToken,
		CapitalComMarketSummary summary = null)
	{
		if (epic.IsEmpty())
			return false;

		var instrument = details?.Instrument;
		var rules = details?.DealingRules;
		var security = new SecurityMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			SecurityId = epic.ToSecurityId(),
			SecurityType = instrument?.Type.IsEmpty(instrumentType).ToSecurityType(),
			Name = instrument?.Name.IsEmpty(name).IsEmpty(symbol).IsEmpty(epic),
			ShortName = instrument?.Symbol.IsEmpty(symbol).IsEmpty(epic),
			Currency = Enum.TryParse<CurrencyTypes>(instrument?.Currency, true, out var currency)
				? currency
				: null,
			VolumeStep = rules?.MinSizeIncrement?.Value ?? rules?.MinDealSize?.Value ??
				instrument?.LotSize ?? summary?.LotSize,
			PriceStep = summary?.TickSize ??
				(rules?.MinStepDistance?.Unit.EqualsIgnoreCase("POINTS") == true
					? rules.MinStepDistance.Value
					: null),
			Multiplier = instrument?.LotSize ?? summary?.LotSize,
		};

		if (!security.IsMatch(lookupMsg, securityTypes))
			return false;

		await SendOutMessageAsync(security, cancellationToken);
		if (details?.Snapshot != null)
			await SendMarketSnapshot(lookupMsg.TransactionId, security.SecurityId, details, cancellationToken);
		else if (summary != null)
			await SendMarketSnapshot(lookupMsg.TransactionId, security.SecurityId, summary, cancellationToken);
		return true;
	}

	private async Task<CapitalComMarketDetails> GetMarket(string epic, CancellationToken cancellationToken)
	{
		if (_markets.TryGetValue(epic, out var details))
			return details;

		details = await _rest.GetMarket(epic, cancellationToken);
		if (details?.Instrument?.Epic.IsEmpty() != false)
			throw new InvalidOperationException(
				$"Capital.com market '{epic}' returned no instrument details.");
		_markets[epic] = details;
		return details;
	}

	private ValueTask SendMarketSnapshot(long transactionId, SecurityId securityId,
		CapitalComMarketDetails details, CancellationToken cancellationToken)
	{
		var snapshot = details?.Snapshot;
		if (snapshot == null)
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = (snapshot.UpdateTimeUtc.ParseCapitalComTime() ??
				snapshot.UpdateTime.ParseCapitalComTime() ?? DateTimeOffset.UtcNow).UtcDateTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
		.TryAdd(Level1Fields.BestAskPrice, snapshot.Offer)
		.TryAdd(Level1Fields.HighPrice, snapshot.High)
		.TryAdd(Level1Fields.LowPrice, snapshot.Low)
		.TryAdd(Level1Fields.Change, snapshot.NetChange)
		.TryAdd(Level1Fields.State, snapshot.MarketStatus.ToSecurityState()), cancellationToken);
	}

	private ValueTask SendMarketSnapshot(long transactionId, SecurityId securityId,
		CapitalComMarketSummary summary, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = (summary.UpdateTimeUtc.ParseCapitalComTime() ??
				summary.UpdateTime.ParseCapitalComTime() ?? DateTimeOffset.UtcNow).UtcDateTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, summary.Bid)
		.TryAdd(Level1Fields.BestAskPrice, summary.Offer)
		.TryAdd(Level1Fields.HighPrice, summary.High)
		.TryAdd(Level1Fields.LowPrice, summary.Low)
		.TryAdd(Level1Fields.Change, summary.NetChange)
		.TryAdd(Level1Fields.State, summary.MarketStatus.ToSecurityState()), cancellationToken);

	private async ValueTask SendHistoricalCandles(MarketDataMessage mdMsg, string epic,
		TimeSpan timeFrame, CancellationToken cancellationToken)
	{
		var count = (int)Math.Clamp(mdMsg.Count ?? 1000, 1, 1000);
		var to = mdMsg.To is null ? DateTimeOffset.UtcNow : new(mdMsg.To.Value.ToUniversalTime());
		var from = mdMsg.From is null
			? to - TimeSpan.FromTicks(checked(timeFrame.Ticks * count))
			: new DateTimeOffset(mdMsg.From.Value.ToUniversalTime());
		var response = await _rest.GetPrices(epic, timeFrame.ToResolution(), from, to, count,
			cancellationToken);

		var candles = (response.Prices ?? [])
			.Select(c => new
			{
				Candle = c,
				Time = c.SnapshotTimeUtc.ParseCapitalComTime() ?? c.SnapshotTime.ParseCapitalComTime(),
			})
			.Where(p => p.Time != null && p.Time >= from && p.Time <= to)
			.OrderBy(p => p.Time)
			.Take(count);

		foreach (var pair in candles)
		{
			var candle = pair.Candle;
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
				OpenTime = pair.Time.Value.UtcDateTime,
				OpenPrice = open.Value,
				HighPrice = high.Value,
				LowPrice = low.Value,
				ClosePrice = close.Value,
				TotalVolume = candle.Volume ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessQuote(CapitalComQuote quote, CancellationToken cancellationToken)
	{
		if (quote?.Epic.IsEmpty() != false)
			return;

		var serverTime = quote.Timestamp > 0
			? DateTimeOffset.FromUnixTimeMilliseconds(quote.Timestamp).UtcDateTime
			: DateTime.UtcNow;
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(s =>
			s.Epic.EqualsIgnoreCase(quote.Epic)))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.BestBidPrice, quote.Bid)
			.TryAdd(Level1Fields.BestBidVolume, quote.BidVolume)
			.TryAdd(Level1Fields.BestAskPrice, quote.Ask)
			.TryAdd(Level1Fields.BestAskVolume, quote.AskVolume), cancellationToken);
		}
	}

	private async ValueTask ProcessOhlc(CapitalComOhlc ohlc, CancellationToken cancellationToken)
	{
		if (ohlc?.Epic.IsEmpty() != false || ohlc.Resolution.IsEmpty() ||
			ohlc.Open == null || ohlc.High == null || ohlc.Low == null || ohlc.Close == null)
			return;

		if (!ohlc.PriceType.IsEmpty() && !ohlc.PriceType.EqualsIgnoreCase("bid"))
			return;

		var timeFrame = ohlc.Resolution.ToTimeFrame();
		var openTime = DateTimeOffset.FromUnixTimeMilliseconds(ohlc.Timestamp);
		foreach (var subscription in _candleSubscriptions.CachedValues.Where(s =>
			s.Epic.EqualsIgnoreCase(ohlc.Epic) && s.TimeFrame == timeFrame))
		{
			if (_lastLiveCandles.TryGetValue(subscription.TransactionId, out var previous) &&
				previous.Timestamp < ohlc.Timestamp)
			{
				await SendLiveCandle(subscription, previous, CandleStates.Finished, cancellationToken);
			}

			_lastLiveCandles[subscription.TransactionId] = ohlc;
			var state = openTime + timeFrame <= DateTimeOffset.UtcNow
				? CandleStates.Finished
				: CandleStates.Active;
			await SendLiveCandle(subscription, ohlc, state, cancellationToken);
		}
	}

	private ValueTask SendLiveCandle(CandleSubscription subscription, CapitalComOhlc ohlc,
		CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TypedArg = subscription.TimeFrame,
			OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(ohlc.Timestamp).UtcDateTime,
			OpenPrice = ohlc.Open.Value,
			HighPrice = ohlc.High.Value,
			LowPrice = ohlc.Low.Value,
			ClosePrice = ohlc.Close.Value,
			TotalVolume = 0,
			State = state,
		}, cancellationToken);
}

namespace StockSharp.LsSecurities;

public partial class LsSecuritiesMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var instrument in await GetRest().GetInstruments(cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			CacheInstrument(instrument);
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = ToSecurityId(instrument.Code),
				SecurityType = instrument.EtfType is null or "0" ? SecurityTypes.Stock : SecurityTypes.Etf,
				Name = instrument.Name.IsEmpty(instrument.Code),
				ShortName = instrument.Code,
				Class = instrument.Market switch { "1" => "KOSPI", "2" => "KOSDAQ", _ => "KRX" },
				Currency = CurrencyTypes.KRW,
				VolumeStep = instrument.LotSize.ToDecimal() is > 0 ? instrument.LotSize.ToDecimal() : 1,
				Multiplier = instrument.LotSize.ToDecimal() is > 0 ? instrument.LotSize.ToDecimal() : 1,
			};
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var removed))
				await RefreshMarketSubscriptions(removed.SecurityId.SecurityCode, cancellationToken);
			return;
		}

		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = (mdMsg.From ?? to.Date).ToUniversalTime();
		if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
		{
			foreach (var tick in await GetRest().GetTicks(mdMsg.SecurityId.SecurityCode, from, to,
				mdMsg.Count, cancellationToken))
			{
				var koreaDate = (to + TimeSpan.FromHours(9)).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
				var serverTime = koreaDate.ToKoreaUtc(tick.Time);
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = mdMsg.SecurityId,
					TradeStringId = $"{mdMsg.SecurityId.SecurityCode}:{tick.Time}:{tick.Price}:{tick.Volume}",
					TradePrice = tick.Price,
					TradeVolume = tick.Volume,
					ServerTime = serverTime,
				}, cancellationToken);
			}
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddMarketSubscription(mdMsg, DataType.Ticks, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessSnapshotSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessSnapshotSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessSnapshotSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var removed))
				await RefreshMarketSubscriptions(removed.SecurityId.SecurityCode, cancellationToken);
			return;
		}

		var quote = await GetRest().GetQuote(mdMsg.SecurityId.SecurityCode, cancellationToken);
		var serverTime = quote.Time.ToKoreaUtc();
		if (dataType == DataType.Level1)
		{
			var bids = quote.GetBids();
			var asks = quote.GetAsks();
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, quote.LastPrice > 0 ? quote.LastPrice : null)
			.TryAdd(Level1Fields.OpenPrice, quote.OpenPrice > 0 ? quote.OpenPrice : null)
			.TryAdd(Level1Fields.HighPrice, quote.HighPrice > 0 ? quote.HighPrice : null)
			.TryAdd(Level1Fields.LowPrice, quote.LowPrice > 0 ? quote.LowPrice : null)
			.TryAdd(Level1Fields.ClosePrice, quote.PreviousClose > 0 ? quote.PreviousClose : null)
			.TryAdd(Level1Fields.Volume, quote.Volume)
			.TryAdd(Level1Fields.BidsVolume, quote.TotalBidVolume)
			.TryAdd(Level1Fields.AsksVolume, quote.TotalAskVolume)
			.TryAdd(Level1Fields.BestBidPrice, bids.Length > 0 ? bids[0].Price : null)
			.TryAdd(Level1Fields.BestBidVolume, bids.Length > 0 ? bids[0].Volume : null)
			.TryAdd(Level1Fields.BestAskPrice, asks.Length > 0 ? asks[0].Price : null)
			.TryAdd(Level1Fields.BestAskVolume, asks.Length > 0 ? asks[0].Volume : null), cancellationToken);
		}
		else
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				ServerTime = serverTime,
				Bids = quote.GetBids(),
				Asks = quote.GetAsks(),
			}, cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		else
			await AddMarketSubscription(mdMsg, dataType, cancellationToken);
	}

	private async ValueTask AddMarketSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		_marketSubscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			DataType = dataType,
		};
		try
		{
			await RefreshMarketSubscriptions(mdMsg.SecurityId.SecurityCode, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask RefreshMarketSubscriptions(string code, CancellationToken cancellationToken)
	{
		code = code.NormalizeCode();
		var subscriptions = _marketSubscriptions.CachedValues
			.Where(s => s.SecurityId.SecurityCode.NormalizeCode().EqualsIgnoreCase(code)).ToArray();
		var key = code.ToSocketKey();
		if (subscriptions.Any(s => s.DataType is var type && (type == DataType.Ticks || type == DataType.Level1)))
			await _stream.Subscribe("US3", key, false, cancellationToken);
		else
			await _stream.Unsubscribe("US3", key, false, cancellationToken);
		if (subscriptions.Any(s => s.DataType == DataType.MarketDepth))
			await _stream.Subscribe("UH1", key, false, cancellationToken);
		else
			await _stream.Unsubscribe("UH1", key, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		var timeFrame = mdMsg.GetTimeFrame();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var estimated = Math.Clamp(mdMsg.Count ?? 500, 1, 10000);
		var from = (mdMsg.From ?? to - TimeSpan.FromTicks(timeFrame.Ticks * estimated *
			(timeFrame >= TimeSpan.FromDays(1) ? 2 : 3))).ToUniversalTime();
		foreach (var candle in await GetRest().GetCandles(mdMsg.SecurityId.SecurityCode, timeFrame,
			from, to, mdMsg.Count, cancellationToken))
		{
			var openTime = candle.Date.ToKoreaUtc(candle.Time);
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				DataType = mdMsg.DataType2,
				OpenTime = openTime,
				CloseTime = openTime + timeFrame,
				OpenPrice = candle.OpenPrice,
				HighPrice = candle.HighPrice,
				LowPrice = candle.LowPrice,
				ClosePrice = candle.ClosePrice,
				TotalVolume = candle.Volume,
				TotalTicks = null,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask ProcessTrade(LsRealtimeTrade trade, CancellationToken cancellationToken)
	{
		if (trade?.Code.IsEmpty() != false)
			return;
		var code = trade.Code.NormalizeCode();
		var subscriptions = _marketSubscriptions.CachedValues
			.Where(s => s.SecurityId.SecurityCode.NormalizeCode().EqualsIgnoreCase(code)).ToArray();
		if (subscriptions.Length == 0)
			return;
		var serverTime = trade.Time.ToKoreaUtc();
		var price = trade.Price.ToDecimal();
		var volume = trade.Volume.ToDecimal();

		foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.Level1))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, price > 0 ? price : null)
			.TryAdd(Level1Fields.LastTradeVolume, volume)
			.TryAdd(Level1Fields.LastTradeTime, serverTime)
			.TryAdd(Level1Fields.OpenPrice, trade.OpenPrice.ToDecimal())
			.TryAdd(Level1Fields.HighPrice, trade.HighPrice.ToDecimal())
			.TryAdd(Level1Fields.LowPrice, trade.LowPrice.ToDecimal())
			.TryAdd(Level1Fields.Volume, trade.TotalVolume.ToDecimal())
			.TryAdd(Level1Fields.Turnover, trade.Turnover.ToDecimal())
			.TryAdd(Level1Fields.BestBidPrice, trade.BidPrice.ToDecimal())
			.TryAdd(Level1Fields.BestAskPrice, trade.AskPrice.ToDecimal())
			.TryAdd(Level1Fields.State, trade.Status == "00" ? SecurityStates.Trading : SecurityStates.Stoped),
				cancellationToken);
		}

		if (price <= 0)
			return;
		foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.Ticks))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				TradeStringId = $"{code}:{trade.Time}:{price}:{volume}",
				TradePrice = price,
				TradeVolume = volume,
				OriginSide = trade.Aggressor switch { "+" => Sides.Buy, "-" => Sides.Sell, _ => null },
				ServerTime = serverTime,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessDepth(LsRealtimeDepth depth, CancellationToken cancellationToken)
	{
		if (depth?.Code.IsEmpty() != false)
			return;
		var code = depth.Code.NormalizeCode();
		var subscriptions = _marketSubscriptions.CachedValues.Where(s =>
			s.DataType == DataType.MarketDepth &&
			s.SecurityId.SecurityCode.NormalizeCode().EqualsIgnoreCase(code)).ToArray();
		var serverTime = depth.Time.ToKoreaUtc();
		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
				Bids = depth.GetBids(),
				Asks = depth.GetAsks(),
			}, cancellationToken);
		}
	}
}

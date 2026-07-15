namespace StockSharp.Moomoo;

partial class MoomooMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var securityTypes = message.GetSecurityTypes();
		var nativeTypes = securityTypes.ToNativeTypes().Distinct().ToArray();
		if (nativeTypes.Length == 0)
			nativeTypes = [
				QotCommon.SecurityType.SecurityType_Eqty,
				QotCommon.SecurityType.SecurityType_Trust,
				QotCommon.SecurityType.SecurityType_Drvt,
				QotCommon.SecurityType.SecurityType_Index,
				QotCommon.SecurityType.SecurityType_Bond,
			];

		var left = message.Count ?? long.MaxValue;
		foreach (var nativeType in nativeTypes)
		{
			foreach (var info in await _client.GetSecurities(nativeType, cancellationToken))
			{
				var basic = info.Basic;
				var security = new SecurityMessage
				{
					OriginalTransactionId = message.TransactionId,
					SecurityId = new() { SecurityCode = basic.Security.Code, BoardCode = ((QotCommon.ExchType)basic.ExchType).ToBoardCode() },
					Name = basic.Name,
					SecurityType = ((QotCommon.SecurityType)basic.SecType).ToSecurityType(),
					VolumeStep = 1,
					MinVolume = 1,
					Multiplier = basic.LotSize,
				};

				if (info.HasOptionExData)
				{
					var option = info.OptionExData;
					security.OptionType = (QotCommon.OptionType)option.Type == QotCommon.OptionType.OptionType_Call ? OptionTypes.Call : OptionTypes.Put;
					security.Strike = (decimal)option.StrikePrice;
					security.ExpiryDate = option.HasStrikeTimestamp ? option.StrikeTimestamp.FromUnix() : null;
					security.TryFillUnderlyingId(option.Owner.Code);
				}

				if (!security.IsMatch(message, securityTypes))
					continue;

				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}

			if (left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var code = message.SecurityId.SecurityCode;
		if (message.IsSubscribe)
		{
			var hasBasic = _level1Subscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(code));
			var hasOrderBook = hasBasic || _depthSubscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(code));
			_level1Subscriptions[message.TransactionId] = message.SecurityId;
			var subTypes = new List<QotCommon.SubType>();
			if (!hasBasic)
				subTypes.Add(QotCommon.SubType.SubType_Basic);
			if (!hasOrderBook)
				subTypes.Add(QotCommon.SubType.SubType_OrderBook);
			if (subTypes.Count > 0)
				await _client.Subscribe(code, subTypes, true, message.IsRegularTradingHours != true, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		else if (_level1Subscriptions.Remove(message.OriginalTransactionId, out var securityId))
		{
			var subTypes = new List<QotCommon.SubType>();
			if (!_level1Subscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)))
				subTypes.Add(QotCommon.SubType.SubType_Basic);
			if (!_level1Subscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)) &&
				!_depthSubscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)))
				subTypes.Add(QotCommon.SubType.SubType_OrderBook);
			if (subTypes.Count > 0)
				await _client.Subscribe(securityId.SecurityCode, subTypes, false, false, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var code = message.SecurityId.SecurityCode;
		if (message.IsSubscribe)
		{
			var hasOrderBook = _level1Subscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(code)) ||
				_depthSubscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(code));
			_depthSubscriptions[message.TransactionId] = message.SecurityId;
			if (!hasOrderBook)
				await _client.Subscribe(code, [QotCommon.SubType.SubType_OrderBook], true, message.IsRegularTradingHours != true, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		else if (_depthSubscriptions.Remove(message.OriginalTransactionId, out var securityId) &&
			!_level1Subscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)) &&
			!_depthSubscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)))
		{
			await _client.Subscribe(securityId.SecurityCode, [QotCommon.SubType.SubType_OrderBook], false, false, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var code = message.SecurityId.SecurityCode;
		if (message.IsSubscribe)
		{
			var hasTicker = _tickSubscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(code));
			_tickSubscriptions[message.TransactionId] = message.SecurityId;
			if (!hasTicker)
				await _client.Subscribe(code, [QotCommon.SubType.SubType_Ticker], true, message.IsRegularTradingHours != true, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		else if (_tickSubscriptions.Remove(message.OriginalTransactionId, out var securityId) &&
			!_tickSubscriptions.Values.Any(id => id.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)))
		{
			await _client.Subscribe(securityId.SecurityCode, [QotCommon.SubType.SubType_Ticker], false, false, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var code = message.SecurityId.SecurityCode;
		var timeFrame = message.GetTimeFrame();
		var candleType = ToCandleType(timeFrame);

		if (!message.IsSubscribe)
		{
			if (_candleSubscriptions.Remove(message.OriginalTransactionId, out var subscription) &&
				!_candleSubscriptions.Values.Any(s => s.SecurityId.SecurityCode.EqualsIgnoreCase(subscription.SecurityId.SecurityCode) && s.TimeFrame == subscription.TimeFrame))
				await _client.Subscribe(subscription.SecurityId.SecurityCode, [ToCandleSubscriptionType(subscription.TimeFrame)], false, false, cancellationToken);
			return;
		}

		if (message.From is not null || message.IsHistoryOnly())
		{
			var to = message.To?.ToUniversalTime() ?? DateTime.UtcNow;
			var start = message.From?.ToUniversalTime() ?? to.AddYears(-1);
			var candles = await _client.GetCandles(code, candleType, start, to, message.IsRegularTradingHours != true, cancellationToken);
			var filtered = candles.Where(c => !c.IsBlank)
				.Select(c => (candle: c, time: GetServerTime(c.Timestamp, c.HasTimestamp)))
				.Where(p => p.time >= start && p.time <= to)
				.OrderBy(p => p.time);
			if (message.Count is > 0 and <= int.MaxValue)
				filtered = filtered.TakeLast((int)message.Count.Value).OrderBy(p => p.time);

			foreach (var (candle, openTime) in filtered)
				await SendCandle(message.TransactionId, message.SecurityId, timeFrame, candle, openTime, CandleStates.Finished, cancellationToken);
		}

		if (!message.IsHistoryOnly())
		{
			var hasCandle = _candleSubscriptions.Values.Any(s => s.SecurityId.SecurityCode.EqualsIgnoreCase(code) && s.TimeFrame == timeFrame);
			_candleSubscriptions[message.TransactionId] = new(message.SecurityId, timeFrame);
			if (!hasCandle)
				await _client.Subscribe(code, [ToCandleSubscriptionType(timeFrame)], true, message.IsRegularTradingHours != true, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask ProcessBasicQuote(QotUpdateBasicQot.Response response, CancellationToken cancellationToken)
	{
		if (response.RetType != 0)
		{
			await SendOutErrorAsync(new InvalidOperationException(response.RetMsg.IsEmpty($"Moomoo quote push failed with code {response.RetType}.")), cancellationToken);
			return;
		}
		if (!response.HasS2C)
			return;

		foreach (var quote in response.S2C.BasicQotListList)
		{
			var subscriptions = _level1Subscriptions.Where(p => p.Value.SecurityCode.EqualsIgnoreCase(quote.Security.Code)).ToArray();
			foreach (var subscription in subscriptions)
			{
				var message = new Level1ChangeMessage
				{
					OriginalTransactionId = subscription.Key,
					SecurityId = subscription.Value,
					ServerTime = GetServerTime(quote.UpdateTimestamp, quote.HasUpdateTimestamp),
				}
				.TryAdd(Level1Fields.LastTradePrice, (decimal)quote.CurPrice)
				.TryAdd(Level1Fields.OpenPrice, (decimal)quote.OpenPrice)
				.TryAdd(Level1Fields.HighPrice, (decimal)quote.HighPrice)
				.TryAdd(Level1Fields.LowPrice, (decimal)quote.LowPrice)
				.TryAdd(Level1Fields.ClosePrice, (decimal)quote.LastClosePrice)
				.TryAdd(Level1Fields.Volume, (decimal)quote.Volume)
				.TryAdd(Level1Fields.Turnover, (decimal)quote.Turnover)
				.TryAdd(Level1Fields.PriceStep, (decimal)quote.PriceSpread)
				.TryAdd(Level1Fields.IsSystem, !quote.IsSuspended);

				if (quote.HasOptionExData)
				{
					var option = quote.OptionExData;
					message
						.TryAdd(Level1Fields.OpenInterest, option.OpenInterest)
						.TryAdd(Level1Fields.ImpliedVolatility, (decimal)option.ImpliedVolatility)
						.TryAdd(Level1Fields.Delta, (decimal)option.Delta)
						.TryAdd(Level1Fields.Gamma, (decimal)option.Gamma)
						.TryAdd(Level1Fields.Vega, (decimal)option.Vega)
						.TryAdd(Level1Fields.Theta, (decimal)option.Theta)
						.TryAdd(Level1Fields.Rho, (decimal)option.Rho);
				}

				await SendOutMessageAsync(message, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessOrderBook(QotUpdateOrderBook.Response response, CancellationToken cancellationToken)
	{
		if (response.RetType != 0)
		{
			await SendOutErrorAsync(new InvalidOperationException(response.RetMsg.IsEmpty($"Moomoo order-book push failed with code {response.RetType}.")), cancellationToken);
			return;
		}
		if (!response.HasS2C)
			return;

		var book = response.S2C;
		var code = book.Security.Code;
		var serverTime = GetServerTime(
			book.HasSvrRecvTimeBidTimestamp ? book.SvrRecvTimeBidTimestamp : book.SvrRecvTimeAskTimestamp,
			book.HasSvrRecvTimeBidTimestamp || book.HasSvrRecvTimeAskTimestamp);
		var bids = book.OrderBookBidListList.Select(q => new QuoteChange((decimal)q.Price, (decimal)q.Volume)).ToArray();
		var asks = book.OrderBookAskListList.Select(q => new QuoteChange((decimal)q.Price, (decimal)q.Volume)).ToArray();

		foreach (var subscription in _depthSubscriptions.Where(p => p.Value.SecurityCode.EqualsIgnoreCase(code)).ToArray())
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscription.Key,
				SecurityId = subscription.Value,
				ServerTime = serverTime,
				Bids = bids,
				Asks = asks,
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		}

		foreach (var subscription in _level1Subscriptions.Where(p => p.Value.SecurityCode.EqualsIgnoreCase(code)).ToArray())
		{
			var level1 = new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.Key,
				SecurityId = subscription.Value,
				ServerTime = serverTime,
			};
			if (bids.Length > 0)
				level1.TryAdd(Level1Fields.BestBidPrice, bids[0].Price).TryAdd(Level1Fields.BestBidVolume, bids[0].Volume);
			if (asks.Length > 0)
				level1.TryAdd(Level1Fields.BestAskPrice, asks[0].Price).TryAdd(Level1Fields.BestAskVolume, asks[0].Volume);
			await SendOutMessageAsync(level1, cancellationToken);
		}
	}

	private async ValueTask ProcessTicker(QotUpdateTicker.Response response, CancellationToken cancellationToken)
	{
		if (response.RetType != 0)
		{
			await SendOutErrorAsync(new InvalidOperationException(response.RetMsg.IsEmpty($"Moomoo ticker push failed with code {response.RetType}.")), cancellationToken);
			return;
		}
		if (!response.HasS2C)
			return;

		var data = response.S2C;
		foreach (var subscription in _tickSubscriptions.Where(p => p.Value.SecurityCode.EqualsIgnoreCase(data.Security.Code)).ToArray())
		{
			foreach (var ticker in data.TickerListList)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = subscription.Key,
					SecurityId = subscription.Value,
					TradeId = ticker.Sequence,
					TradePrice = (decimal)ticker.Price,
					TradeVolume = (decimal)ticker.Volume,
					ServerTime = GetServerTime(ticker.Timestamp, ticker.HasTimestamp),
					OriginSide = (QotCommon.TickerDirection)ticker.Dir switch
					{
						QotCommon.TickerDirection.TickerDirection_Bid => Sides.Buy,
						QotCommon.TickerDirection.TickerDirection_Ask => Sides.Sell,
						_ => null,
					},
				}, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessCandle(QotUpdateKL.Response response, CancellationToken cancellationToken)
	{
		if (response.RetType != 0)
		{
			await SendOutErrorAsync(new InvalidOperationException(response.RetMsg.IsEmpty($"Moomoo candle push failed with code {response.RetType}.")), cancellationToken);
			return;
		}
		if (!response.HasS2C)
			return;

		var data = response.S2C;
		var timeFrame = ToTimeFrame((QotCommon.KLType)data.KlType);
		foreach (var subscription in _candleSubscriptions.Where(p => p.Value.SecurityId.SecurityCode.EqualsIgnoreCase(data.Security.Code) && p.Value.TimeFrame == timeFrame).ToArray())
		{
			foreach (var candle in data.KlListList.Where(c => !c.IsBlank))
				await SendCandle(subscription.Key, subscription.Value.SecurityId, timeFrame, candle, GetServerTime(candle.Timestamp, candle.HasTimestamp), CandleStates.Active, cancellationToken);
		}
	}

	private ValueTask SendCandle(long originalTransactionId, SecurityId securityId, TimeSpan timeFrame, QotCommon.KLine candle, DateTime openTime, CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = (decimal)candle.OpenPrice,
			HighPrice = (decimal)candle.HighPrice,
			LowPrice = (decimal)candle.LowPrice,
			ClosePrice = (decimal)candle.ClosePrice,
			TotalVolume = (decimal)candle.Volume,
			TotalPrice = (decimal)candle.Turnover,
			State = state,
		}, cancellationToken);

	private static DateTime GetServerTime(double timestamp, bool hasTimestamp)
		=> hasTimestamp ? timestamp.FromUnix() : DateTime.UtcNow;

	private static QotCommon.KLType ToCandleType(TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? QotCommon.KLType.KLType_1Min
			: timeFrame == TimeSpan.FromMinutes(3) ? QotCommon.KLType.KLType_3Min
			: timeFrame == TimeSpan.FromMinutes(5) ? QotCommon.KLType.KLType_5Min
			: timeFrame == TimeSpan.FromMinutes(10) ? QotCommon.KLType.KLType_10Min
			: timeFrame == TimeSpan.FromMinutes(15) ? QotCommon.KLType.KLType_15Min
			: timeFrame == TimeSpan.FromMinutes(30) ? QotCommon.KLType.KLType_30Min
			: timeFrame == TimeSpan.FromHours(1) ? QotCommon.KLType.KLType_60Min
			: timeFrame == TimeSpan.FromHours(2) ? QotCommon.KLType.KLType_120Min
			: timeFrame == TimeSpan.FromHours(3) ? QotCommon.KLType.KLType_180Min
			: timeFrame == TimeSpan.FromHours(4) ? QotCommon.KLType.KLType_240Min
			: timeFrame == TimeSpan.FromDays(1) ? QotCommon.KLType.KLType_Day
			: timeFrame == TimeSpan.FromDays(7) ? QotCommon.KLType.KLType_Week
			: timeFrame == TimeSpan.FromDays(30) ? QotCommon.KLType.KLType_Month
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	private static QotCommon.SubType ToCandleSubscriptionType(TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? QotCommon.SubType.SubType_KL_1Min
			: timeFrame == TimeSpan.FromMinutes(3) ? QotCommon.SubType.SubType_KL_3Min
			: timeFrame == TimeSpan.FromMinutes(5) ? QotCommon.SubType.SubType_KL_5Min
			: timeFrame == TimeSpan.FromMinutes(10) ? QotCommon.SubType.SubType_KL_10Min
			: timeFrame == TimeSpan.FromMinutes(15) ? QotCommon.SubType.SubType_KL_15Min
			: timeFrame == TimeSpan.FromMinutes(30) ? QotCommon.SubType.SubType_KL_30Min
			: timeFrame == TimeSpan.FromHours(1) ? QotCommon.SubType.SubType_KL_60Min
			: timeFrame == TimeSpan.FromHours(2) ? QotCommon.SubType.SubType_KL_120Min
			: timeFrame == TimeSpan.FromHours(3) ? QotCommon.SubType.SubType_KL_180Min
			: timeFrame == TimeSpan.FromHours(4) ? QotCommon.SubType.SubType_KL_240Min
			: timeFrame == TimeSpan.FromDays(1) ? QotCommon.SubType.SubType_KL_Day
			: timeFrame == TimeSpan.FromDays(7) ? QotCommon.SubType.SubType_KL_Week
			: timeFrame == TimeSpan.FromDays(30) ? QotCommon.SubType.SubType_KL_Month
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	private static TimeSpan ToTimeFrame(QotCommon.KLType candleType)
		=> candleType switch
		{
			QotCommon.KLType.KLType_1Min => TimeSpan.FromMinutes(1),
			QotCommon.KLType.KLType_3Min => TimeSpan.FromMinutes(3),
			QotCommon.KLType.KLType_5Min => TimeSpan.FromMinutes(5),
			QotCommon.KLType.KLType_10Min => TimeSpan.FromMinutes(10),
			QotCommon.KLType.KLType_15Min => TimeSpan.FromMinutes(15),
			QotCommon.KLType.KLType_30Min => TimeSpan.FromMinutes(30),
			QotCommon.KLType.KLType_60Min => TimeSpan.FromHours(1),
			QotCommon.KLType.KLType_120Min => TimeSpan.FromHours(2),
			QotCommon.KLType.KLType_180Min => TimeSpan.FromHours(3),
			QotCommon.KLType.KLType_240Min => TimeSpan.FromHours(4),
			QotCommon.KLType.KLType_Day => TimeSpan.FromDays(1),
			QotCommon.KLType.KLType_Week => TimeSpan.FromDays(7),
			QotCommon.KLType.KLType_Month => TimeSpan.FromDays(30),
			_ => default,
		};
}

namespace StockSharp.Longbridge;

public partial class LongbridgeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.SecurityId.SecurityCode.IsEmpty() && lookupMsg.SecurityId.Native == null)
			throw new InvalidOperationException("Longbridge security lookup requires an exact symbol such as AAPL.US or 700.HK.");
		var symbol = lookupMsg.SecurityId.ToNativeSymbol();
		var request = new MultiSecurityRequest();
		request.Symbol.Add(symbol);
		var response = await _quoteSocket.Request((byte)LongbridgeQuoteCommand.QuerySecurityStaticInfo,
			request, SecurityStaticInfoResponse.Parser, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		foreach (var info in response.SecuStaticInfo)
		{
			var securityType = GetSecurityType(info, securityTypes);
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = info.Symbol.ToSecurityId(),
				SecurityType = securityType,
				Name = info.NameEn.IsEmpty(info.NameHk).IsEmpty(info.NameCn),
				ShortName = info.Symbol.Split('.')[0],
				Currency = Enum.TryParse<CurrencyTypes>(info.Currency, true, out var currency) ? currency : null,
				VolumeStep = info.LotSize > 0 ? info.LotSize : null,
				Multiplier = info.LotSize > 0 ? info.LotSize : null,
			};
			if (security.IsMatch(lookupMsg, securityTypes))
				await SendOutMessageAsync(security, cancellationToken);
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, SubType.Quote, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, SubType.Depth, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, SubType.Trade, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		var symbol = mdMsg.SecurityId.ToNativeSymbol();
		var timeFrame = mdMsg.GetTimeFrame();
		var period = timeFrame.ToPeriod();
		var count = (int)Math.Clamp(mdMsg.Count ?? 1000, 1, 1000);
		SecurityCandlestickResponse response;
		if (mdMsg.From != null)
		{
			var from = mdMsg.From.Value.ToUniversalTime();
			var to = mdMsg.To?.ToUniversalTime() ?? DateTime.UtcNow;
			var request = new SecurityHistoryCandlestickRequest
			{
				Symbol = symbol,
				Period = period,
				AdjustType = AdjustType.NoAdjust,
				QueryType = HistoryCandlestickQueryType.QueryByDate,
				DateRequest = new()
				{
					StartDate = from.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
					EndDate = to.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
				},
			};
			response = await _quoteSocket.Request((byte)LongbridgeQuoteCommand.QueryHistoryCandlestick,
				request, SecurityCandlestickResponse.Parser, cancellationToken);
		}
		else
		{
			response = await _quoteSocket.Request((byte)LongbridgeQuoteCommand.QueryCandlestick,
				new SecurityCandlestickRequest
				{
					Symbol = symbol,
					Period = period,
					Count = count,
					AdjustType = AdjustType.NoAdjust,
				}, SecurityCandlestickResponse.Parser, cancellationToken);
		}
		var lower = mdMsg.From?.ToUniversalTime();
		var upper = mdMsg.To?.ToUniversalTime();
		foreach (var candle in response.Candlesticks
			.Where(c => (lower == null || c.Timestamp.ToUtcTime() >= lower) && (upper == null || c.Timestamp.ToUtcTime() <= upper))
			.OrderBy(c => c.Timestamp).TakeLast(count))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = candle.Timestamp.ToUtcTime(),
				OpenPrice = candle.Open.ToDecimal(),
				HighPrice = candle.High.ToDecimal(),
				LowPrice = candle.Low.ToDecimal(),
				ClosePrice = candle.Close.ToDecimal(),
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask ProcessMarketSubscription(MarketDataMessage mdMsg, SubType type,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (!_subscriptions.TryGetValue(mdMsg.OriginalTransactionId, out var subscription))
				return;
			_subscriptions.Remove(mdMsg.OriginalTransactionId);
			if (!_subscriptions.CachedValues.Any(s => s.Symbol.EqualsIgnoreCase(subscription.Symbol) && s.Type == subscription.Type))
			{
				var request = new UnsubscribeRequest();
				request.Symbol.Add(subscription.Symbol);
				request.SubType.Add(subscription.Type);
				await _quoteSocket.Request((byte)LongbridgeQuoteCommand.Unsubscribe, request,
					UnsubscribeResponse.Parser, cancellationToken);
			}
			return;
		}

		var symbol = mdMsg.SecurityId.ToNativeSymbol();
		if (mdMsg.IsHistoryOnly())
		{
			await SendMarketSnapshot(mdMsg, symbol, type, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (_subscriptions.CachedValues.Select(s => s.Symbol).Append(symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 500)
			throw new InvalidOperationException("Longbridge quote subscriptions are limited to 500 distinct symbols.");
		var alreadySubscribed = _subscriptions.CachedValues.Any(s => s.Symbol.EqualsIgnoreCase(symbol) && s.Type == type);
		_subscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			Symbol = symbol,
			SecurityId = mdMsg.SecurityId,
			Type = type,
		};
		try
		{
			if (!alreadySubscribed)
			{
				var request = new SubscribeRequest { IsFirstPush = true };
				request.Symbol.Add(symbol);
				request.SubType.Add(type);
				await _quoteSocket.Request((byte)LongbridgeQuoteCommand.Subscribe, request,
					UnsubscribeResponse.Parser, cancellationToken);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			_subscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
	}

	private async ValueTask SendMarketSnapshot(MarketDataMessage mdMsg, string symbol, SubType type,
		CancellationToken cancellationToken)
	{
		switch (type)
		{
			case SubType.Quote:
			{
				var request = new MultiSecurityRequest();
				request.Symbol.Add(symbol);
				var response = await _quoteSocket.Request((byte)LongbridgeQuoteCommand.QuerySecurityQuote,
					request, SecurityQuoteResponse.Parser, cancellationToken);
				foreach (var quote in response.SecuQuote)
					await SendLevel1(mdMsg.TransactionId, mdMsg.SecurityId, quote, cancellationToken);
				break;
			}
			case SubType.Depth:
			{
				var response = await _quoteSocket.Request((byte)LongbridgeQuoteCommand.QueryDepth,
					new SecurityRequest { Symbol = symbol }, SecurityDepthResponse.Parser, cancellationToken);
				await SendDepth(mdMsg.TransactionId, mdMsg.SecurityId, response.Ask, response.Bid,
					DateTime.UtcNow, cancellationToken);
				break;
			}
			case SubType.Trade:
			{
				var response = await _quoteSocket.Request((byte)LongbridgeQuoteCommand.QueryTrade,
					new SecurityTradeRequest { Symbol = symbol, Count = (int)Math.Clamp(mdMsg.Count ?? 100, 1, 1000) },
					SecurityTradeResponse.Parser, cancellationToken);
				var index = 0;
				foreach (var trade in response.Trades.OrderBy(t => t.Timestamp))
					await SendTick(mdMsg.TransactionId, mdMsg.SecurityId, trade, $"snapshot:{index++}", cancellationToken);
				break;
			}
		}
	}

	private async ValueTask ProcessQuotePacket(LongbridgePacket packet, CancellationToken cancellationToken)
	{
		if (packet.Command == (byte)LongbridgeQuoteCommand.PushQuoteData)
		{
			var quote = PushQuote.Parser.ParseFrom(packet.Body);
			foreach (var subscription in FindSubscriptions(quote.Symbol, SubType.Quote))
				await SendLevel1(subscription.TransactionId, subscription.SecurityId, quote, cancellationToken);
		}
		else if (packet.Command == (byte)LongbridgeQuoteCommand.PushDepthData)
		{
			var depth = PushDepth.Parser.ParseFrom(packet.Body);
			foreach (var subscription in FindSubscriptions(depth.Symbol, SubType.Depth))
				await SendDepth(subscription.TransactionId, subscription.SecurityId, depth.Ask, depth.Bid,
					DateTime.UtcNow, cancellationToken);
		}
		else if (packet.Command == (byte)LongbridgeQuoteCommand.PushTradeData)
		{
			var trades = PushTrade.Parser.ParseFrom(packet.Body);
			foreach (var subscription in FindSubscriptions(trades.Symbol, SubType.Trade))
			{
				var index = 0;
				foreach (var trade in trades.Trade)
					await SendTick(subscription.TransactionId, subscription.SecurityId, trade,
						$"{trades.Sequence.ToString(CultureInfo.InvariantCulture)}:{index++}", cancellationToken);
			}
		}
	}

	private MarketSubscription[] FindSubscriptions(string symbol, SubType type)
		=> _subscriptions.CachedValues.Where(s => s.Symbol.EqualsIgnoreCase(symbol) && s.Type == type).ToArray();

	private ValueTask SendLevel1(long transactionId, SecurityId securityId, SecurityQuote quote,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = quote.Timestamp.ToUtcTime(),
		}
		.TryAdd(Level1Fields.LastTradePrice, quote.LastDone.ToNullableDecimal())
		.TryAdd(Level1Fields.OpenPrice, quote.Open.ToNullableDecimal())
		.TryAdd(Level1Fields.HighPrice, quote.High.ToNullableDecimal())
		.TryAdd(Level1Fields.LowPrice, quote.Low.ToNullableDecimal())
		.TryAdd(Level1Fields.ClosePrice, quote.PrevClose.ToNullableDecimal())
		.TryAdd(Level1Fields.Volume, quote.Volume)
		.TryAdd(Level1Fields.State, quote.TradeStatus == TradeStatus.Normal ? SecurityStates.Trading : SecurityStates.Stoped),
			cancellationToken);

	private ValueTask SendLevel1(long transactionId, SecurityId securityId, PushQuote quote,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = quote.Timestamp.ToUtcTime(),
		}
		.TryAdd(Level1Fields.LastTradePrice, quote.LastDone.ToNullableDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, quote.CurrentVolume)
		.TryAdd(Level1Fields.OpenPrice, quote.Open.ToNullableDecimal())
		.TryAdd(Level1Fields.HighPrice, quote.High.ToNullableDecimal())
		.TryAdd(Level1Fields.LowPrice, quote.Low.ToNullableDecimal())
		.TryAdd(Level1Fields.Volume, quote.Volume)
		.TryAdd(Level1Fields.State, quote.TradeStatus == TradeStatus.Normal ? SecurityStates.Trading : SecurityStates.Stoped),
			cancellationToken);

	private ValueTask SendDepth(long transactionId, SecurityId securityId, IEnumerable<Depth> asks,
		IEnumerable<Depth> bids, DateTime serverTime, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
			Bids = [.. bids.Where(d => d.Price.ToNullableDecimal() is > 0).OrderByDescending(d => d.Price.ToDecimal())
				.Select(d => new QuoteChange(d.Price.ToDecimal(), d.Volume) { OrdersCount = (int)Math.Min(int.MaxValue, d.OrderNum) })],
			Asks = [.. asks.Where(d => d.Price.ToNullableDecimal() is > 0).OrderBy(d => d.Price.ToDecimal())
				.Select(d => new QuoteChange(d.Price.ToDecimal(), d.Volume) { OrdersCount = (int)Math.Min(int.MaxValue, d.OrderNum) })],
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);

	private ValueTask SendTick(long transactionId, SecurityId securityId, LongbridgeQuoteTrade trade,
		string tradeId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = trade.Timestamp.ToUtcTime(),
			TradeStringId = tradeId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Volume,
			OriginSide = trade.Direction switch { 1 => Sides.Buy, 2 => Sides.Sell, _ => null },
		}, cancellationToken);

	private static SecurityTypes GetSecurityType(StaticInfo info, HashSet<SecurityTypes> requested)
	{
		if (requested.Count == 1)
			return requested.First();
		if (info.Board.ContainsIgnoreCase("option"))
			return SecurityTypes.Option;
		if (info.Board.ContainsIgnoreCase("warrant"))
			return SecurityTypes.Warrant;
		if (info.Board.ContainsIgnoreCase("fund") || info.Board.ContainsIgnoreCase("etf"))
			return SecurityTypes.Fund;
		return SecurityTypes.Stock;
	}
}

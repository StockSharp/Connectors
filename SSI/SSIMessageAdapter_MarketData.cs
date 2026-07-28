namespace StockSharp.SSI;

public partial class SSIMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var requestedSymbol =
			lookupMsg.SecurityId.SecurityCode?.Trim().ToUpperInvariant();
		var requestedBoard = lookupMsg.SecurityId.BoardCode?.Trim()
			.ToUpperInvariant();
		if (!requestedBoard.IsEmpty() &&
			!AssociatedBoards.Any(requestedBoard.EqualsIgnoreCase))
			throw new InvalidOperationException(
				$"Security board '{requestedBoard}' is not associated " +
					"with SSI.");
		var boards = requestedBoard.IsEmpty()
			? requestedSymbol.IsEmpty()
				? AssociatedBoards
				: [null]
			: [requestedBoard];
		var maximum = lookupMsg.Count is > 0
			? lookupMsg.Count.Value
			: long.MaxValue;
		var sent = 0L;
		var symbols = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var board in boards)
		{
			foreach (var value in await RestClient.GetSecuritiesAsync(
				requestedSymbol, board, cancellationToken))
			{
				var instrument = value.ToSSIInstrument();
				if (instrument.Symbol.IsEmpty() ||
					!symbols.Add(instrument.Symbol))
					continue;
				await SendSecurityAsync(instrument,
					lookupMsg.TransactionId, cancellationToken);
				if (++sent >= maximum)
					break;
			}
			if (sent >= maximum)
				break;
		}
		if (requestedSymbol.IsEmpty() && sent < maximum)
		{
			foreach (var value in await RestClient.GetIndexesAsync(
				requestedBoard, cancellationToken))
			{
				var symbol = value.Value<string>("index");
				if (symbol.IsEmpty() || !symbols.Add(symbol))
					continue;
				var nativeBoard = value.Value<string>("board");
				var board = (nativeBoard.IsEmpty()
					? requestedBoard
					: nativeBoard).ToSSIBoard();
				using (_sync.EnterScope())
				{
					_securityBoards[symbol] = board;
					_securityTypes[symbol] = SecurityTypes.Index;
				}
				await SendOutMessageAsync(new SecurityMessage
				{
					OriginalTransactionId = lookupMsg.TransactionId,
					SecurityId = new()
					{
						SecurityCode = symbol,
						BoardCode = board,
					},
					Name = value.Value<string>("indexName") ?? symbol,
					ShortName = symbol,
					Class = board,
					SecurityType = SecurityTypes.Index,
					Currency = CurrencyTypes.VND,
					PriceStep = 0.01m,
					VolumeStep = 1,
				}, cancellationToken);
				if (++sent >= maximum)
					break;
			}
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private ValueTask SendSecurityAsync(SSIInstrument instrument,
		long target, CancellationToken cancellationToken)
	{
		var board = (instrument.Board.IsEmpty() &&
			instrument.Symbol.StartsWith("VN30F",
				StringComparison.OrdinalIgnoreCase)
				? BoardCodes.Hnx
				: instrument.Board).ToSSIBoard();
		var type = instrument.ToSSISecurityType();
		using (_sync.EnterScope())
		{
			_securityBoards[instrument.Symbol] = board;
			_securityTypes[instrument.Symbol] = type;
		}
		return SendOutMessageAsync(new SecurityMessage
		{
			OriginalTransactionId = target,
			SecurityId = new()
			{
				SecurityCode = instrument.Symbol,
				BoardCode = board,
			},
			Name = instrument.Name.IsEmpty()
				? instrument.Symbol
				: instrument.Name,
			ShortName = instrument.Symbol,
			Class = board,
			SecurityType = type,
			Currency = CurrencyTypes.VND,
			PriceStep = board.EqualsIgnoreCase(BoardCodes.Hose)
				? 10
				: 100,
			VolumeStep = instrument.LotSize > 0
				? instrument.LotSize
				: 1,
			MinVolume = instrument.LotSize > 0
				? instrument.LotSize
				: null,
			ExpiryDate = instrument.MaturityDate?.DateTime,
			UnderlyingSecurityId =
				instrument.UnderlyingSymbol.IsEmpty()
					? default
					: new()
					{
						SecurityCode = instrument.UnderlyingSymbol,
						BoardCode = board,
					},
			Strike = instrument.ExercisePrice,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			SecurityId removed;
			using (_sync.EnterScope())
			{
				if (!_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeTopicAsync(TradeTopic(
				removed.SecurityCode), cancellationToken);
			await UnsubscribeTopicAsync(QuoteTopic(
				removed.SecurityCode), cancellationToken);
			return;
		}
		var security = Normalize(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(security, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = security;
		try
		{
			await SubscribeTopicAsync(TradeTopic(
				security.SecurityCode), cancellationToken);
			await SubscribeTopicAsync(QuoteTopic(
				security.SecurityCode), cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
			await UnsubscribeTopicAsync(TradeTopic(
				security.SecurityCode), CancellationToken.None);
			await UnsubscribeTopicAsync(QuoteTopic(
				security.SecurityCode), CancellationToken.None);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask SendLevel1SnapshotAsync(SecurityId security,
		long target, CancellationToken cancellationToken)
	{
		var today = CurrentTime.Date;
		var values = await RestClient.GetSecuritiesSummaryAsync(
			security.SecurityCode, today, today, 1, 1,
			cancellationToken);
		var value = values.FirstOrDefault();
		if (value is null)
			return;
		var candle = value.ToSSICandle();
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = security,
			ServerTime = candle.Time == default
				? CurrentTime
				: candle.Time.UtcDateTime,
		}
		.TryAdd(Level1Fields.OpenPrice, candle.Open, true)
		.TryAdd(Level1Fields.HighPrice, candle.High, true)
		.TryAdd(Level1Fields.LowPrice, candle.Low, true)
		.TryAdd(Level1Fields.LastTradePrice, candle.Close, true)
		.TryAdd(Level1Fields.Volume, candle.Volume, true)
		.TryAdd(Level1Fields.Turnover, candle.Turnover, true),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			SecurityId removed;
			using (_sync.EnterScope())
			{
				if (!_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeTopicAsync(QuoteTopic(
				removed.SecurityCode), cancellationToken);
			return;
		}
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var security = Normalize(mdMsg.SecurityId);
		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = security;
		try
		{
			await SubscribeTopicAsync(QuoteTopic(
				security.SecurityCode), cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			SecurityId removed;
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeTopicAsync(TradeTopic(
				removed.SecurityCode), cancellationToken);
			return;
		}
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var security = Normalize(mdMsg.SecurityId);
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = security;
		try
		{
			await SubscribeTopicAsync(TradeTopic(
				security.SecurityCode), cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			CandleSubscription removed;
			using (_sync.EnterScope())
			{
				if (!_candleSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeTopicAsync(CandleTopic(
				removed.SecurityId.SecurityCode,
				removed.TimeFrame), cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var security = Normalize(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToSSIInterval();
		var from = mdMsg.From?.ToUniversalTime() ??
			CurrentTime.Date;
		var to = mdMsg.To?.ToUniversalTime() ?? CurrentTime;
		var remaining = mdMsg.Count is > 0
			? Math.Min(mdMsg.Count.Value, int.MaxValue)
			: 1000;
		for (var page = 1; remaining > 0; page++)
		{
			var pageSize = (int)Math.Min(remaining, 1000);
			var candles = await RestClient.GetCandlesAsync(
				security.SecurityCode, timeFrame, from, to, page,
				pageSize, cancellationToken);
			foreach (var candle in candles.Take(pageSize))
				await SendCandleAsync(candle, security, timeFrame,
					mdMsg.TransactionId,
					candle.Time + timeFrame <= CurrentTime
						? CandleStates.Finished
						: CandleStates.Active,
					cancellationToken);
			remaining -= candles.Length;
			if (candles.Length < pageSize)
				break;
		}
		if (mdMsg.IsHistoryOnly() ||
			mdMsg.To is DateTime end &&
			end.ToUniversalTime() <= CurrentTime)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var subscription = new CandleSubscription
		{
			SecurityId = security,
			TimeFrame = timeFrame,
		};
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = subscription;
		try
		{
			await SubscribeTopicAsync(CandleTopic(
				security.SecurityCode, timeFrame), cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private static string TradeTopic(string symbol)
		=> $"trade.{symbol}";

	private static string QuoteTopic(string symbol)
		=> $"quote.{symbol}";

	private static string CandleTopic(string symbol, TimeSpan timeFrame)
		=> $"trade.{symbol}@{timeFrame.ToSSIInterval()}";

	private async ValueTask OnStreamMessageAsync(JObject message,
		CancellationToken cancellationToken)
	{
		try
		{
			var channel = message.Value<string>("channel");
			if (channel.EqualsIgnoreCase("HEARTBEAT"))
				return;
			var topic = message.Value<string>("topic");
			var value = message["data"] as JObject ?? message;
			if (channel.EqualsIgnoreCase("TRADING") ||
				topic?.StartsWith("order.",
					StringComparison.OrdinalIgnoreCase) == true ||
				topic?.StartsWith("portfolio.",
					StringComparison.OrdinalIgnoreCase) == true)
			{
				await ProcessTradingStreamAsync(topic, value,
					cancellationToken);
				return;
			}
			if (topic?.StartsWith("quote.",
				StringComparison.OrdinalIgnoreCase) == true)
			{
				var depth = value.ToSSIDepth();
				var depthTargets = FindTargets(_depthSubscriptions,
					depth.Symbol);
				foreach (var target in depthTargets)
					await SendDepthAsync(depth, target.SecurityId,
						target.Id, cancellationToken);
				var level1Targets = FindTargets(_level1Subscriptions,
					depth.Symbol);
				foreach (var target in level1Targets)
					await SendLevel1DepthAsync(depth, target.SecurityId,
						target.Id, cancellationToken);
				return;
			}
			if (topic?.StartsWith("trade.",
				StringComparison.OrdinalIgnoreCase) != true)
				return;
			if (topic.Contains('@'))
			{
				var candle = value.ToSSICandle();
				(long Id, CandleSubscription Value)[] targets;
				using (_sync.EnterScope())
					targets =
					[
						.. _candleSubscriptions
							.Where(pair =>
								pair.Value.SecurityId.SecurityCode
									.EqualsIgnoreCase(candle.Symbol) &&
								CandleTopic(candle.Symbol,
									pair.Value.TimeFrame)
									.EqualsIgnoreCase(topic))
							.Select(static pair =>
								(pair.Key, pair.Value))
					];
				foreach (var target in targets)
					await SendCandleAsync(candle,
						target.Value.SecurityId,
						target.Value.TimeFrame, target.Id,
						CandleStates.Active, cancellationToken);
				return;
			}
			var trade = value.ToSSITrade();
			foreach (var target in FindTargets(_level1Subscriptions,
				trade.Symbol))
				await SendLevel1TradeAsync(trade, target.SecurityId,
					target.Id, cancellationToken);
			foreach (var target in FindTargets(_tickSubscriptions,
				trade.Symbol))
				await SendTickAsync(trade, target.SecurityId,
					target.Id, cancellationToken);
		}
		catch (Exception error)
			when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private (long Id, SecurityId SecurityId)[] FindTargets(
		Dictionary<long, SecurityId> subscriptions, string symbol)
	{
		using (_sync.EnterScope())
			return subscriptions
				.Where(pair => pair.Value.SecurityCode
					.EqualsIgnoreCase(symbol))
				.Select(static pair => (pair.Key, pair.Value))
				.ToArray();
	}

	private ValueTask SendLevel1TradeAsync(SSITrade trade,
		SecurityId securityId, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			ServerTime = trade.Time.UtcDateTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, trade.Price, true)
		.TryAdd(Level1Fields.LastTradeVolume, trade.Volume, true)
		.TryAdd(Level1Fields.LastTradeOrigin, trade.Side)
		.TryAdd(Level1Fields.Volume, trade.TotalVolume, true),
			cancellationToken);

	private ValueTask SendLevel1DepthAsync(SSIDepth depth,
		SecurityId securityId, long target,
		CancellationToken cancellationToken)
	{
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			ServerTime = depth.Time.UtcDateTime,
		};
		var bid = depth.Bids.FirstOrDefault();
		var ask = depth.Asks.FirstOrDefault();
		if (bid.Price > 0)
			message
				.TryAdd(Level1Fields.BestBidPrice, bid.Price)
				.TryAdd(Level1Fields.BestBidVolume, bid.Volume);
		if (ask.Price > 0)
			message
				.TryAdd(Level1Fields.BestAskPrice, ask.Price)
				.TryAdd(Level1Fields.BestAskVolume, ask.Volume);
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask SendTickAsync(SSITrade trade,
		SecurityId securityId, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = target,
			SecurityId = securityId,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Side,
			ServerTime = trade.Time.UtcDateTime,
		}, cancellationToken);

	private ValueTask SendDepthAsync(SSIDepth depth,
		SecurityId securityId, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			ServerTime = depth.Time.UtcDateTime,
			Bids = depth.Bids.Select(static level =>
				new QuoteChange(level.Price, level.Volume)).ToArray(),
			Asks = depth.Asks.Select(static level =>
				new QuoteChange(level.Price, level.Volume)).ToArray(),
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);

	private ValueTask SendCandleAsync(SSICandle candle,
		SecurityId securityId, TimeSpan timeFrame, long target,
		CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			OpenTime = candle.Time.UtcDateTime,
			CloseTime = (candle.Time + timeFrame).UtcDateTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TotalPrice = candle.Turnover,
			TypedArg = timeFrame,
			State = state,
		}, cancellationToken);

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}

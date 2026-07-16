namespace StockSharp.SierraChartDtc;

using StockSharp.SierraChartDtc.Native;

public partial class SierraChartDtcMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (_capabilities?.IsSecurityDefinitionsSupported == false)
			throw new NotSupportedException("The connected DTC server does not advertise security-definition support.");

		var requestId = GetRequestId();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var context = new SecurityLookupContext
		{
			Message = lookupMsg,
			SecurityTypes = securityTypes,
			Skip = lookupMsg.Skip ?? 0,
			Remaining = lookupMsg.Count ?? long.MaxValue,
		};
		_securityLookups.Add(requestId, context);
		try
		{
			var nativeType = securityTypes.Count == 1 ? securityTypes.First().ToNative() : DtcSecurityTypes.Unset;
			if (lookupMsg.SecurityId.SecurityCode.IsEmpty() && lookupMsg.Name.IsEmpty() && lookupMsg.ShortName.IsEmpty())
			{
				await GetClient().Send(new DtcSymbolsForExchangeRequest
				{
					RequestId = requestId,
					Exchange = lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase("DTC")
						? null : lookupMsg.SecurityId.BoardCode,
					SecurityType = nativeType,
				}, cancellationToken);
			}
			else
			{
				await GetClient().Send(new DtcSymbolSearchRequest
				{
					RequestId = requestId,
					SearchText = lookupMsg.SecurityId.SecurityCode
						.IsEmpty(lookupMsg.ShortName).IsEmpty(lookupMsg.Name),
					Exchange = lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase("DTC")
						? null : lookupMsg.SecurityId.BoardCode,
					SecurityType = nativeType,
					SearchType = lookupMsg.SecurityId.SecurityCode.IsEmpty()
						? DtcSearchTypes.ByDescription : DtcSearchTypes.BySymbol,
				}, cancellationToken);
			}
		}
		catch
		{
			_securityLookups.Remove(requestId);
			throw;
		}
	}

	private async ValueTask ProcessSecurityDefinition(DtcSecurityDefinition definition,
		CancellationToken cancellationToken)
	{
		if (!_securityLookups.TryGetValue(definition.RequestId, out var context))
			return;

		if (!definition.Symbol.IsEmpty() && context.Remaining > 0)
		{
			var securityId = ToSecurityId(definition.Symbol, definition.Exchange);
			var security = new SecurityMessage
			{
				OriginalTransactionId = context.Message.TransactionId,
				SecurityId = securityId,
				SecurityType = definition.SecurityType.ToStockSharp(),
				Name = definition.Description,
				ShortName = definition.ExchangeSymbol.IsEmpty(definition.Symbol),
				Class = definition.ProductIdentifier,
				Currency = definition.Currency.ToCurrency(),
				PriceStep = definition.MinPriceIncrement is > 0 ? definition.MinPriceIncrement : null,
				VolumeStep = definition.QuantityDivisor is > 0 ? 1 / definition.QuantityDivisor : null,
				Multiplier = definition.ContractSize is > 0 ? definition.ContractSize : null,
				ExpiryDate = definition.ExpirationDate,
				Strike = definition.StrikePrice is > 0 ? definition.StrikePrice : null,
				OptionType = definition.PutOrCall switch
				{
					DtcPutCalls.Call => OptionTypes.Call,
					DtcPutCalls.Put => OptionTypes.Put,
					_ => null,
				},
				UnderlyingSecurityId = definition.UnderlyingSymbol.IsEmpty()
					? default
					: ToSecurityId(definition.UnderlyingSymbol, definition.Exchange),
			};

			if (security.IsMatch(context.Message, context.SecurityTypes))
			{
				if (context.Skip > 0)
					context.Skip--;
				else
				{
					await SendOutMessageAsync(security, cancellationToken);
					context.Remaining--;
				}
			}
		}

		if (!definition.IsFinal && context.Remaining > 0)
			return;

		_securityLookups.Remove(definition.RequestId);
		await SendSubscriptionResultAsync(context.Message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessLiveSubscription(mdMsg, DataType.Level1, false, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessLiveSubscription(mdMsg, DataType.MarketDepth, true, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscription(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var hasHistory = mdMsg.IsHistoryOnly() || mdMsg.From != null || mdMsg.To != null || mdMsg.Count != null;
		if (hasHistory)
			await RequestHistory(mdMsg, 0, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscription(mdMsg, DataType.Ticks, false, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var timeFrame = mdMsg.GetTimeFrame();
		if (timeFrame <= TimeSpan.Zero || timeFrame.TotalSeconds > int.MaxValue ||
			timeFrame.Ticks % TimeSpan.TicksPerSecond != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"DTC candle intervals must be positive whole seconds.");
		}

		await RequestHistory(mdMsg, (int)timeFrame.TotalSeconds, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask ProcessLiveSubscription(MarketDataMessage mdMsg, DataType dataType,
		bool isDepth, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscription(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscription(mdMsg, dataType, isDepth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask AddLiveSubscription(MarketDataMessage mdMsg, DataType dataType,
		bool isDepth, CancellationToken cancellationToken)
	{
		if (isDepth && _capabilities?.IsMarketDepthSupported == false)
			throw new NotSupportedException("The connected DTC server does not advertise market-depth support.");
		if (!isDepth && _capabilities?.IsMarketDataSupported == false)
			throw new NotSupportedException("The connected DTC server does not advertise market-data support.");

		var state = GetOrCreateSymbol(mdMsg.SecurityId);
		var subscription = new MarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = state.SecurityId,
			DataType = dataType,
			Symbol = state,
		};
		_marketSubscriptions.Add(mdMsg.TransactionId, subscription);

		var isFirst = false;
		lock (state.SyncRoot)
		{
			var set = isDepth ? state.DepthSubscriptions : state.MarketSubscriptions;
			isFirst = set.Count == 0;
			set.Add(mdMsg.TransactionId);
		}

		if (!isFirst)
			return;

		try
		{
			await SendLiveSubscription(state, isDepth, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			lock (state.SyncRoot)
				(isDepth ? state.DepthSubscriptions : state.MarketSubscriptions).Remove(mdMsg.TransactionId);
			throw;
		}
	}

	private ValueTask SendLiveSubscription(DtcSymbolState state, bool isDepth,
		CancellationToken cancellationToken)
	{
		if (isDepth)
		{
			return new(GetClient().Send(new DtcMarketDepthRequest
			{
				Action = DtcRequestActions.Subscribe,
				SymbolId = state.SymbolId,
				Symbol = state.Symbol,
				Exchange = state.Exchange,
				Levels = MarketDepthLevels,
			}, cancellationToken));
		}

		return new(GetClient().Send(new DtcMarketDataRequest
		{
			Action = DtcRequestActions.Subscribe,
			SymbolId = state.SymbolId,
			Symbol = state.Symbol,
			Exchange = state.Exchange,
			UpdateIntervalMilliseconds = MarketDataTransmissionInterval <= TimeSpan.Zero
				? 0
				: (uint)Math.Min(uint.MaxValue, MarketDataTransmissionInterval.TotalMilliseconds),
		}, cancellationToken));
	}

	private async ValueTask Resubscribe(DtcSymbolState state,
		CancellationToken cancellationToken)
	{
		bool hasMarketData;
		bool hasMarketDepth;
		lock (state.SyncRoot)
		{
			hasMarketData = state.MarketSubscriptions.Count > 0;
			hasMarketDepth = state.DepthSubscriptions.Count > 0;
		}

		if (hasMarketData)
			await SendLiveSubscription(state, false, cancellationToken);
		if (hasMarketDepth)
			await SendLiveSubscription(state, true, cancellationToken);
	}

	private async ValueTask RemoveLiveSubscription(long transactionId,
		CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetAndRemove(transactionId, out var subscription))
			return;
		var state = subscription.Symbol;
		var isDepth = subscription.DataType == DataType.MarketDepth;
		var isLast = false;
		lock (state.SyncRoot)
		{
			var set = isDepth ? state.DepthSubscriptions : state.MarketSubscriptions;
			set.Remove(transactionId);
			isLast = set.Count == 0;
			if (isDepth && isLast)
			{
				state.Bids.Clear();
				state.Asks.Clear();
			}
		}
		if (!isLast)
			return;

		if (isDepth)
		{
			await GetClient().Send(new DtcMarketDepthRequest
			{
				Action = DtcRequestActions.Unsubscribe,
				SymbolId = state.SymbolId,
				Symbol = state.Symbol,
				Exchange = state.Exchange,
				Levels = MarketDepthLevels,
			}, cancellationToken);
		}
		else
		{
			await GetClient().Send(new DtcMarketDataRequest
			{
				Action = DtcRequestActions.Unsubscribe,
				SymbolId = state.SymbolId,
				Symbol = state.Symbol,
				Exchange = state.Exchange,
			}, cancellationToken);
		}
	}

	private async ValueTask RequestHistory(MarketDataMessage mdMsg, int intervalSeconds,
		CancellationToken cancellationToken)
	{
		if (_capabilities?.IsHistoricalPriceDataSupported == false && HistoryAddress?.Equals(Address) == true)
			throw new NotSupportedException("The connected DTC server does not advertise historical-price support.");

		var client = CreateHistoryClient();
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var records = new List<DtcHistoricalPriceRecord>();
		var ticks = new List<DtcHistoricalTickRecord>();
		Exception failure = null;

		async ValueTask OnMessage(DtcMessage message, CancellationToken token)
		{
			switch (message)
			{
				case DtcHistoricalPriceHeader header when header.IsCompressed:
					failure = new InvalidDataException("The DTC history server enabled compression although the request disabled it.");
					completion.TrySetResult();
					break;
				case DtcHistoricalPriceHeader header when header.IsEmpty:
					completion.TrySetResult();
					break;
				case DtcHistoricalPriceRecord record:
					records.Add(record);
					if (record.IsFinal)
						completion.TrySetResult();
					break;
				case DtcHistoricalTickRecord tick:
					ticks.Add(tick);
					if (tick.IsFinal)
						completion.TrySetResult();
					break;
				case DtcHistoricalPriceTrailer:
					completion.TrySetResult();
					break;
				case DtcReject reject when reject.Type == DtcMessageTypes.HistoricalPriceDataReject:
					failure = new InvalidOperationException(reject.Text.IsEmpty(
						$"The DTC history server rejected request {reject.RequestId}."));
					completion.TrySetResult();
					break;
			}
			await ValueTask.CompletedTask;
		}

		ValueTask OnError(Exception error, CancellationToken token)
		{
			failure = error;
			completion.TrySetResult();
			return default;
		}

		client.MessageReceived += OnMessage;
		client.Error += OnError;
		try
		{
			await client.Connect(Login, Password?.UnSecure(), TradeAccount, HeartbeatInterval,
				MarketDataTransmissionInterval, cancellationToken);
			var maxDays = mdMsg.From == null ? 0 : (uint)Math.Clamp(
				(int)Math.Ceiling(((mdMsg.To ?? DateTime.UtcNow).ToUniversalTime() -
					mdMsg.From.Value.ToUniversalTime()).TotalDays), 0, int.MaxValue);
			await client.Send(new DtcHistoricalPriceRequest
			{
				RequestId = 1,
				Symbol = mdMsg.SecurityId.SecurityCode,
				Exchange = mdMsg.SecurityId.BoardCode.EqualsIgnoreCase("DTC")
					? null : mdMsg.SecurityId.BoardCode,
				IntervalSeconds = intervalSeconds,
				From = mdMsg.From?.ToUniversalTime(),
				To = mdMsg.To?.ToUniversalTime(),
				MaxDays = maxDays,
			}, cancellationToken);
			await completion.Task.WaitAsync(cancellationToken);
			if (failure != null)
				throw failure;
		}
		finally
		{
			client.MessageReceived -= OnMessage;
			client.Error -= OnError;
			_historyClients.Remove(client);
			client.Dispose();
		}

		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();
		if (intervalSeconds == 0)
		{
			var items = ticks.Select(tick => new HistoricalItem
			{
				Time = tick.Time,
				Price = tick.Price,
				Volume = tick.Volume,
				AtBidOrAsk = tick.AtBidOrAsk,
			}).Concat(records.Select(record => new HistoricalItem
			{
				Time = record.Time,
				Price = record.Close,
				Volume = record.Volume,
			})).Where(item => (from == null || item.Time >= from) && (to == null || item.Time <= to))
				.OrderBy(item => item.Time).ToArray();
			if (mdMsg.Count is long tickCount && items.LongLength > tickCount)
				items = [.. items.TakeLast((int)Math.Min(tickCount, int.MaxValue))];

			for (var i = 0; i < items.Length; i++)
			{
				var item = items[i];
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = mdMsg.SecurityId,
					TradeStringId = $"DTC-H:{mdMsg.TransactionId}:{i}",
					TradePrice = item.Price,
					TradeVolume = item.Volume,
					ServerTime = item.Time,
					OriginSide = item.AtBidOrAsk switch
					{
						DtcAtBidOrAsks.Bid => Sides.Sell,
						DtcAtBidOrAsks.Ask => Sides.Buy,
						_ => null,
					},
				}, cancellationToken);
			}
			return;
		}

		var candles = records.Where(record => (from == null || record.Time >= from) &&
			(to == null || record.Time <= to)).OrderBy(record => record.Time).ToArray();
		if (mdMsg.Count is long candleCount && candles.LongLength > candleCount)
			candles = [.. candles.TakeLast((int)Math.Min(candleCount, int.MaxValue))];
		var timeFrame = TimeSpan.FromSeconds(intervalSeconds);
		foreach (var record in candles)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				DataType = mdMsg.DataType2,
				OpenTime = record.Time,
				CloseTime = record.Time + timeFrame,
				OpenPrice = record.Open,
				HighPrice = record.High,
				LowPrice = record.Low,
				ClosePrice = record.Close,
				TotalVolume = record.Volume,
				OpenInterest = intervalSeconds >= 86400 ? record.OpenInterestOrTrades : null,
				TotalTicks = intervalSeconds < 86400
					? (int)Math.Min(record.OpenInterestOrTrades, int.MaxValue)
					: null,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessSnapshot(DtcMarketDataSnapshot snapshot,
		CancellationToken cancellationToken)
	{
		if (!_symbolsById.TryGetValue(snapshot.SymbolId, out var state))
			return;
		var serverTime = snapshot.BidAskTime ?? snapshot.LastTime ?? CurrentTime;
		foreach (var subscription in GetSubscriptions(state, DataType.Level1))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.OpenPrice, snapshot.OpenPrice)
			.TryAdd(Level1Fields.HighPrice, snapshot.HighPrice)
			.TryAdd(Level1Fields.LowPrice, snapshot.LowPrice)
			.TryAdd(Level1Fields.SettlementPrice, snapshot.SettlementPrice)
			.TryAdd(Level1Fields.Volume, snapshot.Volume)
			.TryAdd(Level1Fields.TradesCount, snapshot.TradesCount)
			.TryAdd(Level1Fields.OpenInterest, snapshot.OpenInterest)
			.TryAdd(Level1Fields.BestBidPrice, snapshot.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, snapshot.BidVolume)
			.TryAdd(Level1Fields.BestAskPrice, snapshot.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, snapshot.AskVolume)
			.TryAdd(Level1Fields.LastTradePrice, snapshot.LastPrice)
			.TryAdd(Level1Fields.LastTradeVolume, snapshot.LastVolume)
			.TryAdd(Level1Fields.LastTradeTime, snapshot.LastTime)
			.TryAdd(Level1Fields.State, snapshot.TradingStatus.ToStockSharp()), cancellationToken);
		}
	}

	private async ValueTask ProcessFeedStatus(DtcMarketDataFeedStatus status,
		CancellationToken cancellationToken)
	{
		if (status.Status == DtcMarketDataFeedStatuses.Unavailable)
		{
			this.AddWarningLog("The DTC market data feed is unavailable.");
			return;
		}
		if (status.Status != DtcMarketDataFeedStatuses.Available ||
			_capabilities?.IsResubscribeRequired != true)
		{
			return;
		}

		foreach (var state in _symbolsById.SyncGet(symbols => symbols.Values.ToArray()))
			await Resubscribe(state, cancellationToken);
	}

	private ValueTask ProcessSymbolFeedStatus(DtcMarketDataFeedSymbolStatus status,
		CancellationToken cancellationToken)
	{
		if (status.Status == DtcMarketDataFeedStatuses.Unavailable &&
			_symbolsById.TryGetValue(status.SymbolId, out var state))
		{
			this.AddWarningLog("The DTC market data feed is unavailable for {0}.", state.SecurityId);
		}
		return default;
	}

	private ValueTask ProcessTradingStatus(DtcTradingSymbolStatus status,
		CancellationToken cancellationToken)
		=> _symbolsById.TryGetValue(status.SymbolId, out var state)
			? SendSecurityState(state, status.Status.ToStockSharp(), cancellationToken)
			: default;

	private async ValueTask SendSecurityState(DtcSymbolState state, SecurityStates? securityState,
		CancellationToken cancellationToken)
	{
		if (securityState == null)
			return;
		foreach (var subscription in GetSubscriptions(state, DataType.Level1))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = CurrentTime,
			}.Add(Level1Fields.State, securityState.Value), cancellationToken);
		}
	}

	private async ValueTask ProcessTrade(DtcTradeUpdate trade, CancellationToken cancellationToken)
	{
		if (!_symbolsById.TryGetValue(trade.SymbolId, out var state))
			return;
		var time = trade.Time ?? CurrentTime;
		foreach (var subscription in GetSubscriptions(state, DataType.Ticks))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				TradeStringId = $"{state.SymbolId}:{time.Ticks}:{trade.Price}:{trade.Volume}",
				TradePrice = trade.Price,
				TradeVolume = trade.Volume,
				ServerTime = time,
				OriginSide = trade.AtBidOrAsk switch
				{
					DtcAtBidOrAsks.Bid => Sides.Sell,
					DtcAtBidOrAsks.Ask => Sides.Buy,
					_ => null,
				},
			}, cancellationToken);
		}
		foreach (var subscription in GetSubscriptions(state, DataType.Level1))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = time,
			}
			.Add(Level1Fields.LastTradePrice, trade.Price)
			.Add(Level1Fields.LastTradeVolume, trade.Volume)
			.Add(Level1Fields.LastTradeTime, time), cancellationToken);
		}
	}

	private async ValueTask ProcessBidAsk(DtcBidAskUpdate bidAsk,
		CancellationToken cancellationToken)
	{
		if (!_symbolsById.TryGetValue(bidAsk.SymbolId, out var state))
			return;
		foreach (var subscription in GetSubscriptions(state, DataType.Level1))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = bidAsk.Time ?? CurrentTime,
			}
			.TryAdd(Level1Fields.BestBidPrice, bidAsk.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, bidAsk.BidVolume)
			.TryAdd(Level1Fields.BestAskPrice, bidAsk.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, bidAsk.AskVolume), cancellationToken);
		}
	}

	private async ValueTask ProcessSession(DtcSessionUpdate session,
		CancellationToken cancellationToken)
	{
		if (!_symbolsById.TryGetValue(session.SymbolId, out var state))
			return;
		var field = session.Field switch
		{
			DtcSessionUpdateFields.Open => Level1Fields.OpenPrice,
			DtcSessionUpdateFields.High => Level1Fields.HighPrice,
			DtcSessionUpdateFields.Low => Level1Fields.LowPrice,
			DtcSessionUpdateFields.Settlement => Level1Fields.SettlementPrice,
			DtcSessionUpdateFields.Volume => Level1Fields.Volume,
			DtcSessionUpdateFields.OpenInterest => Level1Fields.OpenInterest,
			DtcSessionUpdateFields.TradesCount => Level1Fields.TradesCount,
			_ => (Level1Fields?)null,
		};
		if (field == null || session.Value == null)
			return;
		foreach (var subscription in GetSubscriptions(state, DataType.Level1))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = session.Time ?? CurrentTime,
			}.Add(field.Value, session.Value.Value), cancellationToken);
		}
	}

	private async ValueTask ProcessDepth(DtcDepthUpdate depth, CancellationToken cancellationToken)
	{
		if (!_symbolsById.TryGetValue(depth.SymbolId, out var state) || depth.Side == DtcAtBidOrAsks.Unset)
			return;
		QuoteChange[] bids;
		QuoteChange[] asks;
		lock (state.SyncRoot)
		{
			if (depth.IsSnapshot && depth.IsFirstSnapshot)
			{
				state.Bids.Clear();
				state.Asks.Clear();
			}
			var side = depth.Side == DtcAtBidOrAsks.Bid ? state.Bids : state.Asks;
			if (depth.UpdateType == DtcDepthUpdateTypes.Delete || depth.Volume <= 0)
				side.Remove(depth.Price);
			else
				side[depth.Price] = new(depth.Price, depth.Volume)
				{
					OrdersCount = depth.OrdersCount is long count
						? (int)Math.Min(count, int.MaxValue)
						: null,
				};

			if (depth.FinalUpdate is DtcFinalUpdates.NotFinal or DtcFinalUpdates.BeginBatch)
				return;
			bids = [.. state.Bids.Values.OrderByDescending(quote => quote.Price)];
			asks = [.. state.Asks.Values.OrderBy(quote => quote.Price)];
		}

		foreach (var subscription in GetSubscriptions(state, DataType.MarketDepth))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = depth.Time ?? CurrentTime,
				Bids = bids,
				Asks = asks,
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		}

		if (_capabilities?.IsMarketDepthBestBidAsk == true)
		{
			QuoteChange? bestBid = bids.Length > 0 ? bids[0] : null;
			QuoteChange? bestAsk = asks.Length > 0 ? asks[0] : null;
			foreach (var subscription in GetSubscriptions(state, DataType.Level1))
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = depth.Time ?? CurrentTime,
				}
				.TryAdd(Level1Fields.BestBidPrice, bestBid?.Price)
				.TryAdd(Level1Fields.BestBidVolume, bestBid?.Volume)
				.TryAdd(Level1Fields.BestAskPrice, bestAsk?.Price)
				.TryAdd(Level1Fields.BestAskVolume, bestAsk?.Volume), cancellationToken);
			}
		}
	}

	private MarketSubscription[] GetSubscriptions(DtcSymbolState state, DataType dataType)
		=> _marketSubscriptions.CachedValues.Where(subscription =>
			subscription.Symbol == state && subscription.DataType == dataType).ToArray();
}

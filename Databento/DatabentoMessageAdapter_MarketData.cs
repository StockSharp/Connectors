namespace StockSharp.Databento;

using StockSharp.Databento.Native;

public partial class DatabentoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var requestedTypes = lookupMsg.GetSecurityTypes();
		var symbol = lookupMsg.SecurityId.SecurityCode;
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		var start = DateTime.UtcNow.Date.AddDays(-1);
		while (start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
			start = start.AddDays(-1);
		var end = start.AddDays(1);
		var canApplyServerLimit = requestedTypes.Count == 0 &&
			lookupMsg.SecurityId.BoardCode.IsEmpty() && lookupMsg.Name.IsEmpty() &&
			lookupMsg.ShortName.IsEmpty() && lookupMsg.OptionType == null &&
			lookupMsg.Strike == null && lookupMsg.ExpiryDate == null &&
			lookupMsg.Currency == null && lookupMsg.Class.IsEmpty();
		var request = new DatabentoHistoricalRequest
		{
			Dataset = Dataset,
			Schema = "definition",
			Symbology = symbol.IsEmpty() ? "raw_symbol" : Symbology.ToApi(),
			Symbols = [symbol.IsEmpty("ALL_SYMBOLS")],
			Start = start,
			End = end,
			Limit = canApplyServerLimit && lookupMsg.Count is > 0
				? checked(lookupMsg.Count.Value + skip)
				: null,
		};

		var sent = new HashSet<SecurityId>();
		await foreach (var record in GetHistoricalClient().GetRange(request, cancellationToken))
		{
			if (record is not DbnInstrumentDefinitionRecord definition ||
				definition.RawSymbol.IsEmpty() || definition.SecurityUpdateAction == (byte)'D')
			{
				continue;
			}

			var security = CreateSecurity(definition, lookupMsg.TransactionId);
			if (!sent.Add(security.SecurityId) || !security.IsMatch(lookupMsg, requestedTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}

			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Level1, "mbp-1", null, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Ticks, "trades", null, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.MarketDepth, "mbp-10", null, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnOrderLogSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.OrderLog, "mbo", null, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var timeFrame = mdMsg.GetTimeFrame();
		return ProcessMarketSubscription(mdMsg, mdMsg.DataType2, timeFrame.ToCandleSchema(),
			timeFrame, cancellationToken);
	}

	private async ValueTask ProcessMarketSubscription(MarketDataMessage mdMsg, DataType dataType,
		string schema, TimeSpan? timeFrame, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			RemoveLiveSubscription(mdMsg.OriginalTransactionId);
			return;
		}

		var securityId = NormalizeSecurityId(mdMsg.SecurityId);
		if (mdMsg.From != null || mdMsg.To != null || mdMsg.Count != null || mdMsg.IsHistoryOnly())
			await RequestHistory(mdMsg, securityId, schema, timeFrame, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var liveSubscription = new DatabentoLiveSubscription
		{
			Schema = schema,
			Symbology = Symbology.ToApi(),
			Symbol = securityId.SecurityCode,
			IsSnapshot = dataType == DataType.OrderLog,
		};
		var state = new MarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			RequestedSecurityId = securityId,
			DataType = dataType,
			Schema = schema,
			TimeFrame = timeFrame,
			LiveKey = liveSubscription.Key,
		};
		if (Symbology == DatabentoSymbologyTypes.InstrumentId &&
			uint.TryParse(securityId.SecurityCode, NumberStyles.None, CultureInfo.InvariantCulture,
				out var instrumentId))
		{
			state.Bind(instrumentId);
			_instrumentSecurities[instrumentId] = securityId;
		}
		BindKnownInstruments(state);

		_marketSubscriptions.Add(state.TransactionId, state);
		var isFirst = false;
		lock (_liveGroupsSync)
		{
			if (!_liveGroups.TryGetValue(state.LiveKey, out var transactions))
			{
				transactions = [];
				_liveGroups.Add(state.LiveKey, transactions);
				isFirst = true;
			}
			transactions.Add(state.TransactionId);
		}

		try
		{
			if (isFirst)
				await (await GetLiveClient(cancellationToken)).Subscribe(liveSubscription, cancellationToken);
		}
		catch
		{
			RemoveLiveSubscription(state.TransactionId);
			throw;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private void RemoveLiveSubscription(long transactionId)
	{
		ClearMboState(transactionId);
		if (!_marketSubscriptions.TryGetAndRemove(transactionId, out var subscription))
			return;

		var isLast = false;
		lock (_liveGroupsSync)
		{
			if (_liveGroups.TryGetValue(subscription.LiveKey, out var transactions))
			{
				transactions.Remove(transactionId);
				if (transactions.Count == 0)
				{
					_liveGroups.Remove(subscription.LiveKey);
					isLast = true;
				}
			}
		}
		if (isLast)
			_liveClient?.Unsubscribe(subscription.LiveKey);
	}

	private async Task RequestHistory(MarketDataMessage mdMsg, SecurityId securityId,
		string schema, TimeSpan? timeFrame, CancellationToken cancellationToken)
	{
		var to = mdMsg.To ?? DateTime.UtcNow;
		var from = mdMsg.From ?? GetDefaultHistoryStart(to, mdMsg.Count, timeFrame);
		if (from >= to)
			return;

		var request = new DatabentoHistoricalRequest
		{
			Dataset = Dataset,
			Schema = schema,
			Symbology = Symbology.ToApi(),
			Symbols = [securityId.SecurityCode],
			Start = from,
			End = to,
			Limit = mdMsg.Count is > 0 ? mdMsg.Count : null,
		};
		var left = mdMsg.Count ?? long.MaxValue;
		try
		{
			await foreach (var record in GetHistoricalClient().GetRange(request, cancellationToken))
			{
				var output = CreateMarketDataMessage(mdMsg.TransactionId, securityId, timeFrame, record);
				if (output == null)
					continue;
				await SendOutMessageAsync(output, cancellationToken);
				if (--left <= 0)
					break;
			}
		}
		finally
		{
			ClearMboState(mdMsg.TransactionId);
		}
	}

	private async ValueTask ProcessLiveRecord(DbnRecord record,
		CancellationToken cancellationToken)
	{
		switch (record)
		{
			case DbnSymbolMappingRecord mapping:
				ProcessSymbolMapping(mapping);
				return;
			case DbnErrorRecord error:
				await SendOutErrorAsync(new InvalidOperationException(
					$"Databento live error {error.Code}: {error.Message}"), cancellationToken);
				return;
			case DbnSystemRecord system:
				if (system.Code == DbnSystemCodes.SlowReaderWarning)
					this.AddWarningLog("Databento slow-reader warning: {0}", system.Message);
				return;
		}

		var subscriptions = _marketSubscriptions.CachedValues
			.Where(subscription => subscription.IsBound(record.Header.InstrumentId) &&
				IsRecordMatch(subscription, record))
			.ToArray();
		if (subscriptions.Length == 0)
			return;

		foreach (var subscription in subscriptions)
		{
			var securityId = _instrumentSecurities.TryGetValue(record.Header.InstrumentId,
				out var mapped) ? mapped : subscription.RequestedSecurityId;
			var output = CreateMarketDataMessage(subscription.TransactionId, securityId,
				subscription.TimeFrame, record);
			if (output != null)
				await SendOutMessageAsync(output, cancellationToken);
		}
	}

	private void ProcessSymbolMapping(DbnSymbolMappingRecord mapping)
	{
		var securityId = new SecurityId
		{
			SecurityCode = mapping.OutputSymbol.IsEmpty(mapping.InputSymbol),
			BoardCode = Dataset,
			Native = mapping.Header.InstrumentId,
		};
		_instrumentSecurities[mapping.Header.InstrumentId] = securityId;
		AddSymbolMapping(mapping.InputSymbol, mapping.Header.InstrumentId);
		AddSymbolMapping(mapping.OutputSymbol, mapping.Header.InstrumentId);

		foreach (var subscription in _marketSubscriptions.CachedValues)
		{
			var code = subscription.RequestedSecurityId.SecurityCode;
			if (code.EqualsIgnoreCase(mapping.InputSymbol) || code.EqualsIgnoreCase(mapping.OutputSymbol) ||
				Symbology == DatabentoSymbologyTypes.InstrumentId &&
				code.Equals(mapping.Header.InstrumentId.ToString(CultureInfo.InvariantCulture),
					StringComparison.Ordinal))
			{
				subscription.Bind(mapping.Header.InstrumentId);
			}
		}
	}

	private void AddSymbolMapping(string symbol, uint instrumentId)
	{
		if (symbol.IsEmpty())
			return;
		lock (_symbolMappingsSync)
		{
			if (!_symbolMappings.TryGetValue(symbol, out var instruments))
				_symbolMappings.Add(symbol, instruments = []);
			instruments.Add(instrumentId);
		}
	}

	private void BindKnownInstruments(MarketSubscription subscription)
	{
		uint[] instruments;
		lock (_symbolMappingsSync)
		{
			instruments = _symbolMappings.TryGetValue(subscription.RequestedSecurityId.SecurityCode,
				out var mapped) ? mapped.ToArray() : [];
		}
		foreach (var instrumentId in instruments)
			subscription.Bind(instrumentId);
	}

	private static bool IsRecordMatch(MarketSubscription subscription, DbnRecord record)
		=> subscription.DataType == DataType.Level1 && record is DbnMbp1Record or DbnStatusRecord or DbnStatisticsRecord ||
			subscription.DataType == DataType.Ticks && record is DbnTradeRecord ||
			subscription.DataType == DataType.MarketDepth && record is DbnMbp10Record ||
			subscription.DataType == DataType.OrderLog && record is DbnMboRecord ||
			subscription.TimeFrame != null && record is DbnOhlcvRecord candle &&
				candle.Header.Type.ToTimeFrame() == subscription.TimeFrame;

	private Message CreateMarketDataMessage(long transactionId, SecurityId securityId,
		TimeSpan? timeFrame, DbnRecord record)
		=> record switch
		{
			DbnMbp1Record level1 => CreateLevel1(transactionId, securityId, level1),
			DbnStatisticsRecord statistics => CreateLevel1(transactionId, securityId, statistics),
			DbnStatusRecord status => CreateLevel1(transactionId, securityId, status),
			DbnTradeRecord trade => CreateTick(transactionId, securityId, trade),
			DbnMbp10Record depth => CreateDepth(transactionId, securityId, depth),
			DbnMboRecord order => CreateOrderLog(transactionId, securityId, order),
			DbnOhlcvRecord candle when timeFrame != null =>
				CreateCandle(transactionId, securityId, timeFrame.Value, candle),
			_ => null,
		};

	private static Level1ChangeMessage CreateLevel1(long transactionId, SecurityId securityId,
		DbnMbp1Record record)
	{
		var time = record.ReceiveTimestamp.ToUtc();
		var result = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
			SeqNum = record.Sequence,
		};
		result
			.TryAdd(Level1Fields.BestBidPrice, record.Level.BidPrice.ToPrice())
			.TryAdd(Level1Fields.BestBidVolume, ToVolume(record.Level.BidSize))
			.TryAdd(Level1Fields.BestBidTime, time)
			.TryAdd(Level1Fields.BestAskPrice, record.Level.AskPrice.ToPrice())
			.TryAdd(Level1Fields.BestAskVolume, ToVolume(record.Level.AskSize))
			.TryAdd(Level1Fields.BestAskTime, time);
		if (record.Action == (byte)'T')
		{
			result
				.TryAdd(Level1Fields.LastTradePrice, record.Price.ToPrice())
				.TryAdd(Level1Fields.LastTradeVolume, ToVolume(record.Size))
				.TryAdd(Level1Fields.LastTradeTime, time)
				.TryAdd(Level1Fields.LastTradeOrigin, record.Side.ToSide());
		}
		return result;
	}

	private static Level1ChangeMessage CreateLevel1(long transactionId, SecurityId securityId,
		DbnStatisticsRecord record)
	{
		var result = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = record.ReceiveTimestamp.ToUtc(),
			SeqNum = record.Sequence,
		};
		var price = record.Price.ToPrice();
		var quantity = record.Quantity == long.MaxValue ? null : (decimal?)record.Quantity;
		switch (record.StatisticType)
		{
			case DbnStatisticTypes.OpeningPrice:
				result.TryAdd(Level1Fields.OpenPrice, price);
				break;
			case DbnStatisticTypes.SettlementPrice:
				result.TryAdd(Level1Fields.SettlementPrice, price);
				break;
			case DbnStatisticTypes.TradingSessionLowPrice:
				result.TryAdd(Level1Fields.LowPrice, price);
				break;
			case DbnStatisticTypes.TradingSessionHighPrice:
				result.TryAdd(Level1Fields.HighPrice, price);
				break;
			case DbnStatisticTypes.ClearedVolume:
				result.TryAdd(Level1Fields.Volume, quantity);
				break;
			case DbnStatisticTypes.OpenInterest:
				result.TryAdd(Level1Fields.OpenInterest, quantity);
				break;
			case DbnStatisticTypes.ClosePrice:
				result.TryAdd(Level1Fields.ClosePrice, price);
				break;
			case DbnStatisticTypes.UpperPriceLimit:
				result.TryAdd(Level1Fields.MaxPrice, price);
				break;
			case DbnStatisticTypes.LowerPriceLimit:
				result.TryAdd(Level1Fields.MinPrice, price);
				break;
		}
		return result;
	}

	private static Level1ChangeMessage CreateLevel1(long transactionId, SecurityId securityId,
		DbnStatusRecord record)
		=> new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = record.ReceiveTimestamp.ToUtc(),
		}.TryAdd(Level1Fields.State, record.Action.ToSecurityState());

	private static ExecutionMessage CreateTick(long transactionId, SecurityId securityId,
		DbnTradeRecord record)
		=> new()
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = record.ReceiveTimestamp.ToUtc(),
			TradeStringId = $"{record.Header.PublisherId}:{record.Header.InstrumentId}:{record.Sequence}",
			TradePrice = record.Price.ToPrice(),
			TradeVolume = ToVolume(record.Size),
			OriginSide = record.Side.ToSide(),
			SeqNum = record.Sequence,
			TradeStatus = record.Flags,
		};

	private static QuoteChangeMessage CreateDepth(long transactionId, SecurityId securityId,
		DbnMbp10Record record)
		=> new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = record.ReceiveTimestamp.ToUtc(),
			SeqNum = record.Sequence,
			Bids = record.Levels
				.Where(level => level.BidPrice != long.MaxValue && level.BidSize is > 0 and < uint.MaxValue)
				.Select(level => new QuoteChange(level.BidPrice.ToPrice().Value, level.BidSize,
					(int)Math.Min(int.MaxValue, level.BidCount)))
				.ToArray(),
			Asks = record.Levels
				.Where(level => level.AskPrice != long.MaxValue && level.AskSize is > 0 and < uint.MaxValue)
				.Select(level => new QuoteChange(level.AskPrice.ToPrice().Value, level.AskSize,
					(int)Math.Min(int.MaxValue, level.AskCount)))
				.ToArray(),
			State = QuoteChangeStates.SnapshotComplete,
		};

	private ExecutionMessage CreateOrderLog(long transactionId, SecurityId securityId,
		DbnMboRecord record)
	{
		const byte mbpFlag = 0x10;
		const byte topOfBookFlag = 0x40;

		var orderKey = (record.Flags & topOfBookFlag) != 0
			? $"T:{record.ChannelId}:{(char)record.Side}"
			: (record.Flags & mbpFlag) != 0
				? $"L:{record.ChannelId}:{(char)record.Side}:{record.Price}"
				: record.OrderId.ToString(CultureInfo.InvariantCulture);
		var stateKey = (transactionId, record.Header.InstrumentId, orderKey);
		var isTrade = record.Action is (byte)'T' or (byte)'F';
		var price = record.Price.ToPrice();
		var size = ToVolume(record.Size);
		decimal orderVolume;
		decimal balance;
		Sides side;
		OrderStates orderState;

		lock (_mboSync)
		{
			_mboOrders.TryGetValue(stateKey, out var previous);
			price ??= previous?.Price;
			side = record.Side.ToSide() ?? previous?.Side ?? default;

			switch (record.Action)
			{
				case (byte)'A':
				case (byte)'M':
				{
					orderVolume = size ?? previous?.Size ?? 0;
					balance = orderVolume;
					orderState = balance > 0 ? OrderStates.Active : OrderStates.Done;
					if (orderState == OrderStates.Active)
					{
						_mboOrders[stateKey] = new()
						{
							Price = price ?? 0,
							Size = balance,
							Side = side,
						};
					}
					else
						_mboOrders.Remove(stateKey);
					break;
				}
				case (byte)'C':
				{
					orderVolume = previous?.Size ?? size ?? 0;
					balance = previous == null || size == null
						? 0
						: Math.Max(0, previous.Size - size.Value);
					orderState = balance > 0 ? OrderStates.Active : OrderStates.Done;
					if (orderState == OrderStates.Active)
					{
						_mboOrders[stateKey] = new()
						{
							Price = price ?? 0,
							Size = balance,
							Side = side,
						};
					}
					else
						_mboOrders.Remove(stateKey);
					break;
				}
				case (byte)'R':
					ClearMboStateUnsafe(transactionId, record.Header.InstrumentId);
					orderKey = "CLEAR";
					orderVolume = 0;
					balance = 0;
					orderState = OrderStates.Done;
					break;
				default:
					orderVolume = previous?.Size ?? size ?? 0;
					balance = previous?.Size ?? 0;
					orderState = OrderStates.Active;
					break;
			}
		}

		return new()
		{
			DataTypeEx = DataType.OrderLog,
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = record.ReceiveTimestamp.ToUtc(),
			OrderStringId = orderKey,
			OrderPrice = price ?? 0,
			OrderVolume = orderVolume,
			Balance = balance,
			Side = side,
			OrderState = orderState,
			TradeStringId = isTrade
				? $"{record.Header.PublisherId}:{record.Header.InstrumentId}:{record.Sequence}"
				: null,
			TradePrice = isTrade ? record.Price.ToPrice() : null,
			TradeVolume = isTrade ? ToVolume(record.Size) : null,
			SeqNum = record.Sequence,
			OrderStatus = record.Flags,
		};
	}

	private void ClearMboState(long transactionId)
	{
		lock (_mboSync)
		{
			foreach (var key in _mboOrders.Keys
				.Where(key => key.transactionId == transactionId)
				.ToArray())
			{
				_mboOrders.Remove(key);
			}
		}
	}

	private void ClearMboStateUnsafe(long transactionId, uint instrumentId)
	{
		foreach (var key in _mboOrders.Keys
			.Where(key => key.transactionId == transactionId && key.instrumentId == instrumentId)
			.ToArray())
		{
			_mboOrders.Remove(key);
		}
	}

	private static TimeFrameCandleMessage CreateCandle(long transactionId, SecurityId securityId,
		TimeSpan timeFrame, DbnOhlcvRecord record)
	{
		var openTime = record.Header.EventTimestamp.ToUtc();
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataType = timeFrame.TimeFrame(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = record.Open.ToPrice() ?? 0,
			HighPrice = record.High.ToPrice() ?? 0,
			LowPrice = record.Low.ToPrice() ?? 0,
			ClosePrice = record.Close.ToPrice() ?? 0,
			TotalVolume = record.Volume == ulong.MaxValue ? 0 : record.Volume,
			State = CandleStates.Finished,
		};
	}

	private SecurityMessage CreateSecurity(DbnInstrumentDefinitionRecord definition,
		long transactionId)
	{
		var boardCode = definition.Exchange.IsEmpty(Dataset);
		var securityType = definition.InstrumentClass.ToSecurityType();
		var multiplier = definition.UnitOfMeasureQuantity.ToPrice();
		if (multiplier is not > 0)
			multiplier = definition.ContractMultiplier > 0
				? definition.ContractMultiplier
				: definition.OriginalContractSize > 0 ? definition.OriginalContractSize : null;

		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = new()
			{
				SecurityCode = definition.RawSymbol,
				BoardCode = boardCode,
				Native = definition.Header.InstrumentId,
			},
			SecurityType = securityType,
			ShortName = definition.RawSymbol,
			Class = definition.Asset.IsEmpty(definition.Group),
			Currency = definition.Currency.ToCurrency(),
			PriceStep = PositivePrice(definition.MinimumPriceIncrement),
			VolumeStep = definition.MinimumRoundLotSize > 0
				? definition.MinimumRoundLotSize
				: definition.MinimumLotSize > 0 ? definition.MinimumLotSize : null,
			Multiplier = multiplier,
			ExpiryDate = definition.Expiration == ulong.MaxValue ? null : definition.Expiration.ToUtc(),
			Strike = securityType == SecurityTypes.Option ? definition.StrikePrice.ToPrice() : null,
			OptionType = definition.InstrumentClass.ToOptionType(),
			UnderlyingSecurityId = definition.Underlying.IsEmpty()
				? default
				: new SecurityId
				{
					SecurityCode = definition.Underlying,
					BoardCode = boardCode,
					Native = definition.UnderlyingId,
				},
		};
	}

	private SecurityId NormalizeSecurityId(SecurityId securityId)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		return new()
		{
			SecurityCode = code,
			BoardCode = securityId.BoardCode.IsEmpty(Dataset),
			Native = securityId.Native,
		};
	}

	private static DateTime GetDefaultHistoryStart(DateTime to, long? count,
		TimeSpan? timeFrame)
	{
		if (count is > 0 && timeFrame is { } interval && interval > TimeSpan.Zero)
		{
			var intervals = Math.Min(count.Value, 1_000_000L);
			var ticks = intervals > long.MaxValue / interval.Ticks
				? long.MaxValue
				: intervals * interval.Ticks;
			return to - TimeSpan.FromTicks(Math.Min(ticks, TimeSpan.FromDays(3650).Ticks));
		}
		return to.AddDays(-1);
	}

	private static decimal? ToVolume(uint value)
		=> value == uint.MaxValue ? null : value;

	private static decimal? PositivePrice(long value)
	{
		var price = value.ToPrice();
		return price is > 0 ? price : null;
	}
}

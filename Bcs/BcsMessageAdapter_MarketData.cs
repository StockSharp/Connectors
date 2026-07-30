namespace StockSharp.Bcs;

public partial class BcsMessageAdapter
{
	private static readonly string[] _allInstrumentTypes =
	[
		"CURRENCY", "STOCK", "FOREIGN_STOCK", "BONDS", "NOTES",
		"DEPOSITARY_RECEIPTS", "EURO_BONDS", "MUTUAL_FUNDS", "ETF",
		"FUTURES", "OPTIONS", "GOODS", "INDICES",
	];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		var securityTypes = lookupMsg.GetSecurityTypes();
		var nativeTypes = securityTypes.ToNative();
		if (nativeTypes.Length == 0)
			nativeTypes = _allInstrumentTypes;

		var left = lookupMsg.Count ?? long.MaxValue;
		const int pageSize = 100;
		var query = lookupMsg.SecurityId.SecurityCode;

		if (!query.IsEmpty())
		{
			for (var page = 0; left > 0; page++)
			{
				var instruments = await _rest.GetInstrumentsByTickers(
					[query], page, pageSize, cancellationToken) ?? [];

				foreach (var instrument in instruments)
				{
					left = await SendInstrument(instrument, lookupMsg,
						securityTypes, left, cancellationToken);
					if (left <= 0)
						break;
				}

				if (instruments.Length < pageSize)
					break;
			}
		}
		else
		{
			foreach (var nativeType in nativeTypes)
			{
				for (var page = 0; left > 0; page++)
				{
					var instruments = await _rest.GetInstrumentsByType(
						nativeType, page, pageSize, cancellationToken) ?? [];

					foreach (var instrument in instruments)
					{
						left = await SendInstrument(instrument, lookupMsg,
							securityTypes, left, cancellationToken);
						if (left <= 0)
							break;
					}

					if (instruments.Length < pageSize)
						break;
				}

				if (left <= 0)
					break;
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask<long> SendInstrument(BcsInstrument instrument,
		SecurityLookupMessage lookupMsg, HashSet<SecurityTypes> securityTypes,
		long left, CancellationToken cancellationToken)
	{
		if (instrument?.Ticker.IsEmpty() != false)
			return left;

		var type = instrument.InstrumentType
			.IsEmpty(instrument.Type).ToSecurityType();
		var boards = instrument.Boards ?? [];
		if (boards.Length == 0)
		{
			boards =
			[
				new()
				{
					ClassCode = instrument.PrimaryBoard.IsEmpty("MOEX"),
					Exchange = "MOEX",
				},
			];
		}

		foreach (var board in boards)
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(board.ClassCode))
				continue;

			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = new()
				{
					SecurityCode = instrument.Ticker,
					BoardCode = board.ClassCode.IsEmpty("MOEX"),
					Isin = instrument.Isin,
				},
				Name = instrument.DisplayName,
				ShortName = instrument.ShortName,
				SecurityType = type,
				Currency = instrument.TradingCurrency.ToCurrency(),
				FaceValue = instrument.FaceValue,
				PriceStep = instrument.MinimumStep,
				Decimals = instrument.Scale,
				VolumeStep = 1,
				MinVolume = instrument.LotSize,
				ExpiryDate = instrument.MaturityDate,
				SettlementDate = instrument.SettlementDate,
				Strike = instrument.Strike,
				CfiCode = instrument.Cfi,
			};

			if (!instrument.UnderlyingTicker.IsEmpty())
				security.UnderlyingSecurityId =
					instrument.UnderlyingTicker.ToSecurityId(
						instrument.UnderlyingClassCode);
			else if (!instrument.BaseAsset.IsEmpty())
				security.TryFillUnderlyingId(instrument.BaseAsset);

			if (type == SecurityTypes.Option &&
				instrument.Cfi?.Length > 1)
			{
				security.OptionType =
					char.ToUpperInvariant(instrument.Cfi[1]) == 'C'
						? OptionTypes.Call : OptionTypes.Put;
			}

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		return left;
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);

		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscription(message.OriginalTransactionId,
				cancellationToken);
			return;
		}

		var native = CreateNative(message.SecurityId, 3);

		foreach (var quote in await _rest.GetQuotes(
			[ToInstrument(message.SecurityId)], cancellationToken))
		{
			await SendQuote(quote, message.TransactionId, message.SecurityId,
				cancellationToken);
		}

		if (!message.IsHistoryOnly())
			await AddLiveSubscription(message, native, null, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);

		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscription(message.OriginalTransactionId,
				cancellationToken);
			return;
		}

		var depth = Math.Clamp(message.MaxDepth ?? 20, 1, 20);
		var native = CreateNative(message.SecurityId, 0, depth: depth);
		var book = await _rest.GetOrderBook(message.SecurityId.SecurityCode,
			message.SecurityId.BoardCode, depth, cancellationToken);
		await SendBook(book, message.TransactionId, message.SecurityId,
			cancellationToken);

		if (!message.IsHistoryOnly())
			await AddLiveSubscription(message, native, null, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);

		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscription(message.OriginalTransactionId,
				cancellationToken);
			return;
		}

		if (message.From is not null || message.To is not null ||
			message.Count is not null || message.IsHistoryOnly())
		{
			var trades = await _rest.GetLastTrades(new()
			{
				Ticker = message.SecurityId.SecurityCode,
				ClassCode = message.SecurityId.BoardCode,
				From = message.From?.ToUniversalTime(),
				To = message.To?.ToUniversalTime(),
			}, cancellationToken);

			IEnumerable<BcsTrade> selected = trades.OrderBy(t => t.DateTime);
			if (message.Count is > 0 and <= int.MaxValue)
				selected = selected.TakeLast((int)message.Count.Value);

			foreach (var trade in selected)
				await SendPublicTrade(trade, message.TransactionId,
					message.SecurityId, cancellationToken);
		}

		if (!message.IsHistoryOnly())
			await AddLiveSubscription(message,
				CreateNative(message.SecurityId, 2), null, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);

		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscription(message.OriginalTransactionId,
				cancellationToken);
			return;
		}

		var timeFrame = message.GetTimeFrame();
		var nativeTimeFrame = timeFrame.ToNative();
		var to = (message.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = message.From?.ToUniversalTime();
		if (from is null && (message.Count is > 0 || message.IsHistoryOnly()))
		{
			var count = Math.Clamp(message.Count ?? 1440, 1, 1440);
			from = to - timeFrame.Multiply(count);
		}

		if (from is not null)
		{
			var cursor = from.Value;
			var emitted = new HashSet<DateTime>();
			var left = message.Count ?? long.MaxValue;

			while (cursor <= to && left > 0)
			{
				var chunkEnd = cursor + timeFrame.Multiply(1439);
				if (chunkEnd > to)
					chunkEnd = to;

				var response = await _rest.GetCandles(
					message.SecurityId.SecurityCode,
					message.SecurityId.BoardCode,
					cursor, chunkEnd, nativeTimeFrame, cancellationToken);

				foreach (var candle in (response?.Bars ?? [])
					.OrderBy(c => c.Time))
				{
					if (candle.Time < from || candle.Time > to ||
						!emitted.Add(candle.Time))
						continue;

					await SendCandle(candle, message.TransactionId,
						message.SecurityId, timeFrame, CandleStates.Finished,
						cancellationToken);
					if (--left <= 0)
						break;
				}

				if (chunkEnd >= to)
					break;
				cursor = chunkEnd + timeFrame;
			}
		}

		if (!message.IsHistoryOnly())
		{
			await AddLiveSubscription(message,
				CreateNative(message.SecurityId, 1,
					timeFrame: nativeTimeFrame),
				timeFrame, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask AddLiveSubscription(MarketDataMessage message,
		BcsMarketSubscription native, TimeSpan? timeFrame,
		CancellationToken cancellationToken)
	{
		var isFirst = !_marketSubscriptions.CachedValues
			.Any(s => s.Native == native);
		_marketSubscriptions.Add(message.TransactionId, new()
		{
			TransactionId = message.TransactionId,
			SecurityId = message.SecurityId,
			Native = native,
			TimeFrame = timeFrame,
		});

		try
		{
			if (isFirst)
				await _socket.Subscribe(native, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(message.TransactionId);
			throw;
		}
	}

	private async ValueTask RemoveLiveSubscription(long transactionId,
		CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetAndRemove(transactionId,
			out var subscription))
			return;

		if (!_marketSubscriptions.CachedValues
			.Any(s => s.Native == subscription.Native))
		{
			await _socket.Unsubscribe(subscription.Native, cancellationToken);
		}
	}

	private async ValueTask ProcessQuote(BcsQuote quote,
		CancellationToken cancellationToken)
	{
		if (quote is null)
			return;

		foreach (var subscription in FindSubscriptions(
			3, quote.Ticker, quote.ClassCode))
		{
			await SendQuote(quote, subscription.TransactionId,
				subscription.SecurityId, cancellationToken);
		}
	}

	private ValueTask SendQuote(BcsQuote quote, long transactionId,
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (quote is null)
			return default;

		SecurityStates? state = quote.SecurityTradingStatus is null ? null :
			quote.SecurityTradingStatus is 17 or 101 or 102 or 103 or 104
				? SecurityStates.Trading : SecurityStates.Stoped;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = quote.DateTime == default
				? DateTime.UtcNow : quote.DateTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.Bid)
		.TryAdd(Level1Fields.BestAskPrice, quote.Offer)
		.TryAdd(Level1Fields.OpenPrice, quote.Open)
		.TryAdd(Level1Fields.ClosePrice, quote.Close)
		.TryAdd(Level1Fields.HighPrice, quote.High)
		.TryAdd(Level1Fields.LowPrice, quote.Low)
		.TryAdd(Level1Fields.LastTradePrice, quote.Last)
		.TryAdd(Level1Fields.TheorPrice, quote.TheoreticalPrice)
		.TryAdd(Level1Fields.Yield, quote.OfferYield ?? quote.BidYield)
		.TryAdd(Level1Fields.Change, quote.Change)
		.TryAdd(Level1Fields.State, state), cancellationToken);
	}

	private async ValueTask ProcessOrderBook(BcsOrderBook book,
		CancellationToken cancellationToken)
	{
		if (book is null)
			return;

		foreach (var subscription in FindSubscriptions(
			0, book.Ticker, book.ClassCode))
		{
			await SendBook(book, subscription.TransactionId,
				subscription.SecurityId, cancellationToken);
		}
	}

	private ValueTask SendBook(BcsOrderBook book, long transactionId,
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (book is null)
			return default;
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = book.DateTime == default
				? DateTime.UtcNow : book.DateTime,
			Bids = (book.Bids ?? [])
				.Select(q => new QuoteChange(q.Price, q.Quantity)).ToArray(),
			Asks = (book.Asks ?? [])
				.Select(q => new QuoteChange(q.Price, q.Quantity)).ToArray(),
		}, cancellationToken);
	}

	private async ValueTask ProcessPublicTrade(BcsTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade is null)
			return;

		foreach (var subscription in FindSubscriptions(
			2, trade.Ticker, trade.ClassCode))
		{
			await SendPublicTrade(trade, subscription.TransactionId,
				subscription.SecurityId, cancellationToken);
		}
	}

	private ValueTask SendPublicTrade(BcsTrade trade, long transactionId,
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (trade is null)
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = trade.DateTime == default
				? DateTime.UtcNow : trade.DateTime,
			TradeStringId =
				$"{trade.DateTime:O}:{trade.Price}:{trade.Quantity}:{trade.Side}",
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			OriginSide = trade.Side.ToSide(),
		}, cancellationToken);
	}

	private async ValueTask ProcessCandle(BcsCandle candle,
		CancellationToken cancellationToken)
	{
		if (candle is null)
			return;

		foreach (var subscription in FindSubscriptions(
			1, candle.Ticker, candle.ClassCode)
			.Where(s => s.Native.TimeFrame.EqualsIgnoreCase(candle.TimeFrame)))
		{
			await SendCandle(candle, subscription.TransactionId,
				subscription.SecurityId, subscription.TimeFrame.Value,
				CandleStates.Active, cancellationToken);
		}
	}

	private ValueTask SendCandle(BcsCandle candle, long transactionId,
		SecurityId securityId, TimeSpan timeFrame, CandleStates state,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			OpenTime = candle.Time,
			CloseTime = candle.Time + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			State = state,
		}, cancellationToken);

	private MarketSubscription[] FindSubscriptions(int dataType,
		string ticker, string classCode)
		=> _marketSubscriptions.CachedValues
			.Where(s => s.Native.DataType == dataType &&
				s.Native.Ticker.EqualsIgnoreCase(ticker) &&
				s.Native.ClassCode.EqualsIgnoreCase(classCode))
			.ToArray();

	private static BcsMarketSubscription CreateNative(SecurityId securityId,
		int dataType, string timeFrame = null, int depth = 20)
		=> new(dataType,
			securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode))
				.Trim().ToUpperInvariant(),
			securityId.BoardCode.ThrowIfEmpty(nameof(securityId.BoardCode))
				.Trim().ToUpperInvariant(),
			timeFrame,
			depth);

	private static BcsInstrumentKey ToInstrument(SecurityId securityId)
		=> new()
		{
			Ticker = securityId.SecurityCode,
			ClassCode = securityId.BoardCode,
		};
}

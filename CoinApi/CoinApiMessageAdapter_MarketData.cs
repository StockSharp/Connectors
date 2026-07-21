namespace StockSharp.CoinApi;

public partial class CoinApiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var securityTypes = message.GetSecurityTypes();
		if (!message.SecurityId.BoardCode.IsEmpty() &&
			!message.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinApi))
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		var left = Math.Min(message.Count ?? MaximumItems, MaximumItems);
		if (left <= 0)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		var value = (message.SecurityId.Native as string)
			.IsEmpty(message.SecurityId.SecurityCode).IsEmpty(message.Name)?.Trim();
		var downloadLimit = value.IsEmpty()
			? MaximumItems
			: Math.Min(MaximumItems, 5000);
		var symbols = await SafeRest().GetSymbolsAsync(ExchangeFilter, value,
			AssetFilter, downloadLimit, cancellationToken);
		CacheSymbols(symbols);
		var skip = Math.Max(0L, message.Skip ?? 0);

		foreach (var symbol in symbols.Where(IsValidSymbol)
			.OrderByDescending(static item => item.VolumeOneDayUsd ?? -1)
			.ThenBy(static item => item.SymbolId,
				StringComparer.OrdinalIgnoreCase))
		{
			if (!Matches(symbol, value))
				continue;
			var security = ToSecurityMessage(symbol, message.TransactionId);
			if (!security.IsMatch(message, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			await SendOutMessageAsync(security, cancellationToken);
			if (--left == 0)
				break;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var symbol = await ResolveSymbolAsync(message.SecurityId,
			cancellationToken);
		var securityId = ToSecurityId(symbol);
		var remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var trades = await SafeRest().GetTradesAsync(symbol.SymbolId, from,
				to, GetHistoryLimit(remaining), cancellationToken) ?? [];
			var sent = 0;
			foreach (var trade in trades)
			{
				if (!trade.SymbolId.IsEmpty() &&
					!trade.SymbolId.EqualsIgnoreCase(symbol.SymbolId))
					throw new InvalidDataException(
						$"CoinAPI returned trade data for '{trade.SymbolId}' instead of '{symbol.SymbolId}'.");
				await SendTradeAsync(trade, securityId, message.TransactionId,
					cancellationToken);
				sent++;
			}
			remaining = SubtractCount(remaining, sent);
		}
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, securityId,
			new(CoinApiSocketDataTypes.Trade, symbol.SymbolId,
				CoinApiPeriodIds.Unknown), 0, remaining, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var symbol = await ResolveSymbolAsync(message.SecurityId,
			cancellationToken);
		var securityId = ToSecurityId(symbol);
		var remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var quotes = await SafeRest().GetQuotesAsync(symbol.SymbolId, from,
				to, GetHistoryLimit(remaining), cancellationToken) ?? [];
			var sent = 0;
			foreach (var quote in quotes)
			{
				if (!quote.SymbolId.IsEmpty() &&
					!quote.SymbolId.EqualsIgnoreCase(symbol.SymbolId))
					throw new InvalidDataException(
						$"CoinAPI returned quote data for '{quote.SymbolId}' instead of '{symbol.SymbolId}'.");
				await SendLevel1Async(quote, securityId, message.TransactionId,
					cancellationToken);
				sent++;
			}
			remaining = SubtractCount(remaining, sent);
		}
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, securityId,
			new(CoinApiSocketDataTypes.Quote, symbol.SymbolId,
				CoinApiPeriodIds.Unknown), 0, remaining, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var symbol = await ResolveSymbolAsync(message.SecurityId,
			cancellationToken);
		var securityId = ToSecurityId(symbol);
		var depth = Math.Min(Math.Max(1, message.MaxDepth ?? MarketDepth),
			MarketDepth);
		var remaining = message.Count;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var books = await SafeRest().GetOrderBooksAsync(symbol.SymbolId,
				from, to, GetHistoryLimit(remaining), depth,
				cancellationToken) ?? [];
			var sent = 0;
			foreach (var book in books)
			{
				if (!book.SymbolId.IsEmpty() &&
					!book.SymbolId.EqualsIgnoreCase(symbol.SymbolId))
					throw new InvalidDataException(
						$"CoinAPI returned order-book data for '{book.SymbolId}' instead of '{symbol.SymbolId}'.");
				await SendOrderBookAsync(book.Bids, book.Asks,
					GetServerTime(book.ExchangeTime, book.CoinApiTime,
						"order book"), securityId, message.TransactionId, depth,
					null, cancellationToken);
				sent++;
			}
			remaining = SubtractCount(remaining, sent);
		}
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		var dataType = CoinApiExtensions.ToBookDataType(depth);
		await AddLiveSubscriptionAsync(message, securityId,
			new(dataType, symbol.SymbolId, CoinApiPeriodIds.Unknown), depth,
			remaining, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var timeFrame = message.GetTimeFrame();
		var periodId = timeFrame.ToPeriodId();
		if (!message.IsHistoryOnly() && timeFrame > TimeSpan.FromDays(1))
			throw new NotSupportedException(
				"CoinAPI WebSocket OHLCV supports intervals through one day; larger intervals are historical only.");
		var symbol = await ResolveSymbolAsync(message.SecurityId,
			cancellationToken);
		var securityId = ToSecurityId(symbol);
		var remaining = message.Count;
		DateTime? lastCandleOpenTime = null;
		if (ShouldDownloadHistory(message))
		{
			var (from, to) = GetHistoryRange(message);
			var candles = await SafeRest().GetOhlcvAsync(symbol.SymbolId,
				periodId, from, to, GetHistoryLimit(remaining),
				cancellationToken) ?? [];
			var sent = 0;
			foreach (var candle in candles)
			{
				await SendCandleAsync(candle, securityId, message.TransactionId,
					timeFrame, true, null, cancellationToken);
				var openTime = candle.PeriodStartTime.ParseCoinApiTime(
					"OHLCV period start");
				if (lastCandleOpenTime is null || openTime > lastCandleOpenTime)
					lastCandleOpenTime = openTime;
				sent++;
			}
			remaining = SubtractCount(remaining, sent);
		}
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, securityId,
			new(CoinApiSocketDataTypes.Ohlcv, symbol.SymbolId, periodId), 0,
			remaining, lastCandleOpenTime, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask<CoinApiSymbol> ResolveSymbolAsync(
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinApi))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not CoinAPI.");
		var identity = (securityId.Native as string)
			.IsEmpty(securityId.SecurityCode)?.Trim();
		identity.ThrowIfEmpty(nameof(securityId.SecurityCode));
		using (_sync.EnterScope())
			if (_symbols.TryGetValue(identity, out var cached))
				return cached;

		var symbols = await SafeRest().GetSymbolsAsync(null, identity, null,
			Math.Min(MaximumItems, 5000), cancellationToken);
		CacheSymbols(symbols);
		var exact = symbols.Where(IsValidSymbol).Where(item =>
			item.SymbolId.EqualsIgnoreCase(identity)).Take(2).ToArray();
		if (exact.Length == 1)
			return exact[0];
		var matches = symbols.Where(IsValidSymbol).Where(item =>
			item.ExchangeSymbolId.EqualsIgnoreCase(identity) ||
			(item.BaseAsset + "/" + item.QuoteAsset).EqualsIgnoreCase(identity))
			.Take(2).ToArray();
		if (matches.Length == 1)
			return matches[0];
		throw new InvalidOperationException(
			$"CoinAPI symbol '{identity}' is unknown or ambiguous. Use security lookup and preserve the canonical symbol_id.");
	}

	private void CacheSymbols(IEnumerable<CoinApiSymbol> symbols)
	{
		using (_sync.EnterScope())
			foreach (var symbol in symbols.Where(IsValidSymbol))
				_symbols[symbol.SymbolId] = symbol;
	}

	private static bool IsValidSymbol(CoinApiSymbol symbol)
	{
		if (symbol?.SymbolId.IsEmpty() != false || symbol.ExchangeId.IsEmpty() ||
			symbol.SymbolType == CoinApiSymbolTypes.Unknown)
			return false;
		return symbol.SymbolType is CoinApiSymbolTypes.Index or
			CoinApiSymbolTypes.Contract or CoinApiSymbolTypes.OptionCombo or
			CoinApiSymbolTypes.FutureCombo ||
			!symbol.BaseAsset.IsEmpty() && !symbol.QuoteAsset.IsEmpty();
	}

	private static bool Matches(CoinApiSymbol symbol, string value)
		=> value.IsEmpty() || symbol.SymbolId.ContainsIgnoreCase(value) ||
			symbol.ExchangeSymbolId.ContainsIgnoreCase(value) ||
			symbol.ExchangeId.ContainsIgnoreCase(value) ||
			(symbol.BaseAsset + "/" + symbol.QuoteAsset).ContainsIgnoreCase(value) ||
			symbol.BaseAsset.ContainsIgnoreCase(value) ||
			symbol.QuoteAsset.ContainsIgnoreCase(value) ||
			symbol.IndexDisplayName.ContainsIgnoreCase(value);

	private static SecurityId ToSecurityId(CoinApiSymbol symbol)
		=> new()
		{
			SecurityCode = symbol.SymbolId.ToUpperInvariant(),
			BoardCode = BoardCodes.CoinApi,
			Native = symbol.SymbolId,
		};

	private static SecurityMessage ToSecurityMessage(CoinApiSymbol symbol,
		long originalTransactionId)
	{
		var securityType = symbol.SymbolType.ToSecurityType();
		var priceStep = Positive(symbol.PricePrecision);
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = ToSecurityId(symbol),
			Name = GetSecurityName(symbol),
			ShortName = symbol.ExchangeSymbolId.IsEmpty()
				? symbol.BaseAsset + "/" + symbol.QuoteAsset
				: symbol.ExchangeSymbolId,
			Class = CoinApiEnumConverter<CoinApiSymbolTypes>.ToWire(
				symbol.SymbolType),
			SecurityType = securityType,
			Currency = ToCurrency(symbol.QuoteAsset),
			PriceStep = priceStep,
			Decimals = priceStep?.GetCachedDecimals(),
			VolumeStep = Positive(symbol.SizePrecision),
			Multiplier = GetMultiplier(symbol),
		};
		if (securityType == SecurityTypes.Option)
		{
			message.OptionType = symbol.IsOptionCall switch
			{
				true => OptionTypes.Call,
				false => OptionTypes.Put,
				_ => null,
			};
			message.Strike = Positive(symbol.OptionStrikePrice);
		}
		var expiry = symbol.SymbolType switch
		{
			CoinApiSymbolTypes.Option => symbol.OptionExpirationTime,
			CoinApiSymbolTypes.Futures => symbol.FutureDeliveryTime,
			CoinApiSymbolTypes.Contract => symbol.ContractDeliveryTime,
			_ => null,
		};
		if (!expiry.IsEmpty())
			message.ExpiryDate = expiry.ParseCoinApiTime("expiry");
		return message;
	}

	private static string GetSecurityName(CoinApiSymbol symbol)
	{
		if (!symbol.IndexDisplayName.IsEmpty())
			return symbol.IndexDisplayName;
		var pair = symbol.BaseAsset.IsEmpty()
			? symbol.SymbolId
			: symbol.BaseAsset + (symbol.QuoteAsset.IsEmpty()
				? string.Empty
				: "/" + symbol.QuoteAsset);
		return pair + " @ " + symbol.ExchangeId;
	}

	private static decimal? GetMultiplier(CoinApiSymbol symbol)
		=> Positive(symbol.SymbolType switch
		{
			CoinApiSymbolTypes.Futures or CoinApiSymbolTypes.Perpetual or
				CoinApiSymbolTypes.DeployerPerpetual => symbol.FutureContractUnit,
			CoinApiSymbolTypes.Option => symbol.OptionContractUnit,
			CoinApiSymbolTypes.Contract => symbol.ContractUnit,
			_ => null,
		});

	private static CurrencyTypes? ToCurrency(string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	private ValueTask AddLiveSubscriptionAsync(MarketDataMessage message,
		SecurityId securityId, CoinApiStreamKey key, int depth, long? remaining,
		CancellationToken cancellationToken)
		=> AddLiveSubscriptionAsync(message, securityId, key, depth,
			remaining, null, cancellationToken);

	private async ValueTask AddLiveSubscriptionAsync(MarketDataMessage message,
		SecurityId securityId, CoinApiStreamKey key, int depth, long? remaining,
		DateTime? lastCountedCandle, CancellationToken cancellationToken)
	{
		key = key with
		{
			SymbolId = key.SymbolId.ThrowIfEmpty(nameof(key.SymbolId))
				.ToUpperInvariant(),
		};
		var subscription = new LiveSubscription
		{
			TransactionId = message.TransactionId,
			SecurityId = securityId,
			Key = key,
			Depth = depth,
			Remaining = remaining,
			LastCountedCandle = lastCountedCandle,
		};
		bool isFirst;
		using (_sync.EnterScope())
		{
			if (_liveSubscriptions.ContainsKey(message.TransactionId))
				throw new InvalidOperationException(
					$"CoinAPI subscription {message.TransactionId} already exists.");
			isFirst = !_liveSubscriptions.Values.Any(item => item.Key == key);
			_liveSubscriptions.Add(message.TransactionId, subscription);
		}
		try
		{
			if (isFirst)
				await GetOrCreateSocket().SubscribeAsync(key, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_liveSubscriptions.Remove(message.TransactionId);
			throw;
		}
	}

	private async ValueTask RemoveLiveSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		LiveSubscription removed;
		bool isLast;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.Remove(transactionId, out removed))
				return;
			isLast = !_liveSubscriptions.Values.Any(item =>
				item.Key == removed.Key);
		}
		if (isLast && _socket is not null)
			await _socket.UnsubscribeAsync(removed.Key, cancellationToken);
	}

	private async ValueTask OnSocketMessageAsync(CoinApiSocketMessage update,
		CancellationToken cancellationToken)
	{
		var key = GetStreamKey(update);
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.Key == key)];
		foreach (var subscription in subscriptions)
		{
			DateTime? candleOpenTime = null;
			switch (update.Type)
			{
				case CoinApiSocketMessageTypes.Trade:
					await SendTradeAsync(new()
					{
						SymbolId = update.SymbolId,
						ExchangeTime = update.ExchangeTime,
						CoinApiTime = update.CoinApiTime,
						Uuid = update.Uuid,
						Price = update.Price,
						Size = update.Size,
						TakerSide = update.TakerSide,
					}, subscription.SecurityId, subscription.TransactionId,
						update.Sequence, cancellationToken);
					break;
				case CoinApiSocketMessageTypes.Quote:
					await SendLevel1Async(new()
					{
						SymbolId = update.SymbolId,
						ExchangeTime = update.ExchangeTime,
						CoinApiTime = update.CoinApiTime,
						AskPrice = update.AskPrice,
						AskSize = update.AskSize,
						BidPrice = update.BidPrice,
						BidSize = update.BidSize,
					}, subscription.SecurityId, subscription.TransactionId,
						update.Sequence, cancellationToken);
					break;
				case CoinApiSocketMessageTypes.Book5:
				case CoinApiSocketMessageTypes.Book20:
				case CoinApiSocketMessageTypes.Book50:
					await SendOrderBookAsync(update.Bids, update.Asks,
						GetServerTime(update.ExchangeTime, update.CoinApiTime,
							"order book"), subscription.SecurityId,
						subscription.TransactionId, subscription.Depth,
						update.Sequence, cancellationToken);
					break;
				case CoinApiSocketMessageTypes.Ohlcv:
					candleOpenTime = update.PeriodStartTime.ParseCoinApiTime(
						"OHLCV period start");
					await SendCandleAsync(new()
					{
						PeriodStartTime = update.PeriodStartTime,
						PeriodEndTime = update.PeriodEndTime,
						OpenTime = update.OpenTime,
						CloseTime = update.CloseTime,
						OpenPrice = update.OpenPrice,
						HighPrice = update.HighPrice,
						LowPrice = update.LowPrice,
						ClosePrice = update.ClosePrice,
						TradedVolume = update.TradedVolume,
						TradesCount = update.TradesCount,
					}, subscription.SecurityId, subscription.TransactionId,
						key.PeriodId.ToTimeFrame(), false, update.Sequence,
						cancellationToken);
					break;
				default:
					return;
			}
			await ConsumeLiveItemAsync(subscription, candleOpenTime,
				cancellationToken);
		}
	}

	private static CoinApiStreamKey GetStreamKey(CoinApiSocketMessage update)
	{
		var dataType = update.Type switch
		{
			CoinApiSocketMessageTypes.Trade => CoinApiSocketDataTypes.Trade,
			CoinApiSocketMessageTypes.Quote => CoinApiSocketDataTypes.Quote,
			CoinApiSocketMessageTypes.Book5 => CoinApiSocketDataTypes.Book5,
			CoinApiSocketMessageTypes.Book20 => CoinApiSocketDataTypes.Book20,
			CoinApiSocketMessageTypes.Book50 => CoinApiSocketDataTypes.Book50,
			CoinApiSocketMessageTypes.Ohlcv => CoinApiSocketDataTypes.Ohlcv,
			_ => CoinApiSocketDataTypes.Unknown,
		};
		if (dataType == CoinApiSocketDataTypes.Unknown)
			throw new InvalidDataException(
				"CoinAPI returned an unsupported stream message.");
		var periodId = dataType == CoinApiSocketDataTypes.Ohlcv
			? ResolvePeriodId(update)
			: CoinApiPeriodIds.Unknown;
		return new(dataType, update.SymbolId.ToUpperInvariant(), periodId);
	}

	private static CoinApiPeriodIds ResolvePeriodId(
		CoinApiSocketMessage update)
	{
		if (update.PeriodId != CoinApiPeriodIds.Unknown)
			return update.PeriodId;
		var from = update.PeriodStartTime.ParseCoinApiTime("OHLCV period start");
		var to = update.PeriodEndTime.ParseCoinApiTime("OHLCV period end");
		if (from >= to)
			throw new InvalidDataException(
				"CoinAPI OHLCV period has an invalid time range.");
		return (to - from).ToPeriodId();
	}

	private async ValueTask ConsumeLiveItemAsync(
		LiveSubscription subscription, DateTime? candleOpenTime,
		CancellationToken cancellationToken)
	{
		var isFinished = false;
		var isLast = false;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryGetValue(subscription.TransactionId,
				out var current) || !ReferenceEquals(current, subscription))
				return;
			if (candleOpenTime is { } openTime)
			{
				if (current.LastCountedCandle == openTime)
					return;
				current.LastCountedCandle = openTime;
			}
			if (current.Remaining is not > 0 || --current.Remaining != 0)
				return;
			_liveSubscriptions.Remove(current.TransactionId);
			isLast = !_liveSubscriptions.Values.Any(item =>
				item.Key == current.Key);
			isFinished = true;
		}
		if (!isFinished)
			return;
		await SendSubscriptionFinishedAsync(subscription.TransactionId,
			cancellationToken);
		if (isLast && _socket is not null)
			await _socket.UnsubscribeAsync(subscription.Key, cancellationToken);
	}

	private ValueTask SendTradeAsync(CoinApiTrade trade, SecurityId securityId,
		long transactionId, CancellationToken cancellationToken)
		=> SendTradeAsync(trade, securityId, transactionId, null,
			cancellationToken);

	private ValueTask SendTradeAsync(CoinApiTrade trade, SecurityId securityId,
		long transactionId, long? sequence, CancellationToken cancellationToken)
	{
		if (trade?.Price is not > 0 || trade.Size is null or < 0)
			throw new InvalidDataException(
				"CoinAPI returned an invalid trade price or size.");
		return SendOutMessageAsync(new ExecutionMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = GetServerTime(trade.ExchangeTime, trade.CoinApiTime,
				"trade"),
			TradeStringId = trade.Uuid.IsEmpty(trade.ExchangeTradeId),
			TradePrice = trade.Price,
			TradeVolume = trade.Size,
			OriginSide = trade.TakerSide.ToSide(),
			SeqNum = sequence ?? 0,
		}, cancellationToken);
	}

	private ValueTask SendLevel1Async(CoinApiQuote quote, SecurityId securityId,
		long transactionId, CancellationToken cancellationToken)
		=> SendLevel1Async(quote, securityId, transactionId, null,
			cancellationToken);

	private ValueTask SendLevel1Async(CoinApiQuote quote, SecurityId securityId,
		long transactionId, long? sequence, CancellationToken cancellationToken)
	{
		if (quote is null || quote.BidPrice is not > 0 &&
			quote.AskPrice is not > 0 || quote.BidSize is < 0 ||
			quote.AskSize is < 0)
			throw new InvalidDataException(
				"CoinAPI returned an invalid quote.");
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = GetServerTime(quote.ExchangeTime, quote.CoinApiTime,
				"quote"),
			SeqNum = sequence ?? 0,
		}
		.TryAdd(Level1Fields.BestBidPrice, Positive(quote.BidPrice))
		.TryAdd(Level1Fields.BestBidVolume, Positive(quote.BidSize))
		.TryAdd(Level1Fields.BestAskPrice, Positive(quote.AskPrice))
		.TryAdd(Level1Fields.BestAskVolume, Positive(quote.AskSize)),
			cancellationToken);
	}

	private ValueTask SendOrderBookAsync(CoinApiBookLevel[] bids,
		CoinApiBookLevel[] asks, DateTime serverTime, SecurityId securityId,
		long transactionId, int depth, long? sequence,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
			Bids = ConvertLevels(bids, true, depth),
			Asks = ConvertLevels(asks, false, depth),
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = sequence ?? 0,
		}, cancellationToken);

	private static QuoteChange[] ConvertLevels(CoinApiBookLevel[] levels,
		bool isBid, int depth)
	{
		var result = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level?.Price is not > 0 || level.Size is null or < 0)
				throw new InvalidDataException(
					"CoinAPI returned an invalid order-book level.");
			if (level.Size > 0)
				result.Add(new(level.Price.Value, level.Size.Value));
		}
		return [.. (isBid
			? result.OrderByDescending(static quote => quote.Price)
			: result.OrderBy(static quote => quote.Price)).Take(depth)];
	}

	private ValueTask SendCandleAsync(CoinApiOhlcv candle,
		SecurityId securityId, long transactionId, TimeSpan timeFrame,
		bool isFinished, long? sequence, CancellationToken cancellationToken)
	{
		if (candle?.PeriodStartTime.IsEmpty() != false ||
			candle.PeriodEndTime.IsEmpty() || candle.OpenPrice is not >= 0 ||
			candle.HighPrice is not >= 0 || candle.LowPrice is not >= 0 ||
			candle.ClosePrice is not >= 0 ||
			candle.HighPrice < candle.LowPrice || candle.TradedVolume is < 0 ||
			candle.TradesCount is < 0)
			throw new InvalidDataException(
				"CoinAPI returned an invalid OHLCV candle.");
		var openTime = candle.PeriodStartTime.ParseCoinApiTime(
			"OHLCV period start");
		var closeTime = candle.PeriodEndTime.ParseCoinApiTime(
			"OHLCV period end");
		if (openTime >= closeTime || closeTime - openTime != timeFrame)
			throw new InvalidDataException(
				"CoinAPI OHLCV candle has an unexpected time range.");
		var state = isFinished || closeTime <= CurrentTime.EnsureUtc()
			? CandleStates.Finished
			: CandleStates.Active;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.OpenPrice.Value,
			HighPrice = candle.HighPrice.Value,
			LowPrice = candle.LowPrice.Value,
			ClosePrice = candle.ClosePrice.Value,
			TotalVolume = candle.TradedVolume ?? 0,
			TotalTicks = ToTradeCount(candle.TradesCount),
			State = state,
			SeqNum = sequence ?? 0,
		}, cancellationToken);
	}

	private static int? ToTradeCount(long? value)
		=> value is null ? null : (int)Math.Min(int.MaxValue, value.Value);

	private (DateTime From, DateTime To) GetHistoryRange(
		MarketDataMessage message)
	{
		var to = (message.To ?? CurrentTime).EnsureUtc();
		var earliest = to - DateTime.UnixEpoch < HistoryLookback
			? DateTime.UnixEpoch
			: to - HistoryLookback;
		var from = (message.From ?? earliest).EnsureUtc();
		if (from >= to)
			throw new ArgumentOutOfRangeException(nameof(message),
				"CoinAPI history start time must be earlier than end time.");
		return (from, to);
	}

	private int GetHistoryLimit(long? remaining)
		=> (int)Math.Min(HistoryLimit, remaining ?? HistoryLimit);

	private static bool ShouldDownloadHistory(MarketDataMessage message)
		=> message.IsHistoryOnly() || message.From is not null ||
			message.To is not null;

	private static long? SubtractCount(long? remaining, int sent)
		=> remaining is null ? null : Math.Max(0, remaining.Value - sent);

	private static DateTime GetServerTime(string exchangeTime,
		string coinApiTime, string field)
	{
		if (!exchangeTime.IsEmpty())
			return exchangeTime.ParseCoinApiTime(field + " exchange");
		if (!coinApiTime.IsEmpty())
			return coinApiTime.ParseCoinApiTime(field + " CoinAPI");
		throw new InvalidDataException(
			$"CoinAPI {field} timestamp is missing.");
	}

	private async ValueTask FinishSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;
}

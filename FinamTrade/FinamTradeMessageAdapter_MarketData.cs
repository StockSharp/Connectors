namespace StockSharp.FinamTrade;

public partial class FinamTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode;
		var requestedBoard = lookupMsg.SecurityId.BoardCode;
		var left = Math.Min(lookupMsg.Count ?? long.MaxValue, LookupLimit);

		if (!requestedCode.IsEmpty() && !requestedBoard.IsEmpty())
		{
			var symbol = lookupMsg.SecurityId.ToNativeSymbol();
			var details = await _rest.GetAsset(symbol, _resolvedAccountId,
				cancellationToken);
			if (details is not null)
			{
				var security = ToSecurity(details, symbol,
					lookupMsg.TransactionId);
				if (security.IsMatch(lookupMsg, securityTypes))
					await SendOutMessageAsync(security, cancellationToken);
			}
		}
		else
		{
			string cursor = null;

			while (left > 0)
			{
				var page = await _rest.GetAssets(cursor, cancellationToken);
				var assets = page?.Assets ?? [];

				foreach (var asset in assets)
				{
					if (asset is null || asset.IsArchived ||
						asset.Ticker.IsEmpty() || asset.Mic.IsEmpty())
						continue;
					if (!requestedCode.IsEmpty() &&
						!asset.Ticker.Contains(requestedCode,
							StringComparison.OrdinalIgnoreCase) &&
						!(asset.Name?.Contains(requestedCode,
							StringComparison.OrdinalIgnoreCase) ?? false))
						continue;
					if (!requestedBoard.IsEmpty() &&
						!asset.Mic.EqualsIgnoreCase(requestedBoard))
						continue;

					var security = ToSecurity(asset, lookupMsg.TransactionId);
					if (!security.IsMatch(lookupMsg, securityTypes))
						continue;

					await SendOutMessageAsync(security, cancellationToken);
					if (--left <= 0)
						break;
				}

				var next = page?.NextCursor;
				if (assets.Length == 0 || next.IsEmpty() || next == "0" ||
					next.EqualsIgnoreCase(cursor))
					break;
				cursor = next;
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private static SecurityMessage ToSecurity(FinamAsset asset,
		long originalTransactionId)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = asset.Ticker,
				BoardCode = asset.Mic,
				Native = asset.Id,
				Isin = asset.Isin,
			},
			Name = asset.Name,
			ShortName = asset.Ticker,
			SecurityType = asset.Type.ToSecurityType(),
		};

	private static SecurityMessage ToSecurity(FinamAssetDetails asset,
		string symbol, long originalTransactionId)
	{
		var securityId = symbol.ToSecurityId();
		var type = asset.Type.ToSecurityType();
		var decimals = asset.Decimals ?? 0;
		var priceStep = asset.MinStep is long minStep
			? minStep / DecimalPower(decimals)
			: (decimal?)null;
		var expiry = asset.OptionDetails?.ExpirationDate ??
			asset.FutureDetails?.ExpirationDate;

		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = asset.Ticker.IsEmpty(securityId.SecurityCode),
				BoardCode = asset.Mic.IsEmpty(securityId.BoardCode),
				Native = asset.Id,
				Isin = asset.Isin,
			},
			Name = asset.Name,
			ShortName = asset.Ticker,
			SecurityType = type,
			Currency = asset.QuoteCurrency.ToCurrency(),
			Decimals = asset.Decimals,
			PriceStep = priceStep,
			VolumeStep = 1,
			MinVolume = asset.LotSize.ToDecimal(),
			ExpiryDate = expiry,
			Strike = asset.OptionDetails?.Strike.ToDecimal(),
			FaceValue = asset.BondDetails?.BondFaceValue.ToDecimal(),
		};
	}

	private static decimal DecimalPower(int power)
	{
		var result = 1m;

		for (var i = 0; i < power; i++)
			result *= 10m;

		return result;
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

		var symbol = message.SecurityId.ToNativeSymbol();
		var response = await _rest.GetQuote(symbol, cancellationToken);
		if (response?.Quote is not null)
		{
			response.Quote.Symbol = response.Quote.Symbol.IsEmpty(symbol);
			await SendQuote(response.Quote, message.TransactionId,
				message.SecurityId, cancellationToken);
		}

		if (!message.IsHistoryOnly())
		{
			await AddLiveSubscription(message,
				new("QUOTES", symbol, null, null), null, cancellationToken);
		}

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

		var symbol = message.SecurityId.ToNativeSymbol();
		var response = await _rest.GetOrderBook(symbol, cancellationToken);
		if (response?.Orderbook is not null)
		{
			await SendBook(response.Orderbook.Rows, message.TransactionId,
				message.SecurityId, QuoteChangeStates.SnapshotComplete,
				cancellationToken);
		}

		if (!message.IsHistoryOnly())
		{
			await AddLiveSubscription(message,
				new("ORDER_BOOK", symbol, null, null), null, cancellationToken);
		}

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

		var symbol = message.SecurityId.ToNativeSymbol();
		var response = await _rest.GetLatestTrades(symbol, cancellationToken);
		IEnumerable<FinamMarketTrade> trades = (response?.Trades ?? [])
			.Where(t => (message.From is null || t.Timestamp >= message.From) &&
				(message.To is null || t.Timestamp <= message.To))
			.OrderBy(t => t.Timestamp);
		if (message.Count is long count)
			trades = trades.Take((int)Math.Min(count, int.MaxValue));

		foreach (var trade in trades)
			await SendMarketTrade(trade, message.TransactionId,
				message.SecurityId, cancellationToken);

		if (!message.IsHistoryOnly())
		{
			await AddLiveSubscription(message,
				new("INSTRUMENT_TRADES", symbol, null, null), null,
				cancellationToken);
		}

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

		var timeFrame = message.DataType2.Arg.To<TimeSpan>();
		var nativeTimeFrame = timeFrame.ToNative();
		var symbol = message.SecurityId.ToNativeSymbol();

		if (message.From is not null || message.To is not null ||
			message.Count is not null || message.IsHistoryOnly())
		{
			var to = (message.To ?? DateTime.UtcNow).ToUniversalTime();
			var from = (message.From ?? GetDefaultCandleFrom(to, timeFrame,
				message.Count)).ToUniversalTime();
			var response = await _rest.GetBars(symbol, nativeTimeFrame,
				from, to, cancellationToken);
			IEnumerable<FinamBar> bars = (response?.Bars ?? [])
				.Where(b => b.Timestamp >= from && b.Timestamp <= to)
				.OrderBy(b => b.Timestamp);
			if (message.Count is long count)
				bars = bars.TakeLast((int)Math.Min(count, int.MaxValue));

			foreach (var bar in bars)
			{
				await SendBar(bar, message.TransactionId, message.SecurityId,
					timeFrame, CandleStates.Finished, cancellationToken);
			}
		}

		if (!message.IsHistoryOnly())
		{
			await AddLiveSubscription(message,
				new("BARS", symbol, nativeTimeFrame, null), timeFrame,
				cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private static DateTime GetDefaultCandleFrom(DateTime to,
		TimeSpan timeFrame, long? count)
	{
		var bars = Math.Clamp(count ?? 500, 1, 5000);
		try
		{
			return to - TimeSpan.FromTicks(timeFrame.Ticks * bars);
		}
		catch (OverflowException)
		{
			return DateTime.MinValue;
		}
	}

	private async ValueTask AddLiveSubscription(MarketDataMessage message,
		FinamSocketSubscription native, TimeSpan? timeFrame,
		CancellationToken cancellationToken)
	{
		var subscription = new MarketSubscription
		{
			TransactionId = message.TransactionId,
			SecurityId = message.SecurityId,
			Native = native,
			TimeFrame = timeFrame,
		};
		_marketSubscriptions.Add(message.TransactionId, subscription);
		try
		{
			if (!_marketSubscriptions.CachedValues.Any(s =>
				s.TransactionId != message.TransactionId &&
				s.Native == native))
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
			await _socket.Unsubscribe(subscription.Native, cancellationToken);
	}

	private async ValueTask ProcessQuote(FinamQuote quote,
		CancellationToken cancellationToken)
	{
		if (quote?.Symbol.IsEmpty() != false)
			return;

		foreach (var subscription in FindSubscriptions("QUOTES", quote.Symbol))
		{
			await SendQuote(quote, subscription.TransactionId,
				subscription.SecurityId, cancellationToken);
		}
	}

	private ValueTask SendQuote(FinamQuote quote, long transactionId,
		SecurityId securityId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = quote.Timestamp == default
				? DateTime.UtcNow : quote.Timestamp,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.Bid.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, quote.BidSize.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, quote.Ask.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, quote.AskSize.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, quote.Last.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, quote.LastSize.ToDecimal())
		.TryAdd(Level1Fields.Volume, quote.Volume.ToDecimal())
		.TryAdd(Level1Fields.Turnover, quote.Turnover.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, quote.Open.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, quote.High.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, quote.Low.ToDecimal())
		.TryAdd(Level1Fields.ClosePrice, quote.Close.ToDecimal())
		.TryAdd(Level1Fields.Change, quote.Change.ToDecimal())
		.TryAdd(Level1Fields.OpenInterest, quote.OpenInterest.ToDecimal()),
			cancellationToken);

	private async ValueTask ProcessOrderBook(FinamStreamOrderBook book,
		CancellationToken cancellationToken)
	{
		if (book?.Symbol.IsEmpty() != false)
			return;

		foreach (var subscription in FindSubscriptions(
			"ORDER_BOOK", book.Symbol))
		{
			await SendBook(book.Rows, subscription.TransactionId,
				subscription.SecurityId, QuoteChangeStates.Increment,
				cancellationToken);
		}
	}

	private ValueTask SendBook(FinamBookRow[] rows, long transactionId,
		SecurityId securityId, QuoteChangeStates state,
		CancellationToken cancellationToken)
	{
		rows ??= [];
		var serverTime = rows
			.Select(r => r.Timestamp)
			.Where(t => t != default)
			.DefaultIfEmpty(DateTime.UtcNow)
			.Max();

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
			State = state,
			Bids = rows
				.Where(r => r.BuySize is not null)
				.Select(r => new QuoteChange(
					r.Price.ToDecimal() ?? 0,
					r.Action.EqualsIgnoreCase("ACTION_REMOVE")
						? 0 : r.BuySize.ToDecimal() ?? 0))
				.ToArray(),
			Asks = rows
				.Where(r => r.SellSize is not null)
				.Select(r => new QuoteChange(
					r.Price.ToDecimal() ?? 0,
					r.Action.EqualsIgnoreCase("ACTION_REMOVE")
						? 0 : r.SellSize.ToDecimal() ?? 0))
				.ToArray(),
		}, cancellationToken);
	}

	private async ValueTask ProcessMarketTrades(FinamMarketTradesResponse response,
		CancellationToken cancellationToken)
	{
		if (response?.Symbol.IsEmpty() != false)
			return;

		foreach (var subscription in FindSubscriptions(
			"INSTRUMENT_TRADES", response.Symbol))
		{
			foreach (var trade in response.Trades ?? [])
			{
				await SendMarketTrade(trade, subscription.TransactionId,
					subscription.SecurityId, cancellationToken);
			}
		}
	}

	private ValueTask SendMarketTrade(FinamMarketTrade trade, long transactionId,
		SecurityId securityId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = trade.Timestamp == default
				? DateTime.UtcNow : trade.Timestamp,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Size.ToDecimal(),
			OriginSide = trade.Side.ToSide(),
			OpenInterest = trade.OpenInterest.ToDecimal(),
		}, cancellationToken);

	private async ValueTask ProcessBars(FinamBarsResponse response,
		CancellationToken cancellationToken)
	{
		if (response?.Symbol.IsEmpty() != false)
			return;

		foreach (var subscription in FindSubscriptions("BARS", response.Symbol))
		{
			foreach (var bar in response.Bars ?? [])
			{
				await SendBar(bar, subscription.TransactionId,
					subscription.SecurityId, subscription.TimeFrame.Value,
					CandleStates.Active, cancellationToken);
			}
		}
	}

	private ValueTask SendBar(FinamBar bar, long transactionId,
		SecurityId securityId, TimeSpan timeFrame, CandleStates state,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			OpenTime = bar.Timestamp,
			CloseTime = bar.Timestamp + timeFrame,
			OpenPrice = bar.Open.ToDecimal() ?? 0,
			HighPrice = bar.High.ToDecimal() ?? 0,
			LowPrice = bar.Low.ToDecimal() ?? 0,
			ClosePrice = bar.Close.ToDecimal() ?? 0,
			TotalVolume = bar.Volume.ToDecimal() ?? 0,
			State = state,
		}, cancellationToken);

	private MarketSubscription[] FindSubscriptions(string type, string symbol)
		=> _marketSubscriptions.CachedValues
			.Where(s => s.Native.Type.EqualsIgnoreCase(type) &&
				s.Native.Symbol.EqualsIgnoreCase(symbol))
			.ToArray();
}

namespace StockSharp.Paxos;

using StockSharp.Paxos.Native;
using StockSharp.Paxos.Native.Model;

public partial class PaxosMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
			!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Paxos))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		await RefreshMarketsAsync(cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		var skip = (lookupMsg.Skip ?? 0).Max(0L);
		var left = (lookupMsg.Count ?? long.MaxValue).Max(0L);
		var markets = GetMarkets();
		var securities = markets.Select(market =>
			CreateSecurity(market, lookupMsg.TransactionId)).Concat(
				markets.SelectMany(static market => new[]
					{
						market.BaseAsset,
						market.QuoteAsset,
					})
					.Where(static asset => !asset.IsEmpty())
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.Select(asset => CreateAssetSecurity(asset,
						lookupMsg.TransactionId)))
			.GroupBy(static security => security.SecurityId.ToStringId(),
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static security => security.SecurityId.SecurityCode,
				StringComparer.OrdinalIgnoreCase);

		foreach (var security in securities)
		{
			if (!requestedCode.IsEmpty() &&
				!security.SecurityId.SecurityCode.EqualsIgnoreCase(requestedCode))
				continue;
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			if (left-- <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private static SecurityMessage CreateAssetSecurity(string asset,
		long originalTransactionId)
		=> new()
		{
			SecurityId = ToAssetSecurityId(asset),
			Name = asset,
			ShortName = asset,
			SecurityType = IsFiatAsset(asset)
				? SecurityTypes.Currency
				: SecurityTypes.CryptoCurrency,
			OriginalTransactionId = originalTransactionId,
		};

	private static bool IsFiatAsset(string asset)
		=> asset?.ToUpperInvariant() is "USD" or "EUR" or "GBP" or "SGD" or
			"BRL" or "MXN";

	private static SecurityMessage CreateSecurity(PaxosMarket market,
		long originalTransactionId)
	{
		ArgumentNullException.ThrowIfNull(market);
		var priceStep = market.TickRate.ParsePaxosAmount();
		var minimum = market.MinimumBaseAmount.ParsePaxosAmount();
		var maximum = market.MaximumBaseAmount.ParsePaxosAmount();
		return new()
		{
			SecurityId = ToSecurityId(market.Market),
			Name = market.BaseAsset + "/" + market.QuoteAsset,
			ShortName = market.Market,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = ToCurrency(market.QuoteAsset),
			PriceStep = priceStep > 0 ? priceStep : null,
			Decimals = priceStep > 0
				? priceStep.GetCachedDecimals()
				: null,
			MinVolume = minimum > 0 ? minimum : null,
			MaxVolume = maximum > 0 ? maximum : null,
			OriginalTransactionId = originalTransactionId,
		};
	}

	private static SecurityStates? ToSecurityState(PaxosMarketStatus status)
	{
		if (status is null || status.BuyStatus ==
			PaxosMarketTradingStatuses.Unknown || status.SellStatus ==
			PaxosMarketTradingStatuses.Unknown)
			return null;
		return status.BuyStatus == PaxosMarketTradingStatuses.Available &&
			status.SellStatus == PaxosMarketTradingStatuses.Available
				? SecurityStates.Trading
				: SecurityStates.Stoped;
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessBookSubscriptionAsync(mdMsg, DataType.Level1,
			cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessBookSubscriptionAsync(mdMsg, DataType.MarketDepth,
			cancellationToken);

	private async ValueTask ProcessBookSubscriptionAsync(
		MarketDataMessage message, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!message.IsSubscribe)
		{
			await RemoveMarketSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(message, cancellationToken);
			return;
		}
		if (message.From is not null || message.To is not null)
			throw new NotSupportedException(
				"Paxos exposes the current order book, not historical book changes.");

		EnsureAuthenticated();
		var market = GetMarket(message.SecurityId);
		var depth = dataType == DataType.MarketDepth
			? (message.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth)
			: 1;
		using (_sync.EnterScope())
			_marketSubscriptions.Add(message.TransactionId, new()
			{
				Market = market,
				DataType = dataType,
				Depth = depth,
				Remaining = message.Count,
			});
		try
		{
			await SendCachedBookAsync(market.Market, message.TransactionId, dataType, depth, cancellationToken);
			await UpdateSocketsAsync(market.Market, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_marketSubscriptions.Remove(message.TransactionId);
			await CleanupSocketsAsync(market.Market, cancellationToken);
			throw;
		}
		if (message.Count is null)
			await SendSubscriptionOnlineAsync(message.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!message.IsSubscribe)
		{
			await RemoveMarketSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(message, cancellationToken);
			return;
		}

		if (message.From is not null || message.To is not null)
			throw new NotSupportedException("Paxos exposes live executions through WebSocket, not historical ticks.");
		EnsureAuthenticated();
		var market = GetMarket(message.SecurityId);

		using (_sync.EnterScope())
			_marketSubscriptions.Add(message.TransactionId, new()
			{
				Market = market,
				DataType = DataType.Ticks,
				Depth = 0,
				Remaining = message.Count,
			});
		try
		{
			await UpdateSocketsAsync(market.Market, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_marketSubscriptions.Remove(message.TransactionId);
			await CleanupSocketsAsync(market.Market, cancellationToken);
			throw;
		}
		if (message.Count is null)
			await SendSubscriptionOnlineAsync(message.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!message.IsSubscribe)
			return;
		if (message.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(message, cancellationToken);
			return;
		}

		var market = GetMarket(message.SecurityId);
		var timeFrame = message.GetTimeFrame();
		var increment = timeFrame.ToPaxosIncrement();
		var from = message.From?.EnsureUtc();
		var to = (message.To ?? CurrentTime).EnsureUtc();
		if (from is DateTime start && start > to)
			throw new ArgumentOutOfRangeException(nameof(message),
				"Paxos candle start time cannot be later than end time.");
		var count = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var candles = await RestClient.GetCandlesAsync(market.Market, increment,
			from, to, PageSize, count, cancellationToken);

		foreach (var candle in candles.Where(static candle => candle is not null)
			.OrderBy(static candle => candle.Timestamp.ToPaxosTime(
				DateTime.UnixEpoch)).TakeLast(count))
			await SendCandleAsync(market.Market, candle, timeFrame,
				message.TransactionId, cancellationToken);

		await CompleteMarketSubscriptionAsync(message, cancellationToken);
	}

	private async ValueTask RemoveMarketSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription;
		using (_sync.EnterScope())
			_marketSubscriptions.Remove(transactionId, out subscription);
		if (subscription is not null)
			await UpdateSocketsAsync(subscription.Market.Market,
				cancellationToken);
	}

	private async ValueTask UpdateSocketsAsync(string market,
		CancellationToken cancellationToken)
	{
		bool needsMarketData;
		bool needsExecutions;
		using (_sync.EnterScope())
		{
			var subscriptions = _marketSubscriptions.Values.Where(item =>
				item.Market.Market.EqualsIgnoreCase(market)).ToArray();
			needsMarketData = subscriptions.Any(item => item.DataType ==
				DataType.Level1 || item.DataType == DataType.MarketDepth);
			needsExecutions = subscriptions.Any(item => item.DataType ==
				DataType.Level1 || item.DataType == DataType.Ticks);
		}
		if (needsMarketData)
			await EnsureSocketAsync(market, PaxosSocketFeeds.MarketData,
				cancellationToken);
		else
			await RemoveSocketAsync(market, PaxosSocketFeeds.MarketData,
				cancellationToken);
		if (needsExecutions)
			await EnsureSocketAsync(market, PaxosSocketFeeds.Executions,
				cancellationToken);
		else
			await RemoveSocketAsync(market, PaxosSocketFeeds.Executions,
				cancellationToken);
	}

	private async ValueTask CleanupSocketsAsync(string market,
		CancellationToken cancellationToken)
	{
		try
		{
			await UpdateSocketsAsync(market, cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			this.AddWarningLog(
				"Paxos socket cleanup for {0} failed: {1}", market,
				error.Message);
		}
	}

	private async ValueTask EnsureSocketAsync(string market,
		PaxosSocketFeeds feed, CancellationToken cancellationToken)
	{
		PaxosSocketClient socket;
		var sockets = feed == PaxosSocketFeeds.MarketData
			? _marketSockets
			: _executionSockets;
		using (_sync.EnterScope())
		{
			if (sockets.TryGetValue(market, out socket))
				return;
			socket = new(SocketEndpoint, market, feed,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount, RestClient.GetAccessTokenAsync)
			{
				Parent = this,
			};
			SubscribeSocket(socket);
			sockets.Add(market, socket);
		}
		try
		{
			await socket.ConnectAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				sockets.Remove(market);
			UnsubscribeSocket(socket);
			socket.Dispose();
			throw;
		}
	}

	private async ValueTask RemoveSocketAsync(string market,
		PaxosSocketFeeds feed, CancellationToken cancellationToken)
	{
		PaxosSocketClient socket;
		var sockets = feed == PaxosSocketFeeds.MarketData
			? _marketSockets
			: _executionSockets;
		using (_sync.EnterScope())
			sockets.Remove(market, out socket);
		if (socket is null)
			return;
		UnsubscribeSocket(socket);
		try
		{
			await socket.DisconnectAsync(cancellationToken);
		}
		finally
		{
			socket.Dispose();
			if (feed == PaxosSocketFeeds.MarketData)
				using (_sync.EnterScope())
					_books.Remove(market);
		}
	}

	private void SubscribeSocket(PaxosSocketClient socket)
	{
		socket.BookSnapshotReceived += OnBookSnapshotAsync;
		socket.BookUpdateReceived += OnBookUpdateAsync;
		socket.ExecutionReceived += OnPublicExecutionAsync;
		socket.Error += OnSocketErrorAsync;
	}

	private void UnsubscribeSocket(PaxosSocketClient socket)
	{
		socket.BookSnapshotReceived -= OnBookSnapshotAsync;
		socket.BookUpdateReceived -= OnBookUpdateAsync;
		socket.ExecutionReceived -= OnPublicExecutionAsync;
		socket.Error -= OnSocketErrorAsync;
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnBookSnapshotAsync(PaxosBookMessage snapshot,
		CancellationToken cancellationToken)
	{
		if (snapshot?.Market.IsEmpty() != false)
			throw new InvalidDataException(
				"Paxos returned an incomplete order-book snapshot.");
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		QuoteChange[] bids;
		QuoteChange[] asks;
		var time = snapshot.Time.ToPaxosTime(CurrentTime.EnsureUtc());
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(snapshot.Market, out var state))
				_books[snapshot.Market] = state = new();
			state.Bids.Clear();
			state.Asks.Clear();
			ApplySnapshotLevels(state.Bids, snapshot.Bids);
			ApplySnapshotLevels(state.Asks, snapshot.Asks);
			state.ServerTime = time;
			bids = ToQuotes(state.Bids, MarketDepth);
			asks = ToQuotes(state.Asks, MarketDepth);
			subscriptions = [.. _marketSubscriptions.Where(item =>
				item.Value.Market.Market.EqualsIgnoreCase(snapshot.Market) &&
				(item.Value.DataType == DataType.Level1 ||
					item.Value.DataType == DataType.MarketDepth))];
		}

		foreach (var (transactionId, subscription) in subscriptions)
		{
			if (!TryTakeMarketData(transactionId, out var finished))
				continue;
			if (subscription.DataType == DataType.Level1)
				await SendBestQuotesAsync(snapshot.Market, bids, asks, time,
					transactionId, cancellationToken);
			else
				await SendOutMessageAsync(new QuoteChangeMessage
				{
					SecurityId = ToSecurityId(snapshot.Market),
					ServerTime = time,
					OriginalTransactionId = transactionId,
					State = QuoteChangeStates.SnapshotComplete,
					Bids = [.. bids.Take(subscription.Depth)],
					Asks = [.. asks.Take(subscription.Depth)],
				}, cancellationToken);
			if (finished)
				await FinishMarketDataAsync(transactionId, snapshot.Market, cancellationToken);
		}
	}

	private async ValueTask OnBookUpdateAsync(PaxosBookMessage update,
		CancellationToken cancellationToken)
	{
		if (update?.Market.IsEmpty() != false)
			throw new InvalidDataException(
				"Paxos returned an incomplete order-book update.");
		var changedBids = ConvertLevels(update.Bids, true);
		var changedAsks = ConvertLevels(update.Asks, false);
		var time = update.Time.ToPaxosTime(CurrentTime.EnsureUtc());
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		QuoteChange[] bids;
		QuoteChange[] asks;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(update.Market, out var state))
				throw new InvalidDataException("Paxos order-book update arrived before its snapshot.");
			ApplySnapshotLevels(state.Bids, update.Bids);
			ApplySnapshotLevels(state.Asks, update.Asks);
			state.ServerTime = time;
			bids = ToQuotes(state.Bids, 1);
			asks = ToQuotes(state.Asks, 1);
			subscriptions = [.. _marketSubscriptions.Where(item =>
				item.Value.Market.Market.EqualsIgnoreCase(update.Market) &&
				(item.Value.DataType == DataType.Level1 ||
					item.Value.DataType == DataType.MarketDepth))];
		}

		foreach (var (transactionId, subscription) in subscriptions)
		{
			if (!TryTakeMarketData(transactionId, out var finished))
				continue;
			if (subscription.DataType == DataType.Level1)
				await SendBestQuotesAsync(update.Market, bids, asks, time,
					transactionId, cancellationToken);
			else
				await SendOutMessageAsync(new QuoteChangeMessage
				{
					SecurityId = ToSecurityId(update.Market),
					ServerTime = time,
					OriginalTransactionId = transactionId,
					State = QuoteChangeStates.Increment,
					Bids = changedBids,
					Asks = changedAsks,
				}, cancellationToken);
			if (finished)
				await FinishMarketDataAsync(transactionId, update.Market, cancellationToken);
		}
	}

	private async ValueTask OnPublicExecutionAsync(
		PaxosPublicExecution execution, CancellationToken cancellationToken)
	{
		if (execution?.Market.IsEmpty() != false)
			throw new InvalidDataException(
				"Paxos returned an incomplete public execution.");
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _marketSubscriptions.Where(item =>
				item.Value.Market.Market.EqualsIgnoreCase(execution.Market) &&
				(item.Value.DataType == DataType.Ticks ||
					item.Value.DataType == DataType.Level1))];

		foreach (var (transactionId, subscription) in subscriptions)
		{
			if (!TryTakeMarketData(transactionId, out var finished))
				continue;
			await SendPublicExecutionAsync(execution.Market, execution,
				transactionId, subscription.DataType, cancellationToken);
			if (finished)
				await FinishMarketDataAsync(transactionId, execution.Market, cancellationToken);
		}
	}

	private bool TryTakeMarketData(long transactionId, out bool finished)
	{
		finished = false;
		using (_sync.EnterScope())
		{
			if (!_marketSubscriptions.TryGetValue(transactionId, out var subscription))
				return false;
			if (subscription.Remaining is { } remaining)
			{
				subscription.Remaining = remaining - 1;
				if (remaining <= 1)
				{
					_marketSubscriptions.Remove(transactionId);
					finished = true;
				}
			}
			return true;
		}
	}

	private async ValueTask FinishMarketDataAsync(long transactionId, string market, CancellationToken cancellationToken)
	{
		await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
		await CleanupSocketsAsync(market, cancellationToken);
	}

	private async ValueTask SendPublicExecutionAsync(string market,
		PaxosPublicExecution execution, long transactionId, DataType dataType,
		CancellationToken cancellationToken)
	{
		var price = execution.Price.ParsePaxosAmount();
		var volume = execution.Amount.ParsePaxosAmount();
		if (price <= 0 || volume <= 0)
			return;
		var time = execution.ExecutedAt.ToPaxosTime(CurrentTime.EnsureUtc());
		if (dataType == DataType.Level1)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ToSecurityId(market),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(Level1Fields.LastTradePrice, price)
			.TryAdd(Level1Fields.LastTradeVolume, volume)
			.TryAdd(Level1Fields.LastTradeTime, time), cancellationToken);
			return;
		}
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = ToSecurityId(market),
			TradePrice = price,
			TradeVolume = volume,
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private async ValueTask SendCachedBookAsync(string market, long transactionId,
		DataType dataType, int depth, CancellationToken cancellationToken)
	{
		QuoteChange[] bids;
		QuoteChange[] asks;
		DateTime time;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(market, out var state))
				return;
			bids = ToQuotes(state.Bids, depth);
			asks = ToQuotes(state.Asks, depth);
			time = state.ServerTime;
		}
		if (!TryTakeMarketData(transactionId, out var finished))
			return;
		if (dataType == DataType.Level1)
			await SendBestQuotesAsync(market, bids, asks, time, transactionId, cancellationToken);
		else
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = ToSecurityId(market),
				ServerTime = time,
				OriginalTransactionId = transactionId,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = bids,
				Asks = asks,
			}, cancellationToken);
		if (finished)
			await FinishMarketDataAsync(transactionId, market, cancellationToken);
	}

	private ValueTask SendBestQuotesAsync(string market, QuoteChange[] bids,
		QuoteChange[] asks, DateTime time, long transactionId,
		CancellationToken cancellationToken)
	{
		var bid = bids.FirstOrDefault();
		var ask = asks.FirstOrDefault();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ToSecurityId(market),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid.Price > 0 ? bid.Price : null)
		.TryAdd(Level1Fields.BestBidVolume, bid.Volume > 0 ? bid.Volume : null)
		.TryAdd(Level1Fields.BestAskPrice, ask.Price > 0 ? ask.Price : null)
		.TryAdd(Level1Fields.BestAskVolume, ask.Volume > 0 ? ask.Volume : null)
		.TryAdd(Level1Fields.State, ToSecurityState(GetMarket(ToSecurityId(market)).MarketStatus)),
			cancellationToken);
	}

	private ValueTask SendCandleAsync(string market, PaxosCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.ToPaxosTime(DateTime.UnixEpoch);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = ToSecurityId(market),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open.ParsePaxosAmount(),
			HighPrice = candle.High.ParsePaxosAmount(),
			LowPrice = candle.Low.ParsePaxosAmount(),
			ClosePrice = candle.Close.ParsePaxosAmount(),
			TotalVolume = candle.Volume.ParsePaxosAmount(),
			State = CandleStates.Finished,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private static void ApplySnapshotLevels(
		SortedDictionary<decimal, decimal> destination,
		IEnumerable<PaxosBookLevel> levels)
	{
		foreach (var level in levels ?? [])
		{
			if (level is null)
				continue;
			var price = level.Price.ParsePaxosAmount();
			var amount = level.Amount.ParsePaxosAmount();
			if (price <= 0 || amount < 0)
				throw new InvalidDataException(
					"Paxos returned an invalid order-book level.");
			if (amount == 0)
				destination.Remove(price);
			else
				destination[price] = amount;
		}
	}

	private static QuoteChange[] ToQuotes(
		SortedDictionary<decimal, decimal> levels, int depth)
		=> [.. levels.Take(depth).Select(static item =>
			new QuoteChange(item.Key, item.Value))];

	private static QuoteChange[] ConvertLevels(IEnumerable<PaxosBookLevel> levels,
		bool isBid)
	{
		var result = new List<QuoteChange>();

		foreach (var level in levels ?? [])
		{
			if (level is null)
				continue;
			var price = level.Price.ParsePaxosAmount();
			var amount = level.Amount.ParsePaxosAmount();
			if (price <= 0 || amount < 0)
				throw new InvalidDataException(
					"Paxos returned an invalid order-book level.");
			result.Add(new(price, amount));
		}

		return [.. (isBid
			? result.OrderByDescending(static quote => quote.Price)
			: result.OrderBy(static quote => quote.Price))];
	}

	private ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
		=> SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
}

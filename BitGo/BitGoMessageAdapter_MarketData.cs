namespace StockSharp.BitGo;

public partial class BitGoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
			!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.BitGo))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		var types = lookupMsg.GetSecurityTypes();
		if (types.Count > 0 &&
			!types.Contains(SecurityTypes.CryptoCurrency) &&
			!types.Contains(SecurityTypes.Currency))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		foreach (var product in await RestClient.GetProductsAsync(AccountId,
			cancellationToken))
			AddProduct(product);
		var requestedCode = lookupMsg.SecurityId.SecurityCode;
		var skip = Math.Max(0L, lookupMsg.Skip ?? 0);
		var left = Math.Max(0L, lookupMsg.Count ?? long.MaxValue);
		foreach (var product in GetProducts())
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!requestedCode.IsEmpty() &&
				!requestedCode.Equals(product.Name,
					StringComparison.OrdinalIgnoreCase) &&
				!requestedCode.Equals(product.Id,
					StringComparison.OrdinalIgnoreCase))
				continue;
			var security = CreateSecurity(product, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, types))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(mdMsg, DataType.Level1,
			cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(mdMsg, DataType.MarketDepth,
			cancellationToken);

	private async ValueTask ProcessMarketSubscriptionAsync(
		MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeMarketAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.IsHistoryOnly() || mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"BitGo WebSocket does not publish historical order books.");
		var product = GetProduct(mdMsg.SecurityId);
		var depth = dataType == DataType.MarketDepth
			? (mdMsg.MaxDepth ?? 50).Max(1).Min(1000)
			: 1;
		var isFirst = false;
		using (_sync.EnterScope())
		{
			isFirst = !_marketSubscriptions.Values.Any(subscription =>
				subscription.Product.GetKey().Equals(product.GetKey(),
					StringComparison.OrdinalIgnoreCase));
			_marketSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Product = product,
				DataType = dataType,
				Depth = depth,
			});
		}
		try
		{
			if (isFirst)
				await SocketClient.SubscribeBookAsync(product.GetKey(),
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask UnsubscribeMarketAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var isLast = false;
		using (_sync.EnterScope())
		{
			if (_marketSubscriptions.Remove(transactionId, out subscription))
			{
				isLast = !_marketSubscriptions.Values.Any(value =>
					value.Product.GetKey().Equals(
						subscription.Product.GetKey(),
						StringComparison.OrdinalIgnoreCase));
				if (isLast)
					_books.Remove(subscription.Product.GetKey());
			}
		}
		if (subscription is not null && isLast)
			await SocketClient.UnsubscribeBookAsync(
				subscription.Product.GetKey(), cancellationToken);
	}

	private async ValueTask OnBookReceivedAsync(BitGoBookMessage message,
		CancellationToken cancellationToken)
	{
		if (message?.Product.IsEmpty() != false)
			throw new InvalidDataException(
				"BitGo returned an order book without a product.");
		var product = GetProduct(message.Product) ?? throw new
			InvalidDataException("BitGo returned an unknown order-book product '" +
				message.Product + "'.");
		var time = message.Time.ToBitGoTime() ?? DateTime.UtcNow;
		UpdateServerTime(time);
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		QuoteChange[] bids;
		QuoteChange[] asks;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(product.GetKey(), out var state))
				_books[product.GetKey()] = state = new();
			if (message.Type == BitGoSocketMessageTypes.Snapshot)
			{
				state.Bids.Clear();
				state.Asks.Clear();
				state.IsInitialized = true;
			}
			else if (!state.IsInitialized)
			{
				return;
			}
			ApplyLevels(state.Bids, message.Bids);
			ApplyLevels(state.Asks, message.Asks);
			state.Time = time;
			bids = [.. state.Bids.Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];
			asks = [.. state.Asks.Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];
			subscriptions = [.. _marketSubscriptions.Where(pair =>
				pair.Value.Product.GetKey().Equals(product.GetKey(),
					StringComparison.OrdinalIgnoreCase))];
		}
		foreach (var (transactionId, subscription) in subscriptions)
		{
			if (subscription.DataType == DataType.Level1)
				await SendLevel1Async(product, bids, asks, transactionId, time,
					cancellationToken);
			else
				await SendOutMessageAsync(new QuoteChangeMessage
				{
					SecurityId = product.ToStockSharp(),
					ServerTime = time,
					OriginalTransactionId = transactionId,
					State = QuoteChangeStates.SnapshotComplete,
					Bids = [.. bids.Take(subscription.Depth)],
					Asks = [.. asks.Take(subscription.Depth)],
				}, cancellationToken);
		}
	}

	private ValueTask SendLevel1Async(BitGoProduct product,
		QuoteChange[] bids, QuoteChange[] asks, long transactionId,
		DateTime time, CancellationToken cancellationToken)
	{
		var bid = bids.FirstOrDefault();
		var ask = asks.FirstOrDefault();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = product.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid.Price > 0 ? bid.Price : null)
		.TryAdd(Level1Fields.BestBidVolume, bid.Volume > 0 ? bid.Volume : null)
		.TryAdd(Level1Fields.BestAskPrice, ask.Price > 0 ? ask.Price : null)
		.TryAdd(Level1Fields.BestAskVolume, ask.Volume > 0 ? ask.Volume : null)
		.TryAdd(Level1Fields.State, product.IsTradeDisabled
			? SecurityStates.Stoped
			: SecurityStates.Trading), cancellationToken);
	}

	private static void ApplyLevels(
		SortedDictionary<decimal, decimal> destination,
		BitGoBookLevel[] levels)
	{
		foreach (var level in levels ?? [])
		{
			if (level is null || level.Price <= 0 || level.Size < 0)
				throw new InvalidDataException(
					"BitGo returned an invalid order-book level.");
			if (level.Size == 0)
				destination.Remove(level.Price);
			else
				destination[level.Price] = level.Size;
		}
	}

	private static SecurityMessage CreateSecurity(BitGoProduct product,
		long transactionId)
	{
		var precision = product.QuoteDisplayPrecision is >= 0 and <= 28
			? product.QuoteDisplayPrecision
			: 8;
		var priceStep = 1m;
		for (var i = 0; i < precision; i++)
			priceStep /= 10m;
		var volumeStep = product.BaseIncrement.ToBitGoDecimal();
		return new()
		{
			SecurityId = product.ToStockSharp(),
			Name = product.Name,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = product.QuoteCurrency.ToCurrency(),
			PriceStep = priceStep,
			Decimals = precision,
			VolumeStep = volumeStep is > 0 ? volumeStep : null,
			MinVolume = product.BaseMinSize.ToBitGoDecimal(),
			MaxVolume = product.BaseMaxSize.ToBitGoDecimal(),
			OriginalTransactionId = transactionId,
		};
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}

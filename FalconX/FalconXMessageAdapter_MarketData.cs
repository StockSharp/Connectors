namespace StockSharp.FalconX;

public partial class FalconXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
			!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.FalconX))
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
		foreach (var pair in await RestClient.GetPairsAsync(cancellationToken) ?? [])
			AddPair(pair);
		FalconXTokenPair[] pairs;
		using (_sync.EnterScope())
			pairs = [.. _pairs.Values];
		var requestedCode = lookupMsg.SecurityId.SecurityCode;
		if (!requestedCode.IsEmpty())
		{
			try
			{
				requestedCode = requestedCode.ParseFalconXPair().GetKey();
			}
			catch (FormatException)
			{
				pairs = [];
			}
		}
		var skip = Math.Max(0L, lookupMsg.Skip ?? 0);
		var left = Math.Max(0L, lookupMsg.Count ?? long.MaxValue);
		foreach (var pair in pairs
			.Where(pair => requestedCode.IsEmpty() || pair.GetKey().Equals(
				requestedCode, StringComparison.OrdinalIgnoreCase))
			.OrderBy(static pair => pair.GetKey(),
				StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var security = CreateSecurity(pair, lookupMsg.TransactionId);
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
				"FalconX price streams do not publish historical market data.");
		var pair = GetPair(mdMsg.SecurityId);
		var depth = dataType == DataType.MarketDepth
			? (mdMsg.MaxDepth ?? QuoteLevels.Length).Max(1).Min(
				QuoteLevels.Length)
			: 1;
		using (_sync.EnterScope())
			_marketSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Pair = pair,
				DataType = dataType,
				Depth = depth,
			});
		try
		{
			await MarketSocket.SubscribeAsync(pair, QuoteLevels,
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
		using (_sync.EnterScope())
			_marketSubscriptions.Remove(transactionId, out subscription);
		if (subscription is not null)
			await MarketSocket.UnsubscribeAsync(subscription.Pair,
				cancellationToken);
	}

	private async ValueTask OnPricesReceivedAsync(FalconXPriceTick[] prices,
		CancellationToken cancellationToken)
	{
		foreach (var group in (prices ?? [])
			.Where(static price => price is not null &&
				!price.BaseToken.IsEmpty() && !price.QuoteToken.IsEmpty() &&
				price.Quantity > 0)
			.GroupBy(static price => price.BaseToken.ToUpperInvariant() + "/" +
				price.QuoteToken.ToUpperInvariant(),
				StringComparer.OrdinalIgnoreCase))
		{
			var ticks = group.OrderBy(static price => price.Quantity).ToArray();
			var time = ticks.Max(static price => price.Timestamp)
				.FromFalconXMilliseconds();
			UpdateServerTime(time);
			KeyValuePair<long, MarketSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _marketSubscriptions.Where(pair =>
					pair.Value.Pair.GetKey().Equals(group.Key,
						StringComparison.OrdinalIgnoreCase))];
			foreach (var (transactionId, subscription) in subscriptions)
			{
				if (subscription.DataType == DataType.Level1)
					await SendLevel1Async(subscription.Pair, ticks, transactionId,
						time, cancellationToken);
				else
					await SendDepthAsync(subscription.Pair, ticks, transactionId,
						subscription.Depth, time, cancellationToken);
			}
		}
	}

	private ValueTask SendLevel1Async(FalconXTokenPair pair,
		FalconXPriceTick[] prices, long transactionId, DateTime time,
		CancellationToken cancellationToken)
	{
		var bid = prices
			.Where(static price => price.SellPrice is not null)
			.OrderByDescending(static price => price.SellPrice)
			.ThenBy(static price => price.Quantity)
			.FirstOrDefault();
		var ask = prices
			.Where(static price => price.BuyPrice is not null)
			.OrderBy(static price => price.BuyPrice)
			.ThenBy(static price => price.Quantity)
			.FirstOrDefault();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid?.SellPrice)
		.TryAdd(Level1Fields.BestBidVolume, bid?.Quantity)
		.TryAdd(Level1Fields.BestAskPrice, ask?.BuyPrice)
		.TryAdd(Level1Fields.BestAskVolume, ask?.Quantity), cancellationToken);
	}

	private ValueTask SendDepthAsync(FalconXTokenPair pair,
		FalconXPriceTick[] prices, long transactionId, int depth, DateTime time,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = CreateQuotes(prices, true, depth),
			Asks = CreateQuotes(prices, false, depth),
		}, cancellationToken);

	private static QuoteChange[] CreateQuotes(FalconXPriceTick[] prices,
		bool isBid, int depth)
	{
		var previousQuantity = 0m;
		var previousNotional = 0m;
		var levels = new List<QuoteChange>();
		foreach (var item in prices.OrderBy(static price => price.Quantity))
		{
			var price = isBid ? item.SellPrice : item.BuyPrice;
			if (price is null)
				continue;
			var volume = item.Quantity - previousQuantity;
			if (volume <= 0)
				continue;
			var notional = item.Quantity * price.Value;
			var incrementalNotional = notional - previousNotional;
			previousQuantity = item.Quantity;
			previousNotional = notional;
			if (incrementalNotional > 0)
				levels.Add(new(incrementalNotional / volume, volume));
		}
		return [.. levels
			.GroupBy(static quote => quote.Price)
			.Select(static group => new QuoteChange(group.Key,
				group.Sum(static quote => quote.Volume)))
			.OrderBy(quote => quote.Price * (isBid ? -1 : 1))
			.Take(depth)];
	}

	private static SecurityMessage CreateSecurity(FalconXTokenPair pair,
		long transactionId)
	{
		var code = pair.GetKey();
		var securityType = pair.BaseToken.ToCurrency() is not null &&
			pair.QuoteToken.ToCurrency() is not null
				? SecurityTypes.Currency
				: SecurityTypes.CryptoCurrency;
		var volumeDecimals = pair.BaseToken is "SHIB" or "PEPE" or "BONK"
			? 4
			: 8;
		return new()
		{
			SecurityId = pair.ToStockSharp(),
			Name = code,
			SecurityType = securityType,
			Currency = pair.QuoteToken.ToCurrency(),
			PriceStep = 0.00000001m,
			Decimals = 8,
			VolumeStep = 1m / (decimal)Math.Pow(10, volumeDecimals),
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

namespace StockSharp.Osmosis;

public partial class OsmosisMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		OsmosisMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item => item.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Osmosis))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.SecurityCode))
				continue;
			var security = CreateSecurity(market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State, SecurityStates.Trading),
				cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
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
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
				_level1Fingerprints.Remove(
					mdMsg.OriginalTransactionId.ToString(
						CultureInfo.InvariantCulture));
			}
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Osmosis SQS does not expose historical Level1 snapshots.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(market, mdMsg.TransactionId, true,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
			};
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
			UnsubscribeTicks(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.IsHistoryOnly() ||
			mdMsg.To is DateTime end && end.ToUniversalTime() <= DateTime.UtcNow)
			throw new NotSupportedException(
				"The public Osmosis endpoint provides connector ticks as a live " +
				"CometBFT stream only.");
		var market = GetMarket(mdMsg.SecurityId);
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
				To = mdMsg.To?.ToUniversalTime(),
				Maximum = GetSubscriptionMaximum(mdMsg.Count),
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(OsmosisMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = $"{market.BaseToken.Symbol}/{market.QuoteToken.Symbol}",
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteToken.Symbol.ToCurrency(),
			PriceStep = DecimalStep(market.QuoteToken.Decimals),
			VolumeStep = DecimalStep(market.BaseToken.Decimals),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.BaseToken.Symbol);

	private async ValueTask<(decimal Bid, decimal Ask)> LoadLevel1Async(
		OsmosisMarket market, CancellationToken cancellationToken)
	{
		var amount = ProbeVolume.ToBaseUnits(market.BaseToken.Decimals);
		var bidQuote = await ApiClient.GetExactInputQuoteAsync(
			market.BaseToken.Denomination, market.QuoteToken.Denomination,
			amount, cancellationToken);
		var askQuote = await ApiClient.GetExactOutputQuoteAsync(
			market.QuoteToken.Denomination, market.BaseToken.Denomination,
			amount, cancellationToken);
		var bid = bidQuote.OutputAmount.FromBaseUnits(
			market.QuoteToken.Decimals) / ProbeVolume;
		var ask = askQuote.InputAmount.FromBaseUnits(
			market.QuoteToken.Decimals) / ProbeVolume;
		if (bid <= 0 || ask <= 0)
			throw new InvalidDataException(
				"Osmosis SQS returned a non-positive executable quote.");
		return (bid, ask);
	}

	private async ValueTask SendLevel1Async(OsmosisMarket market, long target,
		bool isForced, CancellationToken cancellationToken)
	{
		var snapshot = await LoadLevel1Async(market, cancellationToken);
		var fingerprint = new Level1Fingerprint(snapshot.Bid, snapshot.Ask);
		var key = target.ToString(CultureInfo.InvariantCulture);
		using (_sync.EnterScope())
		{
			if (!isForced && _level1Fingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_level1Fingerprints[key] = fingerprint;
		}
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
		}
		.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
		.TryAdd(Level1Fields.BestBidVolume, ProbeVolume)
		.TryAdd(Level1Fields.BestAskPrice, snapshot.Ask)
		.TryAdd(Level1Fields.BestAskVolume, ProbeVolume), cancellationToken);
	}

	private async ValueTask PollLevel1Async(
		CancellationToken cancellationToken)
	{
		(long Id, Level1Subscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Select(static pair =>
				(pair.Key, pair.Value))];
		foreach (var item in subscriptions)
		{
			try
			{
				await SendLevel1Async(item.Subscription.Market, item.Id, false,
					cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
	}

	private async ValueTask<bool> SendTradeAsync(OsmosisMarket market,
		long target, string identity, DateTime time, decimal price,
		decimal volume, Sides side, CancellationToken cancellationToken)
	{
		var key = new DeliveryKey(target, identity);
		using (_sync.EnterScope())
		{
			if (!_seenTrades.Add(key))
				return false;
			_tradeDeliveryOrder.Enqueue(key);
			while (_tradeDeliveryOrder.Count > _maximumDeliveryKeys)
				_seenTrades.Remove(_tradeDeliveryOrder.Dequeue());
		}
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = target,
			TradeStringId = identity,
			TradePrice = price,
			TradeVolume = volume,
			OriginSide = side,
		}, cancellationToken);
		return true;
	}

	private void UnsubscribeTicks(long target)
	{
		using (_sync.EnterScope())
			UnsubscribeTicksNoLock(target);
	}

	private void UnsubscribeTicksNoLock(long target)
	{
		_tickSubscriptions.Remove(target);
		_seenTrades.RemoveWhere(key => key.SubscriptionId == target);
		var retained = _tradeDeliveryOrder.Where(_seenTrades.Contains).ToArray();
		_tradeDeliveryOrder.Clear();
		foreach (var key in retained)
			_tradeDeliveryOrder.Enqueue(key);
	}

	private static decimal? DecimalStep(int decimals)
	{
		if (decimals is < 0 or > 28)
			return null;
		var result = 1m;
		for (var index = 0; index < decimals; index++)
			result /= 10m;
		return result;
	}

	private static int GetSubscriptionMaximum(long? count)
		=> count is null
			? int.MaxValue
			: count.Value.Min(int.MaxValue).Max(1).To<int>();

	private static void RemoveFingerprintPrefix<TValue>(
		IDictionary<string, TValue> values, long target)
	{
		var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
		foreach (var key in values.Keys.Where(key =>
			key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
			values.Remove(key);
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}

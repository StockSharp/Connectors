namespace StockSharp.PhillipPoems;

public partial class PhillipPoemsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		var requestedTypes = message.GetSecurityTypes();
		if (requestedTypes.Count > 0 && !requestedTypes.Contains(SecurityTypes.Stock) &&
			!requestedTypes.Contains(SecurityTypes.Etf))
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var exchange = message.SecurityId.BoardCode.ToNativeExchange();
		var market = _markets.FirstOrDefault(item =>
			item.Exchange.EqualsIgnoreCase(exchange))?.Market;
		var keyword = message.SecurityId.SecurityCode
			.IsEmpty(message.ShortName).IsEmpty(message.Name).IsEmpty(string.Empty);
		var requestedCount = message.Count ?? 100;
		var response = await _client.SearchCounters(new PoemsSecuritySearchRequest
		{
			Keyword = keyword,
			Market = market,
			Exchange = exchange,
			Count = (int)Math.Clamp(requestedCount + Math.Max(0, message.Skip ?? 0), 1, 500),
		}, cancellationToken);
		var counters = (response.CounterList ?? [])
			.Where(counter => counter?.Product.EqualsIgnoreCase("ST") == true)
			.ToArray();
		CacheCounters(counters);

		var skip = Math.Max(0, message.Skip ?? 0);
		var left = Math.Max(0, message.Count ?? long.MaxValue);
		foreach (var counter in counters)
		{
			var security = CreateSecurity(counter, message.TransactionId);
			if (!security.IsMatch(message, requestedTypes))
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
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			_level1Subscriptions.Remove(message.OriginalTransactionId);
			return;
		}

		var counter = await ResolveCounter(message.SecurityId, cancellationToken);
		var securityId = counter.ToSecurityId(DefaultExchange);
		var response = await _client.GetPrices([counter.CounterId], cancellationToken);
		foreach (var price in response.PriceList ?? [])
		{
			CachePrice(price);
			await SendLevel1(message.TransactionId, securityId, price, cancellationToken);
		}
		if (!message.IsHistoryOnly())
			_level1Subscriptions[message.TransactionId] = new()
			{
				SecurityId = securityId,
				Counter = counter,
			};
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			_tickSubscriptions.Remove(message.OriginalTransactionId);
			return;
		}

		var counter = await ResolveCounter(message.SecurityId, cancellationToken);
		var subscription = new TickSubscription
		{
			SecurityId = counter.ToSecurityId(DefaultExchange),
			Counter = counter,
		};
		await SendTicks(message.TransactionId, subscription, message.From, message.To,
			message.Count, true, cancellationToken);
		if (!message.IsHistoryOnly())
			_tickSubscriptions[message.TransactionId] = subscription;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			_depthSubscriptions.Remove(message.OriginalTransactionId);
			return;
		}

		var counter = await ResolveCounter(message.SecurityId, cancellationToken);
		var subscription = new MarketSubscription
		{
			SecurityId = counter.ToSecurityId(DefaultExchange),
			Counter = counter,
			MaxDepth = Math.Clamp(message.MaxDepth ?? 20, 1, 20),
		};
		await RefreshDepth(message.TransactionId, subscription, cancellationToken);
		if (!message.IsHistoryOnly())
			_depthSubscriptions[message.TransactionId] = subscription;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async Task RefreshLevel1(CancellationToken cancellationToken)
	{
		var subscriptions = _level1Subscriptions.ToArray();
		var counters = subscriptions.Select(pair => pair.Value.Counter)
			.Where(counter => counter?.CounterId.IsEmpty() == false)
			.GroupBy(counter => counter.CounterId, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First()).ToArray();
		if (counters.Length == 0)
			return;

		var start = _level1Cursor % counters.Length;
		var batch = Enumerable.Range(0, Math.Min(20, counters.Length))
			.Select(index => counters[(start + index) % counters.Length]).ToArray();
		_level1Cursor = (start + batch.Length) % counters.Length;
		var response = await _client.GetPrices(batch.Select(counter => counter.CounterId).ToArray(),
			cancellationToken);
		foreach (var price in response.PriceList ?? [])
		{
			var counter = CachePrice(price);
			foreach (var pair in subscriptions.Where(pair =>
				pair.Value.Counter.CounterId.EqualsIgnoreCase(price.CounterId) ||
				pair.Value.SecurityId.SecurityCode.EqualsIgnoreCase(price.Symbol)))
				await SendLevel1(pair.Key, pair.Value.SecurityId, price, cancellationToken);
			if (counter != null)
				CacheCounter(counter);
		}
	}

	private ValueTask SendLevel1(long originalTransactionId, SecurityId securityId,
		PoemsPrice price, CancellationToken cancellationToken)
	{
		if (price == null)
			return default;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			ServerTime = price.LastDoneDate.ToUtc(securityId.BoardCode, CurrentTime),
		}
		.TryAdd(Level1Fields.LastTradePrice, price.LastDone.ToDecimalValue())
		.TryAdd(Level1Fields.BestBidPrice, price.Bid.ToDecimalValue())
		.TryAdd(Level1Fields.BestBidVolume, price.BidVolumeK.ToThousands())
		.TryAdd(Level1Fields.BestAskPrice, price.Ask.ToDecimalValue())
		.TryAdd(Level1Fields.BestAskVolume, price.AskVolumeK.ToThousands())
		.TryAdd(Level1Fields.OpenPrice, price.Open.ToDecimalValue())
		.TryAdd(Level1Fields.HighPrice, price.High.ToDecimalValue())
		.TryAdd(Level1Fields.LowPrice, price.Low.ToDecimalValue())
		.TryAdd(Level1Fields.ClosePrice, price.PreviousClose.ToDecimalValue())
		.TryAdd(Level1Fields.Change, price.Change.ToDecimalValue())
		.TryAdd(Level1Fields.Volume, price.VolumeK.ToThousands()), cancellationToken);
	}

	private async Task RefreshNextTicks(CancellationToken cancellationToken)
	{
		var subscriptions = _tickSubscriptions.ToArray();
		if (subscriptions.Length == 0)
			return;
		var pair = subscriptions[_tickCursor++ % subscriptions.Length];
		await SendTicks(pair.Key, pair.Value, null, null, 100, false, cancellationToken);
	}

	private async Task SendTicks(long originalTransactionId, TickSubscription subscription,
		DateTime? from, DateTime? to, long? requestedCount, bool isInitial,
		CancellationToken cancellationToken)
	{
		var exchange = subscription.SecurityId.BoardCode.IsEmpty(DefaultExchange);
		var currentLocal = CurrentTime.ToExchangeLocal(exchange);
		if (from != null && from.Value.ToExchangeLocal(exchange).Date != currentLocal.Date)
			throw new NotSupportedException(
				"The documented POEMS stock time-and-sales endpoint exposes the current exchange day only.");
		if (to != null && to.Value.ToExchangeLocal(exchange).Date != currentLocal.Date)
			throw new NotSupportedException(
				"The documented POEMS stock time-and-sales endpoint exposes the current exchange day only.");

		if (subscription.TradeDate != currentLocal.Date)
		{
			subscription.SeenCounts.Clear();
			subscription.TradeDate = currentLocal.Date;
		}

		var left = Math.Max(0, requestedCount ?? (isInitial ? long.MaxValue : 100));
		var page = 1;
		do
		{
			var size = (int)Math.Clamp(left == long.MaxValue ? 100 : left, 1, 100);
			var response = await _client.GetTimeSales(subscription.Counter.CounterId,
				from?.ToExchangeLocal(exchange).ToString("HH:mm", CultureInfo.InvariantCulture),
				to?.ToExchangeLocal(exchange).ToString("HH:mm", CultureInfo.InvariantCulture),
				size, page, cancellationToken);
			var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var trade in (response.TimeSales ?? [])
				.OrderBy(item => item.Time.ToUtc(exchange, CurrentTime)))
			{
				var price = trade.Price.ToDecimalValue();
				var volume = trade.Volume.ToDecimalValue();
				if (price == null || volume == null)
					continue;
				var serverTime = trade.Time.ToUtc(exchange, CurrentTime);
				var key = $"{serverTime:O}|{price.Value.ToString(CultureInfo.InvariantCulture)}|" +
					volume.Value.ToString(CultureInfo.InvariantCulture);
				var occurrence = occurrences.TryGetValue(key, out var count) ? count + 1 : 1;
				occurrences[key] = occurrence;
				var seen = subscription.SeenCounts.TryGetValue(key, out count) ? count : 0;
				if (occurrence <= seen)
					continue;
				subscription.SeenCounts[key] = occurrence;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = originalTransactionId,
					SecurityId = subscription.SecurityId,
					TradeStringId = $"{subscription.Counter.CounterId}|{serverTime:yyyyMMddHHmmss}|" +
						$"{price.Value.ToString(CultureInfo.InvariantCulture)}|{occurrence}",
					TradePrice = price,
					TradeVolume = volume,
					ServerTime = serverTime,
				}, cancellationToken);
				if (left != long.MaxValue && --left <= 0)
					break;
			}
			if (!isInitial || left <= 0 || page >= Math.Max(1, response.TotalPages))
				break;
			page++;
		}
		while (true);
	}

	private async Task RefreshNextDepth(CancellationToken cancellationToken)
	{
		var subscriptions = _depthSubscriptions.ToArray();
		if (subscriptions.Length == 0)
			return;
		var pair = subscriptions[_depthCursor++ % subscriptions.Length];
		await RefreshDepth(pair.Key, pair.Value, cancellationToken);
	}

	private async Task RefreshDepth(long originalTransactionId,
		MarketSubscription subscription, CancellationToken cancellationToken)
	{
		var response = await _client.GetMarketDepth(subscription.Counter.CounterId,
			cancellationToken);
		var entries = response.MarketDepth ?? [];
		var bids = entries.Select(entry => (entry,
			price: entry.BidPrice.IsEmpty(entry.BuyPrice).ToDecimalValue(),
			volume: entry.BidVolume.IsEmpty(entry.BidVolumeShort)
				.IsEmpty(entry.BuyVolume).ToDecimalValue()))
			.Where(item => item.price != null && item.volume != null)
			.OrderByDescending(item => item.price)
			.Take(subscription.MaxDepth)
			.Select(item => new QuoteChange(item.price.Value, item.volume.Value)
			{
				OrdersCount = item.entry.BidOrders,
			}).ToArray();
		var asks = entries.Select(entry => (entry,
			price: entry.AskPrice.IsEmpty(entry.SellPrice).ToDecimalValue(),
			volume: entry.AskVolume.IsEmpty(entry.AskVolumeShort)
				.IsEmpty(entry.SellVolume).ToDecimalValue()))
			.Where(item => item.price != null && item.volume != null)
			.OrderBy(item => item.price)
			.Take(subscription.MaxDepth)
			.Select(item => new QuoteChange(item.price.Value, item.volume.Value)
			{
				OrdersCount = item.entry.AskOrders,
			}).ToArray();
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = CurrentTime,
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private SecurityMessage CreateSecurity(PoemsCounter counter, long originalTransactionId)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = counter.ToSecurityId(DefaultExchange),
			SecurityType = counter.ToSecurityType(),
			Name = counter.Name.IsEmpty(counter.NameDisplay),
			ShortName = counter.SymbolDisplay.IsEmpty(counter.Symbol),
			Class = counter.Product,
		};
}

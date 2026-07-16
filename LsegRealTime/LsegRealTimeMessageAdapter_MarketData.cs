namespace StockSharp.LsegRealTime;

partial class LsegRealTimeMessageAdapter
{
	private sealed class LsegDepthLevel
	{
		public Sides Side { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
		public int? OrdersCount { get; init; }
	}

	private sealed class LsegDepthBook
	{
		private readonly object _sync = new();
		private readonly Dictionary<string, LsegDepthLevel> _levels = new(StringComparer.Ordinal);
		private bool _isSnapshotBuilding;

		public QuoteChangeMessage Apply(SecurityId securityId, LsegDepthUpdate update)
		{
			lock (_sync)
			{
				var serverTime = GetDepthTime(update);
				if (update.IsRefresh)
				{
					var isFirstPart = !_isSnapshotBuilding || update.IsClearCache;
					if (isFirstPart)
						_levels.Clear();
					_isSnapshotBuilding = !update.IsComplete;

					foreach (var entry in update.Entries)
						ApplyEntry(entry, null, null);

					return new QuoteChangeMessage
					{
						OriginalTransactionId = update.SubscriptionId,
						SecurityId = securityId,
						ServerTime = serverTime,
						SeqNum = update.Sequence,
						Bids = ToSnapshot(Sides.Buy),
						Asks = ToSnapshot(Sides.Sell),
						State = update.IsComplete
							? QuoteChangeStates.SnapshotComplete
							: isFirstPart ? QuoteChangeStates.SnapshotStarted : QuoteChangeStates.SnapshotBuilding,
					};
				}

				var isReset = update.IsClearCache;
				if (isReset)
				{
					_levels.Clear();
					_isSnapshotBuilding = false;
				}

				var bids = new List<QuoteChange>();
				var asks = new List<QuoteChange>();
				foreach (var entry in update.Entries)
					ApplyEntry(entry, bids, asks);
				if (isReset)
				{
					return new QuoteChangeMessage
					{
						OriginalTransactionId = update.SubscriptionId,
						SecurityId = securityId,
						ServerTime = serverTime,
						SeqNum = update.Sequence,
						Bids = ToSnapshot(Sides.Buy),
						Asks = ToSnapshot(Sides.Sell),
						State = QuoteChangeStates.SnapshotComplete,
					};
				}

				return new QuoteChangeMessage
				{
					OriginalTransactionId = update.SubscriptionId,
					SecurityId = securityId,
					ServerTime = serverTime,
					SeqNum = update.Sequence,
					Bids = [.. bids],
					Asks = [.. asks],
					State = QuoteChangeStates.Increment,
				};
			}
		}

		private void ApplyEntry(LsegWireMapEntry entry, List<QuoteChange> bids, List<QuoteChange> asks)
		{
			if (entry == null)
				return;

			var fields = entry.Fields;
			var key = entry.Key;
			if (key.IsEmpty())
				key = $"{fields?.OrderSide}:{fields?.OrderPrice?.ToString(CultureInfo.InvariantCulture)}";

			_levels.TryGetValue(key, out var previous);
			var action = entry.Action.ToLsegAction();
			if (action == QuoteChangeActions.Delete)
			{
				_levels.Remove(key);
				var deletedSide = previous?.Side ?? fields?.OrderSide.ToLsegSide();
				var deletedPrice = previous?.Price ?? fields?.OrderPrice;
				if (deletedSide != null && deletedPrice != null)
					AddChange(deletedSide.Value, new QuoteChange(deletedPrice.Value, 0), bids, asks);
				return;
			}

			var side = fields?.OrderSide.ToLsegSide() ?? previous?.Side;
			var price = fields?.OrderPrice ?? previous?.Price;
			if (side == null || price == null)
				return;

			var volume = fields?.AccumulatedSize ?? previous?.Volume ?? 0;
			var ordersCount = fields?.OrdersCount ?? previous?.OrdersCount;
			if (volume <= 0)
			{
				_levels.Remove(key);
				AddChange(side.Value, new QuoteChange(price.Value, 0), bids, asks);
				return;
			}

			var current = new LsegDepthLevel
			{
				Side = side.Value,
				Price = price.Value,
				Volume = volume,
				OrdersCount = ordersCount,
			};
			_levels[key] = current;

			if (previous != null && (previous.Side != current.Side || previous.Price != current.Price))
			{
				AddChange(previous.Side, new QuoteChange(previous.Price, 0), bids, asks);
			}

			AddChange(current.Side, new QuoteChange(current.Price, current.Volume, current.OrdersCount), bids, asks);
		}

		private QuoteChange[] ToSnapshot(Sides side)
		{
			var levels = _levels.Values.Where(level => level.Side == side);
			levels = side == Sides.Buy
				? levels.OrderByDescending(level => level.Price)
				: levels.OrderBy(level => level.Price);
			return levels
				.Select(level => new QuoteChange(level.Price, level.Volume, level.OrdersCount))
				.ToArray();
		}

		private static void AddChange(Sides side, QuoteChange change, List<QuoteChange> bids, List<QuoteChange> asks)
		{
			if (bids == null || asks == null)
				return;
			(side == Sides.Buy ? bids : asks).Add(change);
		}

		private static DateTime GetDepthTime(LsegDepthUpdate update)
		{
			var fields = update.Entries.Select(entry => entry?.Fields).FirstOrDefault(value => value != null);
			return fields == null
				? update.ReceivedTime.UtcKind()
				: fields.LevelDate.ToLsegTime(fields.LevelTimeMilliseconds, update.ReceivedTime);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var ric = message.SecurityId.Ric.IsEmpty(message.SecurityId.SecurityCode);
		if (ric.IsEmpty())
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var snapshot = await GetClient().GetSnapshotAsync(ric, cancellationToken);
		var fields = snapshot.Fields;
		var boardCode = fields.ToLsegBoard();
		var security = new SecurityMessage
		{
			OriginalTransactionId = message.TransactionId,
			SecurityId = new SecurityId
			{
				SecurityCode = snapshot.Ric.IsEmpty(ric),
				BoardCode = boardCode,
				Ric = snapshot.Ric.IsEmpty(ric),
			},
			Name = fields.DisplayName,
			SecurityType = ric.ToLsegSecurityType(fields.RecordType),
			Currency = fields.Currency.ToLsegCurrency(),
			VolumeStep = fields.LotSize,
			MinVolume = fields.LotSize,
		};

		if (security.IsMatch(message, message.GetSecurityTypes()))
			await SendOutMessageAsync(security, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(message, LsegMarketDataKinds.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(message, LsegMarketDataKinds.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(message, LsegMarketDataKinds.MarketDepth, cancellationToken);

	private async ValueTask ProcessMarketSubscriptionAsync(
		MarketDataMessage message,
		LsegMarketDataKinds kind,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var client = GetClient();
		if (!message.IsSubscribe)
		{
			await client.UnsubscribeAsync(message.OriginalTransactionId, cancellationToken);
			_marketSubscriptions.TryRemove(message.OriginalTransactionId, out _);
			_depthBooks.TryRemove(message.OriginalTransactionId, out _);
			return;
		}

		if (message.IsHistoryOnly())
			throw new NotSupportedException("The LSEG Real-Time WebSocket API does not provide historical market data.");

		var ric = message.SecurityId.Ric.IsEmpty(message.SecurityId.SecurityCode)
			.ThrowIfEmpty(nameof(message.SecurityId.SecurityCode));
		var securityId = message.SecurityId;
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(ric);
		securityId.BoardCode = securityId.BoardCode.IsEmpty("LSEG");
		securityId.Ric = ric;
		var subscription = new LsegMarketSubscription
		{
			SecurityId = securityId,
			Kind = kind,
		};
		if (!_marketSubscriptions.TryAdd(message.TransactionId, subscription))
			throw new InvalidOperationException($"LSEG subscription {message.TransactionId} already exists.");
		if (kind == LsegMarketDataKinds.MarketDepth)
			_depthBooks[message.TransactionId] = new LsegDepthBook();

		try
		{
			await client.SubscribeAsync(message.TransactionId, ric,
				kind == LsegMarketDataKinds.MarketDepth
					? LsegSubscriptionKinds.MarketByPrice
					: LsegSubscriptionKinds.MarketPrice,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.TryRemove(message.TransactionId, out _);
			_depthBooks.TryRemove(message.TransactionId, out _);
			throw;
		}
	}

	private ValueTask ProcessMarketPriceAsync(LsegMarketPriceUpdate update, CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(update.SubscriptionId, out var subscription))
			return default;

		var fields = update.Fields;
		var tradeTime = fields.TradeDate.ToLsegTime(fields.TradeTimeMilliseconds, update.ReceivedTime);
		if (subscription.Kind == LsegMarketDataKinds.Ticks)
		{
			if (fields.TradePrice == null)
				return default;
			return SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = update.SubscriptionId,
				SecurityId = subscription.SecurityId,
				TradeId = update.EventId,
				TradePrice = fields.TradePrice.Value,
				TradeVolume = fields.TradeVolume,
				ServerTime = tradeTime,
				SeqNum = update.Sequence,
			}, cancellationToken);
		}

		if (subscription.Kind != LsegMarketDataKinds.Level1)
			return default;

		var quoteTime = fields.TradeDate.ToLsegTime(fields.QuoteTimeMilliseconds, update.ReceivedTime);
		var level1 = new Level1ChangeMessage
		{
			OriginalTransactionId = update.SubscriptionId,
			SecurityId = subscription.SecurityId,
			ServerTime = quoteTime,
			SeqNum = update.Sequence,
		}
		.TryAdd(Level1Fields.LastTradePrice, fields.TradePrice)
		.TryAdd(Level1Fields.LastTradeVolume, fields.TradeVolume)
		.TryAdd(Level1Fields.LastTradeTime, fields.TradeTimeMilliseconds == null ? null : tradeTime)
		.TryAdd(Level1Fields.BestBidPrice, fields.Bid)
		.TryAdd(Level1Fields.BestBidVolume, fields.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, fields.Ask)
		.TryAdd(Level1Fields.BestAskVolume, fields.AskSize)
		.TryAdd(Level1Fields.OpenPrice, fields.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, fields.HighPrice)
		.TryAdd(Level1Fields.LowPrice, fields.LowPrice)
		.TryAdd(Level1Fields.ClosePrice, fields.ClosePrice)
		.TryAdd(Level1Fields.Volume, fields.Volume)
		.TryAdd(Level1Fields.OpenInterest, fields.OpenInterest)
		.TryAdd(Level1Fields.VWAP, fields.Vwap)
		.TryAdd(Level1Fields.Change, fields.ChangePercent ?? fields.Change);

		return level1.Changes.Count == 0
			? default
			: SendOutMessageAsync(level1, cancellationToken);
	}

	private ValueTask ProcessDepthAsync(LsegDepthUpdate update, CancellationToken cancellationToken)
	{
		if (!_marketSubscriptions.TryGetValue(update.SubscriptionId, out var subscription) ||
			subscription.Kind != LsegMarketDataKinds.MarketDepth ||
			!_depthBooks.TryGetValue(update.SubscriptionId, out var book))
		{
			return default;
		}

		return SendOutMessageAsync(book.Apply(subscription.SecurityId, update), cancellationToken);
	}

	private LsegRealTimeClient GetClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
}

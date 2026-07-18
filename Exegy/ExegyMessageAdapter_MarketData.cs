namespace StockSharp.Exegy;

public partial class ExegyMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var types = lookupMsg.GetSecurityTypes();
		var exact = ExegySecurityKey.TryParse(lookupMsg.SecurityId.Native as string,
			out var exactKey);
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		async ValueTask Emit(SecurityMessage message)
		{
			if (left <= 0)
				return;
			var native = message.SecurityId.Native as string;
			if (native.IsEmpty() || !emitted.Add(native) ||
				exact && (!ExegySecurityKey.TryParse(native, out var candidate) ||
					!exactKey.Matches(candidate)) || !MatchesLookup(message, lookupMsg, types))
			{
				return;
			}
			if (skip > 0)
			{
				skip--;
				return;
			}
			await SendOutMessageAsync(message, cancellationToken);
			left--;
		}

		foreach (var source in SafeCatalog().Sources)
		{
			if (left <= 0)
				break;
			await using var file = await SafeCatalog().OpenAsync(source, cancellationToken);
			if (file.Header?.Kind != ExegyFileKinds.Reference)
				continue;
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = ExegyRowParser.ParseReference(file.Header, record,
					source.DisplayName, _defaultTimeZone);
				await Emit(row.ToSecurityMessage(lookupMsg.TransactionId));
				if (left <= 0)
					break;
			}
		}

		if (left > 0)
		{
			foreach (var source in SafeCatalog().Sources)
			{
				if (left <= 0)
					break;
				await using var file = await SafeCatalog().OpenAsync(source, cancellationToken);
				if (file.Header?.Kind != ExegyFileKinds.MarketData)
					continue;
				await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
				{
					var row = ExegyRowParser.ParseMarket(file.Header, record,
						source.DisplayName, _defaultTimeZone);
					await Emit(row.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var requestKey = mdMsg.SecurityId.GetExegyKey();
		var left = mdMsg.Count ?? long.MaxValue;
		await foreach (var row in ReadMarketRows(mdMsg, cancellationToken))
		{
			var key = row.ToKey();
			var price = row.EffectiveTradePrice;
			var volume = row.EffectiveTradeSize;
			if (!requestKey.Matches(key) || price is not > 0 || volume is null or < 0 ||
				(!row.IsCancellation && volume is not > 0) ||
				!ExegyExtensions.InRange(row.EventTime, mdMsg))
			{
				continue;
			}
			await SendOutMessageAsync(new ExecutionMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId.NormalizeExegy(key),
				DataTypeEx = DataType.Ticks,
				ServerTime = row.EventTime,
				TradeStringId = row.TradeId,
				TradePrice = price,
				TradeVolume = volume,
				IsCancellation = row.IsCancellation,
				SeqNum = row.Sequence ?? 0,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}
		await Complete(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var requestKey = mdMsg.SecurityId.GetExegyKey();
		var left = mdMsg.Count ?? long.MaxValue;
		await foreach (var row in ReadMarketRows(mdMsg, cancellationToken))
		{
			var key = row.ToKey();
			if (!requestKey.Matches(key) || !ExegyExtensions.InRange(row.EventTime, mdMsg))
				continue;
			var message = CreateLevel1(mdMsg.TransactionId,
				mdMsg.SecurityId.NormalizeExegy(key), row);
			if (message == null)
				continue;
			await SendOutMessageAsync(message, cancellationToken);
			if (--left <= 0)
				break;
		}
		await Complete(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var requestKey = mdMsg.SecurityId.GetExegyKey();
		var book = new ExegyOrderBook();
		var left = mdMsg.Count ?? long.MaxValue;
		await foreach (var row in ReadMarketRows(mdMsg, cancellationToken))
		{
			var key = row.ToKey();
			if (!requestKey.Matches(key) || !ExegyExtensions.InRange(row.EventTime, mdMsg) ||
				!book.Apply(row))
			{
				continue;
			}
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId.NormalizeExegy(key),
				ServerTime = row.EventTime,
				Bids = book.GetBids(),
				Asks = book.GetAsks(),
				State = QuoteChangeStates.SnapshotComplete,
				SeqNum = row.Sequence ?? 0,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}
		await Complete(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnOrderLogSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var requestKey = mdMsg.SecurityId.GetExegyKey();
		var left = mdMsg.Count ?? long.MaxValue;
		await foreach (var row in ReadMarketRows(mdMsg, cancellationToken))
		{
			var key = row.ToKey();
			if (!requestKey.Matches(key) || row.OrderId.IsEmpty() && !row.IsReset ||
				!ExegyExtensions.InRange(row.EventTime, mdMsg))
			{
				continue;
			}
			var side = row.ToSide();
			if (side == null && !row.IsReset)
				continue;
			await SendOutMessageAsync(new ExecutionMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId.NormalizeExegy(key),
				DataTypeEx = DataType.OrderLog,
				ServerTime = row.EventTime,
				OrderStringId = row.OrderId.IsEmpty("CLEAR"),
				OrderPrice = row.Price ?? 0,
				OrderVolume = row.Size ?? 0,
				Balance = row.ToOrderState() == OrderStates.Done ? 0 : row.Size ?? 0,
				Side = side ?? default,
				OrderState = row.ToOrderState(),
				TradeStringId = row.TradeId,
				TradePrice = row.IsTrade ? row.EffectiveTradePrice : null,
				TradeVolume = row.IsTrade ? row.EffectiveTradeSize : null,
				BrokerCode = row.Participant,
				SeqNum = row.Sequence ?? 0,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}
		await Complete(mdMsg, cancellationToken);
	}

	private async IAsyncEnumerable<ExegyMarketRow> ReadMarketRows(MarketDataMessage message,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		foreach (var source in SafeCatalog().GetSources(message))
		{
			await using var file = await SafeCatalog().OpenAsync(source, cancellationToken);
			if (file.Header?.Kind != ExegyFileKinds.MarketData)
				continue;
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				yield return ExegyRowParser.ParseMarket(file.Header, record,
					source.DisplayName, _defaultTimeZone);
			}
		}
	}

	private static Level1ChangeMessage CreateLevel1(long transactionId,
		SecurityId securityId, ExegyMarketRow row)
	{
		var bidPrice = row.BidPrice ?? (row.IsBid ? row.Price : null);
		var bidSize = row.BidSize ?? (row.IsBid ? row.Size : null);
		var askPrice = row.AskPrice ?? (row.IsAsk ? row.Price : null);
		var askSize = row.AskSize ?? (row.IsAsk ? row.Size : null);
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = row.EventTime,
			SeqNum = row.Sequence ?? 0,
		}
		.TryAdd(Level1Fields.BestBidPrice, ExegyExtensions.Positive(bidPrice))
		.TryAdd(Level1Fields.BestBidVolume, ExegyExtensions.NonNegative(bidSize))
		.TryAdd(Level1Fields.BestAskPrice, ExegyExtensions.Positive(askPrice))
		.TryAdd(Level1Fields.BestAskVolume, ExegyExtensions.NonNegative(askSize))
		.TryAdd(Level1Fields.LastTradePrice,
			ExegyExtensions.Positive(row.EffectiveTradePrice))
		.TryAdd(Level1Fields.LastTradeVolume,
			ExegyExtensions.NonNegative(row.EffectiveTradeSize))
		.TryAdd(Level1Fields.OpenPrice, ExegyExtensions.Positive(row.Open))
		.TryAdd(Level1Fields.HighPrice, ExegyExtensions.Positive(row.High))
		.TryAdd(Level1Fields.LowPrice, ExegyExtensions.Positive(row.Low))
		.TryAdd(Level1Fields.ClosePrice, ExegyExtensions.Positive(row.Close))
		.TryAdd(Level1Fields.Volume, ExegyExtensions.NonNegative(row.CumulativeVolume))
		.TryAdd(Level1Fields.OpenInterest, ExegyExtensions.NonNegative(row.OpenInterest))
		.TryAdd(Level1Fields.State, ExegyExtensions.ToSecurityState(row.TradingStatus));
		if (row.EffectiveTradePrice is > 0)
			message.TryAdd(Level1Fields.LastTradeTime, row.EventTime);
		if (bidPrice is > 0)
			message.TryAdd(Level1Fields.BestBidTime, row.EventTime);
		if (askPrice is > 0)
			message.TryAdd(Level1Fields.BestAskTime, row.EventTime);
		return message.Changes.Count == 0 ? null : message;
	}

	private static bool MatchesLookup(SecurityMessage message, SecurityLookupMessage lookup,
		HashSet<SecurityTypes> types)
	{
		if (!ExegySecurityKey.TryParse(message.SecurityId.Native as string, out var key))
			return false;
		var requested = lookup.SecurityId;
		var code = (requested.Native as string).IsEmpty(requested.SecurityCode);
		if (!code.IsEmpty() && !code.EqualsIgnoreCase(key.InstrumentId) &&
			!code.EqualsIgnoreCase(key.Symbol) && !code.EqualsIgnoreCase(key.SecurityCode))
		{
			return false;
		}
		if (!requested.BoardCode.IsEmpty() &&
			!requested.BoardCode.EqualsIgnoreCase(ExegyExtensions.BoardCode) &&
			!requested.BoardCode.EqualsIgnoreCase(key.Venue) &&
			!requested.BoardCode.EqualsIgnoreCase(key.ToBoard()))
		{
			return false;
		}
		var criteria = (SecurityLookupMessage)lookup.Clone();
		criteria.SecurityId = default;
		return message.IsMatch(criteria, types);
	}

	private async ValueTask Complete(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}

sealed class ExegyOrderBook
{
	private readonly List<ExegyDepthLevel> _bids = [];
	private readonly List<ExegyDepthLevel> _asks = [];
	private readonly List<ExegyBookOrder> _orders = [];

	public bool Apply(ExegyMarketRow row)
	{
		if (row.IsReset)
		{
			Clear();
			return true;
		}

		var changed = false;
		if (row.BidPrice != null)
		{
			SetBest(_bids, row.BidPrice, row.BidSize, row.BidOrderCount);
			changed = true;
		}
		if (row.AskPrice != null)
		{
			SetBest(_asks, row.AskPrice, row.AskSize, row.AskOrderCount);
			changed = true;
		}
		if (!row.OrderId.IsEmpty())
		{
			var index = _orders.FindIndex(order => order.Id.Equals(row.OrderId,
				StringComparison.Ordinal));
			if (row.IsCancellation || row.Size is <= 0)
			{
				if (index >= 0)
					_orders.RemoveAt(index);
			}
			else
			{
				var previous = index >= 0 ? _orders[index] : (ExegyBookOrder?)null;
				if (previous == null && row.Size == null)
					return changed;
				var orderSide = row.ToSide() ?? previous?.Side;
				var price = row.Price ?? previous?.Price;
				if (orderSide == null || price == null)
					return changed;
				var order = new ExegyBookOrder(row.OrderId, orderSide.Value, price.Value,
					Math.Max(0, row.Size ?? previous?.Volume ?? 0));
				if (index >= 0)
					_orders[index] = order;
				else
					_orders.Add(order);
			}
			RebuildFromOrders();
			return true;
		}

		var side = row.ToSide();
		if (side == null)
			return changed;
		var levels = side == Sides.Buy ? _bids : _asks;
		var position = row.Level is > 0 ? row.Level.Value - 1 : row.Level ?? -1;
		var existing = row.Price == null ? -1 :
			levels.FindIndex(level => level.Price == row.Price.Value);
		if (row.IsCancellation || row.Size is <= 0)
		{
			if (existing >= 0)
				levels.RemoveAt(existing);
			else if (position >= 0 && position < levels.Count)
				levels.RemoveAt(position);
			return true;
		}
		if (row.Price == null)
			return changed;
		var value = new ExegyDepthLevel(row.Price.Value, Math.Max(0, row.Size ?? 0),
			ExegyExtensions.NonNegative(row.OrderCount));
		if (existing >= 0)
			levels[existing] = value;
		else if (position >= 0 && position < levels.Count)
			levels[position] = value;
		else
			levels.Add(value);
		Sort(levels, side.Value);
		return true;
	}

	public QuoteChange[] GetBids() => [.. _bids.Select(ToQuote)];
	public QuoteChange[] GetAsks() => [.. _asks.Select(ToQuote)];

	private void RebuildFromOrders()
	{
		_bids.Clear();
		_asks.Clear();
		foreach (var group in _orders.GroupBy(order => (order.Side, order.Price)))
		{
			var level = new ExegyDepthLevel(group.Key.Price, group.Sum(order => order.Volume),
				group.Count());
			(group.Key.Side == Sides.Buy ? _bids : _asks).Add(level);
		}
		Sort(_bids, Sides.Buy);
		Sort(_asks, Sides.Sell);
	}

	private static void SetBest(List<ExegyDepthLevel> levels, decimal? price,
		decimal? volume, int? orders)
	{
		levels.Clear();
		if (price is > 0)
			levels.Add(new(price.Value, Math.Max(0, volume ?? 0),
				ExegyExtensions.NonNegative(orders)));
	}

	private static void Sort(List<ExegyDepthLevel> levels, Sides side)
		=> levels.Sort((left, right) => side == Sides.Buy
			? right.Price.CompareTo(left.Price) : left.Price.CompareTo(right.Price));

	private static QuoteChange ToQuote(ExegyDepthLevel level)
		=> new(level.Price, level.Volume, level.OrderCount);

	private void Clear()
	{
		_bids.Clear();
		_asks.Clear();
		_orders.Clear();
	}
}

readonly record struct ExegyBookOrder(string Id, Sides Side, decimal Price, decimal Volume);

namespace StockSharp.IQFeed;

class IQFeedOrderLogMarketDepthBuilder : IOrderLogMarketDepthBuilder
{
	private sealed class OrderInfo
	{
		public Sides Side { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
	}

	private readonly Dictionary<long, OrderInfo> _orders = [];
	private readonly SortedList<decimal, decimal> _bids = new(new BackwardComparer<decimal>());
	private readonly SortedList<decimal, decimal> _asks = [];
	private readonly QuoteChangeMessage _depth;

	public IQFeedOrderLogMarketDepthBuilder(SecurityId securityId)
		: this(new QuoteChangeMessage
		{
			SecurityId = securityId,
			BuildFrom = DataType.OrderLog,
		})
	{
	}

	public IQFeedOrderLogMarketDepthBuilder(QuoteChangeMessage depth)
	{
		_depth = depth ?? throw new ArgumentNullException(nameof(depth));
		_depth.State = QuoteChangeStates.SnapshotComplete;

		foreach (var bid in depth.Bids)
			_bids[bid.Price] = bid.Volume;
		foreach (var ask in depth.Asks)
			_asks[ask.Price] = ask.Volume;

		UpdateSnapshot();
	}

	QuoteChangeMessage IOrderLogMarketDepthBuilder.GetSnapshot(DateTime serverTime)
	{
		var clone = _depth.TypedClone();
		clone.ServerTime = clone.LocalTime = serverTime;
		return clone;
	}

	QuoteChangeMessage IOrderLogMarketDepthBuilder.Update(ExecutionMessage item)
	{
		if (item == null)
			throw new ArgumentNullException(nameof(item));
		if (item.DataTypeEx != DataType.OrderLog)
			throw new ArgumentException("The message is not an order-log record.", nameof(item));
		if (item.OrderId == IQFeedLevel2.ClearDepthId)
			return Clear(item.Side, item);
		if (item.OrderId == null)
			throw new InvalidOperationException("An IQFeed order-log record has no order identifier.");

		var changes = new Dictionary<(Sides side, decimal price), decimal>();
		var orderId = item.OrderId.Value;
		if (_orders.TryGetValue(orderId, out var previous))
		{
			ApplyLevel(previous.Side, previous.Price, -previous.Volume, changes);
			_orders.Remove(orderId);
		}

		if (item.OrderState != OrderStates.Done)
		{
			var volume = item.Balance ?? item.OrderVolume ?? 0;
			if (item.OrderPrice <= 0 || volume <= 0)
				throw new InvalidOperationException($"Invalid active IQFeed order {orderId}.");

			var current = new OrderInfo
			{
				Side = item.Side,
				Price = item.OrderPrice,
				Volume = volume,
			};
			_orders[orderId] = current;
			ApplyLevel(current.Side, current.Price, current.Volume, changes);
		}

		return CreateIncrement(item, changes);
	}

	private QuoteChangeMessage Clear(Sides side, ExecutionMessage item)
	{
		var levels = side == Sides.Buy ? _bids : _asks;
		var changes = levels.Keys.ToDictionary(price => (side, price), _ => 0m);
		levels.Clear();

		foreach (var orderId in _orders
			.Where(pair => pair.Value.Side == side)
			.Select(pair => pair.Key)
			.ToArray())
		{
			_orders.Remove(orderId);
		}

		return CreateIncrement(item, changes);
	}

	private void ApplyLevel(Sides side, decimal price, decimal delta,
		Dictionary<(Sides side, decimal price), decimal> changes)
	{
		var levels = side == Sides.Buy ? _bids : _asks;
		var volume = levels.TryGetValue(price, out var current) ? current + delta : delta;
		if (volume <= 0)
		{
			levels.Remove(price);
			volume = 0;
		}
		else
			levels[price] = volume;

		changes[(side, price)] = volume;
	}

	private QuoteChangeMessage CreateIncrement(ExecutionMessage item,
		Dictionary<(Sides side, decimal price), decimal> changes)
	{
		if (changes.Count == 0)
			return null;

		UpdateSnapshot();
		_depth.ServerTime = item.ServerTime;
		_depth.LocalTime = item.LocalTime;

		return new()
		{
			SecurityId = _depth.SecurityId,
			ServerTime = item.ServerTime,
			LocalTime = item.LocalTime,
			OriginalTransactionId = item.OriginalTransactionId,
			State = QuoteChangeStates.Increment,
			BuildFrom = DataType.OrderLog,
			Bids = changes
				.Where(pair => pair.Key.side == Sides.Buy)
				.OrderByDescending(pair => pair.Key.price)
				.Select(pair => new QuoteChange(pair.Key.price, pair.Value))
				.ToArray(),
			Asks = changes
				.Where(pair => pair.Key.side == Sides.Sell)
				.OrderBy(pair => pair.Key.price)
				.Select(pair => new QuoteChange(pair.Key.price, pair.Value))
				.ToArray(),
		};
	}

	private void UpdateSnapshot()
	{
		_depth.Bids = _bids.Select(pair => new QuoteChange(pair.Key, pair.Value)).ToArray();
		_depth.Asks = _asks.Select(pair => new QuoteChange(pair.Key, pair.Value)).ToArray();
	}
}

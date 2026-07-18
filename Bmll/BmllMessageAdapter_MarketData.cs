namespace StockSharp.Bmll;

public partial class BmllMessageAdapter
{
	private sealed record BmllBookOrder(decimal Price, decimal Volume, string OrderId);

	private sealed class BmllOrderBook
	{
		public List<BmllBookOrder> Bids { get; } = [];
		public List<BmllBookOrder> Asks { get; } = [];

		public void Clear()
		{
			Bids.Clear();
			Asks.Clear();
		}

		public void Apply(BmllMarketDataRecord record)
		{
			if (record.LobAction is null or BmllLobActions.Unknown)
			{
				return;
			}

			var side = record.ToSide();
			if (side == null)
				return;
			var book = side == Sides.Buy ? Bids : Asks;
			if (record.DeleteOrderIndex is >= 0)
			{
				var index = record.DeleteOrderIndex.Value;
				if (index >= book.Count)
				{
					throw new InvalidDataException(
						$"BMLL delete index {index} exceeds the {side} book size {book.Count}.");
				}
				book.RemoveAt(index);
			}

			if (record.AddOrderIndex is >= 0)
			{
				var index = record.AddOrderIndex.Value;
				if (index > book.Count)
				{
					throw new InvalidDataException(
						$"BMLL add index {index} exceeds the {side} book size {book.Count}.");
				}
				if (record.Price is not > 0 || record.Size is not >= 0)
				{
					throw new InvalidDataException(
						"BMLL add/update row does not contain a valid price and size.");
				}
				book.Insert(index, new(record.Price.Value, record.Size.Value,
					record.GetOrderId()));
			}
		}
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

		ValidateDatasetAccess(TradesDataset);
		var (from, to) = GetRange(mdMsg);
		var request = BuildQuery(mdMsg.SecurityId, from, to, BmllDataKinds.Trades);
		var left = GetRecordLimit(mdMsg);
		await foreach (var record in SafeClient().Query(TradesDataset, request,
			cancellationToken))
		{
			if (!record.TryGetTime(out var time) || time < from || time > to ||
				record.Price is not > 0 || record.Size is null or 0 ||
				IsPrintableOnly && record.IsPrintable == false)
			{
				continue;
			}

			await SendOutMessageAsync(new ExecutionMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = record.GetSecurityId(mdMsg.SecurityId),
				DataTypeEx = DataType.Ticks,
				ServerTime = time,
				TradeStringId = record.TradeId,
				TradePrice = record.Price,
				TradeVolume = Math.Abs(record.Size.Value),
				OriginSide = record.ToAggressorSide(),
				IsCancellation = record.Size < 0 ||
					record.ModificationIndicator.EqualsIgnoreCase("C"),
				SeqNum = record.GetSequence(),
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

		ValidateDatasetAccess(Level3Dataset);
		var (from, to) = GetRange(mdMsg);
		var request = BuildQuery(mdMsg.SecurityId, from, to, BmllDataKinds.Level3);
		var left = GetRecordLimit(mdMsg);
		await foreach (var record in SafeClient().Query(Level3Dataset, request,
			cancellationToken))
		{
			if (!record.TryGetTime(out var time) || time < from || time > to ||
				record.ToSide() is not { } side ||
				record.LobAction is null or BmllLobActions.Unknown)
			{
				continue;
			}

			var state = record.GetOrderState();
			var orderId = record.GetOrderId().IsEmpty(
				record.EventNo?.ToString(CultureInfo.InvariantCulture));
			await SendOutMessageAsync(new ExecutionMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = record.GetSecurityId(mdMsg.SecurityId),
				DataTypeEx = DataType.OrderLog,
				ServerTime = time,
				OrderStringId = orderId,
				OrderPrice = record.Price ?? record.OldPrice ?? record.ExecutionPrice ?? 0,
				OrderVolume = record.Size ?? record.OldSize ?? record.ExecutionSize ?? 0,
				Balance = state == OrderStates.Done ? 0 : record.Size ?? 0,
				Side = side,
				OrderState = state,
				TradeStringId = record.IsOrderExecuted == true ? record.TradeId : null,
				TradePrice = record.IsOrderExecuted == true ? record.ExecutionPrice : null,
				TradeVolume = record.IsOrderExecuted == true ? record.ExecutionSize : null,
				BrokerCode = record.MpidAttribution,
				SeqNum = record.GetSequence(),
			}, cancellationToken);
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

		ValidateDatasetAccess(Level3Dataset);
		var (from, to) = GetRange(mdMsg);
		var request = BuildQuery(mdMsg.SecurityId, from, to, BmllDataKinds.Level3);
		var left = GetRecordLimit(mdMsg);
		var book = new BmllOrderBook();
		string tradeDate = null;
		await foreach (var record in SafeClient().Query(Level3Dataset, request,
			cancellationToken))
		{
			if (!record.TryGetTime(out var time) || time > to)
				continue;
			var currentDate = record.TradeDate.IsEmpty(time.ToString("yyyy-MM-dd",
				CultureInfo.InvariantCulture));
			if (!currentDate.EqualsIgnoreCase(tradeDate))
			{
				tradeDate = currentDate;
				book.Clear();
			}
			book.Apply(record);
			if (time < from || record.IsEndOfEvent != true)
				continue;

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = record.GetSecurityId(mdMsg.SecurityId),
				ServerTime = time,
				Bids = Aggregate(book.Bids, MaxDepth),
				Asks = Aggregate(book.Asks, MaxDepth),
				State = QuoteChangeStates.SnapshotComplete,
				SeqNum = record.GetSequence(),
			}, cancellationToken);
			if (--left <= 0)
				break;
		}
		await Complete(mdMsg, cancellationToken);
	}

	private BmllDataQueryRequest BuildQuery(SecurityId securityId,
		DateTime from, DateTime to, BmllDataKinds kind)
	{
		var symbol = securityId.SecurityCode?.Trim();
		symbol.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var mic = securityId.BoardCode?.Trim();
		if (mic.EqualsIgnoreCase(BmllExtensions.BoardCode))
			mic = null;
		return new()
		{
			Filters = new()
			{
				Mic = mic.IsEmpty() ? null : [mic],
				Ticker = kind == BmllDataKinds.Trades ? [symbol] : null,
				ExchangeTicker = kind == BmllDataKinds.Level3 ? [symbol] : null,
			},
			DateRange = new()
			{
				StartDate = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				EndDate = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			},
			DateTimeRange = kind == BmllDataKinds.Trades ? new()
			{
				Start = from.ToString("O", CultureInfo.InvariantCulture),
				End = to.ToString("O", CultureInfo.InvariantCulture),
				TimestampField = "TradeTimestamp",
			} : null,
		};
	}

	private (DateTime From, DateTime To) GetRange(MarketDataMessage message)
	{
		var to = message.To?.ToUtc() ?? DateTime.UtcNow.Date.AddTicks(-1);
		var from = message.From?.ToUtc() ??
			to.Date.AddDays(1 - DefaultHistoryDays);
		if (from > to)
		{
			throw new ArgumentOutOfRangeException(nameof(message.From), from,
				"The BMLL history start time is after its end time.");
		}
		return (from, to);
	}

	private int GetRecordLimit(MarketDataMessage message)
		=> checked((int)Math.Min(message.Count ?? MaxRecords, MaxRecords));

	private static QuoteChange[] Aggregate(IReadOnlyList<BmllBookOrder> orders,
		int maximumLevels)
	{
		if (orders.Count == 0 || maximumLevels <= 0)
			return [];
		var result = new List<QuoteChange>(Math.Min(orders.Count, maximumLevels));
		var price = orders[0].Price;
		var volume = 0m;
		var count = 0;
		foreach (var order in orders)
		{
			if (order.Price != price)
			{
				result.Add(new(price, volume) { OrdersCount = count });
				if (result.Count == maximumLevels)
					return [.. result];
				price = order.Price;
				volume = 0;
				count = 0;
			}
			volume += order.Volume;
			count++;
		}
		result.Add(new(price, volume) { OrdersCount = count });
		return [.. result];
	}

	private async ValueTask Complete(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}

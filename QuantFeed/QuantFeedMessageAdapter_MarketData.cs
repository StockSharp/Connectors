namespace StockSharp.QuantFeed;

public partial class QuantFeedMessageAdapter
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
		var exact = QuantFeedSecurityKey.TryParse(lookupMsg.SecurityId.Native as string,
			out var exactKey);
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		async ValueTask Emit(SecurityMessage message)
		{
			if (left <= 0)
				return;
			var key = message.SecurityId.Native as string;
			if (key.IsEmpty() || !emitted.Add(key) ||
				exact && (!QuantFeedSecurityKey.TryParse(key, out var candidate) ||
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
			if (file.Header?.Kind != QuantFeedFileKinds.Reference)
				continue;
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = QuantFeedRowParser.ParseReference(file.Header, record,
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
				if (file.Header?.Kind != QuantFeedFileKinds.MarketData)
					continue;
				await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
				{
					var row = QuantFeedRowParser.ParseMarket(file.Header, record,
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

		var requestKey = mdMsg.SecurityId.GetQuantFeedKey();
		var left = mdMsg.Count ?? long.MaxValue;
		await foreach (var row in ReadMarketRows(mdMsg, cancellationToken))
		{
			var rowKey = row.ToKey();
			var price = row.TradePrice;
			var volume = row.TradeQuantity;
			if (!requestKey.Matches(rowKey) || price is not > 0 || volume is null or < 0 ||
				!row.IsCancellation && volume is not > 0 ||
				!QuantFeedExtensions.InRange(row.EventTime, mdMsg))
			{
				continue;
			}
			await SendOutMessageAsync(new ExecutionMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId.NormalizeQuantFeed(rowKey),
				DataTypeEx = DataType.Ticks,
				ServerTime = row.EventTime,
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

		var requestKey = mdMsg.SecurityId.GetQuantFeedKey();
		var left = mdMsg.Count ?? long.MaxValue;
		await foreach (var row in ReadMarketRows(mdMsg, cancellationToken))
		{
			var rowKey = row.ToKey();
			if (!requestKey.Matches(rowKey) ||
				!QuantFeedExtensions.InRange(row.EventTime, mdMsg))
			{
				continue;
			}
			var message = CreateLevel1(mdMsg.TransactionId,
				mdMsg.SecurityId.NormalizeQuantFeed(rowKey), row);
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

		var requestKey = mdMsg.SecurityId.GetQuantFeedKey();
		var book = new QuantFeedOrderBook();
		var left = mdMsg.Count ?? long.MaxValue;
		await foreach (var row in ReadMarketRows(mdMsg, cancellationToken))
		{
			var rowKey = row.ToKey();
			if (!requestKey.Matches(rowKey) ||
				!QuantFeedExtensions.InRange(row.EventTime, mdMsg) || !book.Apply(row))
			{
				continue;
			}
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId.NormalizeQuantFeed(rowKey),
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

	private async IAsyncEnumerable<QuantFeedMarketRow> ReadMarketRows(
		MarketDataMessage message,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		foreach (var source in SafeCatalog().GetSources(message))
		{
			await using var file = await SafeCatalog().OpenAsync(source, cancellationToken);
			if (file.Header?.Kind != QuantFeedFileKinds.MarketData)
				continue;
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				yield return QuantFeedRowParser.ParseMarket(file.Header, record,
					source.DisplayName, _defaultTimeZone);
			}
		}
	}

	private static Level1ChangeMessage CreateLevel1(long transactionId,
		SecurityId securityId, QuantFeedMarketRow row)
	{
		var bidPrice = row.BidPrice ?? (row.IsBid ? row.Price : null);
		var bidSize = row.BidSize ?? (row.IsBid ? row.Quantity : null);
		var askPrice = row.AskPrice ?? (row.IsAsk ? row.Price : null);
		var askSize = row.AskSize ?? (row.IsAsk ? row.Quantity : null);
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = row.EventTime,
			SeqNum = row.Sequence ?? 0,
		}
		.TryAdd(Level1Fields.BestBidPrice, QuantFeedExtensions.Positive(bidPrice))
		.TryAdd(Level1Fields.BestBidVolume, QuantFeedExtensions.NonNegative(bidSize))
		.TryAdd(Level1Fields.BestAskPrice, QuantFeedExtensions.Positive(askPrice))
		.TryAdd(Level1Fields.BestAskVolume, QuantFeedExtensions.NonNegative(askSize))
		.TryAdd(Level1Fields.LastTradePrice,
			QuantFeedExtensions.Positive(row.TradePrice))
		.TryAdd(Level1Fields.LastTradeVolume,
			QuantFeedExtensions.NonNegative(row.TradeQuantity))
		.TryAdd(Level1Fields.OpenPrice, QuantFeedExtensions.Positive(row.Open))
		.TryAdd(Level1Fields.HighPrice, QuantFeedExtensions.Positive(row.High))
		.TryAdd(Level1Fields.LowPrice, QuantFeedExtensions.Positive(row.Low))
		.TryAdd(Level1Fields.ClosePrice, QuantFeedExtensions.Positive(row.Close))
		.TryAdd(Level1Fields.Volume, QuantFeedExtensions.NonNegative(row.Volume))
		.TryAdd(Level1Fields.OpenInterest,
			QuantFeedExtensions.NonNegative(row.OpenInterest))
		.TryAdd(Level1Fields.State,
			QuantFeedExtensions.ToSecurityState(row.TradingStatus));
		if (row.TradePrice is > 0)
			message.TryAdd(Level1Fields.LastTradeTime, row.EventTime);
		if (bidPrice is > 0)
			message.TryAdd(Level1Fields.BestBidTime, row.EventTime);
		if (askPrice is > 0)
			message.TryAdd(Level1Fields.BestAskTime, row.EventTime);
		return message.Changes.Count == 0 ? null : message;
	}

	private static bool MatchesLookup(SecurityMessage message,
		SecurityLookupMessage lookup, HashSet<SecurityTypes> types)
	{
		if (!QuantFeedSecurityKey.TryParse(message.SecurityId.Native as string, out var key))
			return false;
		var requested = lookup.SecurityId;
		var code = (requested.Native as string).IsEmpty(requested.SecurityCode);
		if (!code.IsEmpty() && !code.EqualsIgnoreCase(key.InstrumentCode) &&
			!code.EqualsIgnoreCase(key.Symbol) && !code.EqualsIgnoreCase(key.SecurityCode))
		{
			return false;
		}
		if (!requested.BoardCode.IsEmpty() &&
			!requested.BoardCode.EqualsIgnoreCase(QuantFeedExtensions.BoardCode) &&
			!requested.BoardCode.EqualsIgnoreCase(key.Mic))
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

sealed class QuantFeedOrderBook
{
	private readonly List<QuantFeedDepthLevel> _bids = [];
	private readonly List<QuantFeedDepthLevel> _asks = [];

	public bool Apply(QuantFeedMarketRow row)
	{
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

		var action = row.Action.IsEmpty(row.EventType);
		if (Contains(action, "clear") || Contains(action, "reset") ||
			Contains(action, "empty") || Contains(action, "snapshot") &&
				(Contains(action, "start") || Contains(action, "begin")))
		{
			if (TryGetSide(row, out var clearSide))
				GetLevels(clearSide).Clear();
			else
				Clear();
			return true;
		}

		if (row.Price == null || !TryGetSide(row, out var side))
			return changed;
		var levels = GetLevels(side);
		var position = row.Level is > 0 ? row.Level.Value - 1 : row.Level ?? -1;
		var existing = levels.FindIndex(level => level.Price == row.Price.Value);
		var isDelete = Contains(action, "delete") || Contains(action, "remove") ||
			row.Quantity is <= 0;
		if (isDelete)
		{
			if (existing >= 0)
				levels.RemoveAt(existing);
			else if (position >= 0 && position < levels.Count)
				levels.RemoveAt(position);
			return true;
		}

		var level = new QuantFeedDepthLevel(row.Price.Value,
			Math.Max(0, row.Quantity ?? 0), QuantFeedExtensions.NonNegative(row.OrderCount));
		if (existing >= 0)
			levels[existing] = level;
		else if (Contains(action, "insert") && position >= 0 && position <= levels.Count)
			levels.Insert(position, level);
		else if (position >= 0 && position < levels.Count)
			levels[position] = level;
		else
			levels.Add(level);
		Sort(levels, side);
		return true;
	}

	public QuoteChange[] GetBids()
		=> [.. _bids.Select(ToQuote)];

	public QuoteChange[] GetAsks()
		=> [.. _asks.Select(ToQuote)];

	private static void SetBest(List<QuantFeedDepthLevel> levels, decimal? price,
		decimal? volume, int? orders)
	{
		levels.Clear();
		if (price is > 0)
		{
			levels.Add(new(price.Value, Math.Max(0, volume ?? 0),
				QuantFeedExtensions.NonNegative(orders)));
		}
	}

	private List<QuantFeedDepthLevel> GetLevels(Sides side)
		=> side == Sides.Buy ? _bids : _asks;

	private static bool TryGetSide(QuantFeedMarketRow row, out Sides side)
	{
		if (row.IsBid)
		{
			side = Sides.Buy;
			return true;
		}
		if (row.IsAsk)
		{
			side = Sides.Sell;
			return true;
		}
		side = default;
		return false;
	}

	private static bool Contains(string value, string part)
		=> !value.IsEmpty() && value.Contains(part, StringComparison.OrdinalIgnoreCase);

	private static void Sort(List<QuantFeedDepthLevel> levels, Sides side)
		=> levels.Sort((left, right) => side == Sides.Buy
			? right.Price.CompareTo(left.Price) : left.Price.CompareTo(right.Price));

	private static QuoteChange ToQuote(QuantFeedDepthLevel level)
		=> new(level.Price, level.Volume, level.OrderCount);

	private void Clear()
	{
		_bids.Clear();
		_asks.Clear();
	}
}

namespace StockSharp.Intrinio;

sealed class IntrinioOptionRefreshSnapshot
{
	public decimal? OpenInterest { get; init; }
	public decimal? OpenPrice { get; init; }
	public decimal? ClosePrice { get; init; }
	public decimal? HighPrice { get; init; }
	public decimal? LowPrice { get; init; }
}

public partial class IntrinioMessageAdapter
{
	private readonly ConcurrentDictionary<string, IntrinioOptionRefreshSnapshot>
		_optionRefreshes = new(StringComparer.OrdinalIgnoreCase);

	private ValueTask OnRealtimeEvent(IntrinioStreamSubscription subscription,
		IntrinioRealtimeEvent update, CancellationToken cancellationToken)
		=> update.Type switch
		{
			IntrinioRealtimeEventTypes.EquityTrade =>
				OnEquityTrade(subscription, update.EquityTrade, cancellationToken),
			IntrinioRealtimeEventTypes.EquityQuote =>
				OnEquityQuote(subscription, update.EquityQuote, cancellationToken),
			IntrinioRealtimeEventTypes.OptionTrade =>
				OnOptionTrade(subscription, update.OptionTrade, cancellationToken),
			IntrinioRealtimeEventTypes.OptionQuote =>
				OnOptionQuote(subscription, update.OptionQuote, cancellationToken),
			IntrinioRealtimeEventTypes.OptionRefresh =>
				OnOptionRefresh(update.OptionRefresh),
			_ => throw new ArgumentOutOfRangeException(nameof(update.Type), update.Type, null),
		};

	private ValueTask OnEquityTrade(IntrinioStreamSubscription subscription,
		EquityTrade trade, CancellationToken cancellationToken)
	{
		var price = RequireDecimal(trade.Price, "equity trade price");
		var time = trade.Timestamp.ToUtc();
		if (subscription.DataType == DataType.Ticks)
		{
			return SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = time,
				TradePrice = price,
				TradeVolume = trade.Size,
			}, cancellationToken);
		}
		if (subscription.DataType != DataType.Level1)
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.LastTradePrice, price)
		.TryAdd(Level1Fields.LastTradeVolume, (decimal)trade.Size)
		.TryAdd(Level1Fields.LastTradeTime, time)
		.TryAdd(Level1Fields.Volume, (decimal)trade.TotalVolume), cancellationToken);
	}

	private ValueTask OnEquityQuote(IntrinioStreamSubscription subscription,
		EquityQuote quote, CancellationToken cancellationToken)
	{
		if (subscription.DataType != DataType.Level1)
			return default;
		var price = RequireDecimal(quote.Price, "equity quote price");
		var time = quote.Timestamp.ToUtc();
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
		};
		if (quote.Type == EquityQuoteType.Bid)
		{
			message
				.TryAdd(Level1Fields.BestBidPrice, price)
				.TryAdd(Level1Fields.BestBidVolume, (decimal)quote.Size)
				.TryAdd(Level1Fields.BestBidTime, time);
		}
		else if (quote.Type == EquityQuoteType.Ask)
		{
			message
				.TryAdd(Level1Fields.BestAskPrice, price)
				.TryAdd(Level1Fields.BestAskVolume, (decimal)quote.Size)
				.TryAdd(Level1Fields.BestAskTime, time);
		}
		else
			throw new InvalidOperationException($"Unsupported Intrinio equity quote type '{quote.Type}'.");
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask OnOptionTrade(IntrinioStreamSubscription subscription,
		OptionTrade trade, CancellationToken cancellationToken)
	{
		var price = trade.Price.ToSafeDecimal();
		if (price == null)
			return default;
		var time = trade.Timestamp.ToUtcFromUnixSeconds();
		_optionRefreshes.TryGetValue(trade.Contract, out var refresh);
		if (subscription.DataType == DataType.Ticks)
		{
			return SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = time,
				TradePrice = price,
				TradeVolume = trade.Size,
				OpenInterest = refresh?.OpenInterest,
			}, cancellationToken);
		}
		if (subscription.DataType != DataType.Level1)
			return default;

		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.LastTradePrice, price)
		.TryAdd(Level1Fields.LastTradeVolume, (decimal)trade.Size)
		.TryAdd(Level1Fields.LastTradeTime, time)
		.TryAdd(Level1Fields.Volume, (decimal)trade.TotalVolume)
		.TryAdd(Level1Fields.BestAskPrice, trade.AskPriceAtExecution.ToSafeDecimal())
		.TryAdd(Level1Fields.BestBidPrice, trade.BidPriceAtExecution.ToSafeDecimal())
		.TryAdd(Level1Fields.UnderlyingPrice, trade.UnderlyingPriceAtExecution.ToSafeDecimal());
		ApplyRefresh(message, refresh);
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask OnOptionQuote(IntrinioStreamSubscription subscription,
		OptionQuote quote, CancellationToken cancellationToken)
	{
		if (subscription.DataType != DataType.Level1)
			return default;
		var time = quote.Timestamp.ToUtcFromUnixSeconds();
		var askPrice = quote.AskPrice.ToSafeDecimal();
		var bidPrice = quote.BidPrice.ToSafeDecimal();
		_optionRefreshes.TryGetValue(quote.Contract, out var refresh);
		if (askPrice == null && bidPrice == null && refresh == null)
			return default;
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.BestAskPrice, askPrice)
		.TryAdd(Level1Fields.BestAskVolume,
			askPrice != null ? (decimal)quote.AskSize : null)
		.TryAdd(Level1Fields.BestAskTime,
			askPrice != null ? time : null)
		.TryAdd(Level1Fields.BestBidPrice, bidPrice)
		.TryAdd(Level1Fields.BestBidVolume,
			bidPrice != null ? (decimal)quote.BidSize : null)
		.TryAdd(Level1Fields.BestBidTime,
			bidPrice != null ? time : null);
		ApplyRefresh(message, refresh);
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask OnOptionRefresh(OptionRefresh refresh)
	{
		_optionRefreshes[refresh.Contract] = new()
		{
			OpenInterest = refresh.OpenInterest,
			OpenPrice = refresh.OpenPrice.ToSafeDecimal(),
			ClosePrice = refresh.ClosePrice.ToSafeDecimal(),
			HighPrice = refresh.HighPrice.ToSafeDecimal(),
			LowPrice = refresh.LowPrice.ToSafeDecimal(),
		};
		return default;
	}

	private static void ApplyRefresh(Level1ChangeMessage message,
		IntrinioOptionRefreshSnapshot refresh)
	{
		if (refresh == null)
			return;
		message
			.TryAdd(Level1Fields.OpenInterest, refresh.OpenInterest)
			.TryAdd(Level1Fields.OpenPrice, refresh.OpenPrice)
			.TryAdd(Level1Fields.ClosePrice, refresh.ClosePrice)
			.TryAdd(Level1Fields.HighPrice, refresh.HighPrice)
			.TryAdd(Level1Fields.LowPrice, refresh.LowPrice);
	}

	private static decimal RequireDecimal(double value, string field)
		=> value.ToSafeDecimal()
			?? throw new FormatException($"Intrinio returned an invalid {field} '{value}'.");
}

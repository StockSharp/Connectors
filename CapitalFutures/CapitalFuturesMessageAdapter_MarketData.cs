namespace StockSharp.CapitalFutures;

public partial class CapitalFuturesMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var symbol = lookupMsg.SecurityId.SecurityCode;
		if (symbol.IsEmpty())
			throw new NotSupportedException(
				"Capital Futures supports exact-symbol lookup; specify SecurityId.SecurityCode.");
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedType = securityTypes.Count == 1 ? securityTypes.First() : (SecurityTypes?)null;
		if (requestedType is not null and not SecurityTypes.Future and not SecurityTypes.Option)
			throw new NotSupportedException("Capital Futures lookup supports futures and options only.");

		var instrument = await ResolveInstrumentAsync(lookupMsg.SecurityId, requestedType, cancellationToken);
		var message = new SecurityMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			SecurityId = instrument.ToSecurityId(),
			SecurityType = instrument.SecurityType,
			Name = instrument.Name,
			ShortName = instrument.Symbol,
			Class = "TAIFEX",
			Currency = CurrencyTypes.TWD,
		};
		if (message.IsMatch(lookupMsg, securityTypes))
			await SendOutMessageAsync(message, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, CapitalMarketDataKinds.Level1,
			DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, CapitalMarketDataKinds.Trades,
			DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var depth = mdMsg.MaxDepth ?? 5;
		if (depth is < 1 or > 5)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), depth,
				"Capital Futures publishes five market-depth levels.");
		return ProcessSubscription(mdMsg, CapitalMarketDataKinds.MarketDepth,
			DataType.MarketDepth, cancellationToken);
	}

	private async ValueTask ProcessSubscription(MarketDataMessage mdMsg,
		CapitalMarketDataKinds kind, DataType dataType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await _client.UnsubscribeAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var instrument = await ResolveInstrumentAsync(mdMsg.SecurityId,
			mdMsg.SecurityType, cancellationToken);
		await _client.SubscribeAsync(new()
		{
			TransactionId = mdMsg.TransactionId,
			Kind = kind,
			SecurityId = instrument.ToSecurityId(),
			SecurityType = instrument.SecurityType,
			Symbol = instrument.Symbol,
			DataType = dataType,
		}, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private ValueTask OnQuote(CapitalSubscription subscription,
		CapitalInstrumentInfo update, CancellationToken cancellationToken)
	{
		CacheInstrument(update);
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
		}
		.TryAdd(Level1Fields.OpenPrice, update.Open)
		.TryAdd(Level1Fields.HighPrice, update.High)
		.TryAdd(Level1Fields.LowPrice, update.Low)
		.TryAdd(Level1Fields.LastTradePrice, update.Close)
		.TryAdd(Level1Fields.LastTradeVolume, update.LastVolume)
		.TryAdd(Level1Fields.LastTradeTime, update.Close != null ? update.ServerTime : null)
		.TryAdd(Level1Fields.BestBidPrice, update.BestBidPrice)
		.TryAdd(Level1Fields.BestBidVolume, update.BestBidVolume)
		.TryAdd(Level1Fields.BestAskPrice, update.BestAskPrice)
		.TryAdd(Level1Fields.BestAskVolume, update.BestAskVolume)
		.TryAdd(Level1Fields.ClosePrice, update.PreviousClose)
		.TryAdd(Level1Fields.Volume, update.TotalVolume)
		.TryAdd(Level1Fields.OpenInterest, update.OpenInterest)
		.TryAdd(Level1Fields.MaxPrice, update.MaxPrice)
		.TryAdd(Level1Fields.MinPrice, update.MinPrice);
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask OnTrade(CapitalSubscription subscription,
		CapitalTradeUpdate update, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TradeStringId = $"{update.MarketNo}:{update.NativeIndex}:{update.ServerTime:yyyyMMdd}:{update.Sequence}",
			TradePrice = update.Price,
			TradeVolume = update.Volume > 0 ? update.Volume : null,
			ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
		}, cancellationToken);

	private ValueTask OnBook(CapitalSubscription subscription,
		CapitalBookUpdate update, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
			Bids = [.. update.Bids.Select(level => new QuoteChange(level.Price, level.Volume))],
			Asks = [.. update.Asks.Select(level => new QuoteChange(level.Price, level.Volume))],
		}, cancellationToken);
}

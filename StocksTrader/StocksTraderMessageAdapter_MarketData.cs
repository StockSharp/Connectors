namespace StockSharp.StocksTrader;

public partial class StocksTraderMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		await EnsureInstrumentsAsync(cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		var instruments = GetInstruments()
			.OrderBy(static instrument => instrument.Ticker)
			.ToArray();
		foreach (var instrument in instruments)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var securityType = instrument.ToSecurityType();
			if (securityTypes.Count > 0 &&
				(securityType is null || !securityTypes.Contains(securityType.Value)))
				continue;

			var message = instrument.ToSecurityMessage(lookupMsg.TransactionId);
			if (!message.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;

			await SendOutMessageAsync(message, cancellationToken);
			left--;
		}

		this.AddDebugLog("StocksTrader security lookup {0} completed from {1} instruments.",
			lookupMsg.TransactionId, instruments.Length);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var instrument = await ResolveInstrumentAsync(mdMsg.SecurityId,
			cancellationToken);
		var quote = await Client.GetQuoteAsync(PortfolioName, instrument.Ticker,
			cancellationToken) ?? throw new InvalidOperationException(
				$"StocksTrader returned no quote for {instrument.Ticker}.");
		await SendQuoteAsync(mdMsg.TransactionId, instrument.Ticker.ToSecurityId(), quote,
			cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		this.AddDebugLog(
			"StocksTrader delivered one-shot Level1 snapshot for {0}, transaction {1}.",
			instrument.Ticker, mdMsg.TransactionId);
	}

	private ValueTask SendQuoteAsync(long originalTransactionId, SecurityId securityId,
		StocksTraderQuote quote, CancellationToken cancellationToken)
	{
		if (quote.AskPrice is null && quote.BidPrice is null && quote.LastPrice is null)
			throw new InvalidOperationException(
				$"StocksTrader returned an empty quote for {securityId.SecurityCode}.");

		var timestamp = new[] { quote.AskBidPriceTime, quote.LastPriceTime }
			.Where(static value => value is > 0)
			.Select(static value => value.Value)
			.DefaultIfEmpty(0)
			.Max();
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			ServerTime = ((long?)timestamp).FromStocksTraderEpoch(),
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
		.TryAdd(Level1Fields.LastTradePrice, quote.LastPrice);
		if (quote.LastPriceTime is > 0)
			message.TryAdd(Level1Fields.LastTradeTime,
				quote.LastPriceTime.FromStocksTraderEpoch());
		return SendOutMessageAsync(message, cancellationToken);
	}
}

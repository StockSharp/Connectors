namespace StockSharp.SnapTrade;

public partial class SnapTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var query = lookupMsg.SecurityId.SecurityCode
			.IsEmpty(lookupMsg.ShortName).IsEmpty(lookupMsg.Name).IsEmpty(string.Empty);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var symbols = await _client.SearchSymbols(ResolvePortfolio(null), query,
			cancellationToken) ?? [];
		CacheSymbols(symbols);
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? 20);
		foreach (var symbol in symbols)
		{
			if (symbol?.Symbol.IsEmpty() != false)
				continue;
			var securityType = symbol.Type?.Code.ToSecurityType();
			if (securityTypes.Count > 0 &&
				(securityType == null || !securityTypes.Contains(securityType.Value)))
				continue;
			var message = CreateSecurity(symbol, lookupMsg.TransactionId);
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
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
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

		var code = message.SecurityId.SecurityCode.ThrowIfEmpty(nameof(message.SecurityId.SecurityCode));
		var securityId = _symbols.TryGetValue(code, out var symbol)
			? symbol.ToSecurityId() : message.SecurityId;
		if (securityId.BoardCode.IsEmpty())
			securityId.BoardCode = SnapTradeExtensions.BoardCode;
		if (message.IsHistoryOnly())
		{
			var quotes = await _client.GetQuotes(ResolvePortfolio(null), [code],
				cancellationToken) ?? [];
			await SendLevel1(message.TransactionId, securityId, quotes.FirstOrDefault(),
				cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		}
		else
		{
			_level1Subscriptions[message.TransactionId] = securityId;
			await SendSubscriptionResultAsync(message, cancellationToken);
			_lastPoll = default;
		}
	}

	private async Task RefreshLevel1(CancellationToken cancellationToken)
	{
		var subscriptions = _level1Subscriptions.ToArray();
		var symbols = subscriptions.Select(pair => pair.Value.SecurityCode)
			.Where(code => !code.IsEmpty()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (symbols.Length == 0)
			return;
		var start = _quoteCursor % symbols.Length;
		var count = Math.Min(10, symbols.Length);
		var batch = Enumerable.Range(0, count)
			.Select(index => symbols[(start + index) % symbols.Length]).ToArray();
		_quoteCursor = (start + count) % symbols.Length;
		var quotes = await _client.GetQuotes(ResolvePortfolio(null), batch,
			cancellationToken) ?? [];
		foreach (var quote in quotes)
		{
			if (quote?.Symbol == null)
				continue;
			CacheSymbols([quote.Symbol]);
			foreach (var pair in subscriptions.Where(pair =>
				pair.Value.SecurityCode.EqualsIgnoreCase(quote.Symbol.Symbol) ||
				pair.Value.SecurityCode.EqualsIgnoreCase(quote.Symbol.RawSymbol)))
				await SendLevel1(pair.Key, pair.Value, quote, cancellationToken);
		}
	}

	private ValueTask SendLevel1(long originalTransactionId, SecurityId securityId,
		SnapTradeQuote quote, CancellationToken cancellationToken)
	{
		if (quote == null)
			return default;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			ServerTime = CurrentTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, quote.LastTradePrice)
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, quote.AskSize), cancellationToken);
	}

	private static SecurityMessage CreateSecurity(SnapTradeUniversalSymbol symbol,
		long originalTransactionId)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = symbol.ToSecurityId(),
			SecurityType = symbol.Type?.Code.ToSecurityType(),
			Name = symbol.Description,
			ShortName = symbol.RawSymbol,
			Class = symbol.Type?.Code,
			Currency = symbol.Currency?.Code.ToCurrency(),
		};
}

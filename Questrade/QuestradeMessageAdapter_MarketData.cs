namespace StockSharp.Questrade;

public partial class QuestradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = (int)Math.Clamp(lookupMsg.Count ?? 100, 1, 1000);
		var nativeId = lookupMsg.SecurityId.Native switch
		{
			long value => value,
			int value => value,
			_ => 0,
		};
		if (nativeId > 0)
		{
			foreach (var symbol in (await _client.GetSymbol(nativeId, cancellationToken)).Symbols ?? [])
				await SendSecurity(symbol, lookupMsg, securityTypes, cancellationToken);
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var prefix = lookupMsg.SecurityId.SecurityCode;
		var offset = 0;
		var seen = new HashSet<long>();
		for (var pageNumber = 0; pageNumber < 100 && left > 0; pageNumber++)
		{
			var page = (await _client.SearchSymbols(prefix, offset, cancellationToken)).Symbols ?? [];
			if (page.Length == 0)
				break;
			var ids = page.Select(s => s.SymbolId).Where(id => id > 0 && seen.Add(id)).Take(100).ToArray();
			if (ids.Length == 0)
				break;
			for (var batchOffset = 0; batchOffset < ids.Length; batchOffset += 50)
			{
				var batch = ids.Skip(batchOffset).Take(50).ToArray();
				foreach (var symbol in (await _client.GetSymbols(batch, cancellationToken)).Symbols ?? [])
				{
					if (await SendSecurity(symbol, lookupMsg, securityTypes, cancellationToken) && --left <= 0)
						break;
				}
				if (left <= 0)
					break;
			}
			offset += page.Length;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			_quoteSubscriptions.Remove(mdMsg.OriginalTransactionId);
			await RestartQuoteStream(cancellationToken);
			return;
		}

		var symbol = await ResolveSymbol(mdMsg.SecurityId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			foreach (var quote in (await _client.GetQuote(symbol.SymbolId, cancellationToken)).Quotes ?? [])
				await ProcessQuote(quote, mdMsg.TransactionId, mdMsg.SecurityId, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		_quoteSubscriptions[mdMsg.TransactionId] = new()
		{
			TransactionId = mdMsg.TransactionId,
			SymbolId = symbol.SymbolId,
			SecurityId = mdMsg.SecurityId,
		};
		try
		{
			await RestartQuoteStream(cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			_quoteSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		var symbol = await ResolveSymbol(mdMsg.SecurityId, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToInterval();
		var to = mdMsg.To is null ? DateTimeOffset.UtcNow : new(mdMsg.To.Value.ToUniversalTime());
		var limit = (int)Math.Clamp(mdMsg.Count ?? 2000, 1, 12000);
		var from = mdMsg.From is null
			? to - TimeSpan.FromTicks(checked(timeFrame.Ticks * limit))
			: new DateTimeOffset(mdMsg.From.Value.ToUniversalTime());
		var pageSpan = TimeSpan.FromTicks(checked(timeFrame.Ticks * 1999));
		var candles = new SortedDictionary<DateTime, QuestradeCandle>();
		var cursor = from;
		while (cursor < to && candles.Count < limit)
		{
			var pageTo = cursor + pageSpan;
			if (pageTo > to)
				pageTo = to;
			var page = (await _client.GetCandles(symbol.SymbolId, cursor, pageTo, interval, cancellationToken)).Candles ?? [];
			foreach (var candle in page)
			{
				if (candle.Start >= from && candle.Start <= to)
					candles[candle.Start.UtcDateTime] = candle;
				if (candles.Count >= limit)
					break;
			}
			if (pageTo >= to)
				break;
			cursor = pageTo;
		}
		foreach (var candle in candles.Values)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = candle.Start.UtcDateTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask<bool> SendSecurity(QuestradeSymbol symbol, SecurityLookupMessage lookupMsg,
		HashSet<SecurityTypes> securityTypes, CancellationToken cancellationToken)
	{
		if (symbol == null || symbol.SymbolId <= 0)
			return false;
		var price = symbol.PreviousClosePrice ?? 0;
		var priceStep = (symbol.MinTicks ?? [])
			.Where(t => t.MinTick > 0 && t.Pivot <= price)
			.OrderByDescending(t => t.Pivot)
			.Select(t => (decimal?)t.MinTick)
			.FirstOrDefault()
			?? (symbol.MinTicks ?? []).Where(t => t.MinTick > 0).Select(t => (decimal?)t.MinTick).FirstOrDefault();
		var underlying = symbol.OptionContractDeliverables?.Underlyings?.FirstOrDefault();
		var security = new SecurityMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			SecurityId = symbol.ToSecurityId(),
			SecurityType = symbol.SecurityType.ToSecurityType(),
			Name = symbol.Description,
			ShortName = symbol.Symbol,
			Currency = Enum.TryParse<CurrencyTypes>(symbol.Currency, true, out var currency) ? currency : null,
			ExpiryDate = symbol.OptionExpiryDate?.UtcDateTime,
			Strike = symbol.OptionStrikePrice,
			OptionType = symbol.OptionType.EqualsIgnoreCase("Call") ? OptionTypes.Call :
				symbol.OptionType.EqualsIgnoreCase("Put") ? OptionTypes.Put : null,
			Multiplier = underlying?.Multiplier ?? symbol.TradeUnit,
			VolumeStep = symbol.TradeUnit,
			PriceStep = priceStep,
			UnderlyingSecurityId = underlying == null ? default : new()
			{
				SecurityCode = underlying.Symbol,
				BoardCode = symbol.ListingExchange.IsEmpty("QUESTRADE"),
				Native = underlying.SymbolId,
			},
		};
		if (!security.IsMatch(lookupMsg, securityTypes))
			return false;
		_symbols[symbol.SymbolId] = symbol;
		await SendOutMessageAsync(security, cancellationToken);
		return true;
	}

	private async Task<QuestradeSymbol> ResolveSymbol(SecurityId securityId, CancellationToken cancellationToken)
	{
		long symbolId;
		try
		{
			symbolId = securityId.ToSymbolId();
		}
		catch (InvalidOperationException) when (!securityId.SecurityCode.IsEmpty())
		{
			var match = (await _client.SearchSymbols(securityId.SecurityCode, 0, cancellationToken)).Symbols?
				.FirstOrDefault(s => s.Symbol.EqualsIgnoreCase(securityId.SecurityCode));
			symbolId = match?.SymbolId ?? 0;
			if (symbolId <= 0)
				throw new InvalidOperationException($"Questrade symbol '{securityId.SecurityCode}' was not found.");
		}
		if (_symbols.TryGetValue(symbolId, out var symbol))
			return symbol;
		symbol = (await _client.GetSymbol(symbolId, cancellationToken)).Symbols?.FirstOrDefault()
			?? throw new InvalidOperationException($"Questrade symbol id {symbolId.ToString(CultureInfo.InvariantCulture)} was not found.");
		_symbols[symbolId] = symbol;
		return symbol;
	}

	private async Task RestartQuoteStream(CancellationToken cancellationToken)
	{
		await _quoteGate.WaitAsync(cancellationToken);
		try
		{
			_quoteCts?.Cancel();
			_quoteCts?.Dispose();
			_quoteCts = null;
			_quoteTask = null;
			var subscriptions = _quoteSubscriptions.CachedValues;
			if (subscriptions.Length == 0)
				return;
			var response = await _client.StartQuoteStream(subscriptions.Select(s => s.SymbolId).Distinct(), cancellationToken);
			foreach (var quote in response.Quotes ?? [])
				await ProcessQuote(quote, cancellationToken);
			if (response.StreamPort <= 0)
				throw new InvalidOperationException("Questrade L1 endpoint returned an invalid stream port.");
			_quoteCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
			_quoteTask = RunQuoteStream(response.StreamPort, subscriptions.Select(s => s.SymbolId).Distinct().ToArray(), _quoteCts.Token);
		}
		finally
		{
			_quoteGate.Release();
		}
	}

	private async Task RunQuoteStream(int firstPort, long[] symbolIds, CancellationToken cancellationToken)
	{
		var port = firstPort;
		var failures = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			var started = DateTime.UtcNow;
			try
			{
				if (port <= 0)
				{
					var response = await _client.StartQuoteStream(symbolIds, cancellationToken);
					foreach (var quote in response.Quotes ?? [])
						await ProcessQuote(quote, cancellationToken);
					port = response.StreamPort;
					if (port <= 0)
						throw new InvalidOperationException("Questrade L1 reconnect returned an invalid stream port.");
				}
				var socket = new QuestradeWebSocketClient(_client.ApiServer, port, () => _client.AccessToken) { Parent = this };
				await socket.Run<QuestradeQuotesResponse>(ProcessQuoteEnvelope, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, CancellationToken.None);
			}
			if (cancellationToken.IsCancellationRequested)
				break;
			port = 0;
			failures = DateTime.UtcNow - started > TimeSpan.FromMinutes(1) ? 0 : Math.Min(failures + 1, 4);
			await Task.Delay(TimeSpan.FromSeconds(1 << failures), cancellationToken);
		}
	}

	private async ValueTask ProcessQuoteEnvelope(QuestradeQuotesResponse response, CancellationToken cancellationToken)
	{
		foreach (var quote in response?.Quotes ?? [])
			await ProcessQuote(quote, cancellationToken);
	}

	private async ValueTask ProcessQuote(QuestradeQuote quote, CancellationToken cancellationToken)
	{
		if (quote == null)
			return;
		foreach (var subscription in _quoteSubscriptions.CachedValues.Where(s => s.SymbolId == quote.SymbolId))
			await ProcessQuote(quote, subscription.TransactionId, subscription.SecurityId, cancellationToken);
	}

	private ValueTask ProcessQuote(QuestradeQuote quote, long transactionId, SecurityId securityId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, quote.AskSize)
		.TryAdd(Level1Fields.LastTradePrice, quote.LastTradePrice)
		.TryAdd(Level1Fields.LastTradeVolume, quote.LastTradeSize)
		.TryAdd(Level1Fields.LastTradeTime, quote.LastTradeTime?.UtcDateTime)
		.TryAdd(Level1Fields.OpenPrice, quote.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, quote.HighPrice)
		.TryAdd(Level1Fields.LowPrice, quote.LowPrice)
		.TryAdd(Level1Fields.Volume, quote.Volume)
		.TryAdd(Level1Fields.IsSystem, quote.IsDelayed == null ? null : !quote.IsDelayed.Value)
		.TryAdd(Level1Fields.State, quote.IsHalted == null ? null :
			quote.IsHalted.Value ? SecurityStates.Stoped : SecurityStates.Trading), cancellationToken);
}

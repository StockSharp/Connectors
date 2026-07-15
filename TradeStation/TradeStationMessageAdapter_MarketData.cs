namespace StockSharp.TradeStation;

partial class TradeStationMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var code = message.SecurityId.SecurityCode;
		if (code.IsEmpty())
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var types = message.GetSecurityTypes();
		foreach (var symbol in (await _client.GetSymbols([code], cancellationToken))?.Symbols ?? [])
		{
			var security = new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = new() { SecurityCode = symbol.Symbol, BoardCode = ToBoardCode(symbol.Exchange) },
				Name = symbol.Description,
				SecurityType = symbol.AssetType.ToSecurityType(),
				Currency = symbol.Currency.To<CurrencyTypes?>(),
				PriceStep = symbol.PriceFormat?.Increment,
				VolumeStep = symbol.QuantityFormat?.Increment,
				MinVolume = symbol.QuantityFormat?.MinimumTradeQuantity,
				Multiplier = symbol.PriceFormat?.PointValue,
				ExpiryDate = symbol.ExpirationDate,
				Strike = symbol.StrikePrice,
				OptionType = symbol.OptionType?.EqualsIgnoreCase("CALL") == true ? OptionTypes.Call : symbol.OptionType?.EqualsIgnoreCase("PUT") == true ? OptionTypes.Put : null,
				UnderlyingSecurityId = symbol.Underlying.IsEmpty() ? default : new() { SecurityCode = symbol.Underlying, BoardCode = ToBoardCode(symbol.Exchange) },
			};

			if (security.IsMatch(message, types))
				await SendOutMessageAsync(security, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (message.IsSubscribe)
		{
			_level1Subscriptions[message.TransactionId] = message.SecurityId;
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		else
			_level1Subscriptions.Remove(message.OriginalTransactionId);

		RestartQuoteStream();
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var timeFrame = message.GetTimeFrame();
		var (interval, unit) = ToBarInterval(timeFrame);
		var response = await _client.GetBars(
			message.SecurityId.SecurityCode,
			interval,
			unit,
			message.From,
			message.To,
			message.Count,
			cancellationToken);

		foreach (var bar in response?.Bars ?? [])
		{
			var openTime = bar.TimeStamp.ToUtc();
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = message.SecurityId,
				OpenTime = openTime,
				CloseTime = openTime + timeFrame,
				OpenPrice = bar.Open,
				HighPrice = bar.High,
				LowPrice = bar.Low,
				ClosePrice = bar.Close,
				TotalVolume = bar.TotalVolume,
				OpenInterest = bar.OpenInterest,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private void RestartQuoteStream()
	{
		_quoteCts?.Cancel();
		_quoteCts?.Dispose();
		_quoteCts = null;
		_quoteStreamTask = null;

		var symbols = _level1Subscriptions.Values
			.Select(s => s.SecurityCode)
			.Where(s => !s.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (symbols.Length == 0 || _streamCts is null)
			return;

		_quoteCts = CancellationTokenSource.CreateLinkedTokenSource(_streamCts.Token);
		_quoteStreamTask = RunStream(ct => _client.StreamQuotes(symbols, ProcessQuote, ct), _quoteCts.Token);
	}

	private async ValueTask ProcessQuote(TradeStationQuote quote, CancellationToken cancellationToken)
	{
		if (quote is null)
			return;
		if (!quote.Error.IsEmpty())
		{
			await SendOutErrorAsync(new InvalidOperationException(quote.Message.IsEmpty(quote.Error)), cancellationToken);
			return;
		}
		if (quote.Symbol.IsEmpty())
			return;

		var subscriptions = _level1Subscriptions
			.Where(p => p.Value.SecurityCode.EqualsIgnoreCase(quote.Symbol))
			.ToArray();
		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.Key,
				SecurityId = subscription.Value,
				ServerTime = quote.TradeTime?.ToUtc() ?? DateTime.UtcNow,
			}
			.TryAdd(Level1Fields.OpenPrice, quote.Open)
			.TryAdd(Level1Fields.HighPrice, quote.High)
			.TryAdd(Level1Fields.LowPrice, quote.Low)
			.TryAdd(Level1Fields.ClosePrice, quote.Close)
			.TryAdd(Level1Fields.LastTradePrice, quote.Last)
			.TryAdd(Level1Fields.LastTradeVolume, quote.LastSize)
			.TryAdd(Level1Fields.BestBidPrice, quote.Bid)
			.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, quote.Ask)
			.TryAdd(Level1Fields.BestAskVolume, quote.AskSize)
			.TryAdd(Level1Fields.Volume, quote.Volume)
			.TryAdd(Level1Fields.OpenInterest, quote.DailyOpenInterest)
			.TryAdd(Level1Fields.VWAP, quote.Vwap)
			.TryAdd(Level1Fields.IsSystem, quote.MarketFlags?.IsDelayed == false), cancellationToken);
		}
	}

	private static (int interval, string unit) ToBarInterval(TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromDays(1))
			return (1, "Daily");
		if (timeFrame == TimeSpan.FromDays(7))
			return (1, "Weekly");
		if (timeFrame.TotalMinutes is >= 1 and <= 1440 && timeFrame.TotalMinutes % 1 == 0)
			return ((int)timeFrame.TotalMinutes, "Minute");
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	private static string ToBoardCode(string exchange)
		=> exchange?.ToUpperInvariant() switch
		{
			"NYSE" => BoardCodes.Nyse,
			"NASDAQ" => BoardCodes.Nasdaq,
			"AMEX" => BoardCodes.Amex,
			_ => exchange.IsEmpty(BoardCodes.Nasdaq),
		};
}

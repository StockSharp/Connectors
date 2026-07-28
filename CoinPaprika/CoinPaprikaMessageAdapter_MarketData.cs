namespace StockSharp.CoinPaprika;

public partial class CoinPaprikaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		if (securityTypes.Count > 0 &&
			!securityTypes.Contains(SecurityTypes.CryptoCurrency))
		{
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
			return;
		}
		var instruments = ExchangeId.IsEmpty()
			? await RestClient.GetCoinsAsync(
				QuoteCurrency, cancellationToken)
			: await RestClient.GetExchangeMarketsAsync(
				ExchangeId,
				QuoteCurrency,
				cancellationToken);
		RememberInstruments(instruments);
		var requested =
			(lookupMsg.SecurityId.Native as string)
				.IsEmpty(lookupMsg.SecurityId.SecurityCode)
				.IsEmpty(lookupMsg.Name);
		var skip = Math.Max(0L, lookupMsg.Skip ?? 0);
		var left = Math.Min(
			lookupMsg.Count ?? MaximumItems,
			MaximumItems);
		foreach (var instrument in instruments
			.Where(instrument =>
				Matches(instrument, requested))
			.OrderBy(static instrument =>
				instrument.Rank ?? int.MaxValue)
			.ThenBy(static instrument => instrument.Symbol,
				StringComparer.OrdinalIgnoreCase))
		{
			var security = CreateSecurity(
				instrument, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(
				security, cancellationToken);
			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					SecurityId = security.SecurityId,
					ServerTime =
						instrument.LastUpdated ?? CurrentTime,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				}.TryAdd(
					Level1Fields.State,
					instrument.IsActive
						? SecurityStates.Trading
						: SecurityStates.Stoped),
				cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"CoinPaprika does not expose historical Level1 " +
					"events through this endpoint.");
		var instrument = await ResolveInstrumentAsync(
			mdMsg.SecurityId, cancellationToken);
		var ticker = await RestClient.GetTickerAsync(
			instrument,
			QuoteCurrency,
			cancellationToken);
		if (ticker is null)
			throw new InvalidDataException(
				$"CoinPaprika returned no ticker for " +
					$"'{instrument.CoinId}'.");
		RememberInstruments([ticker]);
		await SendLevel1Async(
			instrument,
			ticker,
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Instrument = instrument,
				LastUpdate = CurrentTime,
			};
		await SendSubscriptionResultAsync(
			mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		var instrument = await ResolveInstrumentAsync(
			mdMsg.SecurityId, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow)
			.ToUniversalTime();
		var maximum = (mdMsg.Count ??
			(Token.IsEmpty() ? 1 : HistoryLimit))
			.Max(1).Min(HistoryLimit).To<int>();
		var from = (mdMsg.From ??
			to - timeFrame * maximum)
			.ToUniversalTime();
		foreach (var candle in
			(await RestClient.GetCandlesAsync(
				instrument.CoinId,
				QuoteCurrency,
				timeFrame,
				from,
				to,
				maximum,
				cancellationToken) ?? [])
			.Where(candle =>
				candle.OpenTime >= from &&
				candle.OpenTime <= to)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(maximum))
			await SendCandleAsync(
				instrument,
				candle,
				timeFrame,
				mdMsg.TransactionId,
				cancellationToken);
		await CompleteMarketSubscriptionAsync(
			mdMsg, cancellationToken);
	}

	private async ValueTask<CoinPaprikaInstrument>
		ResolveInstrumentAsync(
			SecurityId securityId,
			CancellationToken cancellationToken)
	{
		try
		{
			return GetInstrument(securityId);
		}
		catch (InvalidOperationException)
		{
			var instruments = ExchangeId.IsEmpty()
				? await RestClient.GetCoinsAsync(
					QuoteCurrency, cancellationToken)
				: await RestClient.GetExchangeMarketsAsync(
					ExchangeId,
					QuoteCurrency,
					cancellationToken);
			RememberInstruments(instruments);
			return GetInstrument(securityId);
		}
	}

	private ValueTask SendLevel1Async(
		CoinPaprikaInstrument instrument,
		CoinPaprikaInstrument ticker,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = instrument.ToStockSharp(),
				ServerTime =
					ticker.LastUpdated ?? CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(Level1Fields.LastTradePrice, ticker.Price)
			.TryAdd(Level1Fields.Volume, ticker.Volume24Hours)
			.TryAdd(Level1Fields.Change, ticker.Change24Hours)
			.TryAdd(
				Level1Fields.State,
				instrument.IsActive
					? SecurityStates.Trading
					: SecurityStates.Stoped),
			cancellationToken);

	private ValueTask SendCandleAsync(
		CoinPaprikaInstrument instrument,
		CoinPaprikaCandle candle,
		TimeSpan timeFrame,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new TimeFrameCandleMessage
			{
				SecurityId = instrument.ToStockSharp(),
				TypedArg = timeFrame,
				OpenTime = candle.OpenTime,
				CloseTime = candle.CloseTime == default
					? candle.OpenTime + timeFrame
					: candle.CloseTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State =
					(candle.CloseTime == default
						? candle.OpenTime + timeFrame
						: candle.CloseTime) <= DateTime.UtcNow
							? CandleStates.Finished
							: CandleStates.Active,
				OriginalTransactionId =
					originalTransactionId,
			},
			cancellationToken);

	private static SecurityMessage CreateSecurity(
		CoinPaprikaInstrument instrument,
		long originalTransactionId)
		=> new()
		{
			SecurityId = instrument.ToStockSharp(),
			Name = instrument.Name.IsEmpty()
				? instrument.Symbol
				: instrument.Name,
			ShortName = instrument.BaseSymbol,
			Class = instrument.ExchangeId.IsEmpty()
				? instrument.Category
				: $"{instrument.ExchangeId}:{instrument.Category}",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = Enum.TryParse<CurrencyTypes>(
				instrument.QuoteSymbol,
				true,
				out var currency)
					? currency
					: null,
			OriginalTransactionId = originalTransactionId,
		};

	private static bool Matches(
		CoinPaprikaInstrument instrument,
		string requested)
		=> requested.IsEmpty() ||
			instrument.NativeId.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			instrument.CoinId.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			instrument.Symbol.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			instrument.BaseSymbol.EqualsIgnoreCase(requested) ||
			instrument.Name?.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) == true;

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}

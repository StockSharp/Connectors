namespace StockSharp.Coinalyze;

public partial class CoinalyzeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var instruments = await RestClient.GetMarketsAsync(
			MarketType, cancellationToken);
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
				Exchange.IsEmpty() ||
				instrument.Exchange.EqualsIgnoreCase(Exchange))
			.Where(instrument =>
				Matches(instrument, requested))
			.OrderBy(static instrument =>
				instrument.Exchange,
				StringComparer.OrdinalIgnoreCase)
			.ThenBy(static instrument =>
				instrument.Symbol,
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
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);
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
		var maximum = (mdMsg.Count ?? HistoryLimit)
			.Max(1)
			.Min(HistoryLimit)
			.To<int>();
		var from = (mdMsg.From ??
			to - timeFrame * maximum)
			.ToUniversalTime();
		foreach (var candle in
			(await RestClient.GetHistoryAsync(
				instrument,
				CandleMetric,
				timeFrame,
				from,
				to,
				ConvertToUsd,
				cancellationToken) ?? [])
			.Where(candle =>
				candle.OpenTime >= from &&
				candle.OpenTime <= to)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(maximum))
			await SendOutMessageAsync(
				new TimeFrameCandleMessage
				{
					SecurityId = instrument.ToStockSharp(),
					TypedArg = timeFrame,
					OpenTime = candle.OpenTime,
					CloseTime =
						candle.OpenTime + timeFrame,
					OpenPrice = candle.Open,
					HighPrice = candle.High,
					LowPrice = candle.Low,
					ClosePrice = candle.Close,
					TotalVolume = candle.Volume,
					BuyVolume = candle.BuyVolume,
					TotalTicks = candle.Trades,
					State =
						candle.OpenTime + timeFrame <=
							DateTime.UtcNow
								? CandleStates.Finished
								: CandleStates.Active,
					OriginalTransactionId =
						mdMsg.TransactionId,
				},
				cancellationToken);
		await CompleteMarketSubscriptionAsync(
			mdMsg, cancellationToken);
	}

	private async ValueTask<CoinalyzeInstrument>
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
			RememberInstruments(
				await RestClient.GetMarketsAsync(
					MarketType, cancellationToken));
			return GetInstrument(securityId);
		}
	}

	private static SecurityMessage CreateSecurity(
		CoinalyzeInstrument instrument,
		long originalTransactionId)
		=> new()
		{
			SecurityId = instrument.ToStockSharp(),
			Name = instrument.ExchangeSymbol,
			ShortName =
				$"{instrument.BaseAsset}/{instrument.QuoteAsset}",
			Class = instrument.Exchange,
			SecurityType =
				instrument.MarketType ==
					CoinalyzeMarketTypes.Futures
						? SecurityTypes.Future
						: SecurityTypes.CryptoCurrency,
			Currency = Enum.TryParse<CurrencyTypes>(
				instrument.QuoteAsset,
				true,
				out var currency)
					? currency
					: null,
			ExpiryDate = instrument.ExpiryDate,
			OriginalTransactionId = originalTransactionId,
		};

	private static bool Matches(
		CoinalyzeInstrument instrument,
		string requested)
		=> requested.IsEmpty() ||
			instrument.Symbol.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			instrument.ExchangeSymbol.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			instrument.BaseAsset.EqualsIgnoreCase(requested) ||
			instrument.QuoteAsset.EqualsIgnoreCase(requested);

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

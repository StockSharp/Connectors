namespace StockSharp.CoinGlass;

public partial class CoinGlassMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var instruments = await RestClient.GetInstrumentsAsync(
			MarketType,
			Exchange,
			Symbol,
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
			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					SecurityId = security.SecurityId,
					ServerTime =
						instrument.ServerTime ?? CurrentTime,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				}
				.TryAdd(
					Level1Fields.LastTradePrice,
					instrument.LastPrice)
				.TryAdd(
					Level1Fields.Volume,
					instrument.Volume)
				.TryAdd(
					Level1Fields.OpenInterest,
					instrument.OpenInterest)
				.TryAdd(
					Level1Fields.Change,
					instrument.Change)
				.TryAdd(
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
				"CoinGlass does not expose historical Level1 events.");
		var instrument = await ResolveInstrumentAsync(
			mdMsg.SecurityId, cancellationToken);
		var snapshot = await RestClient.GetSnapshotAsync(
			instrument, cancellationToken);
		if (snapshot is null)
			throw new InvalidDataException(
				$"CoinGlass returned no snapshot for " +
					$"'{instrument.InstrumentId}'.");
		await SendLevel1Async(
			instrument,
			snapshot,
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
		var maximum = (mdMsg.Count ?? HistoryLimit)
			.Max(1)
			.Min(HistoryLimit)
			.To<int>();
		var from = (mdMsg.From ??
			to - timeFrame * maximum)
			.ToUniversalTime();
		foreach (var candle in
			(await RestClient.GetCandlesAsync(
				instrument,
				CandleMetric,
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

	private async ValueTask<CoinGlassInstrument>
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
				await RestClient.GetInstrumentsAsync(
					MarketType,
					Exchange,
					Symbol,
					cancellationToken));
			return GetInstrument(securityId);
		}
	}

	private ValueTask SendLevel1Async(
		CoinGlassInstrument instrument,
		CoinGlassInstrument snapshot,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = instrument.ToStockSharp(),
				ServerTime =
					snapshot.ServerTime ?? CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				Level1Fields.LastTradePrice,
				snapshot.LastPrice)
			.TryAdd(
				Level1Fields.TheorPrice,
				snapshot.IndexPrice)
			.TryAdd(
				Level1Fields.Volume,
				snapshot.Volume)
			.TryAdd(
				Level1Fields.OpenInterest,
				snapshot.OpenInterest)
			.TryAdd(
				Level1Fields.Change,
				snapshot.Change)
			.TryAdd(
				Level1Fields.State,
				snapshot.IsActive
					? SecurityStates.Trading
					: SecurityStates.Stoped),
			cancellationToken);

	private ValueTask SendCandleAsync(
		CoinGlassInstrument instrument,
		CoinGlassCandle candle,
		TimeSpan timeFrame,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new TimeFrameCandleMessage
			{
				SecurityId = instrument.ToStockSharp(),
				TypedArg = timeFrame,
				OpenTime = candle.OpenTime,
				CloseTime = candle.OpenTime + timeFrame,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State =
					candle.OpenTime + timeFrame <= DateTime.UtcNow
						? CandleStates.Finished
						: CandleStates.Active,
				OriginalTransactionId =
					originalTransactionId,
			},
			cancellationToken);

	private static SecurityMessage CreateSecurity(
		CoinGlassInstrument instrument,
		long originalTransactionId)
		=> new()
		{
			SecurityId = instrument.ToStockSharp(),
			Name = instrument.Name.IsEmpty()
				? instrument.Symbol
				: instrument.Name,
			ShortName = instrument.InstrumentId,
			Class =
				$"{instrument.MarketType}:{instrument.Exchange}",
			SecurityType = instrument.MarketType switch
			{
				CoinGlassMarketTypes.Futures =>
					SecurityTypes.Future,
				CoinGlassMarketTypes.Spot =>
					SecurityTypes.CryptoCurrency,
				CoinGlassMarketTypes.Options =>
					SecurityTypes.Option,
				_ => SecurityTypes.Stock,
			},
			Currency = Enum.TryParse<CurrencyTypes>(
				instrument.QuoteAsset,
				true,
				out var currency)
					? currency
					: null,
			PriceStep = instrument.PriceStep,
			OriginalTransactionId = originalTransactionId,
		};

	private static bool Matches(
		CoinGlassInstrument instrument,
		string requested)
		=> requested.IsEmpty() ||
			instrument.NativeId.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			instrument.InstrumentId.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			instrument.Symbol.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			instrument.BaseAsset.EqualsIgnoreCase(requested) ||
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

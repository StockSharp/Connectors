namespace StockSharp.MarketDataApp;

public partial class MarketDataAppMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.Skip is < 0)
			throw new ArgumentOutOfRangeException(nameof(lookupMsg.Skip));
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			return;
		}

		var requested = (lookupMsg.SecurityId.Native as string)
			.WithoutAssetPrefix()
			.IsEmpty(lookupMsg.SecurityId.SecurityCode)
			.IsEmpty(lookupMsg.Name)
			.IsEmpty(lookupMsg.ShortName)
			?.Trim();
		if (requested.IsEmpty() &&
			lookupMsg.UnderlyingSecurityId != default)
			requested = (lookupMsg.UnderlyingSecurityId.Native
				as string).WithoutAssetPrefix()
				.IsEmpty(lookupMsg.UnderlyingSecurityId.SecurityCode)
				?.Trim();
		if (requested.IsEmpty())
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			return;
		}

		var types = lookupMsg.GetSecurityTypes();
		var optionsRequested =
			lookupMsg.SecurityType == SecurityTypes.Option ||
			lookupMsg.OptionType is not null ||
			lookupMsg.Strike is not null ||
			lookupMsg.ExpiryDate is not null ||
			lookupMsg.UnderlyingSecurityId != default ||
			requested.IsOptionSymbol() ||
			types.Contains(SecurityTypes.Option);
		var instruments = optionsRequested
			? await LookupOptionsAsync(lookupMsg, requested,
				cancellationToken)
			: await LookupCashAsync(types, requested,
				cancellationToken);
		Remember(instruments);

		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var instrument in instruments
			.GroupBy(static instrument => instrument.NativeId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First()))
		{
			var security = instrument.ToSecurityMessage(
				lookupMsg.TransactionId);
			if (!MatchesLookup(security, lookupMsg, types,
				optionsRequested))
				continue;
			if (skip-- > 0)
				continue;
			if (lookupMsg.OnlySecurityId)
				security = new()
				{
					OriginalTransactionId = lookupMsg.TransactionId,
					SecurityId = security.SecurityId,
				};
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg,
			cancellationToken);
	}

	private async ValueTask<MarketDataAppInstrument[]>
		LookupCashAsync(HashSet<SecurityTypes> types, string symbol,
			CancellationToken cancellationToken)
	{
		var requests = new List<(MarketDataAppAssetKinds Kind,
			SecurityTypes Type)>();
		if (types.Count == 0 ||
			types.Contains(SecurityTypes.Stock) ||
			types.Contains(SecurityTypes.Etf))
			requests.Add((MarketDataAppAssetKinds.Stock,
				types.Count == 1 && types.Contains(SecurityTypes.Etf)
					? SecurityTypes.Etf
					: SecurityTypes.Stock));
		if (types.Contains(SecurityTypes.Index))
			requests.Add((MarketDataAppAssetKinds.Index,
				SecurityTypes.Index));
		if (types.Contains(SecurityTypes.Fund))
			requests.Add((MarketDataAppAssetKinds.Fund,
				SecurityTypes.Fund));

		var result = new List<MarketDataAppInstrument>();
		foreach (var request in requests)
		{
			if (request.Kind == MarketDataAppAssetKinds.Fund)
			{
				var candles = await RestClient.GetCandlesAsync(
					request.Kind, "D", symbol,
					CurrentTime.Date.AddDays(-14),
					CurrentTime.Date, false, true,
					cancellationToken);
				if (candles.Length > 0)
					result.Add(new()
					{
						Symbol = symbol,
						Kind = request.Kind,
						SecurityType = request.Type,
					});
				continue;
			}
			var quotes = await RestClient.GetQuotesAsync(
				request.Kind, symbol, cancellationToken);
			result.AddRange(quotes.Select(quote =>
				quote.ToInstrument(request.Kind, request.Type)));
		}
		return [.. result];
	}

	private async ValueTask<MarketDataAppInstrument[]>
		LookupOptionsAsync(SecurityLookupMessage lookupMsg,
			string requested, CancellationToken cancellationToken)
	{
		MarketDataAppQuote[] quotes;
		if (requested.IsOptionSymbol() &&
			lookupMsg.UnderlyingSecurityId == default)
			quotes = await RestClient.GetQuotesAsync(
				MarketDataAppAssetKinds.Option, requested,
				cancellationToken);
		else
		{
			var limit = lookupMsg.Count is > 0
				? (int)Math.Min(
					(lookupMsg.Skip ?? 0) + lookupMsg.Count.Value,
					MaximumOptionContracts)
				: MaximumOptionContracts;
			quotes = await RestClient.GetOptionChainAsync(requested,
				lookupMsg.ExpiryDate, lookupMsg.OptionType,
				lookupMsg.Strike, Math.Max(1, limit),
				cancellationToken);
		}
		return quotes
			.Select(static quote => quote.ToInstrument(
				MarketDataAppAssetKinds.Option,
				SecurityTypes.Option))
			.Where(instrument => lookupMsg.IncludeExpired ||
				instrument.Expiry?.Date >= DateTime.UtcNow.Date)
			.ToArray();
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var instrument = ResolveInstrument(mdMsg.SecurityId);
		MarketDataAppQuote quote = null;
		if (instrument.Kind == MarketDataAppAssetKinds.Fund)
		{
			var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
			var candles = await RestClient.GetCandlesAsync(
				instrument.Kind, "D", instrument.Symbol,
				to.AddDays(-14), to, false, true,
				cancellationToken);
			var candle = candles.LastOrDefault();
			if (candle is not null)
				quote = new()
				{
					Symbol = instrument.Symbol,
					ServerTime = candle.OpenTime,
					Last = candle.Close,
				};
		}
		else
			quote = (await RestClient.GetQuotesAsync(instrument.Kind,
				instrument.Symbol, cancellationToken))
				.FirstOrDefault(value => value.Symbol
					.EqualsIgnoreCase(instrument.Symbol));
		if (quote is not null)
			await SendOutMessageAsync(
				quote.ToLevel1(instrument, mdMsg.TransactionId),
				cancellationToken);
		await CompleteMarketSubscriptionAsync(mdMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var instrument = ResolveInstrument(mdMsg.SecurityId);
		if (instrument.Kind == MarketDataAppAssetKinds.Option)
			throw new NotSupportedException(
				"MarketData.app does not provide option candles.");
		var timeFrame = mdMsg.GetTimeFrame();
		var resolution = timeFrame.ToResolution(instrument.Kind);
		var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
		var maximum = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, int.MaxValue)
			: 100;
		var from = (mdMsg.From ??
			to - timeFrame * Math.Min(maximum, 10000))
			.ToUniversalTime();
		var candles = await LoadCandlesAsync(instrument, resolution,
			timeFrame, from, to, cancellationToken);
		foreach (var candle in candles
			.Where(candle => candle.OpenTime >= from &&
				candle.OpenTime <= to)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(maximum))
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = instrument.ToSecurityId(),
				TypedArg = timeFrame,
				OpenTime = candle.OpenTime,
				CloseTime = candle.OpenTime + timeFrame,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
		await CompleteMarketSubscriptionAsync(mdMsg,
			cancellationToken);
	}

	private async ValueTask<MarketDataAppCandle[]>
		LoadCandlesAsync(MarketDataAppInstrument instrument,
			string resolution, TimeSpan timeFrame,
			DateTime from, DateTime to,
			CancellationToken cancellationToken)
	{
		var result = new List<MarketDataAppCandle>();
		var cursor = from;
		do
		{
			var end = timeFrame < TimeSpan.FromDays(1)
				? new[] { cursor.AddDays(365), to }.Min()
				: to;
			result.AddRange(await RestClient.GetCandlesAsync(
				instrument.Kind, resolution, instrument.Symbol,
				cursor, end, ExtendedHours, AdjustSplits,
				cancellationToken));
			if (end >= to)
				break;
			cursor = end.AddSeconds(1);
		}
		while (cursor <= to);
		return result
			.GroupBy(static candle => candle.OpenTime)
			.Select(static group => group.First())
			.OrderBy(static candle => candle.OpenTime)
			.ToArray();
	}

	private static bool MatchesLookup(SecurityMessage security,
		SecurityLookupMessage lookup, HashSet<SecurityTypes> types,
		bool optionsRequested)
	{
		if (!optionsRequested)
			return security.IsMatch(lookup, types);
		if (types.Count > 0 &&
			!types.Contains(SecurityTypes.Option))
			return false;
		if (lookup.OptionType is not null &&
			security.OptionType != lookup.OptionType)
			return false;
		if (lookup.Strike is not null &&
			security.Strike != lookup.Strike)
			return false;
		if (lookup.ExpiryDate is not null &&
			security.ExpiryDate?.Date != lookup.ExpiryDate.Value.Date)
			return false;
		return true;
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message,
			cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}

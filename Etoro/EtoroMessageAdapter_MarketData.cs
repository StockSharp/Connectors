namespace StockSharp.Etoro;

public partial class EtoroMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var nativeId = lookupMsg.SecurityId.Native?.To<int?>();
		var left = lookupMsg.Count ?? long.MaxValue;
		var page = 1;
		var pageSize = (int)Math.Min(1000, left);

		while (left > 0)
		{
			var response = nativeId is > 0
				? await _rest.SearchInstrument(nativeId.Value, cancellationToken)
				: await _rest.SearchInstruments(lookupMsg.SecurityId.SecurityCode, lookupMsg.Name,
					page, pageSize, cancellationToken);
			var items = response.Items ?? [];

			foreach (var instrument in items)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (instrument == null || instrument.InstrumentId <= 0 || instrument.InternalSymbolFull.IsEmpty())
					continue;

				var security = new SecurityMessage
				{
					OriginalTransactionId = lookupMsg.TransactionId,
					SecurityId = instrument.ToSecurityId(),
					SecurityType = instrument.InstrumentType.IsEmpty(instrument.InternalAssetClassName).ToSecurityType(),
					Name = instrument.DisplayName,
					ShortName = instrument.InternalSymbolFull,
					Class = instrument.InternalAssetClassName,
				};

				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;

				_instruments[instrument.InstrumentId] = instrument;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}

			if (nativeId is > 0 || items.Length == 0 || page * pageSize >= response.TotalItems || items.Length < pageSize)
				break;
			page++;
			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var old) &&
				!_marketSubscriptions.CachedValues.Any(s => s.InstrumentId == old.InstrumentId))
			{
				await _stream.Unsubscribe([$"instrument:{old.InstrumentId.ToString(CultureInfo.InvariantCulture)}"], cancellationToken);
			}
			return;
		}

		var instrument = await ResolveInstrument(mdMsg.SecurityId, cancellationToken);
		var rate = (await _rest.GetRates([instrument.InstrumentId], cancellationToken)).Rates?.FirstOrDefault();
		if (rate != null)
			await SendRate(mdMsg.TransactionId, mdMsg.SecurityId, rate, cancellationToken);

		var subscription = new MarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			InstrumentId = instrument.InstrumentId,
		};
		_marketSubscriptions[mdMsg.TransactionId] = subscription;
		try
		{
			await _stream.Subscribe([$"instrument:{instrument.InstrumentId.ToString(CultureInfo.InvariantCulture)}"], false,
				cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var instrument = await ResolveInstrument(mdMsg.SecurityId, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		var count = (int)Math.Clamp(mdMsg.Count ?? 1000, 1, 1000);
		var response = await _rest.GetCandles(instrument.InstrumentId, EtoroCandleDirections.Desc,
			timeFrame.ToNativeInterval(), count, cancellationToken);
		var from = mdMsg.From?.UtcKind();
		var to = mdMsg.To?.UtcKind();
		var left = mdMsg.Count ?? long.MaxValue;

		foreach (var candle in (response.Candles ?? []).SelectMany(g => g.Candles ?? []).OrderBy(c => c.FromDate))
		{
			var openTime = candle.FromDate.UtcKind();
			if (from != null && openTime < from.Value)
				continue;
			if (to != null && openTime > to.Value)
				break;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				DataType = mdMsg.DataType2,
				OpenTime = openTime,
				CloseTime = openTime + timeFrame,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<EtoroInstrument> ResolveInstrument(SecurityId securityId, CancellationToken cancellationToken)
	{
		var nativeId = securityId.Native?.To<int?>();
		if (nativeId is > 0 && _instruments.TryGetValue(nativeId.Value, out var cached))
			return cached;

		cached = _instruments.CachedValues.FirstOrDefault(i =>
			i.InternalSymbolFull.EqualsIgnoreCase(securityId.SecurityCode));
		if (cached != null)
			return cached;

		if (nativeId is > 0)
		{
			var byId = await _rest.SearchInstrument(nativeId.Value, cancellationToken);
			cached = byId.Items?.FirstOrDefault(i => i.InstrumentId == nativeId.Value);
		}
		else
		{
			var result = await _rest.SearchInstruments(securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode)),
				null, 1, 100, cancellationToken);
			cached = result.Items?.FirstOrDefault(i => i.InternalSymbolFull.EqualsIgnoreCase(securityId.SecurityCode));
		}

		if (cached == null)
			throw new InvalidOperationException($"eToro instrument '{securityId}' was not found. Run security lookup first.");
		_instruments[cached.InstrumentId] = cached;
		return cached;
	}

	private async Task EnsureInstruments(IEnumerable<int> instrumentIds, CancellationToken cancellationToken)
	{
		var missing = instrumentIds
			.Where(id => id > 0 && !_instruments.ContainsKey(id))
			.Distinct()
			.ToArray();

		for (var offset = 0; offset < missing.Length; offset += 100)
		{
			var response = await _rest.GetInstruments(missing.Skip(offset).Take(100), cancellationToken);
			foreach (var display in response.Items ?? [])
			{
				if (display == null || display.InstrumentId <= 0 || display.SymbolFull.IsEmpty())
					continue;

				_instruments[display.InstrumentId] = new()
				{
					InstrumentId = display.InstrumentId,
					DisplayName = display.DisplayName,
					InstrumentTypeId = display.InstrumentTypeId,
					ExchangeId = display.ExchangeId,
					InternalSymbolFull = display.SymbolFull,
				};
			}
		}
	}

	private async ValueTask ProcessRate(int instrumentId, EtoroRate rate, CancellationToken cancellationToken)
	{
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(s => s.InstrumentId == instrumentId))
			await SendRate(subscription.TransactionId, subscription.SecurityId, rate, cancellationToken);
	}

	private ValueTask SendRate(long transactionId, SecurityId securityId, EtoroRate rate,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = rate.Date == default ? DateTime.UtcNow : rate.Date.UtcKind(),
		}
		.TryAdd(Level1Fields.BestBidPrice, rate.Bid)
		.TryAdd(Level1Fields.BestAskPrice, rate.Ask)
		.TryAdd(Level1Fields.LastTradePrice, rate.LastExecution), cancellationToken);
}

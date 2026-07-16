namespace StockSharp.Saxo;

public partial class SaxoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var count = (int)Math.Clamp(lookupMsg.Count ?? 100, 1, 1000);
		var response = await _client.Rest.FindInstruments(lookupMsg.SecurityId.SecurityCode,
			securityTypes.ToSaxoAssetTypes(), count, _client.Session.AccountKey, cancellationToken);
		var left = count;
		foreach (var summary in response.Data ?? [])
		{
			if (left <= 0)
				break;
			if (summary.SummaryType.ContainsIgnoreCase("OptionRoot"))
			{
				var space = await _client.Rest.GetOptionSpace(summary.Identifier, _client.Session.ClientKey, cancellationToken);
				foreach (var option in (space.OptionSpace ?? []).SelectMany(s => s.SpecificOptions ?? []))
				{
					var details = await _client.Rest.GetInstrument(option.Uic, space.AssetType.IsEmpty(summary.AssetType),
						_client.Session.AccountKey, cancellationToken);
					if (await SendSecurity(details.ToInstrument(), lookupMsg, securityTypes, cancellationToken) && --left <= 0)
						break;
				}
			}
			else if (summary.SummaryType.ContainsIgnoreCase("FuturesSpace") ||
				(summary.AssetType.EqualsIgnoreCase("ContractFutures") && summary.DisplayHint.EqualsIgnoreCase("Continuous")))
			{
				var space = await _client.Rest.GetFutureSpace(summary.Identifier, cancellationToken);
				foreach (var future in space.Elements ?? [])
				{
					var details = await _client.Rest.GetInstrument(future.Uic, future.AssetType.IsEmpty(summary.AssetType),
						_client.Session.AccountKey, cancellationToken);
					if (await SendSecurity(details.ToInstrument(), lookupMsg, securityTypes, cancellationToken) && --left <= 0)
						break;
				}
			}
			else
			{
				var details = await _client.Rest.GetInstrument(summary.Identifier, summary.AssetType,
					_client.Session.AccountKey, cancellationToken);
				if (await SendSecurity(details.ToInstrument(), lookupMsg, securityTypes, cancellationToken))
					left--;
			}
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessPriceSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessPriceSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await _client.UnsubscribeCandles(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		var instrument = await ResolveInstrument(mdMsg.SecurityId, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToSaxoHorizon();

		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
			await SendHistoricalCandles(mdMsg, instrument, timeFrame, cancellationToken);
		if (!mdMsg.IsHistoryOnly())
		{
			await _client.SubscribeCandles(new()
			{
				TransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TimeFrame = timeFrame,
				Instrument = instrument,
			}, cancellationToken);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask ProcessPriceSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await _client.UnsubscribePrice(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		var instrument = await ResolveInstrument(mdMsg.SecurityId, cancellationToken);
		await _client.SubscribePrice(new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			DataType = dataType,
			Instrument = instrument,
		}, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask<bool> SendSecurity(SaxoInstrument instrument, SecurityLookupMessage lookupMsg,
		HashSet<SecurityTypes> securityTypes, CancellationToken cancellationToken)
	{
		if (instrument == null || instrument.Uic <= 0)
			return false;
		var security = new SecurityMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			SecurityId = instrument.ToSecurityId(),
			SecurityType = instrument.AssetType.ToSecurityType(),
			Name = instrument.Description,
			ShortName = instrument.Symbol,
			Currency = Enum.TryParse<CurrencyTypes>(instrument.Currency, true, out var currency) ? currency : null,
			ExpiryDate = instrument.ExpiryDate,
			Strike = instrument.Strike,
			OptionType = instrument.PutCall.EqualsIgnoreCase("Call") ? OptionTypes.Call :
				instrument.PutCall.EqualsIgnoreCase("Put") ? OptionTypes.Put : null,
			Multiplier = instrument.Multiplier,
			PriceStep = instrument.PriceStep,
		};
		if (!security.IsMatch(lookupMsg, securityTypes))
			return false;
		_instruments[instrument.Uic] = instrument;
		await SendOutMessageAsync(security, cancellationToken);
		return true;
	}

	private async Task<SaxoInstrument> ResolveInstrument(SecurityId securityId, CancellationToken cancellationToken)
	{
		var uic = securityId.ToUic();
		if (_instruments.TryGetValue(uic, out var instrument))
			return instrument;
		var assetType = securityId.ToSaxoAssetType();
		if (assetType.IsEmpty())
			throw new InvalidOperationException($"Saxo asset type is missing for '{securityId}'. Run security lookup first.");
		instrument = (await _client.Rest.GetInstrument(uic, assetType, _client.Session.AccountKey, cancellationToken)).ToInstrument();
		_instruments[uic] = instrument;
		return instrument;
	}

	private async ValueTask OnPriceReceived(SaxoPriceEvent priceEvent, CancellationToken cancellationToken)
	{
		var price = priceEvent.Data;
		var serverTime = (priceEvent.Timestamp ?? price.LastUpdated ?? DateTime.UtcNow).UtcKind();
		if (priceEvent.DataType == DataType.Level1)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = priceEvent.TransactionId,
				SecurityId = priceEvent.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.BestBidPrice, price.Quote?.Bid)
			.TryAdd(Level1Fields.BestBidVolume, price.Quote?.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, price.Quote?.Ask)
			.TryAdd(Level1Fields.BestAskVolume, price.Quote?.AskSize)
			.TryAdd(Level1Fields.LastTradePrice, price.PriceInfoDetails?.LastTraded)
			.TryAdd(Level1Fields.LastTradeVolume, price.PriceInfoDetails?.LastTradedSize)
			.TryAdd(Level1Fields.OpenPrice, price.PriceInfoDetails?.Open)
			.TryAdd(Level1Fields.HighPrice, price.PriceInfo?.High)
			.TryAdd(Level1Fields.LowPrice, price.PriceInfo?.Low)
			.TryAdd(Level1Fields.ClosePrice, price.PriceInfoDetails?.LastClose)
			.TryAdd(Level1Fields.Volume, price.PriceInfoDetails?.Volume)
			.TryAdd(Level1Fields.OpenInterest, price.PriceInfoDetails?.OpenInterest), cancellationToken);
			return;
		}

		var depth = price.MarketDepth;
		var bids = depth == null
			? price.Quote?.Bid is > 0 ? [new(price.Quote.Bid.Value, price.Quote.BidSize ?? 0)] : []
			: SaxoExtensions.ToQuotes(depth.Bid, depth.BidSize, depth.BidOrders, true);
		var asks = depth == null
			? price.Quote?.Ask is > 0 ? [new(price.Quote.Ask.Value, price.Quote.AskSize ?? 0)] : []
			: SaxoExtensions.ToQuotes(depth.Ask, depth.AskSize, depth.AskOrders, false);
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = priceEvent.TransactionId,
			SecurityId = priceEvent.SecurityId,
			ServerTime = serverTime,
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private ValueTask OnCandleReceived(SaxoCandleEvent candleEvent, CancellationToken cancellationToken)
		=> SendCandle(candleEvent.TransactionId, candleEvent.SecurityId, candleEvent.TimeFrame,
			candleEvent.Data, candleEvent.State, cancellationToken);

	private async ValueTask SendHistoricalCandles(MarketDataMessage mdMsg, SaxoInstrument instrument, TimeSpan timeFrame,
		CancellationToken cancellationToken)
	{
		var from = mdMsg.From?.ToUniversalTime();
		DateTimeOffset? cursor = mdMsg.To is null ? null : new(mdMsg.To.Value.ToUniversalTime());
		var remaining = (int)Math.Clamp(mdMsg.Count ?? (from is null ? 1200 : 12000), 1, 12000);
		var samples = new SortedDictionary<DateTime, SaxoChartSample>();
		while (remaining > 0)
		{
			var requestCount = Math.Min(remaining, 1200);
			var response = await _client.Rest.GetCandles(instrument, timeFrame.ToSaxoHorizon(),
				requestCount, cursor, cancellationToken);
			var page = response.Data ?? [];
			if (page.Length == 0)
				break;
			foreach (var sample in page)
			{
				var time = sample.Time.UtcKind();
				if (from is null || time >= from)
					samples[time] = sample;
			}
			remaining -= page.Length;
			var oldest = page.Min(s => s.Time).UtcKind();
			if ((from is not null && oldest <= from) || page.Length < requestCount)
				break;
			cursor = new DateTimeOffset(oldest.AddTicks(-1));
		}
		foreach (var sample in samples.Values)
			await SendCandle(mdMsg.TransactionId, mdMsg.SecurityId, timeFrame, sample, CandleStates.Finished, cancellationToken);
	}

	private ValueTask SendCandle(long transactionId, SecurityId securityId, TimeSpan timeFrame, SaxoChartSample sample,
		CandleStates state, CancellationToken cancellationToken)
	{
		var open = sample.Open();
		var high = sample.High();
		var low = sample.Low();
		var close = sample.Close();
		if (open is null || high is null || low is null || close is null)
			return default;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			TypedArg = timeFrame,
			OpenTime = sample.Time.UtcKind(),
			OpenPrice = open.Value,
			HighPrice = high.Value,
			LowPrice = low.Value,
			ClosePrice = close.Value,
			TotalVolume = sample.Volume ?? 0,
			OpenInterest = sample.Interest,
			State = state,
		}, cancellationToken);
	}
}

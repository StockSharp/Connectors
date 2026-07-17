namespace StockSharp.LemonMarkets;

public partial class LemonMarketsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var code = lookupMsg.SecurityId.SecurityCode?.ToUpperInvariant();
		IEnumerable<LemonInstrument> instruments;
		if (code.IsIsin())
		{
			var instrument = await ResolveInstrument(code, cancellationToken);
			instruments = [instrument];
		}
		else
		{
			await EnsureMetadata(cancellationToken);
			instruments = _instruments.CachedValues.OrderBy(instrument => instrument.Isin);
		}

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var instrument in instruments)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (instrument?.Isin.IsEmpty() != false)
				continue;
			var securityType = instrument.Type.ToSecurityType();
			if (securityType == null || securityTypes.Count > 0 &&
				!securityTypes.Contains(securityType.Value))
				continue;

			var message = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = instrument.ToSecurityId(),
				SecurityType = securityType,
				Name = instrument.Title,
				ShortName = instrument.Title,
				Class = instrument.Type,
				Currency = CurrencyTypes.EUR,
				PriceStep = 0.0001m,
				VolumeStep = 0.00001m,
				MinVolume = 0.00001m,
			};
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

		var isin = message.SecurityId.SecurityCode?.ToUpperInvariant();
		await ResolveInstrument(isin, cancellationToken);
		var securityId = isin.ToLemonSecurityId();
		var prices = await _client.GetPrices(isin, cancellationToken) ?? [];
		await SendLevel1(message.TransactionId, securityId, prices, cancellationToken);

		if (message.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		else
		{
			_level1Subscriptions[message.TransactionId] = securityId;
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		_lastLevel1Refresh = CurrentTime;
	}

	private async Task EnsureMetadata(CancellationToken cancellationToken)
	{
		if (_instruments.Count > 0 &&
			CurrentTime - _lastInstrumentRefresh < TimeSpan.FromMinutes(15))
			return;

		var instruments = await _client.GetInstruments(cancellationToken) ?? [];
		_instruments.Clear();
		foreach (var instrument in instruments)
		{
			if (instrument?.Isin.IsEmpty() == false)
				_instruments[instrument.Isin] = instrument;
		}
		_lastInstrumentRefresh = CurrentTime;
	}

	private async Task RefreshLevel1(CancellationToken cancellationToken)
	{
		var subscriptions = _level1Subscriptions.ToArray();
		foreach (var group in subscriptions.GroupBy(pair => pair.Value.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var prices = await _client.GetPrices(group.Key, cancellationToken) ?? [];
			foreach (var pair in group)
				await SendLevel1(pair.Key, pair.Value, prices, cancellationToken);
		}
		_lastLevel1Refresh = CurrentTime;
	}

	private ValueTask SendLevel1(long originalTransactionId, SecurityId securityId,
		LemonPrice[] prices, CancellationToken cancellationToken)
	{
		var quote = prices.FirstOrDefault(price => price?.Type.EqualsIgnoreCase("quote") == true);
		var nav = prices.FirstOrDefault(price => price?.Type.EqualsIgnoreCase("nav") == true);
		var source = quote ?? nav;
		if (source == null)
			return default;

		var serverTime = source.UpdatedAt ?? source.ValuationDate ?? CurrentTime;
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			ServerTime = serverTime == default ? CurrentTime : serverTime.NormalizeUtc(),
		};
		if (quote != null)
		{
			message
				.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
				.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
				.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
				.TryAdd(Level1Fields.BestAskVolume, quote.AskSize);
		}
		else
		{
			message
				.TryAdd(Level1Fields.LastTradePrice, nav.Price)
				.TryAdd(Level1Fields.LastTradeTime,
					nav.ValuationDate?.NormalizeUtc());
		}
		return SendOutMessageAsync(message, cancellationToken);
	}
}

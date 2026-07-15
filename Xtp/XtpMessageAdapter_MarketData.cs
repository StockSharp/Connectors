namespace StockSharp.Xtp;

public partial class XtpMessageAdapter
{
	private readonly SynchronizedDictionary<string, SynchronizedDictionary<DataType, long>> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds = new(StringComparer.OrdinalIgnoreCase);
	private SecurityLookupMessage _securityLookup;
	private int _securityLookupPending;
	private long _securityLookupLeft;

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (_securityLookup != null)
			throw new InvalidOperationException("Only one XTP security lookup can be active at a time.");

		var exchanges = lookupMsg.SecurityId.BoardCode?.ToUpperInvariant() switch
		{
			BoardCodes.Sse => [XtpExchange.Shanghai],
			BoardCodes.Szse => [XtpExchange.Shenzhen],
			BoardCodes.Bse => [XtpExchange.Beijing],
			_ => new[] { XtpExchange.Shanghai, XtpExchange.Shenzhen, XtpExchange.Beijing },
		};

		_securityLookup = lookupMsg.TypedClone();
		_securityLookupPending = exchanges.Length;
		_securityLookupLeft = lookupMsg.Count ?? long.MaxValue;

		try
		{
			foreach (var exchange in exchanges)
				_client.QuerySecurities(exchange);
		}
		catch
		{
			_securityLookup = null;
			_securityLookupPending = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketDataSubscription(mdMsg, DataType.Level1, false, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketDataSubscription(mdMsg, DataType.MarketDepth, false, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketDataSubscription(mdMsg, DataType.Ticks, true, cancellationToken);

	private async ValueTask ProcessMarketDataSubscription(MarketDataMessage mdMsg, DataType dataType, bool isTicks, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var ticker = mdMsg.SecurityId.SecurityCode;
		if (ticker.IsEmpty())
			throw new ArgumentException("Security code is not specified.", nameof(mdMsg));

		var key = GetKey(mdMsg.SecurityId);
		var exchange = mdMsg.SecurityId.ToXtpExchange();
		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				var subscriptions = _marketSubscriptions.SafeAdd(key);
				var hasSameNativeStream = isTicks
					? subscriptions.ContainsKey(DataType.Ticks)
					: subscriptions.ContainsKey(DataType.Level1) || subscriptions.ContainsKey(DataType.MarketDepth);
				subscriptions[dataType] = mdMsg.TransactionId;
				_securityIds[key] = mdMsg.SecurityId;

				if (!hasSameNativeStream)
				{
					if (isTicks)
						_client.SubscribeTicks(ticker, exchange, true);
					else
						_client.SubscribeMarketData(ticker, exchange, true);
				}
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetValue(key, out var subscriptions))
		{
			subscriptions.Remove(dataType);
			var hasSameNativeStream = isTicks
				? subscriptions.ContainsKey(DataType.Ticks)
				: subscriptions.ContainsKey(DataType.Level1) || subscriptions.ContainsKey(DataType.MarketDepth);

			if (!hasSameNativeStream)
			{
				if (isTicks)
					_client.SubscribeTicks(ticker, exchange, false);
				else
					_client.SubscribeMarketData(ticker, exchange, false);
			}

			if (subscriptions.Count == 0)
			{
				_marketSubscriptions.Remove(key);
				_securityIds.Remove(key);
			}
		}
	}

	private void OnNativeSecurity(XtpNativeSecurity? security, XtpNativeError error, bool isLast)
		=> Enqueue(cancellationToken => ProcessNativeSecurity(security, error, isLast, cancellationToken));

	private async ValueTask ProcessNativeSecurity(XtpNativeSecurity? native, XtpNativeError error, bool isLast, CancellationToken cancellationToken)
	{
		var lookup = _securityLookup;
		if (lookup == null)
			return;

		if (error.Id != 0)
			await SendOutErrorAsync(error.ToException("security lookup"), cancellationToken);

		if (native is { } security && _securityLookupLeft > 0)
		{
			var volumeUnit = new[] { security.BuyQuantityUnit, security.SellQuantityUnit }.Where(unit => unit > 0).DefaultIfEmpty().Min();
			var message = new SecurityMessage
			{
				OriginalTransactionId = lookup.TransactionId,
				SecurityId = security.Ticker.ToQuoteSecurityId(security.Exchange),
				Name = security.Name,
				ShortName = security.Name,
				SecurityType = security.SecurityType.ToSecurityType(),
				Currency = CurrencyTypes.CNY,
				PriceStep = security.PriceTick > 0 ? (decimal)security.PriceTick : null,
				VolumeStep = volumeUnit > 0 ? volumeUnit : null,
				MinVolume = volumeUnit > 0 ? volumeUnit : null,
			};

			if (message.IsMatch(lookup, lookup.GetSecurityTypes()))
			{
				_securityIds[GetKey(message.SecurityId)] = message.SecurityId;
				await SendOutMessageAsync(message, cancellationToken);
				_securityLookupLeft--;
			}
		}

		if (isLast && --_securityLookupPending == 0)
		{
			_securityLookup = null;
			await SendSubscriptionResultAsync(lookup, cancellationToken);
		}
	}

	private void OnNativeDepth(XtpNativeDepth depth)
		=> Enqueue(cancellationToken => ProcessNativeDepth(depth, cancellationToken));

	private async ValueTask ProcessNativeDepth(XtpNativeDepth depth, CancellationToken cancellationToken)
	{
		var securityId = depth.Ticker.ToQuoteSecurityId(depth.Exchange);
		var key = GetKey(securityId);
		if (!_marketSubscriptions.TryGetValue(key, out var subscriptions))
			return;

		securityId = _securityIds.TryGetValue2(key) ?? securityId;
		var serverTime = depth.Time.ToXtpTime();

		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			var message = new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, Positive(depth.LastPrice))
			.TryAdd(Level1Fields.OpenPrice, Positive(depth.OpenPrice))
			.TryAdd(Level1Fields.HighPrice, Positive(depth.HighPrice))
			.TryAdd(Level1Fields.LowPrice, Positive(depth.LowPrice))
			.TryAdd(Level1Fields.ClosePrice, Positive(depth.ClosePrice))
			.TryAdd(Level1Fields.SettlementPrice, Positive(depth.PreviousClose))
			.TryAdd(Level1Fields.MinPrice, Positive(depth.LowerLimit))
			.TryAdd(Level1Fields.MaxPrice, Positive(depth.UpperLimit))
			.TryAdd(Level1Fields.Volume, depth.Volume > 0 ? depth.Volume : null)
			.TryAdd(Level1Fields.Turnover, Positive(depth.Turnover))
			.TryAdd(Level1Fields.AveragePrice, Positive(depth.AveragePrice))
			.TryAdd(Level1Fields.TradesCount, depth.TradesCount > 0 ? depth.TradesCount : null)
			.TryAdd(Level1Fields.BestBidPrice, Positive(depth.Bids?.FirstOrDefault() ?? 0))
			.TryAdd(Level1Fields.BestBidVolume, depth.BidVolumes?.FirstOrDefault() is > 0 and var bidVolume ? bidVolume : null)
			.TryAdd(Level1Fields.BestAskPrice, Positive(depth.Asks?.FirstOrDefault() ?? 0))
			.TryAdd(Level1Fields.BestAskVolume, depth.AskVolumes?.FirstOrDefault() is > 0 and var askVolume ? askVolume : null);

			await SendOutMessageAsync(message, cancellationToken);
		}

		if (subscriptions.TryGetValue(DataType.MarketDepth, out var depthId))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = depthId,
				SecurityId = securityId,
				ServerTime = serverTime,
				Bids = [.. Enumerable.Range(0, 10).Where(i => depth.Bids?[i] > 0 && depth.BidVolumes?[i] > 0).Select(i => new QuoteChange((decimal)depth.Bids[i], depth.BidVolumes[i]))],
				Asks = [.. Enumerable.Range(0, 10).Where(i => depth.Asks?[i] > 0 && depth.AskVolumes?[i] > 0).Select(i => new QuoteChange((decimal)depth.Asks[i], depth.AskVolumes[i]))],
			}, cancellationToken);
		}
	}

	private void OnNativeTick(XtpNativeTick tick)
		=> Enqueue(cancellationToken => ProcessNativeTick(tick, cancellationToken));

	private ValueTask ProcessNativeTick(XtpNativeTick tick, CancellationToken cancellationToken)
	{
		if (tick.Type != 2)
			return default;

		var securityId = tick.Ticker.ToQuoteSecurityId(tick.Exchange);
		var key = GetKey(securityId);
		if (!_marketSubscriptions.TryGetValue(key, out var subscriptions) || !subscriptions.TryGetValue(DataType.Ticks, out var transactionId))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transactionId,
			SecurityId = _securityIds.TryGetValue2(key) ?? securityId,
			TradeStringId = $"{tick.Channel}:{tick.SourceSequence}",
			TradePrice = (decimal)tick.Price,
			TradeVolume = tick.Volume,
			ServerTime = tick.Time.ToXtpTime(),
			OriginSide = tick.Flag == 'B' ? Sides.Buy : tick.Flag == 'S' ? Sides.Sell : null,
		}, cancellationToken);
	}

	private static decimal? Positive(double value) => value > 0 ? (decimal)value : null;

	private static string GetKey(SecurityId securityId)
		=> $"{securityId.BoardCode}:{securityId.SecurityCode}";

	private void ClearMarketDataState()
	{
		_marketSubscriptions.Clear();
		_securityIds.Clear();
		_securityLookup = null;
		_securityLookupPending = 0;
		_securityLookupLeft = 0;
	}
}

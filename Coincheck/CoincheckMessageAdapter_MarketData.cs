namespace StockSharp.Coincheck;

partial class CoincheckMessageAdapter
{
	private static readonly string[] _securities =
	[
		"btc_jpy",
		"eth_jpy",
		"etc_jpy",
		"lsk_jpy",
		"fct_jpy",
		"xrp_jpy",
		"xem_jpy",
		"ltc_jpy",
		"bch_jpy",
		"mona_jpy",
	];

	private readonly SynchronizedSet<string> _orderBooks = new(StringComparer.InvariantCultureIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var secId in _securities)
		{
			var secMsg = secId.ToStockSharp().FillDefaultCryptoFields();
			secMsg.OriginalTransactionId = lookupMsg.TransactionId;

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBookAsync(currency, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBookAsync(currency, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.To != null)
			{
				var trades = (await _httpClient.GetTradesAsync(currency, true, (int?)mdMsg.Count, cancellationToken)).ToArray();

				foreach (var trade in trades.OrderBy(t => t.Id))
				{
					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						SecurityId = mdMsg.SecurityId,
						TradeId = trade.Id,
						TradePrice = trade.Price,
						TradeVolume = trade.Amount,
						ServerTime = trade.CreatedAt.ToDto("MM/dd/yyyy HH:mm:ss"),
						OriginalTransactionId = mdMsg.TransactionId,
					}, cancellationToken);
				}
			}
			else
				await _pusherClient.SubscribeTradesAsync(currency, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeTradesAsync(currency, cancellationToken);
		}
	}

	private ValueTask SessionOnNewTrade(Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Currency.ToStockSharp(),
			TradeId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			ServerTime = CurrentTime,
			OriginSide = trade.Type.ToSide(),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookChanged(string currencyPair, OrderBook book, CancellationToken cancellationToken)
	{
		var state = _orderBooks.TryAdd(currencyPair) ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment;

		QuoteChange ToChange(OrderBookEntry entry) => new(entry.Price, entry.Size);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = currencyPair.ToStockSharp(),
			Bids = book.Bids?.Select(ToChange).ToArray() ?? [],
			Asks = book.Asks?.Select(ToChange).ToArray() ?? [],
			State = state,
			ServerTime = CurrentTime,
		}, cancellationToken);
	}
}

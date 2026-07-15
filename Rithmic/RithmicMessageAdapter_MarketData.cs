namespace StockSharp.Rithmic;

using Rti;

public partial class RithmicMessageAdapter
{
	private readonly SynchronizedDictionary<long, MarketDataMessage> _mdSubscriptions = [];

	private void OnTickerMessage(int templateId, byte[] data)
	{
		switch (templateId)
		{
			case TemplateId.LastTrade:
				ProcessLastTrade(data);
				break;
			case TemplateId.BestBidOffer:
				ProcessBestBidOffer(data);
				break;
			case TemplateId.OrderBook:
				ProcessOrderBook(data);
				break;
			case TemplateId.ResponseHeartbeat:
				break;
			default:
				this.AddDebugLog($"Ticker msg: template_id={templateId}");
				break;
		}
	}

	private void OnHistoryMessage(int templateId, byte[] data)
	{
		switch (templateId)
		{
			case TemplateId.ResponseSearchSymbols:
				ProcessSearchSymbols(data);
				break;
			case TemplateId.ResponseReferenceData:
				ProcessReferenceData(data);
				break;
			case TemplateId.ResponseTimeBarReplay:
				ProcessTimeBarReplay(data);
				break;
			case TemplateId.ResponseHeartbeat:
				break;
			default:
				this.AddDebugLog($"History msg: template_id={templateId}");
				break;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secCode = lookupMsg.SecurityId.SecurityCode;

		if (secCode.IsEmpty() || secCode == "*")
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}

		_secLookupTransactions[lookupMsg.TransactionId] = (lookupMsg.TransactionId, cancellationToken);

		var rq = new RequestSearchSymbols
		{
			TemplateId = TemplateId.RequestSearchSymbols,
			SearchText = secCode,
			Pattern = RequestSearchSymbols.Types.Pattern.Contains,
		};

		if (!lookupMsg.SecurityId.BoardCode.IsEmpty())
			rq.Exchange = lookupMsg.SecurityId.BoardCode;

		rq.UserMsg.Add(lookupMsg.TransactionId.To<string>());

		await _historyClient.SendAsync(rq, cancellationToken);
	}

	private readonly SynchronizedDictionary<long, (long transId, CancellationToken token)> _secLookupTransactions = [];

	private async void ProcessSearchSymbols(byte[] data)
	{
		var rp = ResponseSearchSymbols.Parser.ParseFrom(data);

		long transId = 0;
		if (rp.UserMsg.Count > 0)
			long.TryParse(rp.UserMsg[0], out transId);

		if (transId == 0) return;

		// data message
		if (rp.RqHandlerRpCode.Count > 0 && rp.RqHandlerRpCode[0] == "0" && rp.HasSymbol)
		{
			await SendOutMessageAsync(new SecurityMessage
			{
				SecurityId = new SecurityId
				{
					SecurityCode = rp.Symbol,
					BoardCode = rp.HasExchange ? rp.Exchange : string.Empty,
				},
				Name = rp.HasSymbolName ? rp.SymbolName : null,
				SecurityType = rp.HasInstrumentType ? rp.InstrumentType.ToSecurityType() : null,
				ExpiryDate = rp.HasExpirationDate ? rp.ExpirationDate.ToDateTime("yyyyMMdd") : null,
				OriginalTransactionId = transId,
			}, CancellationToken.None);
		}

		// end message
		if (rp.RpCode.Count > 0)
		{
			_secLookupTransactions.Remove(transId);
			await SendSubscriptionFinishedAsync(transId, CancellationToken.None);
		}
	}

	private async void ProcessReferenceData(byte[] data)
	{
		var rp = ResponseReferenceData.Parser.ParseFrom(data);

		if (rp.RpCode.Count > 0 && rp.RpCode[0] != "0")
			return;

		if (!rp.HasSymbol)
			return;

		await SendOutMessageAsync(new SecurityMessage
		{
			SecurityId = new SecurityId
			{
				SecurityCode = rp.Symbol,
				BoardCode = rp.HasExchange ? rp.Exchange : string.Empty,
			},
			Name = rp.HasSymbolName ? rp.SymbolName : null,
			SecurityType = rp.HasInstrumentType ? rp.InstrumentType.ToSecurityType() : null,
			ExpiryDate = rp.HasExpirationDate ? rp.ExpirationDate.ToDateTime("yyyyMMdd") : null,
			PriceStep = rp.HasMinQpriceChange ? (decimal)rp.MinQpriceChange : null,
			Multiplier = rp.HasSinglePointValue ? (decimal)rp.SinglePointValue : null,
			Strike = rp.HasStrikePrice ? (decimal)rp.StrikePrice : null,
			Currency = rp.HasCurrency ? rp.Currency.To<CurrencyTypes?>() : null,
		}, CancellationToken.None);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_mdSubscriptions[mdMsg.TransactionId] = mdMsg.TypedClone();
			await SubscribeMarketData(mdMsg, RequestMarketDataUpdate.Types.UpdateBits.LastTrade, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			if (_mdSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var orig))
				await UnsubscribeMarketData(orig, RequestMarketDataUpdate.Types.UpdateBits.LastTrade, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_mdSubscriptions[mdMsg.TransactionId] = mdMsg.TypedClone();
			await SubscribeMarketData(mdMsg, RequestMarketDataUpdate.Types.UpdateBits.Bbo | RequestMarketDataUpdate.Types.UpdateBits.LastTrade, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			if (_mdSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var orig))
				await UnsubscribeMarketData(orig, RequestMarketDataUpdate.Types.UpdateBits.Bbo | RequestMarketDataUpdate.Types.UpdateBits.LastTrade, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_mdSubscriptions[mdMsg.TransactionId] = mdMsg.TypedClone();
			await SubscribeMarketData(mdMsg, RequestMarketDataUpdate.Types.UpdateBits.OrderBook, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			if (_mdSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var orig))
				await UnsubscribeMarketData(orig, RequestMarketDataUpdate.Types.UpdateBits.OrderBook, cancellationToken);
		}
	}

	private async Task SubscribeMarketData(MarketDataMessage mdMsg, RequestMarketDataUpdate.Types.UpdateBits bits, CancellationToken cancellationToken)
	{
		var rq = new RequestMarketDataUpdate
		{
			TemplateId = TemplateId.RequestMarketDataUpdate,
			Symbol = mdMsg.SecurityId.SecurityCode,
			Exchange = mdMsg.SecurityId.BoardCode,
			Request = RequestMarketDataUpdate.Types.Request.Subscribe,
			UpdateBits = (uint)bits,
		};
		rq.UserMsg.Add(mdMsg.TransactionId.To<string>());

		await _tickerClient.SendAsync(rq, cancellationToken);
	}

	private async Task UnsubscribeMarketData(MarketDataMessage mdMsg, RequestMarketDataUpdate.Types.UpdateBits bits, CancellationToken cancellationToken)
	{
		var rq = new RequestMarketDataUpdate
		{
			TemplateId = TemplateId.RequestMarketDataUpdate,
			Symbol = mdMsg.SecurityId.SecurityCode,
			Exchange = mdMsg.SecurityId.BoardCode,
			Request = RequestMarketDataUpdate.Types.Request.Unsubscribe,
			UpdateBits = (uint)bits,
		};

		await _tickerClient.SendAsync(rq, cancellationToken);
	}

	private async void ProcessLastTrade(byte[] data)
	{
		var msg = LastTrade.Parser.ParseFrom(data);

		if (!msg.HasSymbol)
			return;

		var secId = new SecurityId { SecurityCode = msg.Symbol, BoardCode = msg.HasExchange ? msg.Exchange : string.Empty };
		var time = msg.HasSsboe ? msg.Ssboe.ToDateTime(msg.HasUsecs ? msg.Usecs : 0) : CurrentTime;

		// find matching subscription
		foreach (var (transId, sub) in _mdSubscriptions.CopyAndClear().Select(p => (p.Key, p.Value)))
		{
			_mdSubscriptions[transId] = sub; // put back

			if (sub.SecurityId != secId)
				continue;

			if (sub.DataType2 == DataType.Ticks)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = transId,
					ServerTime = time,
					TradePrice = msg.HasTradePrice ? (decimal)msg.TradePrice : null,
					TradeVolume = msg.HasTradeSize ? msg.TradeSize : null,
					OriginSide = msg.HasAggressor ? msg.Aggressor.ToSide() : null,
				}, CancellationToken.None);
			}
			else if (sub.DataType2 == DataType.Level1)
			{
				var l1 = new Level1ChangeMessage
				{
					OriginalTransactionId = transId,
					ServerTime = time,
				};

				if (msg.HasTradePrice) l1.TryAdd(Level1Fields.LastTradePrice, (decimal)msg.TradePrice);
				if (msg.HasTradeSize) l1.TryAdd(Level1Fields.LastTradeVolume, (decimal)msg.TradeSize);
				if (msg.HasVolume) l1.TryAdd(Level1Fields.Volume, (decimal)msg.Volume);

				await SendOutMessageAsync(l1, CancellationToken.None);
			}
		}
	}

	private async void ProcessBestBidOffer(byte[] data)
	{
		var msg = BestBidOffer.Parser.ParseFrom(data);

		if (!msg.HasSymbol)
			return;

		var secId = new SecurityId { SecurityCode = msg.Symbol, BoardCode = msg.HasExchange ? msg.Exchange : string.Empty };
		var time = msg.HasSsboe ? msg.Ssboe.ToDateTime(msg.HasUsecs ? msg.Usecs : 0) : CurrentTime;

		foreach (var (transId, sub) in _mdSubscriptions.CopyAndClear().Select(p => (p.Key, p.Value)))
		{
			_mdSubscriptions[transId] = sub;

			if (sub.SecurityId != secId || sub.DataType2 != DataType.Level1)
				continue;

			var l1 = new Level1ChangeMessage
			{
				OriginalTransactionId = transId,
				ServerTime = time,
			};

			if (msg.HasBidPrice) l1.TryAdd(Level1Fields.BestBidPrice, (decimal)msg.BidPrice);
			if (msg.HasBidSize) l1.TryAdd(Level1Fields.BestBidVolume, (decimal)msg.BidSize);
			if (msg.HasAskPrice) l1.TryAdd(Level1Fields.BestAskPrice, (decimal)msg.AskPrice);
			if (msg.HasAskSize) l1.TryAdd(Level1Fields.BestAskVolume, (decimal)msg.AskSize);

			await SendOutMessageAsync(l1, CancellationToken.None);
		}
	}

	private async void ProcessOrderBook(byte[] data)
	{
		var msg = Rti.OrderBook.Parser.ParseFrom(data);

		if (!msg.HasSymbol)
			return;

		var secId = new SecurityId { SecurityCode = msg.Symbol, BoardCode = msg.HasExchange ? msg.Exchange : string.Empty };
		var time = msg.HasSsboe ? msg.Ssboe.ToDateTime(msg.HasUsecs ? msg.Usecs : 0) : CurrentTime;

		foreach (var (transId, sub) in _mdSubscriptions.CopyAndClear().Select(p => (p.Key, p.Value)))
		{
			_mdSubscriptions[transId] = sub;

			if (sub.SecurityId != secId || sub.DataType2 != DataType.MarketDepth)
				continue;

			var bids = new QuoteChange[msg.BidPrice.Count];
			for (var i = 0; i < bids.Length; i++)
				bids[i] = new QuoteChange((decimal)msg.BidPrice[i], i < msg.BidSize.Count ? msg.BidSize[i] : 0);

			var asks = new QuoteChange[msg.AskPrice.Count];
			for (var i = 0; i < asks.Length; i++)
				asks[i] = new QuoteChange((decimal)msg.AskPrice[i], i < msg.AskSize.Count ? msg.AskSize[i] : 0);

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = transId,
				ServerTime = time,
				Bids = bids,
				Asks = asks,
			}, CancellationToken.None);
		}
	}

	private async void ProcessTimeBarReplay(byte[] data)
	{
		var msg = ResponseTimeBarReplay.Parser.ParseFrom(data);

		long transId = 0;
		if (msg.UserMsg.Count > 0)
			long.TryParse(msg.UserMsg[0], out transId);

		if (transId == 0) return;

		// data message
		if (msg.RqHandlerRpCode.Count > 0 && msg.RqHandlerRpCode[0] == "0")
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = transId,
				OpenTime = msg.HasMarker ? ((long)msg.Marker).FromUnix() : CurrentTime,
				OpenPrice = msg.HasOpenPrice ? (decimal)msg.OpenPrice : 0,
				HighPrice = msg.HasHighPrice ? (decimal)msg.HighPrice : 0,
				LowPrice = msg.HasLowPrice ? (decimal)msg.LowPrice : 0,
				ClosePrice = msg.HasClosePrice ? (decimal)msg.ClosePrice : 0,
				TotalVolume = msg.HasVolume ? (decimal)msg.Volume : 0,
				State = CandleStates.Finished,
			}, CancellationToken.None);
		}

		// end message
		if (msg.RpCode.Count > 0)
		{
			await SendSubscriptionFinishedAsync(transId, CancellationToken.None);
		}
	}
}

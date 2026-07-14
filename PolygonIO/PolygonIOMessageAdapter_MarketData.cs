namespace StockSharp.PolygonIO;

using System.IO.Compression;

public partial class PolygonIOMessageAdapter
{
	private RestClient _client;

	private readonly SynchronizedPairSet<(DataType dt, string symbol), long> _mdTransIds = [];

	private static readonly DataType _tf1Day = TimeSpan.FromDays(1).TimeFrame().Immutable();
	private static readonly DataType _tf1Min = TimeSpan.FromMinutes(1).TimeFrame().Immutable();
	private static readonly DataType _tf1Sec = TimeSpan.FromSeconds(1).TimeFrame().Immutable();

	private static readonly HashSet<DataType> _flatFilesTypes = new(
	[
		DataType.Ticks,
		DataType.Level1,
		_tf1Min,
		_tf1Day,
	]);

	private const long _maxTickers = 1000;
	private const long _maxNewsDiv = 1000;
	private const long _maxBar = 50000;
	private const string _ascOrd = "asc";

	private async Task ProcessFlatFilesAllAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var dt = mdMsg.DataType2;

		if (!_flatFilesTypes.Contains(mdMsg.DataType2))
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		if (mdMsg.From is not DateTime from)
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var to = (mdMsg.To ?? DateTime.UtcNow).Date;

		// Instantiate downloader
		var accessKey = Key?.UnSecure();
		var secretKey = Secret?.UnSecure();

		if (accessKey.IsEmpty() || secretKey.IsEmpty())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

		using var downloader = new FlatFilesDownloader(accessKey, secretKey, $"https://files.{Address}", FlatFilesRepo);

		var left = mdMsg.Count ?? long.MaxValue;

		foreach (var section in FlatFilesSections)
		{
			if (left <= 0)
				break;

			for (var day = from.Date; day <= to; day = day.AddDays(1))
			{
				if (left <= 0)
					break;

				await downloader.DownloadDayAsync(section, dt, day, async (key, stream, ct) =>
				{
					GZipStream gzip = null;

					// decompress if needed
					if (Path.GetExtension(key).EndsWithIgnoreCase(".gz"))
						stream = gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);

					try
					{
						IAsyncEnumerable<Message> messages;

						if (dt == DataType.Level1)
						{
							messages = section switch
							{
								SecurityTypes.Stock => stream.ParseStocksQuotes(ct),
								SecurityTypes.Currency => stream.ParseForexQuotes(ct),
								SecurityTypes.CryptoCurrency => stream.ParseCryptoQuotes(ct),
								_ => AsyncEnumerable.Empty<Message>(),
							};
						}
						else if (dt.IsTFCandles)
						{
							messages = section switch
							{
								SecurityTypes.Stock => stream.ParseStocksMinuteAggregates(ct),
								SecurityTypes.Index => stream.ParseIndicesMinuteAggregates(ct),
								SecurityTypes.Currency => stream.ParseForexMinuteAggregates(ct),
								SecurityTypes.CryptoCurrency => stream.ParseCryptoMinuteAggregates(ct),
								_ => AsyncEnumerable.Empty<Message>(),
							};
						}
						else // ticks
						{
							messages = section switch
							{
								SecurityTypes.Stock => stream.ParseStocksTrades(ct),
								SecurityTypes.Index => stream.ParseIndicesValues(ct),
								SecurityTypes.CryptoCurrency => stream.ParseCryptoTrades(ct),
								_ => AsyncEnumerable.Empty<Message>(),
							};
						}

						await foreach (var msg in messages.WithEnforcedCancellation(ct))
						{
							if (left <= 0)
								break;

							if (!mdMsg.SecurityId.IsAllSecurity() && msg.TryGetSecurityId() != mdMsg.SecurityId)
								continue;

							// attach subscription id
							switch (msg)
							{
								case TimeFrameCandleMessage c:
									c.OriginalTransactionId = mdMsg.TransactionId;
									c.DataType = dt;
									await SendOutMessageAsync(c, ct);
									break;
								case ExecutionMessage e:
									e.OriginalTransactionId = mdMsg.TransactionId;
									await SendOutMessageAsync(e, ct);
									break;
								case Level1ChangeMessage l1:
									l1.OriginalTransactionId = mdMsg.TransactionId;
									await SendOutMessageAsync(l1, ct);
									break;
								default:
									await SendOutMessageAsync(msg, ct);
									break;
							}

							if (--left <= 0)
								break;
						}
					}
					finally
					{
						gzip?.Dispose();
					}
				}, cancellationToken);
			}
		}

		if (mdMsg.Count is not null)
			mdMsg.Count = left;

		if (mdMsg.To is not null || left <= 0)
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = new HashSet<SecurityTypes?>();
		var secTypesOrigin = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var type in secTypesOrigin)
			secTypes.Add(type);

		if (secTypes.IsEmpty())
			secTypes.Add(null);

		var secIds = new HashSet<SecurityId>();

		foreach (var secType in secTypes)
		{
			await foreach (var ticker in _client.GetTickers(IterationInterval, secType?.ToNative(), lookupMsg.SecurityId.SecurityCode, true, left.Min(_maxTickers), Token.UnSecure(), cancellationToken).WithEnforcedCancellation(cancellationToken))
			{
				var secId = new SecurityId
				{
					SecurityCode = ticker.Code,
					BoardCode = ticker.PrimaryExchange.IsEmpty(BoardCodes.StockSharp),
				};

				if (!secIds.Add(secId))
					continue;

				var secMsg = new SecurityMessage
				{
					SecurityId = secId,
					Name = ticker.Name,
					SecurityType = ticker.Type.IsEmpty(ticker.Market)?.ToSecurityType(),
					OriginalTransactionId = lookupMsg.TransactionId,
				};

				if (ticker.CurrencyName.TryParse(out CurrencyTypes currency))
					secMsg.Currency = currency;

				if (!secMsg.IsMatch(lookupMsg, secTypesOrigin))
					continue;

				await SendOutMessageAsync(secMsg, cancellationToken);

				if (--left <= 0)
					break;
			}

			if (left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
	}

	private bool IsFlatFiles(MarketDataMessage mdMsg)
	{
		return
			FlatFilesSections.Any() &&
			_flatFilesTypes.Contains(mdMsg.DataType2) &&
			mdMsg.From is not null;
	}

	private SocketClient SafeSocket()
		=> _socket ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			if (_mdTransIds.TryGetKey(mdMsg.OriginalTransactionId, out var t))
			{
				if (t.dt == _tf1Min)
					await SafeSocket().UnSubscribeBarsMin(t.symbol, cancellationToken);
				else
					await SafeSocket().UnSubscribeBarsSec(t.symbol, cancellationToken);

				_mdTransIds.RemoveByValue(mdMsg.OriginalTransactionId);
			}

			return;
		}

		if (IsFlatFiles(mdMsg))
			await ProcessFlatFilesAllAsync(mdMsg, cancellationToken);
		else if (mdMsg.From is not null)
		{
			var from = mdMsg.From.Value;
			var to = mdMsg.To ?? DateTime.UtcNow;

			var left = mdMsg.Count ?? long.MaxValue;

			await foreach (var candle in _client.GetBars(IterationInterval, mdMsg.SecurityId.SecurityCode, mdMsg.GetTimeFrame().ToNative(out var multiplier), multiplier, from, to, true, _ascOrd, left.Min(_maxBar), Token.UnSecure(), cancellationToken).WithEnforcedCancellation(cancellationToken))
			{
				if (candle.Time < from)
					continue;

				if (candle.Time > to)
					break;

				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					DataType = mdMsg.DataType2,

					OpenTime = candle.Time,

					OpenPrice = candle.Open.ToDecimal() ?? default,
					HighPrice = candle.High.ToDecimal() ?? default,
					LowPrice = candle.Low.ToDecimal() ?? default,
					ClosePrice = candle.Close.ToDecimal() ?? default,
					TotalVolume = candle.Volume.ToDecimal() ?? default,

					State = CandleStates.Finished,

					TotalTicks = candle.TickCount,
				}, cancellationToken);

				if (--left <= 0)
					break;
			}
		}

		if (!mdMsg.IsHistoryOnly())
		{
			if (mdMsg.DataType2 == _tf1Min)
				await SafeSocket().SubscribeBarsMin(AddTransId(mdMsg), cancellationToken);
			else if (mdMsg.DataType2 == _tf1Sec)
				await SafeSocket().SubscribeBarsSec(AddTransId(mdMsg), cancellationToken);
			else
				await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			if (_mdTransIds.TryGetKey(mdMsg.OriginalTransactionId, out var t))
			{
				await SafeSocket().UnSubscribeTrades(t.symbol, cancellationToken);
				_mdTransIds.RemoveByValue(mdMsg.OriginalTransactionId);
			}

			return;
		}

		if (IsFlatFiles(mdMsg))
			await ProcessFlatFilesAllAsync(mdMsg, cancellationToken);
		else if (mdMsg.From is not null)
		{
			var from = mdMsg.From.Value;
			var to = mdMsg.To ?? DateTime.UtcNow;

			var left = mdMsg.Count ?? long.MaxValue;

			while (true)
			{
				var noData = true;

				await foreach (var trade in _client.GetTrades(IterationInterval, mdMsg.SecurityId.SecurityCode, from, _ascOrd, left.Min(_maxBar), Token.UnSecure(), cancellationToken).WithEnforcedCancellation(cancellationToken))
				{
					var time = trade.ParticipantTimestamp ?? trade.SipTimestamp;

					if (time < from)
						continue;

					if (time > to)
					{
						noData = true;
						break;
					}

					await SendOutMessageAsync(new ExecutionMessage
					{
						OriginalTransactionId = mdMsg.TransactionId,
						DataTypeEx = mdMsg.DataType2,

						ServerTime = time,
						TradeStringId = trade.Id,
						TradePrice = (decimal)trade.Price,
						TradeVolume = (decimal)trade.Size,

						SeqNum = trade.SequenceNumber,
					}, cancellationToken);

					if (--left <= 0)
						break;

					from = time;
					noData = false;
				}

				if (left <= 0 || noData)
					break;
			}
		}

		if (!mdMsg.IsHistoryOnly())
			await SafeSocket().SubscribeTrades(AddTransId(mdMsg), cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var left = mdMsg.Count ?? 10;

		await foreach (var news in _client.GetNews(IterationInterval, mdMsg.SecurityId.SecurityCode, mdMsg.From, _ascOrd, left.Min(_maxNewsDiv), Token.UnSecure(), cancellationToken).WithEnforcedCancellation(cancellationToken))
		{
			if (news.PublishedUtc > mdMsg.To)
				break;

			await SendOutMessageAsync(new NewsMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				ServerTime = news.PublishedUtc,

				Headline = news.Title,
				Story = news.Description,
				Source = news.Author,
				Id = news.Id,
				Url = news.ArticleUrl,
				SecurityId = mdMsg.SecurityId,
			}, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			if (_mdTransIds.TryGetKey(mdMsg.OriginalTransactionId, out var t))
			{
				await SafeSocket().UnSubscribeQuotes(t.symbol, cancellationToken);
				_mdTransIds.RemoveByValue(mdMsg.OriginalTransactionId);
			}

			return;
		}

		var left = mdMsg.Count ?? long.MaxValue;

		if (mdMsg.Fields?.Contains(Level1Fields.Dividend) == true)
		{
			await foreach (var div in _client.GetDividends(IterationInterval, mdMsg.SecurityId.SecurityCode, mdMsg.From, _ascOrd, left.Min(_maxNewsDiv), Token.UnSecure(), cancellationToken).WithEnforcedCancellation(cancellationToken))
			{
				if (div.PayDate.IsEmpty())
					continue;

				var date = div.PayDate.ToDateTime("yyyy-MM-dd").UtcKind();

				if (date > mdMsg.To)
					break;

				await SendOutMessageAsync(new Level1ChangeMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					ServerTime = date,
				}.TryAdd(Level1Fields.Dividend, div.CashAmount.ToDecimal()), cancellationToken);

				if (--left <= 0)
					break;
			}

			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		}
		else
		{
			if (IsFlatFiles(mdMsg))
				await ProcessFlatFilesAllAsync(mdMsg, cancellationToken);
			else if (mdMsg.From is not null)
			{
				var from = mdMsg.From.Value;
				var to = mdMsg.To ?? DateTime.UtcNow;

				while (true)
				{
					var noData = true;

					await foreach (var quote in _client.GetQuotes(IterationInterval, mdMsg.SecurityId.SecurityCode, from, _ascOrd, left.Min(_maxBar), Token.UnSecure(), cancellationToken).WithEnforcedCancellation(cancellationToken))
					{
						var time = quote.ParticipantTimestamp ?? quote.SipTimestamp;

						if (time > to)
						{
							noData = true;
							break;
						}

						await SendOutMessageAsync(new Level1ChangeMessage
						{
							OriginalTransactionId = mdMsg.TransactionId,
							ServerTime = time,
							SeqNum = quote.SequenceNumber,
						}
						.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice.ToDecimal())
						.TryAdd(Level1Fields.BestBidVolume, quote.BidSize?.ToDecimal())
						.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice.ToDecimal())
						.TryAdd(Level1Fields.BestAskVolume, quote.AskSize?.ToDecimal())
						, cancellationToken);

						if (--left <= 0)
							break;

						from = time;
						noData = false;
					}

					if (noData || left <= 0)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
				await SafeSocket().SubscribeQuotes(AddTransId(mdMsg), cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
	}

	private const string _allSymbol = "*";

	private string AddTransId(MarketDataMessage mdMsg)
	{
		var symbol = mdMsg.IsAllSecurity() ? _allSymbol : mdMsg.SecurityId.SecurityCode;
		_mdTransIds.Add((mdMsg.DataType2, symbol.ToUpperInvariant()), mdMsg.TransactionId);
		return symbol;
	}

	private bool TryGetTransId(DataType dt, SocketBase evt, out long transId)
	{
		if (_mdTransIds.TryGetValue((dt, evt.Symbol.ToUpperInvariant()), out transId))
			return true;

		if (_mdTransIds.TryGetValue((dt, _allSymbol), out transId))
			return true;

		return false;
	}

	private ValueTask OnSocketQuoteReceived(SocketQuote quote, CancellationToken cancellationToken)
	{
		if (!TryGetTransId(DataType.Level1, quote, out var transId))
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transId,
			ServerTime = quote.Timestamp,
			SeqNum = quote.SeqNum,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, quote.BidSize.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, quote.AskSize.ToDecimal())
		, cancellationToken);
	}

	private ValueTask OnSocketBarReceived(SocketBar bar, CancellationToken cancellationToken)
	{
		if (!TryGetTransId(bar.EventType.EqualsIgnoreCase("A") ? _tf1Sec : _tf1Min, bar, out var transId))
			return default;

		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transId,

			OpenTime = bar.OpenTime,
			CloseTime = bar.CloseTime ?? default,

			OpenPrice = (decimal)bar.Open,
			HighPrice = (decimal)bar.High,
			LowPrice = (decimal)bar.Low,
			ClosePrice = (decimal)bar.Close,
			TotalVolume = (decimal)bar.TradeSizeAccum,

			State = CandleStates.Finished,
		}, cancellationToken);
	}

	private ValueTask OnSocketTradeReceived(SocketTrade trade, CancellationToken cancellationToken)
	{
		if (!TryGetTransId(DataType.Ticks, trade, out var transId))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			OriginalTransactionId = transId,
			DataTypeEx = DataType.Ticks,

			ServerTime = trade.Timestamp,
			TradeStringId = trade.Id,
			TradePrice = (decimal)trade.Price,
			TradeVolume = (decimal)trade.Size,

			SeqNum = trade.SeqNum,
		}, cancellationToken);
	}
}

namespace StockSharp.IQFeed;

using System.Net.Http;

using Ecng.ComponentModel;

public partial class IQFeedMessageAdapter : MessageAdapter
{
	private class MarketsInfo
	{
		private readonly SynchronizedDictionary<int, IQFeedListedMarketMessage> _markets = [];
		private readonly SynchronizedDictionary<int, IQFeedListedMarketMessage> _roots = [];

		public void Reset()
		{
			_markets.Clear();
			_roots.Clear();
		}

		private IQFeedListedMarketMessage TryFindRoot(IQFeedListedMarketMessage m)
		{
			if (m.Id == m.ParentId || m.Id == 0)
				return m;

			return _markets.TryGetValue(m.ParentId, out var parent) ? TryFindRoot(parent) : m;
		}

		public IQFeedListedMarketMessage GetRoot(int marketId) => _roots.TryGetValue(marketId) ?? _markets.TryGetValue(marketId);

		public void AddListedMarket(IQFeedListedMarketMessage lmMsg)
		{
			if (_markets.ContainsKey(lmMsg.Id))
				return;

			_markets[lmMsg.Id] = lmMsg;

			_roots.Clear();
			foreach (var kv in _markets)
				_roots[kv.Key] = TryFindRoot(kv.Value);
		}
	}

	private static readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
	});

	private readonly SynchronizedPairSet<int, SecurityTypes?> _securityTypes = [];

	private IQFeedAdmin _adminFeed;
	private IQFeedLevel2 _level2Feed;
	private IQFeedLevel1 _level1Feed;
	private IQFeedLookup _lookupFeed;
	private IQFeedDerivatives _derivativeFeed;
	private int _disconnectSignaled;

	private IEnumerable<IQFeed> Feeds => new IQFeed[]{ _adminFeed, _level1Feed, _level2Feed, _lookupFeed, _derivativeFeed }.WhereNotNull();

	private readonly MarketsInfo _marketsInfo = new();

	private void DisposeFeeds()
	{
		Feeds.ForEach(feed => feed.Dispose());
		_adminFeed = null;
		_level1Feed = null;
		_level2Feed = null;
		_lookupFeed = null;
		_derivativeFeed = null;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="IQFeedMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public IQFeedMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Level1ColumnRegistry = IQFeedLevel1ColumnRegistry.Instance;

		_level1Columns =
            [
                Level1ColumnRegistry.OpenInterest,
			Level1ColumnRegistry.Open,
			Level1ColumnRegistry.High,
			Level1ColumnRegistry.Low,
			Level1ColumnRegistry.Close,
			Level1ColumnRegistry.BidPrice,
			Level1ColumnRegistry.BidTime,
			Level1ColumnRegistry.BidVolume,
			Level1ColumnRegistry.AskPrice,
			Level1ColumnRegistry.AskTime,
			Level1ColumnRegistry.AskVolume,
			Level1ColumnRegistry.LastTradeId,
			Level1ColumnRegistry.LastDate,
			Level1ColumnRegistry.LastTradeTime,
			Level1ColumnRegistry.LastTradePrice,
			Level1ColumnRegistry.LastTradeVolume,
			Level1ColumnRegistry.TotalVolume,
			Level1ColumnRegistry.TradeCount,
			Level1ColumnRegistry.VWAP,
			Level1ColumnRegistry.DecimalPrecision,
			Level1ColumnRegistry.MarketOpen,
			Level1ColumnRegistry.MessageContents
		];

		this.AddMarketDataSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.OrderLog);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
		this.AddSupportedMarketDataType(DataType.CandleTick);
		this.AddSupportedMarketDataType(DataType.CandleVolume);
		this.AddSupportedMarketDataType(DataType.News);
	}

	/// <inheritdoc />
	public override IOrderLogMarketDepthBuilder CreateOrderLogMarketDepthBuilder(SecurityId securityId)
		=> new IQFeedOrderLogMarketDepthBuilder(securityId);

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ? IsDownloadSecurityFromSite : base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool ExtraSetup => true;

	private T SubscribeFeed<T>(T feed) where T:IQFeed
	{
		feed.NewMessage += (m, ct) => ProcessIQFeedMessageAsync(feed, m, ct);
		return feed;
	}

	private ValueTask NotifyConnectionLostAsync(Exception error)
		=> Interlocked.Exchange(ref _disconnectSignaled, 1) == 0
			? SendOutDisconnectMessageAsync(error, CancellationToken.None)
			: default;

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		try
		{
			await Task.WhenAll(Feeds.Select(feed => feed.DisconnectAsync()));
		}
		finally
		{
			DisposeFeeds();
		}
		_marketsInfo.Reset();
		_securityTypes.Clear();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		try
		{
			await Task.WhenAll(Feeds.Select(feed => feed.DisconnectAsync()));
		}
		finally
		{
			DisposeFeeds();
		}
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken token)
	{
		Interlocked.Exchange(ref _disconnectSignaled, 0);
		if (IsOffline)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Connected, token);
			return;
		}

		const int attempts = 3;

		for (var i = 0; i < attempts; ++i)
		{
			var isError = false;

			try
			{
				await ProcessConnectAttemptAsync(token).NoWait();
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception e)
			{
				isError = true;
				this.AddLog(i < attempts - 1 ? LogLevels.Warning : LogLevels.Error, () => $"Failed to connect to IQClient: {e}");

				if (i == attempts - 1)
					throw;
			}

			if(isError)
				continue;

			try
			{
				if(_adminFeed == null)
					throw new InvalidOperationException("admin feed is disconnected");
				await _adminFeed.VerifyConnection(token).NoWait();
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception e)
			{
				isError = true;
				this.AddLog(i < attempts - 1 ? LogLevels.Warning : LogLevels.Error, () => $"Failed to verify connection: {e}");

				if (i == attempts - 1)
					throw;
			}

			if (isError)
			{
				try
				{
					await Task.WhenAll(Feeds.Select(feed => feed.DisconnectAsync()));
				}
				finally
				{
					DisposeFeeds();
				}

				var reconnectDelay = TimeSpan.FromSeconds(2);
				this.AddWarningLog($"will try to reconnect in {reconnectDelay:g}");
				await reconnectDelay.Delay(token);
				continue;
			}

			await SendOutMessageAsync(new ConnectMessage(), token);
			break;
		}
	}

	private async Task ProcessConnectAttemptAsync(CancellationToken token)
	{
		try
		{
			if(ProductId?.Trim().IsEmptyOrWhiteSpace() == true || Login.IsEmptyOrWhiteSpace() || Password.IsEmpty())
				throw new InvalidOperationException("product id or username or password is empty");

			async Task runConnection(IQFeed feed, Task connectionTask)
			{
				try
				{
					await connectionTask.NoWait();
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested || feed.IsDisconnecting)
				{
					feed.AddDebugLog("feed task is canceled");
				}
				catch (Exception ex)
				{
					this.AddErrorLog(ex);
					await NotifyConnectionLostAsync(ex);
				}
			}

			_adminFeed      = SubscribeFeed(new IQFeedAdmin(this));
			_lookupFeed     = SubscribeFeed(new IQFeedLookup(this));
			_level1Feed     = SubscribeFeed(new IQFeedLevel1(this));
			_level2Feed     = SubscribeFeed(new IQFeedLevel2(this));
			_derivativeFeed = SubscribeFeed(new IQFeedDerivatives(this));

			var allFeeds = Feeds.ToArray();

			var feedConnections = allFeeds.Select(f => (feed: f, connectTask: f.ConnectAsync(token))).ToArray();

			// connect to feeds and initialize. connectTask represent connection initialization. it finishes when connection established or if connect attempt has failed/canceled
			await Task.WhenAll(feedConnections.Select(f => f.connectTask));
			// run connection tasks. connectTask.Result represent
			var connectionTasks = feedConnections.Where(t => t.connectTask.Result != null).Select(t => runConnection(t.feed, t.connectTask.Result));

			await Task.WhenAll(connectionTasks);
		}
		catch (Exception)
		{
			DisposeFeeds();
			throw;
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeFeeds();
		base.DisposeManaged();
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, token);

		if (!mdMsg.IsSubscribe)
			return;
		if (IsHistorical(mdMsg))
			throw new NotSupportedException("IQFeed does not provide historical Level 1 quotes.");

		await SendSubscriptionResultAsync(mdMsg, token);
		await _level1Feed.SubscribeSymbol(mdMsg, token);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, token);

		if (!mdMsg.IsSubscribe)
			return;

		var isHistorical = IsHistorical(mdMsg);
		await SendSubscriptionResultAsync(mdMsg, token);
		if (isHistorical)
		{
			await _lookupFeed.RequestTicks(mdMsg, token);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, token);
		}
		else
			await _level1Feed.SubscribeSymbol(mdMsg, token);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, token);

		if (!mdMsg.IsSubscribe)
			return;
		if (IsHistorical(mdMsg))
			throw new NotSupportedException("IQFeed does not provide historical Level 2 depth.");

		await SendSubscriptionResultAsync(mdMsg, token);
		await _level2Feed.SubscribeMbp(mdMsg, token);
	}

	/// <inheritdoc />
	protected override async ValueTask OnOrderLogSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, token);

		if (!mdMsg.IsSubscribe)
			return;
		if (IsHistorical(mdMsg))
			throw new NotSupportedException("IQFeed does not provide historical market-by-order data.");

		await SendSubscriptionResultAsync(mdMsg, token);
		await _level2Feed.SubscribeMbo(mdMsg, token);
	}

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, token);

		if (!mdMsg.IsSubscribe)
			return;

		var isFinite = !mdMsg.NewsId.IsEmpty() || mdMsg.From != default;
		await SendSubscriptionResultAsync(mdMsg, token);
		if (mdMsg.NewsId.IsEmpty())
		{
			if (mdMsg.From == default)
				await _level1Feed.SubscribeNews(mdMsg, token);
			else
				await _lookupFeed.RequestNewsHeadlines(mdMsg, token);
		}
		else
			await _lookupFeed.RequestNewsStory(mdMsg, token);

		if (isFinite)
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, token);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, token);

		if (!mdMsg.IsSubscribe)
			return;

		var tf = mdMsg.GetTimeFrame();

		IAsyncEnumerable<CandleMessage> candles;
		var isStreaming = mdMsg.To == null && mdMsg.Count == null;

		// streaming
		if (isStreaming)
		{
			candles = _derivativeFeed.SubscribeCandles(mdMsg, token);
		}
		else
		{
			if (tf.Ticks == TimeHelper.TicksPerMonth)
				candles = _lookupFeed.RequestMonthlyCandles(mdMsg, token);
			else if (tf == TimeSpan.FromDays(7))
				candles = _lookupFeed.RequestWeeklyCandles(mdMsg, token);
			else if (tf == TimeSpan.FromDays(1))
				candles = _lookupFeed.RequestDailyCandles(mdMsg, token);
			else if (tf < TimeSpan.FromDays(1))
				candles = _lookupFeed.RequestIntradayCandles(mdMsg, token);
			else
				throw new InvalidOperationException(LocalizedStrings.IntervalNotSupported.Put(tf));
		}

		await SendSubscriptionResultAsync(mdMsg, token);
		await foreach (var candleMsg in candles.WithEnforcedCancellation(token))
		{
			candleMsg.OriginalTransactionId = mdMsg.TransactionId;
			candleMsg.SecurityId = mdMsg.SecurityId;
			candleMsg.DataType = mdMsg.DataType2;

			if (tf == TimeSpan.FromDays(1))
			{
				candleMsg.OpenTime = candleMsg.CloseTime;
				candleMsg.CloseTime = candleMsg.OpenTime.EndOfDay();
			}
			else// if (tf == TimeSpan.FromDays(7) || tf.Ticks == TimeHelper.TicksPerMonth)
			{
				candleMsg.CloseTime -= TimeSpan.FromTicks(1);
				candleMsg.OpenTime = tf.GetCandleBounds(candleMsg.CloseTime).Min;
			}

			await SendOutMessageAsync(candleMsg, token);
		}

		if (!isStreaming)
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, token);
	}

	private static bool IsHistorical(MarketDataMessage message)
		=> message.From != null || message.To != null || message.Count != null || message.IsHistoryOnly();

	private async Task ProcessSecurityLookupAllAsync(SecurityLookupMessage lookupMsg, HashSet<SecurityTypes> securityTypes, CancellationToken token)
	{
		IAsyncEnumerable<SecurityMessage> securities;

		var file = SecuritiesFile;
		if (file.IsEmpty())
		{
			this.AddInfoLog("downloading symbols...");
			securities = IQFeedSecurityParser.ParseFromUrlAsync(
				securityTypes: securityTypes,
				maxCount: lookupMsg.Count is > 0
					? checked(lookupMsg.Count.Value + (lookupMsg.Skip ?? 0))
					: null);
		}
		else
		{
			securities = IQFeedSecurityParser.ParseFromFileAsync(
				file,
				securityTypes: securityTypes,
				maxCount: lookupMsg.Count is > 0
					? checked(lookupMsg.Count.Value + (lookupMsg.Skip ?? 0))
					: null);
		}

		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		var boards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		await foreach (var secMsg in securities.WithEnforcedCancellation(token))
		{
			if (!secMsg.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}

			var boardCode = secMsg.SecurityId.BoardCode;

			if (secMsg.SecurityType == null)
				this.AddWarningLog(LocalizedStrings.UnknownType.Put(boardCode));

			if (!boardCode.IsEmpty() && boards.Add(boardCode))
			{
				await SendOutMessageAsync(new BoardMessage
				{
					Code = boardCode,
					ExchangeCode = boardCode,
					OriginalTransactionId = lookupMsg.TransactionId
				}, token);
			}

			secMsg.OriginalTransactionId = lookupMsg.TransactionId;
			await SendOutMessageAsync(secMsg, token);
			if (--left <= 0)
				break;
		}

		this.AddInfoLog("symbols download complete!");
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, token);
		if (lookupMsg.Count == 0)
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, token);
			return;
		}

		var securityTypes = lookupMsg.GetSecurityTypes();

		if (securityTypes.Count == 0)
			securityTypes.AddRange(Enumerator.GetValues<SecurityTypes>());

		if (lookupMsg.IsLookupAll() && IsDownloadSecurityFromSite)
		{
			await ProcessSecurityLookupAllAsync(lookupMsg, securityTypes, token);
		}
		else
		{
			var skip = lookupMsg.Skip ?? 0;
			var left = lookupMsg.Count ?? long.MaxValue;
			var boards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			await foreach (var msg in _lookupFeed.RequestSecurities(lookupMsg, securityTypes, token).WithEnforcedCancellation(token))
			{
				if (!msg.IsMatch(lookupMsg, securityTypes))
					continue;
				if (skip > 0)
				{
					skip--;
					continue;
				}
				var boardCode = msg.SecurityId.BoardCode;
				if (!boardCode.IsEmpty() && boards.Add(boardCode))
				{
					await SendOutMessageAsync(new BoardMessage
					{
						Code = boardCode,
						ExchangeCode = boardCode,
						OriginalTransactionId = lookupMsg.TransactionId,
					}, token);
				}
				await SendOutMessageAsync(msg, token);
				if (--left <= 0)
					break;
			}
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, token);
	}

	private async ValueTask ProcessIQFeedMessageAsync(IQFeed _, Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case ExtendedMessageTypes.ListedMarket:
			{
				_marketsInfo.AddListedMarket((IQFeedListedMarketMessage)message);
				break;
			}

			case ExtendedMessageTypes.SecurityType:
			{
				var stMsg = (IQFeedSecurityTypeMessage)message;
				var secType = stMsg.Code.ToSecurityType();

				if (secType == null)
					this.AddWarningLog(LocalizedStrings.UnknownType.Put(stMsg.Code));

				_securityTypes[stMsg.Id] = secType;

				break;
			}

			default:
				await SendOutMessageAsync(message, cancellationToken);
				break;
		}
	}

	internal SecurityId CreateSecurityId(string secCode, int marketId) =>
		new()
		{
			SecurityCode = secCode,
			BoardCode = (_marketsInfo.GetRoot(marketId)?.Code).IsEmpty("IQFEED"),
		};

	internal SecurityTypes? FromNativeSecurityType(int nativeId) => _securityTypes.TryGetValue(nativeId);

	internal IEnumerable<int> ToNativeSecurityTypes(HashSet<SecurityTypes> types)
		=> _securityTypes.Where(t => t.Value != null && types.Contains(t.Value.Value)).Select(t => t.Key);
}

namespace StockSharp.Binance;

using System.Threading.Channels;

public partial class BinanceMessageAdapter
{
	private HttpClient _httpClient;
	private readonly PusherClient _pusherClient;
	private readonly SynchronizedDictionary<(BinanceSections section, string symbol), (string key, DateTime keyTime)> _listenKeys = [];
	private DateTime? _lastTimeSync;
	private DateTime? _lastSubscriptionSync;

	private static readonly TimeSpan _listenKeyUpdateInterval = TimeSpan.FromMinutes(30);

	/// <summary>
	/// Initializes a new instance of the <see cref="BinanceMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BinanceMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.OrderLog);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);

		_pusherClient = new(ReConnectionSettings.WorkingTime) { Parent = this };
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Binance, BoardCodes.BinanceCoin, BoardCodes.BinanceFut];

	private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SendOutErrorAsync;
		_pusherClient.TickerChanged += SessionOnTickerChanged;
		_pusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		_pusherClient.NewOrderLog += SessionOnNewOrderLog;
		_pusherClient.NewTrade += SessionOnNewTrade;
		_pusherClient.NewCandle += SessionOnNewCandle;
		_pusherClient.NewExecutionReport += SessionOnNewExecutionReport;
		_pusherClient.AccountUpdated += SessionOnAccountUpdated;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SendOutErrorAsync;
		_pusherClient.TickerChanged -= SessionOnTickerChanged;
		_pusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		_pusherClient.NewOrderLog -= SessionOnNewOrderLog;
		_pusherClient.NewTrade -= SessionOnNewTrade;
		_pusherClient.NewCandle -= SessionOnNewCandle;
		_pusherClient.NewExecutionReport -= SessionOnNewExecutionReport;
		_pusherClient.AccountUpdated -= SessionOnAccountUpdated;
	}

	private IEnumerable<BinanceSections> NonMarginSections => Sections.Where(s => s != BinanceSections.Margin);

	private async Task<string> EnsureListenKey(BinanceSections section, string isolatedSymbol, CancellationToken cancellationToken)
	{
		CheckSectionSymbol(section, isolatedSymbol);

		var key = (section, isolatedSymbol);

		if (_listenKeys.TryGetValue(key, out var lk))
		{
			if ((DateTime.UtcNow - lk.keyTime) > _listenKeyUpdateInterval)
			{
				this.AddDebugLog("refreshing listen key ({0}, {1})", section, isolatedSymbol);
				await _httpClient.PingListenKey(section, lk.key, isolatedSymbol, cancellationToken);
				_listenKeys[key] = (lk.key, DateTime.UtcNow);
			}
		}
		else
		{
			this.AddInfoLog("creating listen key ({0}, {1})", section, isolatedSymbol);
			var listenerKey = await _httpClient.CreateListenKey(section, isolatedSymbol, cancellationToken);
			lk = (listenerKey, DateTime.UtcNow);
			_listenKeys[key] = lk;
		}

		return lk.key;
	}

	private async Task SubscribeAccount(BinanceSections section, string isolatedSymbol, CancellationToken cancellationToken)
	{
		this.AddDebugLog("subscribe account ({0}, {1})", section, isolatedSymbol);

		if(_pusherClient.IsAccountSubscribed(section, isolatedSymbol))
			return;

		var listenKey = await EnsureListenKey(section, isolatedSymbol, cancellationToken);

		await SubscribeAccount(section, isolatedSymbol, listenKey, cancellationToken);
	}

	private async Task SubscribeAccount(BinanceSections section, string isolatedSymbol, string listenKey, CancellationToken cancellationToken)
	{
		const int attempts = 5;

		for (var i = 0; i < attempts; i++)
		{
			try
			{
				if (i > 0)
					await IterationInterval.Delay(cancellationToken);

				await _pusherClient.SubscribeAccount(section, isolatedSymbol, listenKey, cancellationToken);
				this.AddDebugLog("subscribe OK ({0}, {1})", section, isolatedSymbol);

				break;
			}
			catch (Exception e)
			{
				if (cancellationToken.IsCancellationRequested)
					throw;

				this.AddWarningLog("subscribe error ({0}, {1}): {2}", section, isolatedSymbol, e);

				_pusherClient.UnSubscribeAccount(section, isolatedSymbol);

				if (i == attempts - 1)
					throw;
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (this.IsTransactional())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new(Key, Secret, IsDemo) { Parent = this };

		SubscribePusherClient();

		await _httpClient.RefreshTimeDiff(cancellationToken);
		_lastSubscriptionSync = _lastTimeSync = DateTime.UtcNow;

		_snapshotRequests = Channel.CreateUnbounded<DepthInfo>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false
		});

		_ = StartSnapshotsThread(cancellationToken);

		if (this.IsTransactional())
		{
			foreach (var section in NonMarginSections)
			{
				try
				{
					await SubscribeAccount(section, null, cancellationToken);
				}
				catch (Exception ex)
				{
					if (!cancellationToken.IsCancellationRequested)
						this.AddErrorLog(ex);
				}
			}
		}

		await SendOutMessageAsync(new ConnectMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		try
		{
			_pusherClient.DisconnectAll();
		}
		catch
		{
		}

		if (_listenKeys.Count > 0)
		{
			if (!RemoveListenKeyOnDisconnect)
			{
				_listenKeys.Clear();
			}
			else
			{
				foreach (var pair in _listenKeys.CopyAndClear())
				{
					try
					{
						await _httpClient.DeleteListenKey(pair.Key.section, pair.Value.key, pair.Key.symbol, cancellationToken);
					}
					catch (Exception ex)
					{
						if (!cancellationToken.IsCancellationRequested)
							this.AddErrorLog(ex);
					}
				}
			}
		}

		_snapshotRequests?.Writer.TryComplete();

		_httpClient.Dispose();
		_httpClient = null;

		UnsubscribePusherClient();

		await SendOutMessageAsync(new DisconnectMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_httpClient != null)
		{
			try
			{
				_httpClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_httpClient = null;
		}

		try
		{
			UnsubscribePusherClient();
			_pusherClient.DisconnectAll();
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}

		_listenKeys.Clear();

		_orderBooks.Clear();
		_candleTransactions.Clear();

		_snapshotRequests?.Writer.TryComplete();
		_snapshotRequests = null;

		_lastSubscriptionSync = _lastTimeSync = null;

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_listenKeys.Count > 0)
		{
			foreach (var ((section, isolatedSymbol), _) in _listenKeys)
			{
				try
				{
					await EnsureListenKey(section, isolatedSymbol, cancellationToken);
				}
				catch (Exception ex)
				{
					if (!cancellationToken.IsCancellationRequested)
						this.AddErrorLog(ex);

					// This listenKey does not exist
					if (ex.Message.ContainsIgnoreCase("not exist"))
					{
						try
						{
							this.AddInfoLog("creating listen key ({0}, {1})", section, isolatedSymbol);
							var listenKey = await _httpClient.CreateListenKey(section, isolatedSymbol, cancellationToken);
							_listenKeys[(section, isolatedSymbol)] = (listenKey, DateTime.UtcNow);

							await SubscribeAccount(section, isolatedSymbol, listenKey, cancellationToken);
						}
						catch (Exception ex1)
						{
							if (!cancellationToken.IsCancellationRequested)
								this.AddErrorLog(ex1);
						}
					}
				}
			}
		}

		var now = DateTime.UtcNow;
		if ((now - _lastTimeSync) > TimeSpan.FromHours(1))
		{
			_lastTimeSync = now;
			await _httpClient.RefreshTimeDiff(cancellationToken);
		}

		if ((now - _lastSubscriptionSync) > TimeSpan.FromSeconds(5))
		{
			_lastSubscriptionSync = now;
			await _pusherClient.ProcessSubscriptions(cancellationToken);
		}
	}
}
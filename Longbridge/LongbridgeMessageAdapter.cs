namespace StockSharp.Longbridge;

public partial class LongbridgeMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public string Symbol { get; init; }
		public SecurityId SecurityId { get; init; }
		public SubType Type { get; init; }
	}

	private LongbridgeRestClient _restClient;
	private LongbridgeSocketClient _quoteSocket;
	private LongbridgeSocketClient _tradeSocket;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _subscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _executions = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;

	/// <summary>Initializes a new instance of the <see cref="LongbridgeMessageAdapter"/> class.</summary>
	public LongbridgeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(60);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(LongbridgeExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["US", "HK", "SH", "SZ"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		_restClient = new(Key?.UnSecure(), Secret?.UnSecure(), Token?.UnSecure(), ApiUrl) { Parent = this };
		_quoteSocket = CreateSocket(QuoteUrl);
		_tradeSocket = CreateSocket(TradeUrl);
		_quoteSocket.PushReceived += ProcessQuotePacket;
		_quoteSocket.Connected += RestoreQuoteSubscriptions;
		_tradeSocket.PushReceived += ProcessTradePacket;
		_tradeSocket.Connected += SubscribeTradePush;
		try
		{
			await Task.WhenAll(_quoteSocket.Connect(cancellationToken), _tradeSocket.Connect(cancellationToken));
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_restClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await Task.WhenAll(_quoteSocket.Disconnect(), _tradeSocket.Disconnect());
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClients();
		_subscriptions.Clear();
		_orderTransactions.Clear();
		_executions.Clear();
		_orderStatusSubscriptionId = 0;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private LongbridgeSocketClient CreateSocket(string url)
	{
		var socket = new LongbridgeSocketClient(url, async cancellationToken =>
			(await _restClient.GetOtp(cancellationToken))?.Otp) { Parent = this };
		socket.Error += (error, _) => SendOutErrorAsync(error, CancellationToken.None);
		return socket;
	}

	private async ValueTask RestoreQuoteSubscriptions(CancellationToken cancellationToken)
	{
		foreach (var group in _subscriptions.CachedValues.GroupBy(s => s.Symbol))
		{
			var request = new SubscribeRequest { IsFirstPush = true };
			request.Symbol.Add(group.Key);
			request.SubType.Add(group.Select(s => s.Type).Distinct());
			await _quoteSocket.Request((byte)LongbridgeQuoteCommand.Subscribe, request,
				UnsubscribeResponse.Parser, cancellationToken);
		}
	}

	private async ValueTask SubscribeTradePush(CancellationToken cancellationToken)
	{
		var request = new Sub();
		request.Topics.Add("private");
		var response = await _tradeSocket.Request((byte)LongbridgeTradeCommand.CmdSub, request,
			SubResponse.Parser, cancellationToken);
		if (response.Fail.Count > 0)
			throw new InvalidOperationException("Longbridge private trade subscription failed: " +
				string.Join("; ", response.Fail.Select(f => $"{f.Topic}: {f.Reason}")));
	}

	private void DisposeClients()
	{
		if (_quoteSocket != null)
		{
			_quoteSocket.PushReceived -= ProcessQuotePacket;
			_quoteSocket.Connected -= RestoreQuoteSubscriptions;
			_quoteSocket.Dispose();
			_quoteSocket = null;
		}
		if (_tradeSocket != null)
		{
			_tradeSocket.PushReceived -= ProcessTradePacket;
			_tradeSocket.Connected -= SubscribeTradePush;
			_tradeSocket.Dispose();
			_tradeSocket = null;
		}
		_restClient?.Dispose();
		_restClient = null;
	}
}

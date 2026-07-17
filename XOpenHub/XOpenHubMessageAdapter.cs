namespace StockSharp.XOpenHub;

public partial class XOpenHubMessageAdapter
{
	private sealed class CandleSubscription
	{
		public string Symbol { get; init; }
		public TimeSpan TimeFrame { get; init; }
	}

	private XApiCommandClient _command;
	private XApiStreamClient _stream;
	private readonly SynchronizedDictionary<string, XApiSymbol> _symbols =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly SynchronizedDictionary<long, long> _orderTransactions = [];
	private readonly SynchronizedDictionary<long, XApiTrade> _positions = [];
	private readonly SynchronizedSet<long> _reportedTrades = [];
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastPing;

	/// <summary>Initializes a new instance.</summary>
	public XOpenHubMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(5);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames([
			TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
			TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(4),
			TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
		]);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles ||
			dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> subscription.GetTimeFrame() == TimeSpan.FromMinutes(1);

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Fxcm];

	private string PortfolioName => Login.IsEmpty(LocalizedStrings.XOpenHub);

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_command != null || _stream != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		var command = new XApiCommandClient(IsDemo) { Parent = this };
		_command = command;
		try
		{
			await command.Connect(Login, Password?.UnSecure(), "StockSharp", cancellationToken);
			foreach (var symbol in await command.GetAllSymbols(cancellationToken) ?? [])
			{
				if (!symbol.Symbol.IsEmpty())
					_symbols[symbol.Symbol] = symbol;
			}

			var stream = new XApiStreamClient(IsDemo, command.StreamSessionId,
				Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
			_stream = stream;
			stream.TickReceived += ProcessTick;
			stream.CandleReceived += ProcessCandle;
			stream.BalanceReceived += ProcessBalance;
			stream.TradeReceived += ProcessTrade;
			stream.TradeStatusReceived += ProcessTradeStatus;
			stream.Error += SendOutErrorAsync;
			stream.StateChanged += SendOutConnectionStateAsync;
			await stream.Connect(cancellationToken);
			connectMsg.SessionId = command.StreamSessionId;
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_command == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		if (_stream != null)
			await _stream.Disconnect();
		await _command.Disconnect();
		DisposeClients();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClients();
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		if (_command != null && CurrentTime - _lastPing >= TimeSpan.FromSeconds(15))
		{
			try
			{
				await _command.Ping(cancellationToken);
			}
			catch (Exception error)
			{
				await SendOutErrorAsync(error, cancellationToken);
				await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
			}
			_lastPing = CurrentTime;
		}
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private XApiSymbol ResolveSymbol(SecurityId securityId)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		return _symbols.TryGetValue(code, out var symbol)
			? symbol
			: throw new InvalidOperationException($"X Open Hub symbol '{code}' was not found.");
	}

	private static SecurityId ToSecurityId(string symbol)
		=> new() { SecurityCode = symbol, BoardCode = BoardCodes.Fxcm };

	private void DisposeClients()
	{
		if (_stream != null)
		{
			_stream.TickReceived -= ProcessTick;
			_stream.CandleReceived -= ProcessCandle;
			_stream.BalanceReceived -= ProcessBalance;
			_stream.TradeReceived -= ProcessTrade;
			_stream.TradeStatusReceived -= ProcessTradeStatus;
			_stream.Error -= SendOutErrorAsync;
			_stream.StateChanged -= SendOutConnectionStateAsync;
			_stream.Dispose();
			_stream = null;
		}
		_command?.Dispose();
		_command = null;
	}

	private void ClearState()
	{
		_symbols.Clear();
		_level1Subscriptions.Clear();
		_candleSubscriptions.Clear();
		_orderTransactions.Clear();
		_positions.Clear();
		_reportedTrades.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastPing = default;
	}
}

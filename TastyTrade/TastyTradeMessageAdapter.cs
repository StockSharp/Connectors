namespace StockSharp.TastyTrade;

public partial class TastyTradeMessageAdapter
{
	private TastyTradeClient _client;
	private TastyTradeMarketStreamer _marketStreamer;
	private TastyTradeAccountStreamer _accountStreamer;
	private TastyAccount[] _accounts = [];
	private readonly SynchronizedDictionary<long, (SecurityId securityId, string symbol, DxEventTypes type, bool isHistoryOnly)> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, string> _streamerSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _processedFills = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Initializes a new instance of the adapter.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public TastyTradeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>
	/// Supported candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4),
		TimeSpan.FromDays(1), TimeSpan.FromDays(7),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (ClientSecret.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		if (Scopes == TastyTradeScopes.None)
			throw new InvalidOperationException("tastytrade OAuth scopes are not specified.");
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_client = new(IsDemo, Token, ClientSecret, Scopes);
		_accounts = (await _client.GetAccounts(cancellationToken)).Select(a => a.Account).Where(a => a is not null && !a.IsClosed).ToArray();

		if (this.IsMarketData())
		{
			var quoteToken = await _client.GetQuoteToken(cancellationToken);
			var address = quoteToken?.DxLinkUrl.IsEmpty(quoteToken?.WebSocketUrl)
				?? throw new InvalidOperationException("tastytrade did not return a DXLink address.");
			_marketStreamer = new(address, quoteToken.Token, ReConnectionSettings.WorkingTime, ReConnectionSettings.AttemptCount) { Parent = this };
			_marketStreamer.DataReceived += ProcessMarketData;
			_marketStreamer.Error += ProcessStreamError;
			await _marketStreamer.ConnectAsync(cancellationToken);
		}

		if (this.IsTransactional() && _accounts.Length > 0)
		{
			_accountStreamer = new(_client, _client.AccountStreamerUrl(IsDemo), _accounts.Select(a => a.AccountNumber), ReConnectionSettings.WorkingTime, ReConnectionSettings.AttemptCount) { Parent = this };
			_accountStreamer.OrderReceived += ProcessOrder;
			_accountStreamer.PositionReceived += ProcessPosition;
			_accountStreamer.BalanceReceived += ProcessBalance;
			_accountStreamer.Error += ProcessStreamError;
			await _accountStreamer.ConnectAsync(cancellationToken);
		}

		await base.ConnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage message, CancellationToken cancellationToken)
	{
		_marketStreamer?.Disconnect();
		_accountStreamer?.Disconnect();
		return base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
	{
		_marketSubscriptions.Clear();
		_streamerSymbols.Clear();
		_orderTransactions.Clear();
		_processedFills.Clear();
		_accounts = [];
		if (_marketStreamer is not null)
		{
			_marketStreamer.DataReceived -= ProcessMarketData;
			_marketStreamer.Error -= ProcessStreamError;
			_marketStreamer.Dispose();
			_marketStreamer = null;
		}
		if (_accountStreamer is not null)
		{
			_accountStreamer.OrderReceived -= ProcessOrder;
			_accountStreamer.PositionReceived -= ProcessPosition;
			_accountStreamer.BalanceReceived -= ProcessBalance;
			_accountStreamer.Error -= ProcessStreamError;
			_accountStreamer.Dispose();
			_accountStreamer = null;
		}
		_client?.Dispose();
		_client = null;
		await base.ResetAsync(message, cancellationToken);
	}

	private ValueTask ProcessStreamError(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);
}

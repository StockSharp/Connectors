namespace StockSharp.Tradier;

using Ecng.Configuration;

[OrderCondition(typeof(TradierOrderCondition))]
public partial class TradierMessageAdapter
{
	private HttpClient _httpClient;
	private MarketDataClient _mdClient;
	private AccountClient _accClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="TradierMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public TradierMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);

		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_mdClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_accClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var token = Token;

		if (token.IsEmpty())
		{
			var provider = ConfigManager.GetService<IOAuthProvider>();
			var socialId = 10; // https://admin.stocksharp.com/settings/socials/10/

			var authToken = await provider.RequestToken(
				socialId,
				IsDemo,
				cancellationToken);

			token = (authToken ?? throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified)).Value.Secure();
		}

		_httpClient = new(IsDemo, token) { Parent = this };

		var wsPrefix = IsDemo ? "sandbox-" : string.Empty;

		_mdClient = this.IsMarketData() ? new("wss://ws.tradier.com/v1/markets/events", (await _httpClient.CreateMarketStreaming(cancellationToken)).sessionId, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this } : null;
		_accClient = this.IsTransactional() ? new($"wss://{wsPrefix}ws.tradier.com/v1/accounts/events", (await _httpClient.CreateAccountStreaming(cancellationToken)).sessionId, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this } : null;

		if (_mdClient is MarketDataClient mdc)
		{
			mdc.QuoteReceived += SessionOnQuoteReceived;
			mdc.TradeReceived += SessionOnTradeReceived;
			mdc.SummaryReceived += SessionOnSummaryReceived;
			mdc.StateChanged += SendOutConnectionStateAsync;
			mdc.Error += SendOutErrorAsync;
		}

		if (_accClient is AccountClient ac)
		{
			ac.OrderReceived += SessionOnOrderReceived;
			ac.StateChanged += SendOutConnectionStateAsync;
			ac.Error += SendOutErrorAsync;
		}

		await _mdClient.Connect(cancellationToken);
		await _accClient.Connect(cancellationToken);

		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_mdClient?.Disconnect();
		_accClient?.Disconnect();

		return base.DisconnectAsync(disconnectMsg, cancellationToken);
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

		if (_mdClient is MarketDataClient mdc)
		{
			try
			{
				mdc.QuoteReceived -= SessionOnQuoteReceived;
				mdc.TradeReceived -= SessionOnTradeReceived;
				mdc.SummaryReceived -= SessionOnSummaryReceived;
				mdc.StateChanged -= SendOutConnectionStateAsync;
				mdc.Error -= SendOutErrorAsync;

				mdc.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_mdClient = null;
		}

		if (_accClient is AccountClient ac)
		{
			try
			{
				ac.OrderReceived -= SessionOnOrderReceived;
				ac.StateChanged -= SendOutConnectionStateAsync;
				ac.Error -= SendOutErrorAsync;

				ac.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_accClient = null;
		}

		_mdTransIds.Clear();
		_orderBalances.Clear();

		await base.ResetAsync(resetMsg, cancellationToken);
	}
}

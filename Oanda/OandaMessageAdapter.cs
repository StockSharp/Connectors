namespace StockSharp.Oanda;

partial class OandaMessageAdapter
{
	private OandaRestClient _restClient;
	private OandaStreamingClient _streamigClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="OandaMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public OandaMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = DefaultHeartbeatInterval;

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Ond];

	private void StreamingClientDispose()
	{
		_streamigClient.Log -= StreamigClientOnLog;
		_streamigClient.NewError -= SendOutErrorAsync;
		_streamigClient.NewTransaction -= SessionOnNewTransaction;
		_streamigClient.NewPricing -= SessionOnNewPricing;

		_streamigClient.Dispose();
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		_defaultAccount = null;
		//_orderSecurities.Clear();
		_isPositionsStreaming = false;
		_currentPositions.Clear();
		_orderBalance.Clear();
		_pfSubs.Clear();

		if (_streamigClient != null)
		{
			try
			{
				StreamingClientDispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_streamigClient = null;
		}

		_restClient = null;

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_streamigClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_restClient = new OandaRestClient(IsDemo, Token, UseCompression);

		_streamigClient = new OandaStreamingClient(IsDemo, Token, UseCompression);
		_streamigClient.Log += StreamigClientOnLog;
		_streamigClient.NewError += SendOutErrorAsync;
		_streamigClient.NewTransaction += SessionOnNewTransaction;
		_streamigClient.NewPricing += SessionOnNewPricing;

		_defaultAccount = (await _restClient.GetAccountsAsync(cancellationToken)).FirstOrDefault()?.Id;

		if (_defaultAccount == null)
			throw new InvalidOperationException(LocalizedStrings.NoPortfoliosReceived);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_restClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_streamigClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		StreamingClientDispose();
		_streamigClient = null;

		_restClient = null;

		return default;
	}

	private void StreamigClientOnLog(string source, string data)
	{
		if (LogOnlyTransactions && source.EqualsIgnoreCase(OandaStreamingNames.Pricing))
			return;

		this.AddDebugLog($"{source}: {data}");
	}
}

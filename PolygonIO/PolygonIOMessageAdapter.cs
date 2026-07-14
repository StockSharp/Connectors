namespace StockSharp.PolygonIO;

public partial class PolygonIOMessageAdapter
{
	private SocketClient _socket;

	/// <summary>
	/// Initializes a new instance of the <see cref="PolygonIOMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public PolygonIOMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();

		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.News);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);

		IterationInterval = default;
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || (FlatFilesSections.Any() && _flatFilesTypes.Contains(dataType));

	/// <inheritdoc />
	public override bool IsSecurityRequired(DataType dataType)
		=> FlatFilesSections.Any() ? !_flatFilesTypes.Contains(dataType) : base.IsSecurityRequired(dataType);

	private void SubscribeSocket()
	{
		_socket.StateChanged += SendOutConnectionStateAsync;
		_socket.Error += SendOutErrorAsync;
		_socket.TradeReceived += OnSocketTradeReceived;
		_socket.BarReceived += OnSocketBarReceived;
		_socket.QuoteReceived += OnSocketQuoteReceived;
	}

	private void UnSubscribeSocket()
	{
		_socket.StateChanged -= SendOutConnectionStateAsync;
		_socket.Error -= SendOutErrorAsync;
		_socket.TradeReceived -= OnSocketTradeReceived;
		_socket.BarReceived -= OnSocketBarReceived;
		_socket.QuoteReceived -= OnSocketQuoteReceived;
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);

		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_client = new(new HttpClient(), Address);

		if (ConnectionType == PolygonIOConnectionTypes.History)
		{
			await base.ConnectAsync(connectMsg, cancellationToken);
			return;
		}

		_socket = new($"wss://{0}.{Address}/stocks".Put(ConnectionType == PolygonIOConnectionTypes.Delayed ? "delayed" : "socket"), Token, ReConnectionSettings.WorkingTime);
		SubscribeSocket();

		await _socket.Connect(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (ConnectionType == PolygonIOConnectionTypes.History)
			return base.DisconnectAsync(disconnectMsg, cancellationToken);

		SafeSocket().Disconnect();

		return default;
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_socket is not null)
		{
			try
			{
				UnSubscribeSocket();
				_socket.Disconnect();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_socket = null;
		}

		_mdTransIds.Clear();

		await base.ResetAsync(resetMsg, cancellationToken);
	}
}
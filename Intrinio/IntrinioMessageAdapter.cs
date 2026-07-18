namespace StockSharp.Intrinio;

public partial class IntrinioMessageAdapter
{
	internal const string EquityBoard = "INTRINIO";
	internal const string OptionBoard = "INTRINIOOPT";

	private IntrinioRestClient _restClient;
	private IntrinioRealtimeClient _realtimeClient;

	/// <summary>Initializes a new instance.</summary>
	public IntrinioMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.News);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>All candle time frames exposed by the Intrinio REST endpoints.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
		TimeSpan.FromDays(90),
		TimeSpan.FromDays(365),
	];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [EquityBoard, OptionBoard];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Ticks ||
			dataType == DataType.News || dataType.IsTFCandles;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient != null || _realtimeClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (Address == null || !Address.IsAbsoluteUri || Address.Scheme != Uri.UriSchemeHttps)
			throw new InvalidOperationException("Intrinio REST address must be an absolute HTTPS URI.");
		if (EquityThreads <= 0 || OptionThreads <= 0)
			throw new InvalidOperationException("Intrinio decoder thread counts must be positive.");
		if (EquityBufferSize < 2048 || OptionBufferSize < 2048)
			throw new InvalidOperationException("Intrinio SDK buffer sizes must be at least 2048.");

		var apiKey = Token.UnSecure();
		_restClient = new(Address, apiKey) { Parent = this };
		_realtimeClient = new(apiKey, EquityProvider, OptionProvider,
			IsDelayedOptions, EquityThreads, OptionThreads,
			EquityBufferSize, OptionBufferSize) { Parent = this };
		_realtimeClient.EventReceived += OnRealtimeEvent;
		_realtimeClient.Error += OnRealtimeError;

		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient == null && _realtimeClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClients();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClients();
		_optionRefreshes.Clear();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async Task DisposeClients()
	{
		var realtime = _realtimeClient;
		_realtimeClient = null;
		Exception stopError = null;
		if (realtime != null)
		{
			realtime.EventReceived -= OnRealtimeEvent;
			realtime.Error -= OnRealtimeError;
			try
			{
				await realtime.StopAsync();
			}
			catch (Exception error)
			{
				stopError = error;
			}
			finally
			{
				realtime.Dispose();
			}
		}

		_restClient?.Dispose();
		_restClient = null;
		if (stopError != null)
			throw new AggregateException("Failed to stop Intrinio real-time clients.", stopError);
	}

	private IntrinioRestClient SafeRest()
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private IntrinioRealtimeClient SafeRealtime()
		=> _realtimeClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ValueTask OnRealtimeError(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);
}

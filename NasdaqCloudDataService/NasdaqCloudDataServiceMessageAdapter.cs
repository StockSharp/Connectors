namespace StockSharp.NasdaqCloudDataService;

public partial class NasdaqCloudDataServiceMessageAdapter
{
	private const string _equityBoard = "NCDS";
	private const string _indexBoard = "NCDSIDX";
	private const string _etpBoard = "NCDSETP";
	private const string _optionBoard = "NCDSOPT";
	private NasdaqCloudDataServiceClient _client;

	/// <summary>Initializes a new instance.</summary>
	public NasdaqCloudDataServiceMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames([
			TimeSpan.FromMinutes(1),
			TimeSpan.FromMinutes(5),
			TimeSpan.FromMinutes(10),
			TimeSpan.FromMinutes(15),
			TimeSpan.FromMinutes(30),
			TimeSpan.FromDays(1),
			TimeSpan.FromDays(7),
			TimeSpan.FromDays(30),
		]);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[_equityBoard, _indexBoard, _etpBoard, _optionBoard];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Login.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);
		if (Password.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.PasswordNotSpecified);
		if (Address == null || !Address.IsAbsoluteUri)
			throw new InvalidOperationException(
				"Nasdaq Cloud customer-specific API address must be absolute.");

		_client = new NasdaqCloudDataServiceClient(
			Address, Login, Password.UnSecure())
		{
			Parent = this,
		};
		try
		{
			await _client.Authenticate(cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeClient();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
	}

	private NasdaqCloudDataServiceClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
}

namespace StockSharp.CboeDataShop;

public partial class CboeDataShopMessageAdapter
{
	private const string _stockBoardCode = "CBOE";
	private const string _optionBoardCode = "CBOEOPT";
	private static readonly DataType _dailyCandles = TimeSpan.FromDays(1).TimeFrame().Immutable();

	private CboeDataShopClient _client;

	/// <summary>Initializes a new instance.</summary>
	public CboeDataShopMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames([TimeSpan.FromDays(1)]);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Level1 || dataType == _dailyCandles;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [_stockBoardCode, _optionBoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Login.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);
		if (Password.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		if (Address == null || !Address.IsAbsoluteUri)
			throw new InvalidOperationException("Cboe API address must be absolute.");
		if (TokenAddress == null || !TokenAddress.IsAbsoluteUri)
			throw new InvalidOperationException("Cboe OAuth token address must be absolute.");

		var client = new CboeDataShopClient(Address, TokenAddress, Login, Password.UnSecure(), DataMode)
		{
			Parent = this,
		};
		_client = client;
		try
		{
			await client.Authenticate(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			client.Dispose();
			_client = null;
			throw;
		}
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

	private CboeDataShopClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
}

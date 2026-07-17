namespace StockSharp.Morningstar;

public partial class MorningstarMessageAdapter
{
	private const string _boardCode = "MORNINGSTAR";
	private static readonly DataType _dailyCandles = TimeSpan.FromDays(1).TimeFrame().Immutable();

	private MorningstarClient _client;
	private readonly SynchronizedDictionary<string, MorningstarInvestment> _investments =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Initializes a new instance.</summary>
	public MorningstarMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames([TimeSpan.FromDays(1)]);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Level1 || dataType == _dailyCandles;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [_boardCode];

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

		var client = new MorningstarClient(Region.ToAddress(), Login, Password.UnSecure())
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
		_investments.Clear();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
	}

	private MorningstarClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void CacheInvestment(MorningstarInvestment investment)
	{
		if (investment == null)
			return;
		foreach (var alias in investment.GetAliases())
			_investments[alias] = investment;
	}

	private (string identifier, MorningstarIdentifierTypes type) GetIdentifier(SecurityId securityId)
	{
		if (!securityId.SecurityCode.IsEmpty() &&
			_investments.TryGetValue(securityId.SecurityCode, out var investment))
		{
			var performanceId = investment.GetPerformanceId();
			if (!performanceId.IsEmpty())
				return (performanceId, MorningstarIdentifierTypes.PerformanceId);
		}
		return securityId.GetMorningstarIdentifier(IdentifierType);
	}
}

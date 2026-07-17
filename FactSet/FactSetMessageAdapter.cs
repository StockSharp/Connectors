namespace StockSharp.FactSet;

public partial class FactSetMessageAdapter
{
	private const string _boardCode = "FACTSET";
	private static readonly DataType _dailyCandles = TimeSpan.FromDays(1).TimeFrame().Immutable();

	private FactSetClient _client;
	private readonly SynchronizedDictionary<string, FactSetReference> _references =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Initializes a new instance.</summary>
	public FactSetMessageAdapter(IdGenerator transactionIdGenerator)
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

		switch (AuthenticationMode)
		{
			case FactSetAuthenticationModes.ApiKey:
				if (Login.IsEmpty())
					throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);
				if (Password.IsEmpty())
					throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
				break;
			case FactSetAuthenticationModes.OAuth:
				if (OAuthConfigFile.IsEmpty())
					throw new InvalidOperationException(
						$"{LocalizedStrings.File}: {LocalizedStrings.InvalidValue}.");
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(AuthenticationMode), AuthenticationMode, null);
		}

		var client = new FactSetClient(AuthenticationMode, Login, Password.UnSecure(), OAuthConfigFile)
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
		_references.Clear();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
	}

	private FactSetClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void CacheReference(FactSetReference reference)
	{
		if (reference == null)
			return;
		if (!reference.RequestId.IsEmpty())
			_references[reference.RequestId] = reference;
		if (!reference.FsymId.IsEmpty())
			_references[reference.FsymId] = reference;
	}

	private async Task<FactSetReference> ResolveReference(SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (_references.TryGetValue(code, out var cached))
			return cached;

		var references = await SafeClient().GetReferences(code, cancellationToken);
		foreach (var reference in references)
			CacheReference(reference);
		return references.FirstOrDefault(reference => reference.Matches(code))
			?? throw new InvalidOperationException(
				$"FactSet security '{code}' was not found or is not entitled.");
	}
}

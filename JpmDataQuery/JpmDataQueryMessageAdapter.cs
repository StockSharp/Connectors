namespace StockSharp.JpmDataQuery;

public partial class JpmDataQueryMessageAdapter
{
	private const string _boardCode = "JPMDQ";

	private JpmDataQueryClient _client;
	private readonly SynchronizedDictionary<string, JpmDataQueryInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Initializes a new instance.</summary>
	public JpmDataQueryMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Level1;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [_boardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty())
			throw new InvalidOperationException(
				$"{LocalizedStrings.Key}: {LocalizedStrings.InvalidValue}.");
		if (Secret.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		if (GroupId.IsEmpty())
			throw new InvalidOperationException(
				$"{LocalizedStrings.GroupId}: {LocalizedStrings.InvalidValue}.");
		if (Attribute.IsEmpty())
			throw new InvalidOperationException(
				$"{LocalizedStrings.Field}: {LocalizedStrings.InvalidValue}.");

		var client = new JpmDataQueryClient(Key?.UnSecure(), Secret.UnSecure()) { Parent = this };
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
		_instruments.Clear();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
	}

	private JpmDataQueryClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void CacheInstrument(JpmDataQueryInstrument instrument)
	{
		if (instrument?.InstrumentId.IsEmpty() != false)
			return;

		_instruments[instrument.InstrumentId] = instrument;
		if (!instrument.Isin.IsEmpty())
			_instruments[instrument.Isin] = instrument;
		if (!instrument.Cusip.IsEmpty())
			_instruments[instrument.Cusip] = instrument;
	}

	private async Task<JpmDataQueryInstrument> ResolveInstrument(SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (_instruments.TryGetValue(code, out var cached))
			return cached;

		await foreach (var instrument in SafeClient().LookupInstruments(
			GroupId, code, exactIdentifier: true, cancellationToken)
			.WithEnforcedCancellation(cancellationToken))
		{
			CacheInstrument(instrument);
			if (instrument.Matches(code))
				return instrument;
		}

		await foreach (var instrument in SafeClient().LookupInstruments(
			GroupId, code, exactIdentifier: false, cancellationToken)
			.WithEnforcedCancellation(cancellationToken))
		{
			CacheInstrument(instrument);
			if (instrument.Matches(code))
				return instrument;
		}

		throw new InvalidOperationException(
			$"J.P. Morgan DataQuery instrument '{code}' was not found in group '{GroupId}' or is not entitled.");
	}
}

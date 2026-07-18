namespace StockSharp.Orats;

public partial class OratsMessageAdapter
{
	private OratsRestClient _rest;
	private TimeZoneInfo _marketTimeZone;

	/// <summary>Initializes a new instance.</summary>
	public OratsMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(Extensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
	[
		Extensions.StockBoard,
		Extensions.OptionBoard,
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (Address == null || !Address.IsAbsoluteUri ||
			!Address.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
		{
			throw new InvalidOperationException(
				"ORATS address must be an absolute HTTPS URI.");
		}
		if (SessionStart < TimeSpan.Zero || SessionEnd <= SessionStart ||
			SessionEnd >= TimeSpan.FromDays(1))
		{
			throw new InvalidOperationException(
				"ORATS market session must be an increasing range within one day.");
		}

		_marketTimeZone = Extensions.ResolveMarketTimeZone(MarketTimeZoneId);
		var rest = new OratsRestClient(Address, Token.UnSecure(),
			Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
		_rest = rest;
		try
		{
			await rest.Validate(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			rest.Dispose();
			_rest = null;
			_marketTimeZone = null;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeRest();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeRest();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private OratsRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void DisposeRest()
	{
		_rest?.Dispose();
		_rest = null;
		_marketTimeZone = null;
	}
}

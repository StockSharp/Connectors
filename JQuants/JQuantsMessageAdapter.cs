namespace StockSharp.JQuants;

public partial class JQuantsMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, JQuantsInstrument>
		_instrumentsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, JQuantsInstrument>
		_instrumentsByCode =
			new(StringComparer.OrdinalIgnoreCase);
	private JQuantsRestClient _restClient;

	/// <summary>
	/// Initialize <see cref="JQuantsMessageAdapter"/>.
	/// </summary>
	public JQuantsMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.Tse];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.IsAssociated(BoardCodes.Tse);

	private JQuantsRestClient RestClient =>
		_restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void Remember(
		IEnumerable<JQuantsInstrument> instruments)
	{
		using (_sync.EnterScope())
		{
			foreach (var instrument in instruments ?? [])
			{
				if (instrument?.Code.IsEmpty() != false)
					continue;
				_instrumentsByNative[instrument.NativeId] =
					instrument;
				if (instrument.Kind == JQuantsInstrumentKinds.Equity)
					_instrumentsByCode[instrument.Code] = instrument;
				else
					_instrumentsByCode[
						$"{instrument.Kind}:{instrument.Code}"] =
							instrument;
			}
		}
	}

	private JQuantsInstrument ResolveInstrument(SecurityId securityId)
	{
		var native = securityId.Native?.ToString();
		using (_sync.EnterScope())
		{
			if (!native.IsEmpty() &&
				_instrumentsByNative.TryGetValue(native,
					out var known))
				return known;
			if (!securityId.SecurityCode.IsEmpty() &&
				_instrumentsByCode.TryGetValue(
					securityId.SecurityCode, out known))
				return known;
		}
		var code = securityId.SecurityCode
			.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var kind = native?.StartsWith("F:",
			StringComparison.OrdinalIgnoreCase) == true
				? JQuantsInstrumentKinds.Future
				: native?.StartsWith("O:",
					StringComparison.OrdinalIgnoreCase) == true
					? JQuantsInstrumentKinds.Option
					: JQuantsInstrumentKinds.Equity;
		if (!native.IsEmpty() && native.Length > 2)
			code = native[2..];
		return new()
		{
			Code = code,
			Name = code,
			Kind = kind,
		};
	}

	private static SecurityId ToSecurityId(
		JQuantsInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Code,
			BoardCode = BoardCodes.Tse,
			Native = instrument.NativeId,
		};

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instrumentsByNative.Clear();
			_instrumentsByCode.Clear();
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_restClient?.Dispose();
		_restClient = null;
		base.DisposeManaged();
	}
}

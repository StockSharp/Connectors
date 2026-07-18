namespace StockSharp.Toobit;

public partial class ToobitMessageAdapter
{
	private readonly CachedSynchronizedDictionary<string, IToobitNativeAdapter> _adapters =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Initializes a new instance of the <see cref="ToobitMessageAdapter"/>.
	/// </summary>
	public ToobitMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Toobit, BoardCodes.ToobitFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Toobit)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.ToobitFutures)
			|| securityId.IsAssociated(BoardCodes.Toobit)
			|| securityId.IsAssociated(BoardCodes.ToobitFutures);

	private IToobitNativeAdapter GetAdapter(SecurityId securityId)
	{
		if (securityId == default || securityId.BoardCode.IsEmpty())
			throw new InvalidOperationException("SecurityId.BoardCode must identify the Toobit market section.");

		if (_adapters.TryGetValue(securityId.BoardCode, out var adapter))
			return adapter;

		throw new InvalidOperationException($"Toobit board '{securityId.BoardCode}' is not enabled.");
	}

	private void EnsureConnected()
	{
		if (_adapters.Count == 0)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}
}

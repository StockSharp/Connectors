namespace StockSharp.Hyperliquid;

public partial class HyperliquidMessageAdapter
{
	private readonly Dictionary<HyperliquidSections, INativeAdapter> _nativeAdapters = [];

	private bool _suppressNativeConnectionState;
	private ConnectionStates? _lastNativeConnectionState;

	/// <summary>
	/// Initializes a new instance of the <see cref="HyperliquidMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public HyperliquidMessageAdapter(IdGenerator transactionIdGenerator)
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
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.HyperliquidSpot, BoardCodes.HyperliquidDerivatives];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId secId)
		=> secId.BoardCode.IsEmpty()
			|| secId.BoardCode.EqualsIgnoreCase(BoardCodes.HyperliquidSpot)
			|| secId.BoardCode.EqualsIgnoreCase(BoardCodes.HyperliquidDerivatives)
			|| secId.IsAssociated(BoardCodes.HyperliquidSpot)
			|| secId.IsAssociated(BoardCodes.HyperliquidDerivatives);

	internal int GetReconnectAttempts()
		=> ReConnectionSettings.ReAttemptCount;

	internal DateTime GetServerTime()
		=> CurrentTime;

	internal ValueTask SendOutFromNativeAsync(Message message, HyperliquidSections section, CancellationToken cancellationToken)
	{
		ApplySectionContext(message, section);
		return SendOutMessageAsync(message, cancellationToken);
	}

	internal ValueTask SendOutErrorFromNativeAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	internal ValueTask SendConnectionStateFromNativeAsync(ConnectionStates state, CancellationToken cancellationToken)
	{
		if (_suppressNativeConnectionState)
			return default;

		if (_lastNativeConnectionState == state)
			return default;

		_lastNativeConnectionState = state;
		return SendOutConnectionStateAsync(state, cancellationToken);
	}

	internal ValueTask SendSubscriptionReplyFromNativeAsync(long transactionId, CancellationToken cancellationToken, Exception error = null)
		=> SendSubscriptionReplyAsync(transactionId, cancellationToken, error);

	internal ValueTask SendSubscriptionResultFromNativeAsync(ISubscriptionMessage subscription, CancellationToken cancellationToken)
		=> SendSubscriptionResultAsync(subscription, cancellationToken);

	internal ValueTask SendSubscriptionFinishedFromNativeAsync(long transactionId, CancellationToken cancellationToken)
		=> SendSubscriptionFinishedAsync(transactionId, cancellationToken);

	internal void AddWarningFromNative(string message)
		=> this.AddWarningLog(message);

	private void SuppressNativeConnectionState(bool suppress)
		=> _suppressNativeConnectionState = suppress;

	private void ClearRoutingCaches()
		=> _lastNativeConnectionState = null;

	private INativeAdapter EnsureGetAdapter(SecurityId secId)
	{
		if (secId == default || secId.BoardCode.IsEmpty())
			throw new InvalidOperationException("SecurityId.BoardCode must be specified to route request to a Hyperliquid section.");

		var section =
			secId.BoardCode.EqualsIgnoreCase(BoardCodes.HyperliquidSpot) || secId.IsAssociated(BoardCodes.HyperliquidSpot)
				? HyperliquidSections.Spot
				: secId.BoardCode.EqualsIgnoreCase(BoardCodes.HyperliquidDerivatives) || secId.IsAssociated(BoardCodes.HyperliquidDerivatives)
					? HyperliquidSections.Derivatives
					: throw new InvalidOperationException($"Board '{secId.BoardCode}' is not supported by {nameof(HyperliquidMessageAdapter)}.");

		if (_nativeAdapters.TryGetValue(section, out var adapter))
			return adapter;

		throw new InvalidOperationException($"Section '{section}' is not enabled for board '{secId.BoardCode}'.");
	}

	private IReadOnlyDictionary<HyperliquidSections, INativeAdapter> NativeAdapters => _nativeAdapters;

	private string GetBoardCode(HyperliquidSections section)
		=> section switch
		{
			HyperliquidSections.Spot => BoardCodes.HyperliquidSpot,
			HyperliquidSections.Derivatives => BoardCodes.HyperliquidDerivatives,
			_ => throw new InvalidOperationException($"Unknown section '{section}'."),
		};

	private INativeAdapter CreateNativeAdapter(HyperliquidSections section)
		=> section switch
		{
			HyperliquidSections.Derivatives => new Native.Derivatives.DerivativesAdapter(this),
			HyperliquidSections.Spot => new Native.Spot.SpotAdapter(this),
			_ => throw new InvalidOperationException($"Unknown section '{section}'."),
		};

	private void RecreateAdapters()
	{
		_nativeAdapters.Clear();

		foreach (var section in Sections.Distinct())
			_nativeAdapters[section] = CreateNativeAdapter(section);
	}

	internal HyperliquidSigner CreateSignerFromNative()
	{
		var key = PrivateKey.UnSecure();

		if (key.IsEmpty())
			return null;

		return new HyperliquidSigner(key, !IsTestnet);
	}

	private void ApplySectionContext(Message message, HyperliquidSections section)
	{
		var boardCode = GetBoardCode(section);

		switch (message)
		{
			case ISecurityIdMessage securityIdMessage:
				securityIdMessage.SecurityId = RewriteBoardCode(securityIdMessage.SecurityId, boardCode);
				break;

			case INullableSecurityIdMessage nullableSecurityIdMessage when nullableSecurityIdMessage.SecurityId is SecurityId secId:
				secId = RewriteBoardCode(secId, boardCode);
				nullableSecurityIdMessage.SecurityId = secId;
				break;

			case ISecurityIdsMessage securityIdsMessage when securityIdsMessage.SecurityIds?.Length > 0:
				for (var i = 0; i < securityIdsMessage.SecurityIds.Length; i++)
				{
					var secId = RewriteBoardCode(securityIdsMessage.SecurityIds[i], boardCode);
					securityIdsMessage.SecurityIds[i] = secId;
				}
				break;
		}

		if (message is PortfolioMessage portfolioMsg)
			portfolioMsg.BoardCode = boardCode;
	}

	private static SecurityId RewriteBoardCode(SecurityId securityId, string boardCode)
	{
		if (securityId == default)
			return securityId;

		securityId.BoardCode = boardCode;

		return securityId;
	}

}

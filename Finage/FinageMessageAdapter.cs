namespace StockSharp.Finage;

public partial class FinageMessageAdapter
{
	private readonly Lock _subscriptionSync = new();
	private readonly Dictionary<long, string> _subscriptions = [];
	private FinageRestClient _restClient;
	private FinageWebSocketClient _streamClient;

	/// <summary>Initialize <see cref="FinageMessageAdapter"/>.</summary>
	public FinageMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>Supported Finage candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		FinageExtensions.TimeFrames;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.Finage];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> false;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.IsAssociated(BoardCodes.Finage);

	private FinageRestClient RestClient =>
		_restClient ?? throw new InvalidOperationException(
			"Finage REST API key is not configured.");

	private FinageWebSocketClient StreamClient =>
		_streamClient ?? throw new InvalidOperationException(
			"Finage streaming token is not configured.");

	private static FinageInstrument ResolveInstrument(
		SecurityId securityId)
		=> (securityId.Native?.ToString())
			.IsEmpty(securityId.SecurityCode)
			.ToInstrument();

	private bool HasSubscriptions(string symbol)
	{
		using (_subscriptionSync.EnterScope())
			return _subscriptions.Values.Any(
				value => value.EqualsIgnoreCase(symbol));
	}

	private void ClearState()
	{
		using (_subscriptionSync.EnterScope())
			_subscriptions.Clear();
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_streamClient?.Dispose();
		_streamClient = null;
		_restClient?.Dispose();
		_restClient = null;
		base.DisposeManaged();
	}
}

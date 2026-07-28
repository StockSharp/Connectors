namespace StockSharp.TraderMade;

public partial class TraderMadeMessageAdapter
{
	private readonly Lock _subscriptionSync = new();
	private readonly Dictionary<long, string>
		_level1Subscriptions = [];
	private readonly Dictionary<long, string>
		_depthSubscriptions = [];
	private TraderMadeRestClient _restClient;
	private TraderMadeWebSocketClient _streamClient;

	/// <summary>
	/// Initialize <see cref="TraderMadeMessageAdapter"/>.
	/// </summary>
	public TraderMadeMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>Supported TraderMade candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		TraderMadeExtensions.TimeFrames;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.TraderMade];

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
			securityId.IsAssociated(BoardCodes.TraderMade);

	private TraderMadeRestClient RestClient =>
		_restClient ?? throw new InvalidOperationException(
			"TraderMade REST API key is not configured.");

	private TraderMadeWebSocketClient StreamClient =>
		_streamClient ?? throw new InvalidOperationException(
			"TraderMade streaming API key is not configured.");

	private static TraderMadeInstrument ResolveInstrument(
		SecurityId securityId)
		=> (securityId.Native?.ToString())
			.IsEmpty(securityId.SecurityCode)
			.ToInstrument();

	private bool HasSubscriptions(string symbol)
	{
		using (_subscriptionSync.EnterScope())
			return _level1Subscriptions.Values.Any(
					value => value.EqualsIgnoreCase(symbol)) ||
				_depthSubscriptions.Values.Any(
					value => value.EqualsIgnoreCase(symbol));
	}

	private void ClearState()
	{
		using (_subscriptionSync.EnterScope())
		{
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
		}
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

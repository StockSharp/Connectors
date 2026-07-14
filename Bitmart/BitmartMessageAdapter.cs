namespace StockSharp.Bitmart;

[OrderCondition(typeof(BitmartOrderCondition))]
public partial class BitmartMessageAdapter
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Native.Extensions.TimeFrames.Keys;

	private Authenticator _authenticator;
	private INativeAdapter _nativeAdapter;

	/// <summary>
	/// Initializes a new instance of the <see cref="BitmartMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BitmartMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(15);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Bitmart];

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5, 20, 50];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_nativeAdapter is INativeAdapter a)
		{
			await a.ResetAsync(cancellationToken);
			a.OutMessage -= SendOutMessageAsync;

			_nativeAdapter = null;
		}

		if (_authenticator is Authenticator auth)
		{
			try
			{
				auth.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_authenticator = null;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (this.IsTransactional())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

		_authenticator = new(Key, Secret, Memo);

		_nativeAdapter = Section switch
		{
			BitmartSections.Spot => new Native.Spot.SpotAdapter(TransactionIdGenerator, _authenticator, this, SpotPublicWsAddress, SpotPrivateWsAddress),
			BitmartSections.Futures => new Native.Futures.FuturesAdapter(TransactionIdGenerator, _authenticator, this, FuturesPublicWsAddress, FuturesPrivateWsAddress),
			_ => throw new InvalidOperationException(Section.ToString()),
		};

		_nativeAdapter.OutMessage += SendOutMessageAsync;

		await _nativeAdapter.ConnectAsync(Address, this.IsMarketData(), this.IsTransactional(), ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.DisconnectAsync(cancellationToken);

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		this.AddDebugLog("ping");

		return _nativeAdapter.TimeAsync(cancellationToken);
	}
}
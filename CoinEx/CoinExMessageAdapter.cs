namespace StockSharp.CoinEx;

#if !NO_LICENSE
using StockSharp.Licensing;
#endif

public partial class CoinExMessageAdapter
{
	private Authenticator _authenticator;
	private Native.Spot.SpotAdapter _spot;
	private Native.Futures.FuturesAdapter _futures;
	private ConnectionStateTracker _traker;

	/// <summary>
	/// Initializes a new instance of the <see cref="CoinExMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public CoinExMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.CoinEx, BoardCodes.CoinExFT];

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => nameof(CoinEx);
#endif

	private ValueTask NativeAdapterOutMessageAsync(NativeAdapter adapter, Message message, CancellationToken cancellationToken)
	{
		if (message is ExecutionMessage execMsg && execMsg.DataType == DataType.Transactions && execMsg.HasOrderInfo && execMsg.TransactionId > 0)
			_adaptersByTransId[execMsg.TransactionId] = adapter;

		return SendOutMessageAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_spot != null)
		{
			try
			{
				_spot.NewOutMessage -= NativeAdapterOutMessageAsync;
				_spot.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_spot = null;
		}

		if (_futures != null)
		{
			try
			{
				_futures.NewOutMessage -= NativeAdapterOutMessageAsync;
				_futures.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_futures = null;
		}

		if (_authenticator != null)
		{
			try
			{
				_authenticator.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_authenticator = null;
		}

		if (_traker != null)
		{
			_traker.StateChanged -= SendOutConnectionStateAsync;
			_traker.Dispose();
			_traker = null;
		}

		_adaptersByTransId.Clear();

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
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

#if !NO_LICENSE
		var msg = await nameof(CoinEx).ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);
		if (!msg.IsEmpty())
			throw new InvalidOperationException(msg);
#endif

		if (_spot != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_futures != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_authenticator = new(this.IsTransactional(), Key, Secret);

		_traker = new();
		_traker.StateChanged += SendOutConnectionStateAsync;

		if (Sections.Contains(CoinExSections.Spot))
		{
			_spot = new(_authenticator, TransactionIdGenerator, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_spot.NewOutMessage += NativeAdapterOutMessageAsync;

			_traker.Add(_spot);
		}

		if (Sections.Contains(CoinExSections.Futures))
		{
			_futures = new(_authenticator, TransactionIdGenerator, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
			_futures.NewOutMessage += NativeAdapterOutMessageAsync;

			_traker.Add(_futures);
		}

		await _traker.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_spot == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_futures == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_traker.Disconnect();
		return default;
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_spot is not null)
			await _spot.Time(timeMsg, cancellationToken);

		if (_futures is not null)
			await _futures.Time(timeMsg, cancellationToken);
	}

	private NativeAdapter GetAdapter(SecurityId secId)
	{
		if (secId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinEx))
			return _spot ?? throw new InvalidOperationException("spot");
		else if (secId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinExFT))
			return _futures ?? throw new InvalidOperationException("spot");
		else
			throw new ArgumentOutOfRangeException(nameof(secId), secId, LocalizedStrings.InvalidValue);
	}
}
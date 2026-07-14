namespace StockSharp.Huobi;

partial class HuobiMessageAdapter
{
	private NativeAdapter _underlying;
	private Authenticator _authenticator;

	/// <summary>
	/// Initializes a new instance of the <see cref="HuobiMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public HuobiMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Section = HuobiSections.Futures;

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
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths
		=> Section == HuobiSections.Futures ? new[] { 20, 150 } : [5, 20, 150];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Huobi];

	private ValueTask OnUnderlyingNewOutMessage(Message message, CancellationToken cancellationToken)
		=> SendOutMessageAsync(message, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_underlying != null)
		{
			_underlying.NewOutMessage -= OnUnderlyingNewOutMessage;
			await _underlying.ResetAsync(cancellationToken);
			_underlying = null;
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

		await base.ResetAsync(resetMsg, cancellationToken);
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

		if (_underlying is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var isData = this.IsMarketData();
		var isTrans = this.IsTransactional();

		_authenticator = new(isTrans, Key, Secret);

		var domain = Address;

		if (Section == HuobiSections.Spot)
		{
			_underlying = new Native.Spot.SpotAdapter(this, _authenticator, domain);
		}
		else if (Section == HuobiSections.Futures)
		{
			_underlying = new Native.Futures.FuturesAdapter(this, _authenticator, domain);
		}
		else if (Section == HuobiSections.Usdt)
		{
			_underlying = new Native.Usdt.UsdtAdapter(this, _authenticator, domain);
		}
		//else if (Section == HuobiSections.Swap)
		//{
		//	// TODO
		//	throw new NotImplementedException();
		//}
		else
			throw new InvalidOperationException(Section.ToString());

		_underlying.NewOutMessage += OnUnderlyingNewOutMessage;
		await _underlying.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_underlying is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		return _underlying.DisconnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> _underlying.TimeAsync(timeMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
		=> _underlying.PortfolioLookupAsync(lookupMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
		=> _underlying.OrderStatusAsync(statusMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
		=> _underlying.RegisterOrderAsync(regMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> _underlying.CancelOrderAsync(cancelMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> _underlying.CancelOrderGroupAsync(cancelMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
		=> _underlying.SecurityLookupAsync(lookupMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask MarketDataAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> _underlying.MarketDataAsync(mdMsg, cancellationToken);

	/// <inheritdoc />
	public override ValueTask SendInMessageAsync(Message msg, CancellationToken cancellationToken)
	{
        return msg.Type switch
        {
            MessageTypes.ProcessSuspended => _underlying.SuspendedAsync((ProcessSuspendedMessage)msg, cancellationToken),
            _ => base.SendInMessageAsync(msg, cancellationToken),
        };
    }
}
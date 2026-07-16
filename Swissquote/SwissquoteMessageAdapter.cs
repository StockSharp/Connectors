namespace StockSharp.Swissquote;

public partial class SwissquoteMessageAdapter
{
	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public string ClientOrderId { get; init; }
		public string BrokerOrderId { get; set; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public DateTime? ExpiryDate { get; init; }
		public SwissquoteOrderCondition Condition { get; init; }
		public decimal ReportedFilled { get; set; }
	}

	private SwissquoteRestClient _rest;
	private readonly SynchronizedDictionary<string, OrderTracker> _orders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _brokerOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _reportedTrades = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastOrderRefresh;
	private DateTime _lastTransactionRefresh;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="SwissquoteMessageAdapter"/> class.</summary>
	public SwissquoteMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(5);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["SWISSQUOTE"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		_rest = new(Token.UnSecure(), IsDemo, Math.Max(1, ReConnectionSettings.ReAttemptCount))
		{
			Parent = this,
		};
		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		return base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		_rest?.Dispose();
		_rest = null;
		_orders.Clear();
		_brokerOrders.Clear();
		_reportedTrades.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastOrderRefresh = default;
		_lastTransactionRefresh = default;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;
		if (_orderStatusSubscriptionId != 0 &&
			now - _lastOrderRefresh >= TimeSpan.FromSeconds(10))
		{
			var isTransactionRefresh = now - _lastTransactionRefresh >= TimeSpan.FromMinutes(5);
			await RefreshOrders(_orderStatusSubscriptionId, isTransactionRefresh, cancellationToken);
			_lastOrderRefresh = now;
			if (isTransactionRefresh)
				_lastTransactionRefresh = now;
		}
		if (_portfolioSubscriptionId != 0 &&
			now - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
		{
			await SendPortfolioSnapshot(_portfolioSubscriptionId, null, cancellationToken);
			_lastPortfolioRefresh = now;
		}
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private SwissquoteRestClient GetRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private static DateTime GetSwissDate()
	{
		var utc = DateTime.UtcNow;
		var zone = GetSwissTimeZone();
		var localDate = zone == null ? utc.Date : TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Date;
		return DateTime.SpecifyKind(localDate, DateTimeKind.Utc);
	}

	private static TimeSpan GetSwissOffset(DateTime date)
	{
		var zone = GetSwissTimeZone();
		return zone?.GetUtcOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified)) ?? TimeSpan.Zero;
	}

	private static TimeZoneInfo GetSwissTimeZone()
	{
		foreach (var id in new[] { "Europe/Zurich", "W. Europe Standard Time" })
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(id);
			}
			catch (TimeZoneNotFoundException)
			{
			}
			catch (InvalidTimeZoneException)
			{
			}
		}
		return null;
	}
}

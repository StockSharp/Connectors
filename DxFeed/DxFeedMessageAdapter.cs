namespace StockSharp.DxFeed;

public partial class DxFeedMessageAdapter
{
	private sealed class MarketSubscription
	{
		private readonly object _sync = new();
		private long _remaining;
		private bool _isCompleted;
		private readonly HashSet<string> _snapshotSources = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _pendingSnapshotSources = new(StringComparer.OrdinalIgnoreCase);

		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public SecurityTypes? SecurityType { get; init; }
		public DataType DataType { get; init; }
		public string Symbol { get; init; }
		public string[] EventTypes { get; init; }
		public string[] Sources { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public TimeSpan? TimeFrame { get; init; }
		public bool IsHistoryOnly { get; init; }
		public bool IsDepth { get; init; }

		public void SetCount(long? count)
			=> _remaining = count ?? long.MaxValue;

		public bool IsTimeAllowed(DateTime time)
			=> (From == null || time >= From.Value) && (To == null || time <= To.Value);

		public bool TryConsume()
		{
			lock (_sync)
			{
				if (_isCompleted || _remaining <= 0)
					return false;
				if (_remaining != long.MaxValue)
					_remaining--;
				return true;
			}
		}

		public bool IsCountExhausted
		{
			get
			{
				lock (_sync)
					return _remaining <= 0;
			}
		}

		public bool TryComplete()
		{
			lock (_sync)
			{
				if (_isCompleted)
					return false;
				_isCompleted = true;
				return true;
			}
		}

		public bool UpdateSnapshotState(string source, int flags)
		{
			lock (_sync)
			{
				var key = source ?? string.Empty;
				if (flags.IsSnapshotComplete())
					_pendingSnapshotSources.Add(key);

				if ((flags & DxIndexedEventFlags.TransactionPending) == 0 &&
					_pendingSnapshotSources.Remove(key))
					_snapshotSources.Add(key);

				return _snapshotSources.Count >= Math.Max(1, Sources.Length);
			}
		}
	}

	private sealed class SecurityLookupContext
	{
		private readonly object _sync = new();
		private readonly Dictionary<string, SecurityId> _pending;

		public SecurityLookupContext(SecurityLookupMessage message,
			IEnumerable<SecurityId> securityIds, SecurityTypes? securityType)
		{
			Message = message;
			SecurityType = securityType;
			Deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
			_pending = securityIds.ToDictionary(id => id.SecurityCode, StringComparer.OrdinalIgnoreCase);
		}

		public SecurityLookupMessage Message { get; }
		public SecurityTypes? SecurityType { get; }
		public DateTime Deadline { get; }

		public string[] Symbols
		{
			get
			{
				lock (_sync)
					return [.. _pending.Keys];
			}
		}

		public bool TryTake(string symbol, out SecurityId securityId, out bool isComplete)
		{
			lock (_sync)
			{
				if (!_pending.Remove(symbol, out securityId))
				{
					isComplete = false;
					return false;
				}
				isComplete = _pending.Count == 0;
				return true;
			}
		}

		public SecurityId[] TakeAll()
		{
			lock (_sync)
			{
				var result = _pending.Values.ToArray();
				_pending.Clear();
				return result;
			}
		}
	}

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2),
		TimeSpan.FromHours(4), TimeSpan.FromDays(1), TimeSpan.FromDays(7),
	];

	private DxLinkClient _client;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<long, SecurityLookupContext> _securityLookups = [];
	private string[] _depthSources = [];

	/// <summary>Initializes a new instance of the <see cref="DxFeedMessageAdapter"/> class.</summary>
	public DxFeedMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.OrderLog);
		this.AddSupportedCandleTimeFrames(_timeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["DXFEED"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (!Uri.TryCreate(Address, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("ws" or "wss"))
			throw new ArgumentException("dxLink address must be an absolute ws:// or wss:// URI.", nameof(Address));

		_depthSources = MarketDepthSources.ParseDxSources();
		if (_depthSources.Length == 0)
			throw new InvalidOperationException("At least one dxFeed DOM source must be specified.");

		_client = new(Address, Token?.UnSecure(), AggregationPeriod, MarketDepthLevels,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.AttemptCount)
		{
			Parent = this,
		};
		_client.FeedDataReceived += ProcessFeedData;
		_client.DomSnapshotReceived += ProcessDomSnapshot;
		_client.Error += SendOutErrorAsync;
		_client.StateChanged += SendOutConnectionStateAsync;

		try
		{
			await _client.ConnectAsync(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		try
		{
			_client.Disconnect();
			await base.DisconnectAsync(disconnectMsg, cancellationToken);
		}
		finally
		{
			DisposeClient();
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		_marketSubscriptions.Clear();
		_securityLookups.Clear();
		_depthSources = [];
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var expired = _securityLookups
			.Where(p => p.Value.Deadline <= DateTime.UtcNow)
			.Select(p => p.Key)
			.ToArray();

		foreach (var transactionId in expired)
		{
			if (!_securityLookups.TryGetAndRemove(transactionId, out var context))
				continue;

			foreach (var securityId in context.TakeAll())
			{
				await SendOutMessageAsync(CreateSecurityMessage(context, securityId, null), cancellationToken);
				await SafeClient().UnsubscribeFeed(DxFeedEventTypes.Profile,
					securityId.SecurityCode, null, null, cancellationToken);
			}
			await SendSubscriptionResultAsync(context.Message, cancellationToken);
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private DxLinkClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void DisposeClient()
	{
		if (_client == null)
			return;

		_client.FeedDataReceived -= ProcessFeedData;
		_client.DomSnapshotReceived -= ProcessDomSnapshot;
		_client.Error -= SendOutErrorAsync;
		_client.StateChanged -= SendOutConnectionStateAsync;
		_client.Dispose();
		_client = null;
	}
}

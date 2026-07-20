namespace StockSharp.Grvt;

public partial class GrvtMessageAdapter
{
	private class MarketSubscription
	{
		public string Instrument { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
	}

	private sealed class TickSubscription : MarketSubscription
	{
		private const int _maximumTradeIds = 2048;
		private readonly HashSet<string> _tradeIds =
			new(StringComparer.OrdinalIgnoreCase);
		private readonly Queue<string> _tradeIdOrder = [];

		public DateTime LatestTime { get; private set; }

		public bool TryAccept(string tradeId, DateTime time)
		{
			if (tradeId.IsEmpty() || time < LatestTime || !_tradeIds.Add(tradeId))
				return false;
			_tradeIdOrder.Enqueue(tradeId);
			while (_tradeIdOrder.Count > _maximumTradeIds)
				_tradeIds.Remove(_tradeIdOrder.Dequeue());
			if (time > LatestTime)
				LatestTime = time;
			return true;
		}
	}

	private readonly record struct StreamSubscription(string Stream,
		string Selector);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, GrvtInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamSubscription, int> _streamReferences = [];
	private GrvtRestClient _restClient;
	private GrvtWebSocketClient _marketSocket;
	private GrvtWebSocketClient _tradingSocket;
	private GrvtSigner _signer;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private TimeSpan _serverTimeOffset;
	private DateTime _nextServerTimeSync;

	/// <summary>
	/// Initializes a new instance of the <see cref="GrvtMessageAdapter"/>.
	/// </summary>
	public GrvtMessageAdapter(IdGenerator transactionIdGenerator)
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
		=> dataType == DataType.Securities ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Grvt];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Grvt) ||
			securityId.IsAssociated(BoardCodes.Grvt);

	private GrvtRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private GrvtWebSocketClient MarketSocket => _marketSocket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime => DateTime.UtcNow + _serverTimeOffset;

	private void EnsureConnected()
	{
		if (_restClient is null || _marketSocket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(bool isSigningRequired)
	{
		EnsureConnected();
		if (!RestClient.IsAuthenticated || RestClient.SubAccountId.IsEmpty() ||
			_tradingSocket is null)
			throw new InvalidOperationException(
				"A GRVT trading API key and subaccount are required.");
		if (isSigningRequired && _signer is null)
			throw new InvalidOperationException(
				"The EVM private key associated with the GRVT API key is " +
				"required to submit orders.");
	}

	private string GetInstrumentCode(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not GRVT.");
		return securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim().ToUpperInvariant();
	}

	private GrvtInstrument GetInstrument(string code)
	{
		using (_sync.EnterScope())
			return _instruments.TryGetValue(code ?? string.Empty, out var instrument)
				? instrument
				: throw new InvalidOperationException(
					$"Unknown GRVT instrument '{code}'.");
	}

	private string PortfolioName => "GRVT_" +
		RestClient.SubAccountId.ThrowIfEmpty("GRVT subaccount ID");

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(PortfolioName) &&
			!portfolioName.EqualsIgnoreCase(RestClient.SubAccountId))
			throw new InvalidOperationException(
				$"Unknown GRVT portfolio '{portfolioName}'.");
	}

	private bool AddStreamReference(StreamSubscription subscription)
	{
		if (_streamReferences.TryGetValue(subscription, out var count))
		{
			_streamReferences[subscription] = count + 1;
			return false;
		}
		_streamReferences.Add(subscription, 1);
		return true;
	}

	private bool ReleaseStreamReference(StreamSubscription subscription)
	{
		if (!_streamReferences.TryGetValue(subscription, out var count))
			return false;
		if (count > 1)
		{
			_streamReferences[subscription] = count - 1;
			return false;
		}
		_streamReferences.Remove(subscription);
		return true;
	}

	private StreamSubscription GetTickerStream(string instrument)
		=> new("v1.ticker.s", $"{instrument}@{SnapshotInterval}");

	private StreamSubscription GetBookStream(string instrument)
		=> new("v1.book.s",
			$"{instrument}@{SnapshotInterval}-{MarketDepth}");

	private static StreamSubscription GetTradeStream(string instrument)
		=> new("v1.trade", $"{instrument}@500");

	private static StreamSubscription GetCandleStream(string instrument,
		TimeSpan timeFrame)
		=> new("v1.candle",
			$"{instrument}@{timeFrame.ToGrvtInterval().ToWire()}-TRADE");

	private static int GetHistoryLimit(long? count, int fallback)
		=> (count ?? fallback).Min(1000L).Max(1L).To<int>();

	private int GetRequestedDepth(int? depth)
		=> (depth ?? MarketDepth).Max(1).Min(MarketDepth);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}

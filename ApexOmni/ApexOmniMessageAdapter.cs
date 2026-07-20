namespace StockSharp.ApexOmni;

public partial class ApexOmniMessageAdapter
{
	private class MarketSubscription
	{
		public ApexOmniContract Instrument { get; init; }
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

	private readonly Lock _sync = new();
	private readonly Dictionary<string, ApexOmniContract> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ApexOmniContract> _publicInstruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ApexOmniAsset> _assets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<string, int> _topicReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private const int _maximumFillFingerprints = 8192;
	private readonly HashSet<string> _fillFingerprints =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Queue<string> _fillFingerprintOrder = [];
	private ApexOmniRestClient _restClient;
	private ApexOmniWebSocketClient _publicSocket;
	private ApexOmniWebSocketClient _privateSocket;
	private ApexOmniZkSigner _signer;
	private ApexOmniAccount _account;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private TimeSpan _serverTimeOffset;
	private DateTime _nextServerTimeSync;

	/// <summary>
	/// Initializes a new instance of the <see cref="ApexOmniMessageAdapter"/>.
	/// </summary>
	public ApexOmniMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(15);
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
	public override string[] AssociatedBoards => [BoardCodes.ApexOmni];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.ApexOmni) ||
			securityId.IsAssociated(BoardCodes.ApexOmni);

	private ApexOmniRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ApexOmniWebSocketClient PublicSocket => _publicSocket ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime => DateTime.UtcNow + _serverTimeOffset;

	private string PortfolioName => "APEXOMNI_" +
		(_account?.Id).ThrowIfEmpty("ApeX Omni account ID");

	private void EnsureConnected()
	{
		if (_restClient is null || _publicSocket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(bool isSigningRequired)
	{
		EnsureConnected();
		if (!RestClient.IsAuthenticated || _account is null ||
			_privateSocket is null)
			throw new InvalidOperationException(
				"ApeX Omni API key, secret, and passphrase are required.");
		if (isSigningRequired && _signer is null)
			throw new InvalidOperationException(
				"The ApeX Omni zkLink seed is required to submit orders.");
	}

	private ApexOmniContract GetInstrument(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not ApeX Omni.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _instruments.TryGetValue(code, out var instrument) ||
				_publicInstruments.TryGetValue(code, out instrument)
					? instrument
					: throw new InvalidOperationException(
						$"Unknown ApeX Omni instrument '{code}'.");
	}

	private ApexOmniContract GetPublicInstrument(string publicSymbol)
	{
		using (_sync.EnterScope())
			return _publicInstruments.TryGetValue(publicSymbol ?? string.Empty,
				out var instrument)
					? instrument
					: null;
	}

	private ApexOmniAsset GetSettlementAsset(ApexOmniContract instrument)
	{
		using (_sync.EnterScope())
			return _assets.TryGetValue(instrument.SettleAssetId ?? string.Empty,
				out var asset)
					? asset
					: throw new InvalidOperationException(
						$"No ApeX Omni settlement asset for '{instrument.Symbol}'.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(PortfolioName) &&
			!portfolioName.EqualsIgnoreCase(_account?.Id))
			throw new InvalidOperationException(
				$"Unknown ApeX Omni portfolio '{portfolioName}'.");
	}

	private bool AddTopicReference(string topic)
	{
		if (_topicReferences.TryGetValue(topic, out var count))
		{
			_topicReferences[topic] = count + 1;
			return false;
		}
		_topicReferences.Add(topic, 1);
		return true;
	}

	private bool ReleaseTopicReference(string topic)
	{
		if (!_topicReferences.TryGetValue(topic, out var count))
			return false;
		if (count > 1)
		{
			_topicReferences[topic] = count - 1;
			return false;
		}
		_topicReferences.Remove(topic);
		return true;
	}

	private static string GetTickerTopic(ApexOmniContract instrument)
		=> $"instrumentInfo.H.{instrument.CrossSymbolName}";

	private string GetBookTopic(ApexOmniContract instrument)
		=> $"orderBook{MarketDepth}.H.{instrument.CrossSymbolName}";

	private static string GetTradeTopic(ApexOmniContract instrument)
		=> $"recentlyTrade.H.{instrument.CrossSymbolName}";

	private static string GetCandleTopic(ApexOmniContract instrument,
		TimeSpan timeFrame)
		=> $"candle.{timeFrame.ToApexOmniInterval()}." +
			instrument.CrossSymbolName;

	private static int GetHistoryLimit(long? count, int fallback, int maximum)
		=> (count ?? fallback).Min(maximum).Max(1L).To<int>();

	private int GetRequestedDepth(int? depth)
		=> (depth ?? MarketDepth).Max(1).Min(MarketDepth);

	private bool TryAcceptFill(long subscriptionId, string fillId)
	{
		if (subscriptionId == 0 || fillId.IsEmpty())
			return false;
		var fingerprint = subscriptionId.ToString(
			CultureInfo.InvariantCulture) + ":" + fillId;
		using (_sync.EnterScope())
		{
			if (!_fillFingerprints.Add(fingerprint))
				return false;
			_fillFingerprintOrder.Enqueue(fingerprint);
			while (_fillFingerprintOrder.Count > _maximumFillFingerprints)
				_fillFingerprints.Remove(_fillFingerprintOrder.Dequeue());
			return true;
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}

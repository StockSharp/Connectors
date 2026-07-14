namespace StockSharp.Alor;

using Ecng.Configuration;

#if !NO_LICENSE
using StockSharp.Licensing;
#endif

public partial class AlorMessageAdapter
{
	private HttpClient _httpClient;
	private DataSocketClient _dataSocketClient;
	private OrderSocketClient _orderSocketClient;
	private readonly SynchronizedDictionary<long, long> _subTransIdMap = [];
	private readonly SynchronizedDictionary<string, SecurityId> _secMapByBrokSymbol = [];
	private ConnectionStateTracker _stateTracker;

	/// <summary>
	/// Initializes a new instance of the <see cref="AlorMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public AlorMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => IsDemo ? base.FeatureName : nameof(Alor);
#endif

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage msg, CancellationToken cancellationToken)
	{
#if !NO_LICENSE
		var licMsg = IsDemo ? null : await nameof(Alor).ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);
		if (!licMsg.IsEmpty())
			throw new InvalidOperationException(licMsg);
#endif

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_dataSocketClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_orderSocketClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var domainPrefix = IsDemo ? "dev" : string.Empty;
		var domain = $"api{domainPrefix}.alor.ru";

		var token = Token;
		SecureString jwtToken;

		if (token.IsEmpty())
		{
			var provider = ConfigManager.GetService<IOAuthProvider>();
			var socialId = 16; // https://admin.stocksharp.com/settings/socials/16/

			var authToken = await provider.RequestToken(
				socialId,
				IsDemo,
				cancellationToken);

			jwtToken = (authToken ?? throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified)).Value.Secure();
		}
		else
		{
			var url = $"https://oauth{domainPrefix}.alor.ru/refresh?token={token.UnSecure()}".To<Uri>();

			var jwnTokenRequest = new RestRequest { Method = Method.Post };

			dynamic response = await jwnTokenRequest.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

			jwtToken = ((string)response.AccessToken).Secure();
		}

		static string[] extractPortfolios(string token)
		{
			var parts = token.DecodeJWT().ToArray();

			dynamic payload = parts[1].FromJson(typeof(object));

			return ((string)payload.portfolios).SplitBySpace();
		}

		_pfNames.AddRange(extractPortfolios(jwtToken.UnSecure()));

		_httpClient = new(domain, jwtToken) { Parent = this };

		_dataSocketClient = new(domain, jwtToken, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
		_dataSocketClient.Response += OnResponse;
		_dataSocketClient.InstrumentStatus += OnInstrumentStatus;
		_dataSocketClient.Ohlc += OnOhlc;
		_dataSocketClient.Tick += OnTick;
		_dataSocketClient.Order += OnOrder;
		_dataSocketClient.OrderBook += OnOrderBook;
		_dataSocketClient.OwnTrade += OnOwnTrade;
		_dataSocketClient.Portfolio += OnPortfolio;
		_dataSocketClient.Position += OnPosition;
		_dataSocketClient.Quote += OnQuote;
		_dataSocketClient.Risk += OnRisk;
		_dataSocketClient.SpectraRisk += OnSpectraRisk;
		_dataSocketClient.StopOrder += OnStopOrder;

		_orderSocketClient = new(domain, jwtToken, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };

		_orderSocketClient.OrderCreated += OnOrderCreated;
		_orderSocketClient.TransError += OnTransError;

		_stateTracker = new();
		_stateTracker.StateChanged += SendOutConnectionStateAsync;

		_stateTracker.Add(_dataSocketClient);
		_stateTracker.Add(_orderSocketClient);

		await _stateTracker.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage msg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_dataSocketClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_orderSocketClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_httpClient.Dispose();
		_httpClient = null;

		_dataSocketClient.Response -= OnResponse;
		_dataSocketClient.InstrumentStatus -= OnInstrumentStatus;
		_dataSocketClient.Ohlc -= OnOhlc;
		_dataSocketClient.Tick -= OnTick;
		_dataSocketClient.Order -= OnOrder;
		_dataSocketClient.OrderBook -= OnOrderBook;
		_dataSocketClient.OwnTrade -= OnOwnTrade;
		_dataSocketClient.Portfolio -= OnPortfolio;
		_dataSocketClient.Position -= OnPosition;
		_dataSocketClient.Quote -= OnQuote;
		_dataSocketClient.Risk -= OnRisk;
		_dataSocketClient.SpectraRisk -= OnSpectraRisk;
		_dataSocketClient.StopOrder -= OnStopOrder;

		_orderSocketClient.OrderCreated -= OnOrderCreated;
		_orderSocketClient.TransError -= OnTransError;

		_stateTracker.Disconnect();

		return default;
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_httpClient != null)
		{
			try
			{
				_httpClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_httpClient = null;
		}

		async ValueTask<T> disposeSocket<T>(T socketClient)
			where T : BaseSocketClient
		{
			if (socketClient == null)
				return null;

			try
			{
				socketClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			return null;
		}

		_dataSocketClient = await disposeSocket(_dataSocketClient);
		_orderSocketClient = await disposeSocket(_orderSocketClient);

		_orderInfo.Clear();
		_pfNames.Clear();
		_subTransIdMap.Clear();
		_orderStatusIds.Clear();

		if (_stateTracker is not null)
		{
			_stateTracker.StateChanged -= SendOutConnectionStateAsync;
			_stateTracker.Dispose();
			_stateTracker = null;
		}

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	private ValueTask OnResponse(long id, RequestResponse obj, CancellationToken cancellationToken)
	{
		_orderStatusIds.Remove(id);

		return default;
	}

	private long AddSubTransId(long transId)
	{
		var subTransId = TransactionIdGenerator.GetNextId();
		_subTransIdMap.Add(subTransId, transId);
		return subTransId;
	}

	private long[] GetSubTransId(long parentId)
	{
		using (_subTransIdMap.EnterScope())
			return [.. _subTransIdMap.Where(p => p.Value == parentId).Select(p => p.Key)];
	}

	private long GetParentId(long subTransId)
	{
		if (_subTransIdMap.TryGetValue(subTransId, out var parentId))
			return parentId;

		return subTransId;
	}
}
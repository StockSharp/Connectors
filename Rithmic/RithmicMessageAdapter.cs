namespace StockSharp.Rithmic;

public partial class RithmicMessageAdapter
{
	private SocketClient _tickerClient;
	private SocketClient _orderClient;
	private SocketClient _historyClient;
	private SocketClient _pnlClient;

	private string _fcmId;
	private string _ibId;
	private string _accountId;
	private string _tradeRoute;

	/// <summary>
	/// Initializes a new instance of the <see cref="RithmicMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public RithmicMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);

		HeartbeatInterval = TimeSpan.FromSeconds(30);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		var hb = new RequestHeartbeat { TemplateId = TemplateId.RequestHeartbeat };

		if (_tickerClient?.IsConnected == true)
			await _tickerClient.SendAsync(hb, cancellationToken);

		if (_orderClient?.IsConnected == true)
			await _orderClient.SendAsync(hb, cancellationToken);

		if (_historyClient?.IsConnected == true)
			await _historyClient.SendAsync(hb, cancellationToken);

		if (_pnlClient?.IsConnected == true)
			await _pnlClient.SendAsync(hb, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (Login.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);

		if (ServerAddress.IsEmpty())
			throw new InvalidOperationException("Server address is not specified.");

		// Connect Ticker Plant for market data
		if (this.IsMarketData())
		{
			_tickerClient = CreateClient("Ticker");
			_tickerClient.MessageReceived += OnTickerMessage;
			_tickerClient.PostConnect += (isReconnect, token) =>
				isReconnect ? LoginAsync(_tickerClient, RequestLogin.Types.SysInfraType.TickerPlant, token) : default;

			await _tickerClient.ConnectAsync(cancellationToken);
			await LoginAsync(_tickerClient, RequestLogin.Types.SysInfraType.TickerPlant, cancellationToken);
		}

		// Connect Order Plant for transactions
		if (this.IsTransactional())
		{
			_orderClient = CreateClient("Order");
			_orderClient.MessageReceived += OnOrderMessage;
			_orderClient.PostConnect += (isReconnect, token) =>
				isReconnect ? LoginAsync(_orderClient, RequestLogin.Types.SysInfraType.OrderPlant, token) : default;

			await _orderClient.ConnectAsync(cancellationToken);
			await LoginAsync(_orderClient, RequestLogin.Types.SysInfraType.OrderPlant, cancellationToken);

			// get login info, accounts, trade routes
			await RequestLoginInfoAsync(cancellationToken);
		}

		// Connect PnL Plant
		if (this.IsTransactional())
		{
			_pnlClient = CreateClient("PnL");
			_pnlClient.MessageReceived += OnPnLMessage;
			_pnlClient.PostConnect += (isReconnect, token) =>
				isReconnect ? LoginAsync(_pnlClient, RequestLogin.Types.SysInfraType.PnlPlant, token) : default;

			await _pnlClient.ConnectAsync(cancellationToken);
			await LoginAsync(_pnlClient, RequestLogin.Types.SysInfraType.PnlPlant, cancellationToken);
		}

		// Connect History Plant
		_historyClient = CreateClient("History");
		_historyClient.MessageReceived += OnHistoryMessage;
		_historyClient.PostConnect += (isReconnect, token) =>
			isReconnect ? LoginAsync(_historyClient, RequestLogin.Types.SysInfraType.HistoryPlant, token) : default;

		await _historyClient.ConnectAsync(cancellationToken);
		await LoginAsync(_historyClient, RequestLogin.Types.SysInfraType.HistoryPlant, cancellationToken);

		await SendOutMessageAsync(new ConnectMessage(), cancellationToken);
	}

	private SocketClient CreateClient(string name)
		=> new(ServerAddress, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime)
		{
			Parent = this,
			Name = nameof(Rithmic) + "_" + name,
		};

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage msg, CancellationToken cancellationToken)
	{
		await LogoutAndDisconnect(_tickerClient, cancellationToken);
		await LogoutAndDisconnect(_orderClient, cancellationToken);
		await LogoutAndDisconnect(_historyClient, cancellationToken);
		await LogoutAndDisconnect(_pnlClient, cancellationToken);

		_tickerClient = null;
		_orderClient = null;
		_historyClient = null;
		_pnlClient = null;

		await SendOutMessageAsync(new DisconnectMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage msg, CancellationToken cancellationToken)
	{
		_mdSubscriptions.Clear();

		DisposeClient(ref _tickerClient);
		DisposeClient(ref _orderClient);
		DisposeClient(ref _historyClient);
		DisposeClient(ref _pnlClient);

		_fcmId = null;
		_ibId = null;
		_accountId = null;
		_tradeRoute = null;

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	private async ValueTask LoginAsync(SocketClient client, RequestLogin.Types.SysInfraType infraType, CancellationToken cancellationToken)
	{
		var loginCompleted = new TaskCompletionSource<ResponseLogin>();

		void onMsg(int templateId, byte[] data)
		{
			if (templateId == TemplateId.ResponseLogin)
			{
				var rp = ResponseLogin.Parser.ParseFrom(data);
				loginCompleted.TrySetResult(rp);
			}
		}

		client.MessageReceived += onMsg;

		try
		{
			var rq = new RequestLogin
			{
				TemplateId = TemplateId.RequestLogin,
				TemplateVersion = "3.9",
				User = Login,
				Password = Password.UnSecure(),
				AppName = nameof(StockSharp),
				AppVersion = "1.0.0.0",
				SystemName = SystemName,
				InfraType = infraType,
			};
			rq.UserMsg.Add("login");

			await client.SendAsync(rq, cancellationToken);

			var response = await loginCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

			if (response.RpCode.Count > 0 && response.RpCode[0] != "0")
				throw new InvalidOperationException($"Login failed ({infraType}): {response.RpCode.JoinComma()}");

			if (response.HasHeartbeatInterval)
				HeartbeatInterval = TimeSpan.FromSeconds(response.HeartbeatInterval);

			if (response.HasFcmId && _fcmId.IsEmpty())
				_fcmId = response.FcmId;

			if (response.HasIbId && _ibId.IsEmpty())
				_ibId = response.IbId;

			this.AddInfoLog($"Logged in to {infraType}: heartbeat={HeartbeatInterval.TotalSeconds}s");
		}
		finally
		{
			client.MessageReceived -= onMsg;
		}
	}

	private async Task RequestLoginInfoAsync(CancellationToken cancellationToken)
	{
		var infoCompleted = new TaskCompletionSource<ResponseLoginInfo>();
		var accountsCompleted = new TaskCompletionSource<bool>();
		var routesCompleted = new TaskCompletionSource<bool>();

		void onMsg(int templateId, byte[] data)
		{
			switch (templateId)
			{
				case TemplateId.ResponseLoginInfo:
				{
					var rp = ResponseLoginInfo.Parser.ParseFrom(data);
					infoCompleted.TrySetResult(rp);
					break;
				}
				case TemplateId.ResponseAccountList:
				{
					var rp = ResponseAccountList.Parser.ParseFrom(data);

					if (_accountId.IsEmpty() && rp.RqHandlerRpCode.Count > 0 && rp.RqHandlerRpCode[0] == "0"
						&& rp.HasAccountId)
					{
						_fcmId = rp.FcmId;
						_ibId = rp.IbId;
						_accountId = rp.AccountId;
					}

					if (rp.RpCode.Count > 0)
						accountsCompleted.TrySetResult(true);

					break;
				}
				case TemplateId.ResponseTradeRoutes:
				{
					var rp = ResponseTradeRoutes.Parser.ParseFrom(data);

					if (_tradeRoute.IsEmpty() && rp.RqHandlerRpCode.Count > 0 && rp.RqHandlerRpCode[0] == "0"
						&& rp.HasTradeRoute && rp.HasIsDefault && rp.IsDefault)
					{
						_tradeRoute = rp.TradeRoute;
					}

					if (rp.RpCode.Count > 0)
						routesCompleted.TrySetResult(true);

					break;
				}
			}
		}

		_orderClient.MessageReceived += onMsg;

		try
		{
			// request login info
			var infoRq = new RequestLoginInfo { TemplateId = TemplateId.RequestLoginInfo };
			infoRq.UserMsg.Add("info");
			await _orderClient.SendAsync(infoRq, cancellationToken);

			var loginInfo = await infoCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

			// request accounts
			var accRq = new RequestAccountList
			{
				TemplateId = TemplateId.RequestAccountList,
				FcmId = loginInfo.FcmId,
				IbId = loginInfo.IbId,
				UserType = (RequestAccountList.Types.UserType)loginInfo.UserType,
			};
			accRq.UserMsg.Add("accounts");
			await _orderClient.SendAsync(accRq, cancellationToken);
			await accountsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

			// request trade routes
			var routeRq = new RequestTradeRoutes { TemplateId = TemplateId.RequestTradeRoutes, SubscribeForUpdates = false };
			routeRq.UserMsg.Add("routes");
			await _orderClient.SendAsync(routeRq, cancellationToken);
			await routesCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

			this.AddInfoLog($"Account={_accountId}, TradeRoute={_tradeRoute}");
		}
		finally
		{
			_orderClient.MessageReceived -= onMsg;
		}
	}

	private static async ValueTask LogoutAndDisconnect(SocketClient client, CancellationToken cancellationToken)
	{
		if (client?.IsConnected == true)
		{
			try
			{
				var rq = new RequestLogout { TemplateId = TemplateId.RequestLogout };
				rq.UserMsg.Add("logout");
				await client.SendAsync(rq, cancellationToken);
			}
			catch { }

			client.Disconnect();
		}
	}

	private static void DisposeClient(ref SocketClient client)
	{
		client?.Dispose();
		client = null;
	}
}

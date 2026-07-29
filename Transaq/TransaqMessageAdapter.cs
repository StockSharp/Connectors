namespace StockSharp.Transaq;

using System.Threading.Channels;

using ConnectMessage = Messages.ConnectMessage;
using DisconnectMessage = Messages.DisconnectMessage;

partial class TransaqMessageAdapter
{
	static class ExtendedMessageTypes
	{
		public const MessageTypes Init = (MessageTypes)(-7001);
	}

	private class InitMessage : Message
	{
		public InitMessage()
			: base(ExtendedMessageTypes.Init)
		{
		}

		public override Message Clone()
		{
			return new InitMessage();
		}
	}

	private readonly SynchronizedDictionary<Type, Func<BaseResponse, ValueTask>> _handlerBunch = [];
	private ApiClient _client;
	private bool _isInitialized;

	private Channel<string> _callbackChannel;
	private Task _callbackProcessorTask;
	private CancellationTokenSource _callbackCts;

	private class SuspendInfo(TransaqMessageAdapter parent)
	{
		private readonly Lock _lock = new();
		private readonly TransaqMessageAdapter _parent = parent ?? throw new ArgumentNullException(nameof(parent));
		private readonly List<ISubscriptionIdMessage> _messages = [];

		private long _subscriptionId;

		public async ValueTask ProcessAsync(ISubscriptionMessage message)
		{
			if (message is null)
				throw new ArgumentNullException(nameof(message));

			ISubscriptionIdMessage[] outMsgs;

			using (_lock.EnterScope())
			{
				_subscriptionId = message.TransactionId;
				outMsgs = _messages.CopyAndClear();
			}

			foreach (var outMsg in outMsgs)
			{
				outMsg.OriginalTransactionId = _subscriptionId;
				await _parent.SendOutMessageAsync((Message)outMsg, default);
			}

			await _parent.SendOutMessageAsync(message.CreateResult(), default);
		}

		public async ValueTask ProcessOutAsync<TMessage>(TMessage message)
			where TMessage : Message, ISubscriptionIdMessage
		{
			if (message is null)
				throw new ArgumentNullException(nameof(message));

			using (_lock.EnterScope())
			{
				if (_subscriptionId == 0)
				{
					_messages.Add(message);
					return;
				}
				else
					message.OriginalTransactionId = _subscriptionId;
			}

			await _parent.SendOutMessageAsync(message, default);
		}
	}

	private readonly SynchronizedDictionary<MessageTypes, SuspendInfo> _suspended = [];

	private async ValueTask TrySuspendOutAsync<TMessage>(TMessage message)
		where TMessage : Message, ISubscriptionIdMessage
	{
		if (message.OriginalTransactionId == 0)
			await _suspended.SafeAdd(message is PortfolioMessage ? MessageTypes.PositionChange : message.Type, key => new SuspendInfo(this)).ProcessOutAsync(message);
		else
			await SendOutMessageAsync(message, default);
	}

	/// <summary>
	/// Создать <see cref="TransaqMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Генератор идентификаторов транзакций.</param>
	public TransaqMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		AddHandler<ClientLimitsResponse>(OnClientLimitsResponse);
		AddHandler<ClientResponse>(OnClientResponse);
		AddHandler<PortfolioResponse>(OnPortfolioResponse);
		AddHandler<LeverageControlResponse>(OnLeverageControlResponse);
		AddHandler<MarketOrdResponse>(OnMarketOrdResponse);
		AddHandler<OrdersResponse>(OnOrdersResponse);
		AddHandler<OvernightResponse>(OnOvernightResponse);
		AddHandler<PositionsResponse>(OnPositionsResponse);
		AddHandler<TradesResponse>(OnTradesResponse);
		AddHandler<UnionResponse>(OnUnionResponse);

		AddHandler<AllTradesResponse>(OnAllTradesResponse);
		AddHandler<CandleKindsResponse>(OnCandleKindsResponse);
		AddHandler<CandlesResponse>(OnCandlesResponse);
		AddHandler<MarketsResponse>(OnMarketsResponse);
		AddHandler<NewsBodyResponse>(OnNewsBodyResponse);
		AddHandler<NewsHeaderResponse>(OnNewsHeaderResponse);
		AddHandler<QuotationsResponse>(OnQuotationsResponse);
		AddHandler<QuotesResponse>(OnQuotesResponse);
		AddHandler<SecInfoResponse>(OnSecInfoResponse);
		AddHandler<SecuritiesResponse>(OnSecuritiesResponse);
		AddHandler<TicksResponse>(OnTicksResponse);
		AddHandler<BoardsResponse>(OnBoardsResponse);
		AddHandler<PitsResponse>(OnPitsResponse);
		AddHandler<MessagesResponse>(OnMessagesResponse);

		AddHandler<ServerStatusResponse>(OnServerStatusResponse);
		AddHandler<ConnectorVersionResponse>(OnConnectorVersionResponse);
		AddHandler<CurrentServerResponse>(OnCurrentServerResponse);
		AddHandler<AuthenticationResponse>(OnAuthenticationResponse);

		this.AddSupportedMessage(MessageTypes.MarketData, true);
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.PortfolioLookup);
		this.AddSupportedMessage(MessageTypes.BoardLookup, true);
		this.AddSupportedMessage(MessageTypes.DataTypeLookup, true);
		this.AddSupportedMessage(MessageTypes.ChangePassword, null);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.News);

		foreach (var tf in _candlePeriods.Values)
			this.AddSupportedMarketDataType(tf);

		MaxParallelMessages = 1;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_handlerBunch.Clear();
		base.DisposeManaged();
	}

	private void AddHandler<T>(Func<T, ValueTask> handler)
		where T : BaseResponse
	{
		if (handler == null)
			throw new ArgumentNullException(nameof(handler));

		_handlerBunch[typeof(T)] = response => handler((T)response);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool IsAutoReplyOnTransactonalUnsubscription => true;

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		_isInitialized = false;

		_registeredSecurityIds.Clear();
		_candleTransactions.Clear();
		_quotes.Clear();

		_orders.Clear();
		_unions.Clear();
		_pfSubs.Clear();

		_secBoards.Clear();
		_secIdsByMarket.Clear();

		_suspended.Clear();

		_useCreditSecurities.Clear();

		StopCallbackProcessor();

		if (_client != null)
		{
			try
			{
				_client.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_client = null;
		}

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		await Connect(cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		await Disconnect(cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ChangePasswordAsync(ChangePasswordMessage pwdMsg, CancellationToken cancellationToken)
	{
		await SendCommand(new ChangePassMessage
		{
			OldPass = Password.UnSecure(),
			NewPass = pwdMsg.NewPassword.UnSecure()
		}, cancellationToken);

		await SendOutMessageAsync(new ChangePasswordMessage { OriginalTransactionId = pwdMsg.TransactionId }, cancellationToken);
	}

	/// <inheritdoc />
	public override ValueTask SendInMessageAsync(Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case ExtendedMessageTypes.Init:
				return TryInit(cancellationToken);

			case MessageTypes.SecurityLookup:
			case MessageTypes.BoardLookup:
			case MessageTypes.PortfolioLookup:
			case MessageTypes.OrderStatus:
			case MessageTypes.DataTypeLookup:
			{
				var subscriptionMsg = (ISubscriptionMessage)message;

				if (subscriptionMsg.IsSubscribe)
					return _suspended.SafeAdd(subscriptionMsg.DataType.ToMessageType2(), key => new SuspendInfo(this)).ProcessAsync(subscriptionMsg);

				return default;
			}

			default:
				return base.SendInMessageAsync(message, cancellationToken);
		}
	}

	private async ValueTask Connect(CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (Login.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);

		if (Password.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.PasswordNotSpecified);

		var dllPath = DllPath;

		if (dllPath.IsEmpty())
		{
			var os = Environment.Is64BitProcess ? "win-x64" : "win-x86";
			var dllName = IsHFT ? "txcn" : "txmlconnector";
			var bits = Environment.Is64BitProcess ? "64" : string.Empty;
			dllPath = $"runtimes\\{os}\\native\\{dllName}{bits}.dll";
		}

		StartCallbackProcessor();

		_client = new ApiClient(OnCallback,
				dllPath,
				ApiLogsPath,
				ApiLogLevel);

		await SendCommand(new Native.Commands.ConnectMessage
		{
			Login = Login,
			Password = Password.UnSecure(),
			EndPoint = Address.To<EndPoint>(),
			Proxy = Proxy,
			MicexRegisters = MicexRegisters,
			RqDelay = MarketDataInterval == null ? null : ((int)MarketDataInterval.Value.TotalMilliseconds).Max(100),
			Milliseconds = true,
			Utc = true,
		}, cancellationToken, false);
	}

	private void StartCallbackProcessor()
	{
		StopCallbackProcessor();

		_callbackChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
		_callbackCts = new CancellationTokenSource();
		_callbackProcessorTask = Task.Run(() => ProcessCallbacksAsync(_callbackCts.Token));
	}

	private void StopCallbackProcessor()
	{
		if (_callbackChannel != null)
		{
			_callbackChannel.Writer.TryComplete();
			_callbackCts?.Cancel();

			try { _callbackProcessorTask?.Wait(); } catch { }

			_callbackCts?.Dispose();
			_callbackCts = null;
			_callbackChannel = null;
			_callbackProcessorTask = null;
		}
	}

	private void OnCallback(string data)
	{
		_callbackChannel?.Writer.TryWrite(data);
	}

	private async Task ProcessCallbacksAsync(CancellationToken token)
	{
		var reader = _callbackChannel.Reader;

		try
		{
			await foreach (var data in reader.ReadAllAsync(token))
			{
				try
				{
					this.AddVerboseLog(data);

					string name = null;
					var response = Do.Invariant(() => XmlSerializeHelper.Deserialize(data, out name));

					if (!response.IsSuccess)
					{
						await SendOutErrorAsync(response.Text, default);
						continue;
					}

					this.AddDebugLog(name);

					var handler = _handlerBunch.TryGetValue(response.GetType());
					if (handler != null)
						await handler(response);
				}
				catch (Exception ex)
				{
					await SendOutErrorAsync(new InvalidOperationException(LocalizedStrings.ErrorParsing.Put(data), ex), default);
				}
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
	}

	private async ValueTask<BaseResponse> SendCommand(BaseCommandMessage command, CancellationToken cancellationToken, bool throwError = true)
	{
		var inXml = XmlSerializeHelper.Serialize(command);

		this.AddVerboseLog("Transaq in: '{0}'.", inXml);

		var outXml = await _client.SendCommand(inXml, cancellationToken);

		this.AddVerboseLog("Transaq out: '{0}'.", outXml);

		var result = XmlSerializeHelper.Deserialize(outXml, out _);

		if (!result.IsSuccess)
		{
			this.AddErrorLog(LocalizedStrings.CommandNotProcessedReason, command.Id, result.Text);

			if (throwError)
				throw new InvalidOperationException(result.Text);
		}

		return result;
	}

	private async ValueTask Disconnect(CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		await SendCommand(new Native.Commands.DisconnectMessage(), cancellationToken, false);

		_client.Dispose();
		_client = null;
	}

	private async ValueTask OnDisconnectedAsync(Exception error)
	{
		ConnectorVersion = null;
		CurrentServer = -1;
		ServerTimeDiff = null;

		await SendOutMessageAsync(error == null
			? new DisconnectMessage()
			: new ConnectMessage { Error = error },
			default);
	}

	private async ValueTask OnServerStatusResponse(ServerStatusResponse response)
	{
		var error = response.Connected == "error";

		if (error)
		{
			await OnDisconnectedAsync(new InvalidOperationException(response.Text));
			return;
		}

		var isConnected = response.Connected == "true";

		if (isConnected)
		{
			await SendOutMessageAsync(new ConnectMessage(), default);
			await SendOutMessageAsync(new InitMessage().LoopBack(this), default);
		}
		else
		{
			await OnDisconnectedAsync(null);
		}
	}

	private async ValueTask TryInit(CancellationToken cancellationToken)
	{
		if (_isInitialized)
			return;

		_isInitialized = true;

		await SendCommand(new RequestConnectorVersionMessage(), cancellationToken, false);
		await SendCommand(new RequestServerIdMessage(), cancellationToken, false);
		//await SendCommand(new RequestSecuritiesInfoMessage(), cancellationToken, false);

		var result = await SendCommand(new RequestServTimeDifferenceMessage(), cancellationToken, false);

		var diff = TimeSpan.FromSeconds(result.Diff);
		XmlSerializeHelper.GetNow = () => TimeHelper.Now + diff;
	}

	private ValueTask OnConnectorVersionResponse(ConnectorVersionResponse response)
	{
		ConnectorVersion = response.Version;
		return default;
	}

	private ValueTask OnCurrentServerResponse(CurrentServerResponse response)
	{
		CurrentServer = response.Id;
		return default;
	}

	private ValueTask OnAuthenticationResponse(AuthenticationResponse reponse)
	{
		this.AddWarningLog(reponse.Content);
		return default;
	}
}

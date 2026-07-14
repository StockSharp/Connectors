namespace StockSharp.cTrader;

using Ecng.Configuration;

public partial class cTraderMessageAdapter
{
	private readonly ProtoHeartbeatEvent _heartbeatEvent = new();
	private OpenClient _client;
	private IOAuthToken _token;
	private long _accountId;
	private string _connectingMsgId;
	private readonly SynchronizedDictionary<long, (ISubscriptionMessage subscription, bool isFirstTime)> _subscriptions = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="cTraderMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public cTraderMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);

		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsNativeIdentifiers => true;

	/// <inheritdoc />
	public override bool IsNativeIdentifiersPersistable => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage msg, CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var provider = ConfigManager.GetService<IOAuthProvider>();
		var socialId = 14; // https://admin.stocksharp.com/settings/socials/14/

		var token = await provider.RequestToken(
			socialId,
			IsDemo,
			cancellationToken);

		_token = token ?? throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);

		_client = new(Address);
		_client.MessageReceived += SessionOnMessageReceived;

		await _client.Connect(cancellationToken);

		_connectingMsgId = Guid.NewGuid().To<string>();

		await _client.SendMessage(new ProtoOAApplicationAuthReq
		{
			ClientId = "10530_CnAR6l68etisSrtI00Gg6QQ6qVLV04OmbV3FnqMfRqUg1syfZO",
			ClientSecret = "50fVRmtOLtNkTfuTeIjRfTFAvIWgVe6xaSnkGn7LlOCxYgSYwl"
		}, _connectingMsgId, cancellationToken);

		await TimeSpan.FromSeconds(2).Delay(cancellationToken);

		await _client.SendMessage(new ProtoOAGetAccountListByAccessTokenReq
		{
			AccessToken = _token.Value,
		}, _connectingMsgId, cancellationToken);

		await TimeSpan.FromSeconds(2).Delay(cancellationToken);

		await _client.SendMessage(new ProtoOAAccountAuthReq
		{
			AccessToken = _token.Value,
			CtidTraderAccountId = _accountId
		}, _connectingMsgId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage msg, CancellationToken cancellationToken)
	{
		if (_client is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_client.MessageReceived -= SessionOnMessageReceived;
		_client.Dispose();

		return base.DisconnectAsync(msg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
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

		_quotes.Clear();
		_subscriptions.Clear();
		_posValues.Clear();
		_symbolSubscriptions.Clear();
		_histLevel1.Clear();
		_pendingInstruments.Clear();
		_secNameMap.Clear();
		_accountId = default;
		_connectingMsgId = default;
		_token = default;

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_client is OpenClient c)
			await c.SendMessage(_heartbeatEvent, ProtoPayloadType.HeartbeatEvent, timeMsg.TransactionId.DefaultAsNull().To<string>(), cancellationToken);
	}

	private async void SessionOnMessageReceived(ProtoMessage pm)
	{
		try
		{
			var msgType = (ProtoOAPayloadType)pm.PayloadType;

			this.AddDebugLog("Received: {0}", msgType);

			long.TryParse(pm.ClientMsgId, out var transId);

			const ProtoOAPayloadType heartbeat = (ProtoOAPayloadType)(int)ProtoPayloadType.HeartbeatEvent;

			var payload = pm.Payload;

			switch (msgType)
			{
				case ProtoOAPayloadType.ProtoOaErrorRes:
				{
					var errorMsg = ProtoOAErrorRes.Parser.ParseFrom(payload);
					var error = new InvalidOperationException(errorMsg.Description);

					if (pm.ClientMsgId == _connectingMsgId)
						await SendOutDisconnectMessageAsync(error, CancellationToken.None);
					else if (transId != 0)
						await SendSubscriptionReplyAsync(transId, CancellationToken.None, error);
					else
						await SendOutErrorAsync(error, CancellationToken.None);

					break;
				}

				case ProtoOAPayloadType.ProtoOaAccountAuthRes:
					await OnAccAuthResponse(ProtoOAAccountAuthRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaApplicationAuthRes:
					OnAppAuthResponse(ProtoOAApplicationAuthRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaClientDisconnectEvent:
					await OnDisconnectResponse(ProtoOAClientDisconnectEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaDealListRes:
					OnDealListResponse(ProtoOADealListRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaAssetListRes:
					OnAssetListResponse(ProtoOAAssetListRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaAssetClassListRes:
					OnAssetClassListResponse(ProtoOAAssetClassListRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaAccountsTokenInvalidatedEvent:
					OnAccountsTokenInvalidatedEvent(ProtoOAAccountsTokenInvalidatedEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaCashFlowHistoryListRes:
					OnCashFlowHistoryListResponse(ProtoOACashFlowHistoryListRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaExecutionEvent:
					await OnExecutionEvent(transId, ProtoOAExecutionEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaExpectedMarginRes:
					OnExpectedMarginResponse(ProtoOAExpectedMarginRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaGetAccountsByAccessTokenRes:
					OnGetAccountsByAccessTokenResponse(ProtoOAGetAccountListByAccessTokenRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaGetTickdataRes:
					await OnGetTickdataResponse(transId, ProtoOAGetTickDataRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaGetTrendbarsRes:
					await OnGetTrendbarsResponse(transId, ProtoOAGetTrendbarsRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaMarginChangedEvent:
					OnMarginChangedEvent(ProtoOAMarginChangedEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaOrderErrorEvent:
					await OnOrderErrorEvent(transId, ProtoOAOrderErrorEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaReconcileRes:
					await OnReconcileResponse(transId, ProtoOAReconcileRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaSpotEvent:
					await OnSpotEvent(ProtoOASpotEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaSubscribeSpotsRes:
					await OnSubscribeSpotsResponse(transId, ProtoOASubscribeSpotsRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaSymbolsForConversionRes:
					OnSymbolsForConversionResponse(ProtoOASymbolsForConversionRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaSymbolsListRes:
					await OnSymbolsListResponse(transId, ProtoOASymbolsListRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaSymbolByIdRes:
					await OnSymbolByIdResponse(transId, ProtoOASymbolByIdRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaSymbolChangedEvent:
					OnSymbolChangedEvent(ProtoOASymbolChangedEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaTraderRes:
					OnTraderResponse(ProtoOATraderRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaTraderUpdateEvent:
					OnTraderUpdateEvent(ProtoOATraderUpdatedEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaTrailingSlChangedEvent:
					OnTrailingSlChangedEvent(ProtoOATrailingSLChangedEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaUnsubscribeSpotsRes:
					await OnUnsubscribeSpotsResponse(transId, ProtoOAUnsubscribeSpotsRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaVersionRes:
					OnVersionResponse(ProtoOAVersionRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaGetCtidProfileByTokenRes:
					OnGetCtidProfileByTokenResponse(ProtoOAGetCtidProfileByTokenRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaSymbolCategoryRes:
					OnSymbolCategoryResponse(ProtoOASymbolCategoryListRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaDepthEvent:
					await OnDepthEvent(ProtoOADepthEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaSubscribeDepthQuotesRes:
					await OnSubscribeDepthQuotesResponse(transId, ProtoOASubscribeDepthQuotesRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaUnsubscribeDepthQuotesRes:
					await OnUnsubscribeDepthQuotesResponse(transId, ProtoOAUnsubscribeDepthQuotesRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaAccountLogoutRes:
					OnAccountLogoutResponse(ProtoOAAccountLogoutRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaRefreshTokenRes:
					OnRefreshTokenResponse(ProtoOARefreshTokenRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaAccountDisconnectEvent:
					OnAccountDisconnectEvent(ProtoOAAccountDisconnectEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaMarginCallListRes:
					OnMarginCallListResponse(ProtoOAMarginCallListRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaMarginCallUpdateRes:
					OnMarginCallUpdateResponse(ProtoOAMarginCallUpdateRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaMarginCallUpdateEvent:
					OnMarginCallUpdateEvent(ProtoOAMarginCallUpdateEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaMarginCallTriggerEvent:
					OnMarginCallTriggerEvent(ProtoOAMarginCallTriggerEvent.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaGetDynamicLeverageRes:
					OnGetDynamicLeverageResponse(ProtoOAGetDynamicLeverageByIDRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaSubscribeLiveTrendbarRes:
					await OnSubscribeLiveTrendbarResponse(transId, ProtoOASubscribeLiveTrendbarRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaUnsubscribeLiveTrendbarRes:
					await OnUnsubscribeLiveTrendbarResponse(transId, ProtoOAUnsubscribeLiveTrendbarRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaOrderListRes:
					await OnOrderListResponse(transId, ProtoOAOrderListRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaDealListByPositionIdRes:
					OnDealListByPositionIdResponse(ProtoOADealListByPositionIdRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaOrderDetailsRes:
					OnOrderDetailsResponse(ProtoOAOrderDetailsRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaOrderListByPositionIdRes:
					OnOrderListByPositionIdResponse(ProtoOAOrderListByPositionIdRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaDealOffsetListRes:
					OnDealOffsetListResponse(ProtoOADealOffsetListRes.Parser.ParseFrom(payload));
					break;

				case ProtoOAPayloadType.ProtoOaGetPositionUnrealizedPnlRes:
					await OnGetPositionUnrealizedPnlResponse(transId, ProtoOAGetPositionUnrealizedPnLRes.Parser.ParseFrom(payload));
					break;

				case heartbeat:
					// Do nothing for heartbeat event
					break;

				default:
					// Handle unknown message types if needed
					break;
			}
		}
		catch (Exception ex)
		{
			this.AddErrorLog(ex);
		}
	}

	private void OnGetAccountsByAccessTokenResponse(ProtoOAGetAccountListByAccessTokenRes msg)
	{
		foreach (var account in msg.CtidTraderAccount)
		{
			_accountId = (long)account.CtidTraderAccountId;
		}
	}

	private void OnAccountDisconnectEvent(ProtoOAAccountDisconnectEvent msg) { }
	private void OnAccountLogoutResponse(ProtoOAAccountLogoutRes msg) { }
	private void OnGetCtidProfileByTokenResponse(ProtoOAGetCtidProfileByTokenRes msg) { }
	private void OnVersionResponse(ProtoOAVersionRes msg) { }
	private void OnAccountsTokenInvalidatedEvent(ProtoOAAccountsTokenInvalidatedEvent msg) { }

	private async Task OnDisconnectResponse(ProtoOAClientDisconnectEvent msg)
	{
		await SendOutDisconnectMessageAsync(new InvalidOperationException(msg.Reason), CancellationToken.None);
	}

	private async Task OnAccAuthResponse(ProtoOAAccountAuthRes msg)
	{
		await SendOutMessageAsync(new ConnectMessage(), CancellationToken.None);
	}

	private void OnAppAuthResponse(ProtoOAApplicationAuthRes msg)
	{
	}

	private void OnRefreshTokenResponse(ProtoOARefreshTokenRes msg)
	{
		//_token = new()
		//{
		//	AccessToken = msg.AccessToken,
		//	RefreshToken = msg.RefreshToken,
		//	ExpiresIn = msg.ExpiresIn.FromUnix(false),
		//	TokenType = msg.TokenType,
		//};
	}
}
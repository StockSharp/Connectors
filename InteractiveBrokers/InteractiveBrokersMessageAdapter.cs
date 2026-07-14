namespace StockSharp.InteractiveBrokers;

using System.Diagnostics;
using System.Net;

[OrderCondition(typeof(InteractiveBrokersOrderCondition))]
partial class InteractiveBrokersMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _pfRequests = [];
	private readonly SynchronizedDictionary<long, MessageTypes> _messageRequests = [];

	private TimeZoneInfo _connectedTimeZone = TimeZoneInfo.Local;

	private IBSocket _socket;

	/// <summary>
	/// Initializes a new instance of the <see cref="InteractiveBrokersMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public InteractiveBrokersMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.PortfolioLookup);
		this.AddSupportedMessage(ExtendedMessageTypes.FinancialAdvise, true);
		this.AddSupportedMessage(MessageTypes.UserRequest, null);

		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
		this.AddSupportedMarketDataType(DataType.News);
		this.AddSupportedMarketDataType(ExtendedDataTypes.FundamentalReport);
		this.AddSupportedMarketDataType(ExtendedDataTypes.Histogram);
		this.AddSupportedMarketDataType(ExtendedDataTypes.OptionCalc);
		this.AddSupportedMarketDataType(ExtendedDataTypes.OptionParameters);
		this.AddSupportedMarketDataType(ExtendedDataTypes.Scanner);
		this.AddSupportedMarketDataType(ExtendedDataTypes.SoftDollarTier);
		this.AddSupportedMarketDataType(ExtendedDataTypes.WshMetaData);
		this.AddSupportedMarketDataType(ExtendedDataTypes.WshEventData);
	}

	///// <summary>
	///// Identify security in messages by native identifier <see cref="SecurityId.Native"/>.
	///// </summary>
	//public override bool IsNativeIdentifiers => true;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsAutoReplyOnTransactonalUnsubscription => true;

	/// <inheritdoc />
	public override bool ExtraSetup => true;

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Extensions.AllTimeFrames;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths => Messages.Extensions.AnyDepths;

	/// <inheritdoc />
	public override IEnumerable<Level1Fields> CandlesBuildFrom { get; } =
		[.. typeof(CandleDataTypes)
			.GetFields()
			.Select(f => (Level1Fields)f.GetValue(null))
			// TODO
			.Where(f => f >= 0)];

	private ValueTask OnProcessTimeShiftAsync(TimeSpan timeShift, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeMessage { ServerTime = DateTime.UtcNow + timeShift }, cancellationToken);
	}

	private IBSocket Session => _socket ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private const string _codeSeparator = "##";

	private static string GetSymbol(SecurityMessage secMsg)
	{
		var secCode = secMsg.SecurityId.SecurityCode;

		if (secCode.IsEmpty())
			return string.Empty;

		var values = secCode.SplitBySep(_codeSeparator, false);

		if (values.Length != 5)
			return secCode;

		return values[0];
	}

	private static string GetExchange(SecurityMessage secMsg)
	{
		var boardCode = secMsg.SecurityId.BoardCode;
		return boardCode.EqualsIgnoreCase(BoardCodes.IBKR) ? string.Empty : boardCode;
	}

	private static string GetPrimaryExchange(SecurityMessage secMsg)
	{
		var boardCode = secMsg.PrimaryId.BoardCode;
		return boardCode.EqualsIgnoreCase(BoardCodes.IBKR) ? string.Empty : boardCode;
	}

	private static string GetLocalSymbol(SecurityMessage secMsg)
	{
		return secMsg.PrimaryId.SecurityCode;
	}

	private static string GetSecurityCode(string symbol, string type, string currency, string localSymbol, string expiry)
	{
		return new[] { symbol, type, currency, localSymbol, expiry }.Join(_codeSeparator);
	}

	private static string GetBoardCode(string boardCode)
	{
		return boardCode.IsEmpty() ? BoardCodes.IBKR : boardCode;
	}

	private CurrencyTypes? ToCurrency(string currency) => currency.FromMicexCurrencyName(this.AddErrorLog);

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_socket != null)
		{
			try
			{
				_socket.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_socket = null;
		}

		_depths.Clear();
		_secIdByTradeIds.Clear();
		_pnlAccounts.Clear();
		_messageRequests.Clear();
		_requestSecIdMap.Clear();
		_pfRequests.Clear();
		_newsProviders.Clear();
		_newsProviders2.Clear();
		_histContracts.Clear();
		_realTimeSubscriptions.Clear();
		_mdRequests.Clear();
		_mdCancellingRequests.Clear();
		_orderCancelErrors.Clear();
		_orderRegErrors.Clear();
		_pfSubs.Clear();

		ConnectedTime = null;
		_connectedTimeZone = TimeZoneInfo.Local;

		_nativeOrderIds.Clear();
		_nextNativeOrderId = 0;

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_socket != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_socket = new IBSocket(this) { Parent = this };
		await _socket.ConnectAsync(Address, cancellationToken);

		if ((int)_socket.ServerVersion == -1)
		{
			// redirect logic
			var host = await _socket.ReadStringAsync(cancellationToken);
			var newAddr = host.To<EndPoint>();

			_socket.Dispose();

			_socket = new IBSocket(this) { Parent = this };
			await _socket.ConnectAsync(newAddr, cancellationToken);
		}

		if (_socket.ServerVersion >= ServerVersions.V20)
		{
			try
			{
				ConnectedTime = (await _socket.ReadStringAsync(cancellationToken)).ReadDateTime(this, out _connectedTimeZone);
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}
		}

		if (_socket.ServerVersion >= ServerVersions.V3)
		{
			if (_socket.ServerVersion < ServerVersions.Linking)
				_socket.Send(ClientId);
		}

		await StartApi(cancellationToken);

		await TimeSpan.FromSeconds(1).Delay(cancellationToken);

		var signalTcs = AsyncHelper.CreateTaskCompletionSource<bool>();

		_ = StartListening(signalTcs, cancellationToken);

		// отправляется автоматически
		//await RequestIds(1, cancellationToken);

		try
		{
			await SetServerLogLevel(cancellationToken);
			await RequestMarketDataType(cancellationToken);
			await RequestNewsProviders(cancellationToken);
			await RequestFamilyCodes(cancellationToken);
			await RequestMarketDepthExchanges(cancellationToken);

			await RequestCurrentTime(cancellationToken);

			signalTcs.TrySetResult(true);
		}
		catch
		{
			signalTcs.TrySetResult(false);
			throw;
		}
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_socket == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_socket.Dispose();

		return default;
	}

	/// <inheritdoc />
	public override ValueTask SendInMessageAsync(Message message, CancellationToken cancellationToken)
	{
		return message.Type switch
		{
			ExtendedMessageTypes.FinancialAdvise => ProcessFinancialAdvise((FinancialAdviseMessage)message, cancellationToken),
			MessageTypes.UserRequest => RequestUserInfo((UserRequestMessage)message, cancellationToken),
			_ => base.SendInMessageAsync(message, cancellationToken),
		};
	}

	// TODO add more codes from http://interactivebrokers.github.io/tws-api/message_codes.html
	private enum NotifyCodes
	{
		OrderDuplicateId = 103,
		OrderFilled = 104,
		OrderNotMatchPrev = 105,
		OrderCannotTransmitId = 106,
		OrderCannotTransmitIncomplete = 107,
		OrderPriceOutOfRange = 109,
		OrderCannotTransmit = 132,
		OrderSubmitFailed = 133,
		HistServiceError = 162,
		SecurityNoDefinition = 200,
		Rejected = 201,
		OrderCancelled = 202,
		ErrorValidation = 321,
		OrderVolumeTooSmall = 481,
	}

	private Task StartListening(TaskCompletionSource<bool> signalTcs, CancellationToken cancellationToken)
	{
		return Task.Run(async () =>
		{
			try
			{
				// wait for start signal
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				cts.CancelAfter(TimeSpan.FromSeconds(30));

				bool startOk;

				try
				{
					startOk = await signalTcs.Task.WaitAsync(cts.Token).NoWait();
				}
				catch (OperationCanceledException)
				{
					// didn't get start signal in time
					await SendOutMessageAsync(new ConnectMessage { Error = new InvalidOperationException() }, cancellationToken);
					return;
				}

				if (!startOk)
				{
					await SendOutMessageAsync(new ConnectMessage { Error = new InvalidOperationException() }, cancellationToken);
					return;
				}

				await SendOutMessageAsync(new ConnectMessage(), cancellationToken);

				var socket = _socket;

				try
				{
					while (!socket.IsDisconnected)
					{
						if (UseV100Plus)
						{
							try
							{
								await socket.ReadBufferAsync(cancellationToken);
							}
							catch (Exception ex)
							{
								if (!socket.IsDisconnected)
								{
									await SendOutDisconnectMessageAsync(ex, cancellationToken);
									return;
								}

								break;
							}
						}

						string str;

						try
						{
							str = await socket.ReadStringAsync(cancellationToken);
						}
						catch
						{
							str = null;
						}

						if (str.IsEmpty())
						{
							if (socket?.IsDisposed == false)
								socket.AddErrorLog(LocalizedStrings.NoData2);

							break;
						}

						var message = (ResponseMessages)str.To<int>();

						socket.AddDebugLog("Msg: {0}", message);

						if (message == ResponseMessages.Error || !message.IsDefined())
							break;

						try
						{
							if (!await ProcessResponse(socket, message, cancellationToken))
								throw new InvalidOperationException(LocalizedStrings.UnsupportedType.Put(message));
						}
						catch (Exception ex)
						{
							await SendOutErrorAsync(ex, cancellationToken);
						}
					}
				}
				catch (Exception ex)
				{
					if (!socket.IsDisposed)
					{
						if (UseV100Plus)
							await SendOutErrorAsync(ex, cancellationToken);
						else
						{
							await SendOutDisconnectMessageAsync(ex, cancellationToken);
							return;
						}
					}
				}

				await SendOutMessageAsync(new DisconnectMessage(), cancellationToken);
			}
			catch (Exception ex)
			{
				try
				{
					if (!cancellationToken.IsCancellationRequested)
						await SendOutErrorAsync(ex, cancellationToken);
				}
				catch (Exception ex2)
				{
					Trace.WriteLine(ex2);
				}
			}
		}, cancellationToken);
	}

	private ValueTask StartApi(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.StartApi)
			.SendVersion(ServerVersions.V2)
			.Send(ClientId)
			.SendIfEqualOrMore(ServerVersions.OptionalCaps, s => s.Send(OptionalCapabilities))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Changes the TWS/GW log level.
	/// The default is <see cref="ServerLogLevels.Error"/>.
	/// </summary>
	private ValueTask SetServerLogLevel(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.SetServerLogLevel)
			.SendVersion(ServerVersions.V1)
			.Send((int)ServerLogLevel)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Returns the current system time on the server side.
	/// </summary>
	private ValueTask RequestCurrentTime(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestCurrentTime)
			.SendVersion(ServerVersions.V1)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Returns one next valid Id.
	/// </summary>
	/// <param name="numberOfIds">Has No Effect.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestIds(int numberOfIds, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestIds)
			.SendVersion(ServerVersions.V1)
			.Send(numberOfIds)
			.SendAsync(cancellationToken);
	}

	private ValueTask VerifyRequest(string apiName, string apiVersion, CancellationToken cancellationToken)
	{
		if (!ExtraAuth)
			return default;

		return Session
			.SendMessage(RequestMessages.VerifyRequest)
			.SendVersion(ServerVersions.V1)
			.Send(apiName)
			.Send(apiVersion)
			.SendAsync(cancellationToken);
	}

	private ValueTask VerifyMessage(string apiData, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.VerifyMessage)
			.SendVersion(ServerVersions.V1)
			.Send(apiData)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// For IB's internal purpose. Allows to provide means of verification between the TWS and third party programs.
	/// </summary>
	private ValueTask VerifyVerifyAndAuthRequest(string apiName, string apiVersion, string opaqueIsvKey, CancellationToken cancellationToken)
	{
		if (!ExtraAuth)
			return default;

		return Session
			.SendMessage(RequestMessages.VerifyAndAuthRequest)
			.SendVersion(ServerVersions.V1)
			.Send(apiName)
			.Send(apiVersion)
			.Send(opaqueIsvKey)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// For IB's internal purpose. Allows to provide means of verification between the TWS and third party programs.
	/// </summary>
	private ValueTask VerifyAndAuthMessage(string apiData, string xyzResponse, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.VerifyAndAuthMessage)
			.SendVersion(ServerVersions.V1)
			.Send(apiData)
			.Send(xyzResponse)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests all available Display Groups in TWS.
	/// </summary>
	/// <param name="requestId">The ID of this request.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask QueryDisplayGroups(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.QueryDisplayGroups)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Integrates API client and TWS window grouping.
	/// </summary>
	/// <param name="requestId">The Id chosen for this subscription request.</param>
	/// <param name="groupId">The display group for integration.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribeToGroupEvents(long requestId, int groupId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.SubscribeToGroupEvents)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.Send(groupId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Updates the contract displayed in a TWS Window Group.
	/// </summary>
	/// <param name="requestId">The ID chosen for this request.</param>
	/// <param name="contractInfo">Is an encoded value designating a unique IB contract. Possible values include:
	/// none = empty selection
	/// contractID@exchange - any non-combination contract.Examples 8314@SMART for IBM SMART; 8314@ARCA for IBM ARCA
	/// combo = if any combo is selected
	/// </param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask UpdateDisplayGroup(long requestId, string contractInfo, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UpdateDisplayGroup)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.Send(contractInfo)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Cancels a TWS Window Group subscription.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask UnSubscribeFromGroupEvents(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeFromGroupEvents)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}
}

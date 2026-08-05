namespace StockSharp.Fix;

using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using Ecng.Net;

#if !NO_LICENSE
#endif

public partial class FixMessageAdapter : MessageAdapter
{
	private readonly SubscriptionReplayTracker _subscriptionTracker = new();
	private TcpClient _client;
	private bool _isDisconnecting;
	private bool _isReconnecting;
	private bool _connectedOnce;
	private Task _readerTask;

	private async Task WaitReaderTaskAsync()
	{
		if (_readerTask == null)
			return;

		try
		{
			await _readerTask;
		}
		catch
		{
			// Ignore exceptions from reader task during cleanup
		}

		_readerTask = null;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="FixMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public FixMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Dialect = typeof(DefaultFixDialect);

		HeartbeatInterval = TimeSpan.FromSeconds(60);
	}

	/// <inheritdoc />
	public override MessageAdapterCategories Categories => FixDialect.Categories == default ? base.Categories : FixDialect.Categories;

	/// <inheritdoc />
	public override Type OrderConditionType => FixDialect.OrderConditionType;

	/// <inheritdoc />
	public override bool IsNativeIdentifiers => FixDialect.IsNativeIdentifiers;

	/// <inheritdoc />
	public override bool IsNativeIdentifiersPersistable => FixDialect.IsNativeIdentifiersPersistable;

	/// <inheritdoc />
	public override string StorageName => FixDialect.StorageName;

	/// <inheritdoc />
	public override IEnumerable<(string, Type)> SecurityExtendedFields => FixDialect.SecurityExtendedFields ?? [];

	/// <inheritdoc />
	public override IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
		=> FixDialect.GetSupportedMarketDataTypesAsync(securityId, from, to);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => FixDialect.IsSupportCandlesUpdates(subscription);

	/// <inheritdoc />
	public override bool IsSupportCandlesPriceLevels(MarketDataMessage subscription) => FixDialect.IsSupportCandlesPriceLevels(subscription);

	/// <inheritdoc />
	public override bool CheckTimeFrameByRequest => FixDialect.CheckTimeFrameByRequest;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => FixDialect.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages
		=>
			FixDialect
				.PossibleSupportedMessages
				//.Concat(!IsResetCounter ? new[] { FixMessageTypes.ConnectAttempt.ToInfo() } : [])
				.Concat(!IsResetCounter ? [MessageTypes.OrderStatus.ToInfo()] : [])
				.Distinct();

	/// <inheritdoc />
	public override IEnumerable<MessageTypes> SupportedInMessages
	{
		get => FixDialect.SupportedInMessages;
		set => FixDialect.SupportedInMessages = base.SupportedInMessages = value;
	}

	/// <inheritdoc />
	public override IEnumerable<MessageTypes> NotSupportedResultMessages => FixDialect.NotSupportedResultMessages;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths => FixDialect.SupportedOrderBookDepths;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => FixDialect.IsSupportOrderBookIncrements;

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => FixDialect.IsSupportExecutionsPnL;

	/// <inheritdoc />
	public override bool IsSecurityNewsOnly => FixDialect.IsSecurityNewsOnly;

	/// <inheritdoc />
	public override bool IsSecurityRequired(DataType dataType) => FixDialect.IsSecurityRequired(dataType);

	/// <inheritdoc />
	public override Uri Icon => FixDialect.Icon ?? base.Icon;

	/// <inheritdoc />
	public override bool IsAutoReplyOnTransactonalUnsubscription => FixDialect.IsAutoReplyOnTransactonalUnsubscription;

	/// <inheritdoc />
	public override bool IsFullCandlesOnly => FixDialect.IsFullCandlesOnly;

	/// <inheritdoc />
	public override bool IsSupportSubscriptions => FixDialect.IsSupportSubscriptions;

	/// <inheritdoc />
	public override IEnumerable<Level1Fields> CandlesBuildFrom => FixDialect.CandlesBuildFrom;

	/// <inheritdoc />
	public override bool EnqueueSubscriptions
	{
		get => FixDialect.EnqueueSubscriptions;
		set => FixDialect.EnqueueSubscriptions = value;
	}

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => FixDialect.IsSupportTransactionLog;

	/// <inheritdoc />
	public override IOrderLogMarketDepthBuilder CreateOrderLogMarketDepthBuilder(SecurityId securityId)
		=> FixDialect.CreateOrderLogMarketDepthBuilder(securityId);

	/// <inheritdoc />
	public override TimeSpan IterationInterval => FixDialect.IterationInterval;

	/// <inheritdoc />
	public override string FeatureName => FixDialect.FeatureName;

	/// <inheritdoc />
	public override bool? IsPositionsEmulationRequired => FixDialect.IsPositionsEmulationRequired;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => FixDialect.IsReplaceCommandEditCurrent;

	/// <inheritdoc />
	public override string[] AssociatedBoards => FixDialect.AssociatedBoards;

	/// <inheritdoc />
	public override bool ExtraSetup => FixDialect.ExtraSetup;

	/// <inheritdoc />
	public override async ValueTask SendInMessageAsync(Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case MessageTypes.Reset:
			{
				var dialect = FixDialect;

				_isDisconnecting = true;

				if (_client != null || _isReconnecting)
				{
					try
					{
						await dialect.SendInMessageAsync(new DisconnectMessage(), cancellationToken).NoWait();
					}
					catch (Exception ex)
					{
						await SendOutErrorAsync(ex, cancellationToken);
					}

					await WaitReaderTaskAsync();

					try
					{
						_client?.Close();
					}
					catch (Exception ex)
					{
						await SendOutErrorAsync(ex, cancellationToken);
					}

					_client = null;
				}

				_isReconnecting = false;
				_subscriptionTracker.Clear();

				try
				{
					await dialect.SendInMessageAsync(message, cancellationToken).NoWait();
				}
				catch (Exception ex)
				{
					await SendOutErrorAsync(ex, cancellationToken);
				}

				await SendOutMessageAsync(new ResetMessage(), cancellationToken);

				break;
			}
			case MessageTypes.Connect:
			{
				if (_client != null || _isReconnecting)
					throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

				_isDisconnecting = false;
				_connectedOnce = false;

				var logonMessage = (ConnectMessage)message;

				if (logonMessage.ClientVersion.IsEmpty())
					logonMessage.ClientVersion = ClientVersion;

				await SendLogonAsync(logonMessage, 0, IsResetCounter ? 1 : 2, cancellationToken).NoWait();
				break;
			}
			case MessageTypes.Disconnect:
			{
				_isDisconnecting = true;

				// Logout is a courtesy to a live connection. When the peer hung up first - or our own
				// reader gave up on the socket and closed it - the write lands on a disposed stream,
				// and that exception must not reach the caller: it asked for the connection to end,
				// and it already has. Reported out, as the reset path above does, so it stays visible
				// without failing the disconnect, and the local teardown below always runs.
				try
				{
					await FixDialect.SendInMessageAsync(message, cancellationToken).NoWait();
				}
				catch (Exception ex)
				{
					await SendOutErrorAsync(ex, cancellationToken);
				}

				await WaitReaderTaskAsync();

				await SendOutMessageAsync(new DisconnectMessage(), cancellationToken);

				break;
			}
			case MessageTypes.ChangePassword:
			{
				if (_client != null)
					throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

				_isDisconnecting = false;

				await SendLogonAsync(message, 0, IsResetCounter ? 1 : 2, cancellationToken);
				break;
			}
			case FixMessageTypes.ConnectAttempt:
			{
				var attemptMsg = (FixConnectAttemptMessage)message;

				var logonMessage = new ConnectMessage();

				if (logonMessage.ClientVersion.IsEmpty())
					logonMessage.ClientVersion = ClientVersion;

				await SendLogonAsync(logonMessage, attemptMsg.ExpectedSeqNum - 1, 1, cancellationToken);
				break;
			}

			default:
				if (message is ISubscriptionMessage subMsg)
					_subscriptionTracker.Process(subMsg);

				await FixDialect.SendInMessageAsync(message, cancellationToken);
				break;
		}
	}

	private async ValueTask SendLogonAsync(Message logonMessage, long counter, int expectingLogonCount, CancellationToken cancellationToken, bool reconnect = false)
	{
		//if (counter <= 0)
		//	throw new ArgumentOutOfRangeException(nameof(counter), counter, LocalizedStrings.InvalidValue);

		var addrStr = Address.To<string>();

		_client = new TcpClient();

		this.AddInfoLog("Connect to {0}...", addrStr);

		try
		{
			await _client.ConnectAsync(Address, cancellationToken);
		}
		catch
		{
			try
			{
				_client.Dispose();
			}
			catch { }

			_client = null;
			throw;
		}

		this.AddInfoLog("Connect to {0} is OK.", addrStr);

		Stream stream = _client.GetStream();

		if (SslProtocol != SslProtocols.None)
		{
			this.AddInfoLog("Authentication...");

			var host = TargetHost;

			if (host.IsEmpty())
				host = Address.GetHost();

			stream = stream.ToSsl(SslProtocol, CheckCertificateRevocation, ValidateRemoteCertificates, host, SslCertificate, SslCertificatePassword, UserCertificateValidationCallback, UserCertificateSelectionCallback);

			this.AddInfoLog("Auth OK.");
		}

		stream.ReadTimeout = ReadTimeout == TimeSpan.Zero ? Timeout.Infinite : (int)ReadTimeout.TotalMilliseconds;
		stream.WriteTimeout = WriteTimeout == TimeSpan.Zero ? Timeout.Infinite : (int)WriteTimeout.TotalMilliseconds;

		var dialect = FixDialect;

		IFixReader reader;
		IFixWriter writer;

		reader = new TextFixReader(stream, dialect.Encoding);
		writer = new TextFixWriter(stream, dialect.Encoding);

		writer.IsDump = this.IsDump();
		reader.IsDump = this.IsDump();

		dialect.HeartbeatInterval = HeartbeatInterval;
		dialect.NewOutMessageAsync -= SendOutMessageAsync;
		dialect.Init(writer, reader, Address);
		dialect.NewOutMessageAsync += SendOutMessageAsync;

		dialect.CurrentCounter = counter;

		this.AddInfoLog("Sending logon to {0}...", addrStr);

		if (logonMessage is ConnectMessage or ChangePasswordMessage)
			await dialect.SendInMessageAsync(logonMessage, cancellationToken).NoWait();
		else
			throw new UnauthorizedAccessException();

		this.AddInfoLog("Logon sent to {0} OK.", addrStr);

		_readerTask = Task.Run(() => ProcessIncomingFixMessages(dialect, expectingLogonCount, reconnect, cancellationToken), cancellationToken);
	}

	private static X509Certificate UserCertificateSelectionCallback(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate remotecertificate, string[] acceptableIssuers)
	{
		// No certificate can be selected if we have no local certificates at all
		if (localCertificates == null || localCertificates.Count == 0)
			return null;

		// Otherwise we select the first available certificate as per msdn documentation
		// http://msdn.microsoft.com/en-us/library/system.net.security.localcertificateselectioncallback.aspx
		if (acceptableIssuers != null)
		{
			// Use the first certificate that is from an acceptable issuer.
			foreach (var certificate in localCertificates)
			{
				var issuer = certificate.Issuer;

				if (acceptableIssuers.Contains(issuer))
					return certificate;
			}
		}

		// Just use any certificate
		return localCertificates[0];
	}

	private static bool UserCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
	{
		return true;
	}

	private async Task ProcessIncomingFixMessages(IFixDialect dialect, int expectingLogonCount, bool reconnect, CancellationToken cancellationToken)
	{
		try
		{
			this.AddInfoLog("Start message processing.");

			var hasDisconnected = false;

			var reader = dialect.Reader;
			var isDumpable = reader.IsDump;

			var messages = new AllocationArray<Message>();

			while (!_isDisconnecting)
			{
				reader.ClearState();
				messages.Reset();

				try
				{
					await foreach (var msg in dialect.ReadAsync(cancellationToken))
						messages.Add(msg);
				}
				catch (Exception ex)
				{
					//this.AddErrorLog(ex);

					if (ex is OperationCanceledException)
						break;

					if (expectingLogonCount > 0)
						throw;

					if (ex is SocketException || ex is IOException)
					{
						if (_isDisconnecting)
							break;
						else
							throw;
					}
					else
						this.AddErrorLog(ex);
				}
				finally
				{
					if (isDumpable)
					{
						var dump = reader.FlushDump();

						if (!dump.IsEmpty())// && !dump.Contains("^35=W") && !dump.Contains("^35=d"))
							this.AddDebugLog(LocalizedStrings.SessionReceivedDetails, this, dump);
					}
				}

				foreach (var m in messages)
				{
					var message = m;

					//if (message.IsBack)
					//	message.Adapter = this;

					if (expectingLogonCount > 0)
					{
						if (message is not ConnectMessage connect)
							throw new InvalidOperationException(message.ToString());

						if (!connect.IsOk())
						{
							expectingLogonCount--;

							if (expectingLogonCount > 0)
							{
								var nextSeqNum = dialect.TryParseNextMsqSeqNum(connect.Error.Message);

								if (nextSeqNum != null)
								{
									this.AddInfoLog("Attempt connect with SeqNum {0}.", nextSeqNum.Value);

									try
									{
										_client.Close();
									}
									catch
									{
									}

									_client = null;

									await SendOutMessageAsync(new FixConnectAttemptMessage { ExpectedSeqNum = nextSeqNum.Value }.LoopBack(this), cancellationToken);
									return;
								}
							}

							await SendOutMessageAsync(connect, cancellationToken);
							return;
						}

						expectingLogonCount = 0;

						if (reconnect)
						{
							// Replay active subscriptions after reconnect (online-only, From=null)
							foreach (var sub in _subscriptionTracker.GetSubscriptionsForReplay())
								await dialect.SendInMessageAsync((Message)sub, cancellationToken);

							await SendOutConnectionStateAsync(ConnectionStates.Restored, cancellationToken);
							continue;
						}

						// First logon succeeded — enable internal reconnection for future disconnects
						_connectedOnce = true;
					}

					try
					{
						if (message is FixSeqResetMessage seqResetMsg && seqResetMsg.PossMissingApplMsg == true)
						{
							message = new FixResendRequestMessage
							{
								BeginSeqNo = seqResetMsg.SeqNum + 1,
							}.LoopBack(this);
						}

						await SendOutMessageAsync(message, cancellationToken);
					}
					catch (Exception ex)
					{
						this.AddErrorLog(ex);
					}

					if (_isDisconnecting && message.Type == MessageTypes.Disconnect)
						hasDisconnected = true;
				}
			}

			if (isDumpable)
			{
				var dump = reader.FlushDump();

				if (!dump.IsEmpty())
					this.AddDebugLog(LocalizedStrings.SessionReceivedDetails, this, dump);
			}

			if (!hasDisconnected && !cancellationToken.IsCancellationRequested)
			{
				await SendOutDisconnectMessageAsync(_isDisconnecting, cancellationToken);
			}

			try
			{
				_client?.Close();
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}

			_client = null;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			if (_isDisconnecting || cancellationToken.IsCancellationRequested || !_connectedOnce)
			{
				// First connect or explicit disconnect — report error to caller
				// (internal reconnect only after a successful connection was established)
				await SendOutDisconnectMessageAsync(ex, cancellationToken);
				return;
			}

			// Unexpected disconnect — try internal reconnection (like WebSocketClient)
			try { _client?.Close(); } catch { }
			_client = null;

			await TryReconnectLoopAsync(cancellationToken);
		}
	}

	private async Task TryReconnectLoopAsync(CancellationToken cancellationToken)
	{
		_isReconnecting = true;

		try
		{
			var attempts = ReConnectionSettings.ReAttemptCount;
			var interval = ReConnectionSettings.Interval;

			if (attempts == 0)
			{
				await SendOutConnectionStateAsync(ConnectionStates.Failed, cancellationToken);
				return;
			}

			await SendOutConnectionStateAsync(ConnectionStates.Reconnecting, cancellationToken);

			while (!cancellationToken.IsCancellationRequested && !_isDisconnecting)
			{
				try
				{
					await Task.Delay(interval, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				if (_isDisconnecting)
					break;

				if (attempts > 0)
					attempts--;

				try
				{
					this.AddInfoLog("Reconnecting, attempts left: {0}", attempts);

					// Reset dialect state (clear sequence counters, pending messages, etc.)
					try
					{
						await FixDialect.SendInMessageAsync(new ResetMessage(), cancellationToken);
					}
					catch { }

					var logonMessage = new ConnectMessage();

					if (logonMessage.ClientVersion.IsEmpty())
						logonMessage.ClientVersion = ClientVersion;

					// SendLogonAsync creates new TcpClient, connects, sends logon,
					// and starts new _readerTask. ConnectionRestoredMessage is sent
					// by the new reader when logon response arrives.
					await SendLogonAsync(logonMessage, 0, IsResetCounter ? 1 : 2, cancellationToken, reconnect: true);

					_isReconnecting = false;
					return; // new _readerTask is running
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception reconnectEx)
				{
					this.AddErrorLog("Reconnect failed: {0}", reconnectEx.Message);

					try { _client?.Close(); } catch { }
					_client = null;

					if (attempts == 0)
						break;
				}
			}

			await SendOutConnectionStateAsync(ConnectionStates.Failed, cancellationToken);
		}
		finally
		{
			_isReconnecting = false;
		}
	}
}

namespace StockSharp.IQFeed;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

using Ecng.ComponentModel;

using Nito.AsyncEx;

/// <summary>
/// The wrapper to work with the IQFeed using the TCP/IP protocol.
/// </summary>
abstract class IQFeed : BaseLogReceiver
{
	protected const string TimeFormatFull = "yyyy-MM-dd HH:mm:ss.ffffff";
	protected const string DateFormat = "yyyy-MM-dd";

	private Socket _socket;
	private Task _connectionTask;
	private bool _isConnected;
	private bool _isDisconnecting;
	private CancellationTokenSource _connectionCts;
	private CancellationTokenSource _pendingRequestsCts;
	private readonly SynchronizedSet<Task> _pendingTasks = [];

	internal bool IsDisconnecting => _isDisconnecting;

	protected IQFeedMessageAdapter Adapter => (IQFeedMessageAdapter)Parent;

	public event Func<Message, CancellationToken, ValueTask> NewMessage;

	protected IQFeed(IQFeedMessageAdapter parent, string name)
	{
		if (name.IsEmpty())
			throw new ArgumentNullException(nameof(name));

		// ReSharper disable once VirtualMemberCallInConstructor
		Name = name;
		Parent = parent ?? throw new ArgumentNullException(nameof(parent));
	}

	protected override void DisposeManaged()
	{
		DisposeSocket();
		base.DisposeManaged();
	}

	protected async ValueTask RaiseNewMessageAsync(Message m, Message origMsg, CancellationToken cancellationToken)
	{
		if(m is IOriginalTransactionIdMessage otm && origMsg is ITransactionIdMessage tm)
			otm.OriginalTransactionId = tm.TransactionId;

		try
		{
			var evt = NewMessage;
			if (evt is not null)
				await evt(m, cancellationToken);
		}
		catch (Exception e)
		{
			this.AddErrorLog(e);
		}
	}

	protected abstract EndPoint GetAddress();

	protected static string GetSymbol(SecurityId securityId)
		=> securityId.SecurityCode.EnsureProtocolField(nameof(securityId.SecurityCode));

	private readonly IncrementalIdGenerator _requestIdGenerator = new();
	private long GetNextRequestId() => _requestIdGenerator.GetNextId();

	private readonly SynchronizedList<FeedRequest> _allHandlers = [];
	private long _receviedLineNo;

	private void AddMessageHandler(FeedRequest handler)
	{
		_allHandlers.Add(handler);
	}

	private void RemoveMessageHandler(FeedRequest handler)
	{
		_allHandlers.Remove(handler);
	}

	private void EnqueueResponseLines(IEnumerable<string> data, CancellationToken token)
	{
		try
		{
			foreach (var line in data)
			{
				++_receviedLineNo;

				token.ThrowIfCancellationRequested();
				this.AddVerboseLog("Response: {0}", line);

				var message = line.ParseResponseLine();

				do
				{
					FeedRequest handler;

					using (_allHandlers.EnterScope())
						handler = _allHandlers.FirstOrDefault(c => c.ProcessedLineNo < _receviedLineNo);

					if(handler == null)
						break;

					handler.ProcessedLineNo = _receviedLineNo;

					handler.TryEnqueue(message);
				}
				while(true);
			}
		}
		catch (Exception ex)
		{
			if (!token.IsCancellationRequested)
				this.AddErrorLog(ex);
		}
	}

	protected virtual Task OnBeforeConnect(CancellationToken token) => Task.CompletedTask;

	protected void RunSysHandler(CancellationToken token)
	{
		_ = Task.Run(async () =>
		{
			try
			{
				await SubscribeSystem(token).NoWait();
			}
			catch (Exception e)
			{
				if (!token.IsCancellationRequested)
					this.AddErrorLog("syshandler: {0}", e);
			}
		}, token);
	}

	private static void GetMessageLines(StringBuilder buf, List<string> outList)
	{
		outList.Clear();

		int lastN, start;
		var lastSym = lastN = start = -1;

		for (var i = 0; i < buf.Length; ++i)
		{
			var c = buf[i];

			switch (c)
			{
				case '\n':
				{
					if(lastSym >= start && start >= 0)
						outList.Add(buf.ToString(start, lastSym - start + 1));

					lastSym = start = -1;
					lastN = i;
					break;
				}
				case '\r':
					lastN = i;
					break;
				default:
					lastSym = i;
					if(start < 0)
						start = i;
					break;
			}
		}

		if(lastN >= 0)
			buf.Remove(0, lastN + 1);
	}

	protected bool EnsureConnected(bool isConnected, bool throwerr = true)
	{
		var actualConnected = _isConnected && !IsDisconnecting;

		if (actualConnected == isConnected)
			return true;

		var msg = actualConnected ? "already connected" : "not connected";
		this.AddWarningLog(msg);

		if(throwerr)
			throw new InvalidOperationException(msg);

		return false;
	}

	private async Task RunFeed(CancellationToken token)
	{
		EnsureConnected(true);

		var socket = _socket;

		var buf = new StringBuilder();
		var buffer = new byte[1500];
		var messageLines = new List<string>(32);

		while (true)
		{
			token.ThrowIfCancellationRequested();

			if(!socket.Poll(200000, SelectMode.SelectRead))
				continue;

			// http://stackoverflow.com/questions/2661764/how-to-check-if-a-socket-is-connected-disconnected-in-c
			if (socket.Available == 0)
				throw new InvalidOperationException($"{Name}: connection dropped.");

			var bytesRecv = await socket.ReceiveAsync(buffer, SocketFlags.None, token);

			token.ThrowIfCancellationRequested();

			// ReSharper disable once AssignNullToNotNullAttribute
			buf.Append(buffer.ASCII((uint)bytesRecv));

			GetMessageLines(buf, messageLines);

			EnqueueResponseLines(messageLines, token);
		}
	}

	private void DisposeSocket()
	{
		var s = _socket;
		if (s != null)
		{
			this.AddDebugLog("disposing socket");
			s.Close();
			_socket = null;
		}

		_connectionTask = null;
		_isConnected = false;
		var connectionCts = Interlocked.Exchange(ref _connectionCts, null);
		connectionCts?.Cancel();
		connectionCts?.Dispose();
		var pendingRequestsCts = Interlocked.Exchange(ref _pendingRequestsCts, null);
		pendingRequestsCts?.Cancel();
		pendingRequestsCts?.Dispose();
		_pendingTasks.Clear();
	}

	public async Task<Task> ConnectAsync(CancellationToken token)
	{
		LogInfo("Connect...");
		_isDisconnecting = false;

		await Task.Yield();

		EnsureConnected(false);

		var address = GetAddress();
		if(address == null)
		{
			this.AddWarningLog("no address for this feed, can't connect.");
			return null;
		}

		try
		{
			var (cts, childToken) = token.CreateChildToken();
			_connectionCts = cts;
			_pendingRequestsCts = new();

			await OnBeforeConnect(childToken);

			var socket = _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			LogInfo("connecting to '{0}'...", address);
			await socket.TryConnect(address, 5, TimeSpan.FromMilliseconds(500), this, childToken);
			LogInfo("socket is connected");

			_isConnected = true;
			var connTask = _connectionTask = RunAsync(childToken);
			try
			{
				await InitConnection(childToken);
			}
			catch (Exception)
			{
				// ReSharper disable once MethodSupportsCancellation
				_ = connTask.ObserveErrorAndLog();

				throw;
			}

			return _connectionTask;
		}
		catch (OperationCanceledException) { }
		catch (Exception)
		{
			DisposeSocket();
			throw;
		}

		return null;
	}

	public async Task DisconnectAsync()
	{
		if(!EnsureConnected(true, false))
			return;
		_isDisconnecting = true;

		Task[] pendingTasks;
		using (_pendingTasks.EnterScope())
		{
		_pendingRequestsCts?.Cancel();
			pendingTasks = _pendingTasks.CopyAndClear();
		}

		try
		{
			await pendingTasks.WhenAll().NoWait();
		}
		catch { }

		_connectionCts?.Cancel();

		try
		{
			await _connectionTask.NoWait();
		}
		catch { }
	}

	protected virtual async Task InitConnection(CancellationToken token)
	{
		EnsureConnected(true);

		RunSysHandler(token);

		await RequestSetProtocol(token);
		await RequestSetClientName(token);
	}

	private async Task RunAsync(CancellationToken token)
	{
		await Task.Yield();

		try
		{
			using var culture = Do.WithInvariantCulture();
			await RunFeed(token);
		}
		finally
		{
			DisposeSocket();
			LogInfo("feed processing has stopped!");
		}
	}

	private async Task RequestSetProtocol(CancellationToken token)
	{
		var version = Adapter.Version;

		var req = new FeedRequest(this, $"S,SET PROTOCOL,{version.Major}.{version.Minor}")
		{
			Filter = m => m.Type == IQFeedMessage.MsgType.System && m[0] == "CURRENT PROTOCOL"
					|| m.Type == IQFeedMessage.MsgType.Error
		};

		await req.GetOneAsync(token);
	}

	private async Task RequestSetClientName(CancellationToken token) =>
		await new FeedRequest(this, $"S,SET CLIENT NAME,S#.IQFeed({Name})").SendAsync(token);

	private async Task SubscribeSystem(CancellationToken token)
	{
		static bool filter(IQFeedMessage m) =>
			m.Type is IQFeedMessage.MsgType.System
				   or IQFeedMessage.MsgType.Error
				   or IQFeedMessage.MsgType.Unknown
				   or IQFeedMessage.MsgType.NotFound
				   or IQFeedMessage.MsgType.Time;

		var req = new FeedRequest(this) { Filter = filter };
		await foreach (var m in req.ExecuteAsync(token).WithEnforcedCancellation(token))
		{
			switch (m.Type)
			{
				case IQFeedMessage.MsgType.System:
					if(m[0].StartsWith("SYMBOL LIMIT REACHED") || m[0].StartsWith("INVALID PARAMETERS FOR"))
						this.AddErrorLog("error: '{0}'", m.Message);
					else if(m[0].StartsWith("SERVER DISCONNECTED") || m[0].StartsWith("REPLACED PREVIOUS WATCH") || m[0].StartsWith("SERVER RECONNECT FAILED"))
						this.AddWarningLog("error: '{0}'", m.Message);
					else if(m[0].StartsWith("CUST"))
						LogInfo("{0}", m.Message);

					await RaiseNewMessageAsync(new IQFeedSystemMessage(this, m[0]), null, token);
					break;

				case IQFeedMessage.MsgType.Error:
					this.AddErrorLog("error: {0}", m.Message);
					break;

				case IQFeedMessage.MsgType.Unknown:
					this.AddWarningLog("unknown message: {0}", m.Message);
					break;

				case IQFeedMessage.MsgType.NotFound:
					this.AddErrorLog("not found: {0}", m.Message);
					break;

				case IQFeedMessage.MsgType.Time:
					await RaiseNewMessageAsync(new TimeMessage { ServerTime = m[0].ToDateTime("yyyyMMdd HH:mm:ss").FromEst() }, null, token);
					break;
			}
		}
	}

	protected static NewsMessage ToNewsMessage(IQFeedMessage m)
	{
		var timeFormat = m[3].Contains(' ') ? "yyyyMMdd HHmmss" : "yyyyMMddHHmmss";
		var story = m[2]?.SplitByColon();

		return new NewsMessage
		{
			Source = m[0],
			Id = m[1],
			ServerTime = m[3].ToDateTime(timeFormat).FromEst(),
			Headline = m.RejoinFrom(4),

			// this is headline message so just symbol list here
			Story = story is null ? string.Empty : $"Related symbols: {story.JoinCommaSpace()}"
		};
	}

	private async ValueTask Unsubscribe(string command)
	{
		var req = new FeedRequest(this, command) { IsFeedCancelable = false };

		using var cts = Adapter.DisconnectTimeout.CreateTimeout();
		var token = cts.Token;

		try
		{
			await req.SendAsync(token).NoWait();
		}
		catch (Exception e)
		{
			if (!token.IsCancellationRequested)
				this.AddErrorLog("Unsubscribe '{0}':\n{1}", command, e);
		}
	}

	private class PendingRequest : IDisposable
	{
		private readonly IQFeed _parent;
		private readonly TaskCompletionSource<object> _tcs;

		public PendingRequest(IQFeed parent)
		{
			_parent = parent ?? throw new ArgumentNullException(nameof(parent));

			_tcs = new();
			_parent._pendingTasks.Add(_tcs.Task);
		}

		void IDisposable.Dispose()
		{
			_parent._pendingTasks.Remove(_tcs.Task);
			_tcs.TrySetResult(null);

			GC.SuppressFinalize(this);
		}
	}

	private PendingRequest AddPendingRequest() => new(this);

	protected class FeedRequest
	{
		private readonly IQFeed _feed;
		private readonly Func<string, string> _getRequest;
		private readonly bool _byId;

		public Func<string, string> GetUnsubscribeCmd { get; init; }
		public Func<IQFeedMessage, bool> Filter { get; init; }
		public Func<IQFeedMessage, bool> IsStopMessage { get; init; }
		public bool IsNoLog { get; init; }
		public bool IsFeedCancelable { get; init; } = true;

		private long _requestId = IQFeedMessage.InvalidRequestId;
		private long _maxNumResponses = -1;

		private readonly Channel<IQFeedMessage> _messageChannel;

		private bool _requestWasSent;
		private bool _isComplete;
		private long _numEnqueued;
		private long _numHandled;
		public string Request { get; private set; }

		DateTime _lastQSizeWarnTime;

		private long QueueSize => _numEnqueued - _numHandled;
		private bool NoResponseExpected => _maxNumResponses == 0;
		private bool SingleResponseExpected => _maxNumResponses == 1;

		public long ProcessedLineNo { get; set; }

		public FeedRequest(IQFeed feed, string cmd = null) : this(feed, _ => cmd) => _byId = false;

		public FeedRequest(IQFeed feed, Func<string, string> getRequest)
		{
			_feed = feed;
			_getRequest = getRequest ?? throw new ArgumentNullException(nameof(getRequest));
			_messageChannel = Channel.CreateUnbounded<IQFeedMessage>(new UnboundedChannelOptions
			{
				AllowSynchronousContinuations = true,
				SingleReader = true,
				SingleWriter = true,
			});

			_byId = true;
		}

		private bool CheckFilter(IQFeedMessage msg)
		{
			if(_requestId != IQFeedMessage.InvalidRequestId)
			{
				//if(msg.RequestId != IQFeedMessage.InvalidRequestId)
				return _requestId == msg.RequestId;
			}

			if(msg.RequestId != IQFeedMessage.InvalidRequestId)
				return false;

			return Filter?.Invoke(msg) == true;
		}

		private bool CheckIsStopMessage(IQFeedMessage msg)
		{
			if(_requestId != IQFeedMessage.InvalidRequestId)
			{
				if(msg.RequestId != _requestId)
					return false;

				return msg.Type.IsLastMessage();
			}

			return IsStopMessage?.Invoke(msg) ?? false;
		}

		public void TryEnqueue(IQFeedMessage message)
		{
			if(_isComplete || NoResponseExpected)
				return;
			if(!CheckFilter(message))
				return;

			if(SingleResponseExpected)
				_isComplete = true;

			var qsize = QueueSize;
			// _feed.AddVerboseLog($"{Request}: Q size = {qsize}");

			if (qsize > 1000)
			{
				var now = DateTime.UtcNow;

				if(now - _lastQSizeWarnTime > TimeSpan.FromSeconds(5))
				{
					_lastQSizeWarnTime = now;
					_feed.AddWarningLog("{0}: message queue size is {1}", Request, qsize);
				}
			}

			++_numEnqueued;
			if (!_messageChannel.Writer.TryWrite(message))
			{
				--_numEnqueued;
				_isComplete = true;

				var ex = new InvalidOperationException($"channel overflow for request '{Request}', current queue size = {QueueSize}");
				_messageChannel.Writer.TryComplete(ex);
				throw ex;
			}

			if (_isComplete)
				_messageChannel.Writer.TryComplete();
		}

		public async Task SendAsync(CancellationToken token)
		{
			_maxNumResponses = 0;

			await foreach (var _ in ExecuteAsync(token))
				return;
		}

		public ValueTask<IQFeedMessage> GetOneAsync(CancellationToken token)
		{
			_maxNumResponses = 1;
			return ExecuteAsync(token).FirstAsync(token);
		}

		public async IAsyncEnumerable<T> ExecuteAsync<T>(Func<IQFeedMessage, T> converter, [EnumeratorCancellation]CancellationToken token)
		{
			await foreach (var item in ExecuteAsync(token).WithEnforcedCancellation(token))
			{
				if (converter(item) is T v)
					yield return v;
			}
		}

		public async IAsyncEnumerable<IQFeedMessage> ExecuteAsync([EnumeratorCancellation] CancellationToken token = default)
		{
			if(Request != null)
				throw new InvalidOperationException($"this request was already started '{Request}'");

			var pendingRequestsToken = _feed._pendingRequestsCts?.Token
				?? throw new InvalidOperationException($"Connection '{_feed.Name}' is not initialized.");
			using var linkedCts = IsFeedCancelable
				? CancellationTokenSource.CreateLinkedTokenSource(pendingRequestsToken, token)
				: null;
			if (linkedCts != null)
				token = linkedCts.Token;

			if(_byId)
			{
				_requestId = _feed.GetNextRequestId();
				Request = _getRequest(_requestId.CreateRequestId());
			}
			else
			{
				Request = _getRequest(null);
			}

			Request ??= string.Empty;
			Request.EnsureProtocolLine(nameof(Request));

			if(!IsNoLog)
				_feed.AddVerboseLog("Request: '{0}'", Request);

			var socket = _feed._socket ?? throw new InvalidOperationException($"connection '{_feed.Name}' is not initialized");

			var bytes = (Request + "\r\n").ASCII();

			token.ThrowIfCancellationRequested();

			await using var activeRequest = new ActiveFeedRequest(this);

			if (!Request.IsEmptyOrWhiteSpace())
			{
				var bytesSent = 0;
				while (bytesSent < bytes.Length)
				{
					var sent = await socket.SendAsync(
						new ArraySegment<byte>(bytes, bytesSent, bytes.Length - bytesSent),
						SocketFlags.None, token);
					if (sent == 0)
						throw new EndOfStreamException("The IQFeed socket closed while sending a command.");
					bytesSent += sent;
				}
				_requestWasSent = true;

				token.ThrowIfCancellationRequested();
			}

			if(NoResponseExpected)
				yield break;

			IQFeedMessage msg;

			do
			{
				try
				{
					// _feed.AddVerboseLog($"{Request}: READ Q size = {QueueSize}");
					msg = await _messageChannel.Reader.ReadAsync(token);
				}
				catch (ChannelClosedException)
				{
					_isComplete = true;
					break;
				}

				if (msg.Type.IsErrorMessage())
					throw new InvalidOperationException(
						$"{(IsNoLog ? "Sensitive request" : Request)}: error response {msg.Type}: '{msg.Message}'");

				++_numHandled;

				var isStopMessage = CheckIsStopMessage(msg);

				_isComplete |= _maxNumResponses > 0 && _numHandled >= _maxNumResponses || isStopMessage;

				if (!isStopMessage)
					yield return msg;
			}
			while (!_isComplete);
		}

		private class ActiveFeedRequest : AsyncDisposable
		{
			private readonly FeedRequest _request;
			private readonly IDisposable _feedRegistration;

			public ActiveFeedRequest(FeedRequest req)
			{
				_request = req ?? throw new ArgumentNullException(nameof(req));

				if(req.IsFeedCancelable)
					_feedRegistration = req._feed.AddPendingRequest();

				req._feed.AddMessageHandler(req);
			}

			protected override async ValueTask DisposeManaged()
			{
				try
				{
					_request._isComplete = true;

					if (_request._requestWasSent)
					{
						var cmd = _request.GetUnsubscribeCmd;

						if (cmd is null)
							return;

						await _request._feed.Unsubscribe(cmd(_request._byId ? _request._requestId.CreateRequestId() : null));
					}
				}
				finally
				{
					_request._feed.RemoveMessageHandler(_request);
					_feedRegistration?.Dispose();
				}
			}
		}
	}
}

class IQFeedAdmin : IQFeed
{
	private readonly string _productId;
	private readonly string _username;
	private readonly SecureString _password;

	public IQFeedAdmin(IQFeedMessageAdapter parent) : base(parent, "Admin")
	{
		_productId = Adapter.ProductId?.Trim();
		_username  = Adapter.Login;
		_password  = Adapter.Password;

		if(_productId.IsEmptyOrWhiteSpace() || _username.IsEmptyOrWhiteSpace() || _password.IsEmpty())
			throw new InvalidOperationException("product id or username or password is empty");

		_productId.EnsureProtocolField(nameof(Adapter.ProductId));
		_username.EnsureProtocolField(nameof(Adapter.Login));
	}

	public async Task VerifyConnection(CancellationToken token)
	{
		static bool filter(IQFeedMessage m) => m.Type is IQFeedMessage.MsgType.SystemStats;

		var req = new FeedRequest(this) { Filter = filter };
		var counter = 0;

		await foreach (var m in req.ExecuteAsync(token).WithEnforcedCancellation(token))
		{
			++counter;

			EnsureConnected(true);

			if (m[10] != "Connected")
			{
				this.AddWarningLog("feed is not connected (state={0})", m[10]);

				if(counter > 15)
					throw new InvalidOperationException("failed waiting the feed to connect");
			}
			else if (counter > 3)
			{
				return;
			}
		}
	}

	protected override EndPoint GetAddress()
	{
		return Adapter.AdminAddress ?? throw new InvalidOperationException("admin address is not set");
	}

	private async Task RequestConnect(CancellationToken token)
		=> await new FeedRequest(this, "S,CONNECT").SendAsync(token);

	private async Task RequestRegisterClientApp(CancellationToken token)
	{
		var asm = typeof(IQFeedMessageAdapter).Assembly;
		var version = asm.GetAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

		var req = new FeedRequest(this, $"S,REGISTER CLIENT APP,{_productId},{version}")
		{
			Filter = m => m.Type == IQFeedMessage.MsgType.System && m[0] == "REGISTER CLIENT APP COMPLETED"
					|| m.Type == IQFeedMessage.MsgType.Error
		};

		await req.GetOneAsync(token);
	}

	private async Task RequestSetLoginId(CancellationToken token)
	{
		var req = new FeedRequest(this, $"S,SET LOGINID,{_username}")
		{
			Filter = m => m.Type == IQFeedMessage.MsgType.System && m[0] == "CURRENT LOGINID"
					|| m.Type == IQFeedMessage.MsgType.Error
		};

		await req.GetOneAsync(token);
	}

	private async Task RequestSetPassword(CancellationToken token)
	{
		this.AddVerboseLog("RequestSetPassword: S,SET PASSWORD,******");
		var password = _password.UnSecure().EnsureProtocolField(nameof(Adapter.Password));

		var req = new FeedRequest(this, $"S,SET PASSWORD,{password}")
		{
			Filter = m => m.Type == IQFeedMessage.MsgType.System && m[0] == "CURRENT PASSWORD"
					|| m.Type == IQFeedMessage.MsgType.Error,
			IsNoLog = true
		};

		await req.GetOneAsync(token);
	}

	protected override async Task OnBeforeConnect(CancellationToken token)
	{
		await base.OnBeforeConnect(token);
		await RunIQConnect(token);
	}

	protected override async Task InitConnection(CancellationToken token)
	{
		await base.InitConnection(token);

		await RequestRegisterClientApp(token);
		await RequestSetLoginId(token);
		await RequestSetPassword(token);
		await RequestConnect(token);
	}
	private static readonly TimeSpan _sleep = TimeSpan.FromMilliseconds(200);

	private static bool IsIQConnectRunning()
	{
		var processes = Process.GetProcessesByName("IQConnect");
		try
		{
			return processes.Length > 0;
		}
		finally
		{
			foreach (var process in processes)
				process.Dispose();
		}
	}

	private static async Task RunIQConnect(CancellationToken cancellationToken)
	{
		if (IsIQConnectRunning())
			return;

		for (var j = 0; j < 3; ++j)
		{
			using var process = Process.Start(new ProcessStartInfo
			{
				FileName = "IQConnect.exe",
				UseShellExecute = true,
			});

			for (var i = 0; i < 15; ++i)
			{
				await _sleep.Delay(cancellationToken);

				if (IsIQConnectRunning())
					return;
			}
		}

		throw new InvalidOperationException("IQConnect process not found");
	}
}

class IQFeedLevel1(IQFeedMessageAdapter parent) : IQFeed(parent, "Level1")
{
	protected override EndPoint GetAddress() => Adapter.Level1Address;

	private IReadOnlyDictionary<IQFeedLevel1Column, int> SelectedColumns { get; set; }

	private async Task SetLevel1FieldSet(IEnumerable<IQFeedLevel1Column> fields, CancellationToken token)
	{
		var idx = 0;
		var dict = new Dictionary<IQFeedLevel1Column, int>();

		var fieldsList = fields.ToList();
		var hasTradeDate = fieldsList.Contains(IQFeedLevel1ColumnRegistry.Instance.LastDate);
		var hasTradeTime = fieldsList.Contains(IQFeedLevel1ColumnRegistry.Instance.LastTradeTime);

		if (hasTradeDate != hasTradeTime)
			fieldsList.Add(hasTradeDate ? IQFeedLevel1ColumnRegistry.Instance.LastTradeTime : IQFeedLevel1ColumnRegistry.Instance.LastDate);

		foreach(var f in fieldsList)
			dict[f] = idx++;

		SelectedColumns = dict;

		var req = new FeedRequest(this, $"S,SELECT UPDATE FIELDS,{fieldsList.Select(c => c.Name).JoinComma()}")
		{
			Filter = m => m.Type == IQFeedMessage.MsgType.System && m[0] == "CURRENT UPDATE FIELDNAMES"
					|| m.Type == IQFeedMessage.MsgType.Error
		};

		await req.GetOneAsync(token);
	}

	readonly SynchronizedDictionary<SecurityId, SymbolSubscription> _symbolSubscriptions = [];

	class SymbolSubscription(IQFeedLevel1 feed)
	{
		class ParentSub
		{
			public MarketDataMessage Message { get; init; }
			public TaskCompletionSource<object> Tcs { get; } = new();
		}

		readonly IQFeedLevel1 _feed = feed;
		readonly CancellationTokenSource _cts = new();
		private readonly Lock _lock = new();

		private ParentSub _subLevel1;
		private ParentSub _subTicks;

		private long _lastTradeId;

		bool IsEmpty => _subLevel1 == null && _subTicks == null;

		public async ValueTask RaiseAsync(Message msg, CancellationToken token)
		{
			var l1Sub = _subLevel1?.Message;
			var ticksSub = _subTicks?.Message;

			async ValueTask raise(Message m, MarketDataMessage sub)
			{
				if(sub != null)
					await _feed.RaiseNewMessageAsync(m, sub, token);
			}

			switch (msg)
			{
				case SecurityMessage:
					await raise(msg, l1Sub);
					await raise(msg, ticksSub);
					break;

				case Level1ChangeMessage l1:
				{
					await raise(msg, l1Sub);

					var lastTradePrice = l1.Changes.TryGetValue(Level1Fields.LastTradePrice);
					var lastTradeId = l1.Changes.TryGetValue(Level1Fields.LastTradeId) as long?;

					if (ticksSub != null && lastTradePrice != null && lastTradeId > _lastTradeId)
					{
						var tick = new ExecutionMessage
						{
							SecurityId = l1.SecurityId,
							TradePrice = (decimal)lastTradePrice,
							ServerTime = l1.ServerTime,
							DataTypeEx = DataType.Ticks,
							OriginSide = l1.Changes.TryGetValue(Level1Fields.LastTradeOrigin) as Sides?,
							TradeId = _lastTradeId = lastTradeId.Value
						};

						if(l1.Changes.TryGetValue(Level1Fields.LastTradeVolume) is decimal lastTradeVol)
							tick.TradeVolume = lastTradeVol;

						if(l1.Changes.TryGetValue(Level1Fields.OpenInterest) is decimal oi)
							tick.OpenInterest = oi;

						await raise(tick, ticksSub);
					}

					break;
				}

				default:
					_feed.AddWarningLog("l1 sub unhandled msg type: " + msg?.GetType().Name);
					break;
			}
		}

		ParentSub this[DataType t]
		{
			get
			{
				if (t == DataType.Level1)
					return _subLevel1;
				else if (t == DataType.Ticks)
					return _subTicks;
				else
					throw new ArgumentOutOfRangeException(nameof(t), t, LocalizedStrings.InvalidValue);
			}
			set
			{
				if (t == DataType.Level1)
					_subLevel1 = value;
				else if (t == DataType.Ticks)
					_subTicks = value;
				else
					throw new ArgumentOutOfRangeException(nameof(t), t, LocalizedStrings.InvalidValue);
			}
		}

		async Task RunSub(SecurityId secId)
		{
			var isException = false;
			_feed.AddVerboseLog($"symsub {secId} Run");

			try
			{
				await _feed.SubscribeSymbol(this, secId, _cts.Token);
			}
			catch (OperationCanceledException)
			{
				isException = true;
				_feed.AddVerboseLog($"symsub {secId} canceled");
				using (_lock.EnterScope())
				{
					_subLevel1?.Tcs.TrySetCanceled();
					_subTicks?.Tcs.TrySetCanceled();
				}
			}
			catch (Exception e)
			{
				isException = true;
				_feed.AddVerboseLog($"symsub {secId} error {e.GetType().Name}, {e.Message}");
				using (_lock.EnterScope())
				{
					_subLevel1?.Tcs.TrySetException(e);
					_subTicks?.Tcs.TrySetException(e);
				}
			}
			finally
			{
				if(!isException)
					_feed.AddVerboseLog($"symsub {secId} complete");

				_feed._symbolSubscriptions.Remove(secId);

				using (_lock.EnterScope())
				{
					_subLevel1?.Tcs.TrySetResult(null);
					_subTicks?.Tcs.TrySetResult(null);
				}
			}
		}

		public Task RunOrAttach(MarketDataMessage mdMsg, CancellationToken token)
		{
			ParentSub parSub;

			_feed.AddVerboseLog($"symsub attach {mdMsg.SecurityId} {mdMsg.DataType2} tranid={mdMsg.TransactionId} regularonly={mdMsg.IsRegularTradingHours}");

			using (_lock.EnterScope())
			{
				if(this[mdMsg.DataType2] != null)
					throw new InvalidOperationException($"{mdMsg.DataType2} is already subscribed");

				var isNew = IsEmpty;
				this[mdMsg.DataType2] = parSub = new ParentSub { Message = mdMsg };

				if (isNew)
					_ = Task.Run(async () => await RunSub(mdMsg.SecurityId), _cts.Token);
			}

			token.Register(() =>
			{
				bool needToStopSubscription;

				_feed.AddVerboseLog($"symsub attach canceled {mdMsg.DataType2} tranid={mdMsg.TransactionId}");

				using (_lock.EnterScope())
				{
					this[parSub.Message.DataType2] = null;
					needToStopSubscription = IsEmpty;
				}

				try
				{
					if (needToStopSubscription)
						_cts.Cancel();
				}
				finally
				{
					parSub.Tcs.TrySetCanceled();
				}
			});

			return parSub.Tcs.Task;
		}
	}

	public async ValueTask SubscribeSymbol(MarketDataMessage mdMsg, CancellationToken token)
		=> await _symbolSubscriptions.SafeAdd(mdMsg.SecurityId, _ => new SymbolSubscription(this)).RunOrAttach(mdMsg, token);

	private async Task SubscribeSymbol(SymbolSubscription sub, SecurityId secId, CancellationToken token)
	{
		static bool filter(IQFeedMessage m) =>
			m.Type is IQFeedMessage.MsgType.L1Summary
				   or IQFeedMessage.MsgType.L1Update
				   or IQFeedMessage.MsgType.Fundamental
				   or IQFeedMessage.MsgType.End;

		var symbol = GetSymbol(secId);
		var req = new FeedRequest(this, $"w{symbol}")
		{
			Filter = filter,
			IsStopMessage = m => m.Type == IQFeedMessage.MsgType.NotFound && m[0] == symbol,
			GetUnsubscribeCmd = _ => $"r{symbol}",
		};

		var bidask = new BidAskState();

		await foreach (var m in req.ExecuteAsync(token).WithEnforcedCancellation(token))
		{
			switch (m.Type)
			{
				case IQFeedMessage.MsgType.Fundamental:
					foreach (var msg in ToSecurityMessages(m, secId))
						await sub.RaiseAsync(msg, token);
					break;

				case IQFeedMessage.MsgType.L1Summary:
				case IQFeedMessage.MsgType.L1Update:
					foreach (var msg in ToLevel1(m, bidask, secId))
						await sub.RaiseAsync(msg, token);
					break;

				case IQFeedMessage.MsgType.NotFound:
				case IQFeedMessage.MsgType.End:
				case IQFeedMessage.MsgType.NoData:
					break;

				default:
					this.AddWarningLog("{0}: unhandled message '{1}': '{2}'", req.Request, m.Type, m.Message);
					break;
			}
		}
	}

	public async ValueTask SubscribeNews(MarketDataMessage mdMsg, CancellationToken token)
	{
		static bool filter(IQFeedMessage m) => m.Type is IQFeedMessage.MsgType.News;

		var req = new FeedRequest(this, "S,NEWSON")
		{
			Filter = filter,
			GetUnsubscribeCmd = _ => "S,NEWSOFF",
		};

		await foreach (var m in req.ExecuteAsync(token).WithEnforcedCancellation(token))
		{
			switch (m.Type)
			{
				case IQFeedMessage.MsgType.News:
					await RaiseNewMessageAsync(ToNewsMessage(m), mdMsg, token);
					break;
				default:
					this.AddWarningLog("{0}: unhandled message '{1}': '{2}'", req.Request, m.Type, m.Message);
					break;
			}
		}
	}

	protected override async Task InitConnection(CancellationToken token)
	{
		await base.InitConnection(token);

		var a = Adapter;
		var columns = new[]
		{
			a.Level1ColumnRegistry.Symbol,
			a.Level1ColumnRegistry.ExchangeId,
			a.Level1ColumnRegistry.MessageContents,
		}
		.Concat(a.Level1Columns);

		await SetLevel1FieldSet(columns, token);
	}

	private IEnumerable<Message> ToSecurityMessages(IQFeedMessage m, SecurityId secId)
	{
		var symbol = m[0];
		if(symbol != secId.SecurityCode)
			yield break;

		var pe = m[2].To<decimal?>();
		var beta = m[20].To<decimal?>();
		var precision = m[30].To<int>();
		var sic = m[31].To<int?>();
		var historicalVolatility = m[32].To<decimal?>();
		var securityType = m[33].To<int>();
		var maturityDate = m[40].TryToDateTime("MM/dd/yyyy")?.UtcKind();
		var expirationDate = m[42].TryToDateTime("MM/dd/yyyy")?.UtcKind();
		var strikePrice = m[43].To<decimal?>();
		var naics = m[44].To<int?>();
		var exchangeRoot = m[45];

		var secMsg = new SecurityMessage
		{
			SecurityId = secId,
			Decimals = precision,
			Strike = strikePrice ?? 0,
			ExpiryDate = expirationDate ?? maturityDate,
			SecurityType = Adapter.FromNativeSecurityType(securityType),
		}.TryFillUnderlyingId(exchangeRoot);

		if (sic != null || naics != null)
			secMsg.Class = $"SIC={sic}&NAICS={naics}";

		yield return secMsg;

		yield return new Level1ChangeMessage
		{
			SecurityId = secId,
			ServerTime = CurrentTime
		}
		.TryAdd(Level1Fields.HistoricalVolatility, historicalVolatility)
		.TryAdd(Level1Fields.Beta, beta)
		.TryAdd(Level1Fields.PriceEarnings, pe);
	}

	class BidAskState
	{
		public decimal Bid { get; set; } = decimal.MinValue;
		public decimal Ask { get; set; } = decimal.MaxValue;

		public void Update(ref Level1UpdateContentFlags flags, Level1ChangeMessage msg)
		{
			if(!flags.Bid && !flags.Ask)
				return;

			var newBid = flags.Bid ? (decimal)msg.Changes[Level1Fields.BestBidPrice] : Bid;
			var newAsk = flags.Ask ? (decimal)msg.Changes[Level1Fields.BestAskPrice] : Ask;

			if (flags.Bid && newBid > Bid)
				Bid = newBid;
			else if(flags.Ask)
				Bid = Bid.Min(newAsk);

			if (flags.Ask && newAsk < Ask)
				Ask = newAsk;
			else if(flags.Bid)
				Ask = Ask.Max(newBid);
		}

		public Sides? InferOrigin(decimal tradePrice)
		{
			if(tradePrice <= Bid)
				return tradePrice >= Ask ? null : Sides.Sell;

			if(tradePrice >= Ask)
				return tradePrice <= Bid ? null : Sides.Buy;

			return null;
		}
	}

	private IEnumerable<Message> ToLevel1(IQFeedMessage m, BidAskState state, SecurityId secId)
	{
		string part(IQFeedLevel1Column c) => m[SelectedColumns[c]];

		var reg = Adapter.Level1ColumnRegistry;

		var secCode = part(reg.Symbol);
		if(secCode != secId.SecurityCode)
			yield break;

		var contentInfo    = new Level1UpdateContentFlags(part(reg.MessageContents));

		var msg = new Level1ChangeMessage
		{
			SecurityId = secId
		};

		object GetLastTradeTime() => IQFeedHelper.TryConvertToDateTime(reg.LastDate, part(reg.LastDate), reg.LastTradeTime, part(reg.LastTradeTime));
		object GetBidTime()       => IQFeedHelper.TryConvertToDateTime(null, null, reg.BidTime, part(reg.BidTime));
		object GetAskTime()       => IQFeedHelper.TryConvertToDateTime(null, null, reg.AskTime, part(reg.AskTime));

		object GetTradeOrigin() =>
			part(reg.MostRecentTradeAggressor)?.Trim() switch
			{
				"1" => Sides.Buy,
				"2" => Sides.Sell,
				_ => null
			};

		foreach(var kv in SelectedColumns)
		{
			Func<object> getVal = kv.Key.Field switch
			{
				Level1Fields.BestBidTime     => GetBidTime,
				Level1Fields.BestAskTime     => GetAskTime,
				Level1Fields.LastTradeTime   => GetLastTradeTime,
				Level1Fields.LastTradeOrigin => GetTradeOrigin,
				_                           => () => kv.Key.Convert(part(kv.Key))
			};

			kv.Key.TrySetMessageField(msg, getVal, ref contentInfo);
		}

		if(msg.HasChanges())
		{
			if (msg.ServerTime == default)
				msg.ServerTime = CurrentTime;

			if (SelectedColumns.ContainsKey(IQFeedLevel1ColumnRegistry.Instance.MostRecentTradeAggressor))
			{
				state.Update(ref contentInfo, msg);

				if (contentInfo.IsTradeInfo && !msg.Changes.ContainsKey(Level1Fields.LastTradeOrigin))
				{
					var lastTradePrice = msg.Changes.TryGetValue(Level1Fields.LastTradePrice);

					if (lastTradePrice != null)
					{
						var origin = state.InferOrigin((decimal)lastTradePrice);
						if(origin != null)
							msg.Changes.Add(Level1Fields.LastTradeOrigin, origin);
					}
				}
			}

			yield return msg;
		}
	}
}

class IQFeedLevel2(IQFeedMessageAdapter parent) : IQFeed(parent, "Level2")
{
	public const long ClearDepthId = int.MinValue;

	protected override EndPoint GetAddress() => Adapter.Level2Address;

	private struct MsgParseResult
	{
		public QuoteChangeActions Action;
		public Sides Side;
		public decimal? Price;
		public uint? Vol;
		public ulong? Priority;
		public DateTime ServerTime;
		public ulong? OrderId;
		public string MarketMakerId;
	}

	private class NasdaqLevel2Book(MarketDataMessage sub, Func<Message, CancellationToken, ValueTask> handler)
	{
		class OrderInfo
		{
			public string MarketMakerId { get; init; }
			public Sides Side { get; init; }
			public ulong? Priority { get; init; }

			public decimal Volume { get; set; }
			public decimal Price { get; set; }
		}

		readonly MarketDataMessage _subscription = sub;
		readonly Func<Message, CancellationToken, ValueTask> _msgHandler = handler;

		readonly Dictionary<string, List<OrderInfo>> _ordersByMmId = [];

		private readonly SortedList<decimal, List<OrderInfo>> _bids = new(new BackwardComparer<decimal>());
		private readonly SortedList<decimal, List<OrderInfo>> _asks = [];

		private async ValueTask RemoveOrdersAsync(string mmid, Sides side, CancellationToken cancellationToken)
		{
			var changes = new List<QuoteChange>();

			if(!_ordersByMmId.TryGetValue(mmid, out var orders))
				return;

			var ordersToRemove = orders.RemoveWhere(oi => oi.Side == side).ToArray();

			if(orders.Count == 0)
				_ordersByMmId.Remove(mmid);

			var levels = side == Sides.Buy ? _bids : _asks;

			foreach (var oi in ordersToRemove)
			{
				var priceOrders = levels[oi.Price];
				priceOrders.Remove(oi);
				if(priceOrders.Count == 0)
					levels.Remove(oi.Price);

				var newVolume = priceOrders.Sum(oi2 => oi2.Volume);
				changes.Add(new(oi.Price, newVolume));
			}

			var quotesMsg = new QuoteChangeMessage
			{
				SecurityId = _subscription.SecurityId,
				ServerTime = IQFeedHelper.CurrentTimeUtc,
				State = QuoteChangeStates.Increment,
			};

			if(side == Sides.Buy)
				quotesMsg.Bids = [.. changes];
			else
				quotesMsg.Asks = [.. changes];

			await _msgHandler(quotesMsg, cancellationToken);
		}

		// private void TryMatchAndRemove(decimal orderPrice, Sides orderSide, SortedList<decimal, List<OrderInfo>> oppositeLevels, List<QuoteChange> outChanges)
		// {
		// 	Func<decimal, bool> intersects = orderSide == Sides.Buy ?
		// 		price => price <= orderPrice :
		// 		price => price >= orderPrice;
		//
		// 	while(oppositeLevels.Count > 0)
		// 	{
		// 		var first = oppositeLevels.First();
		// 		var price = first.Key;
		// 		var orders = first.Value;
		//
		// 		if(!intersects(price))
		// 			break;
		//
		// 		outChanges.Add(new QuoteChange(price, 0));
		//
		// 		foreach (var o in orders)
		// 			_ordersByMmId[o.MarketMakerId].Remove(o);
		//
		// 		oppositeLevels.RemoveAt(0);
		// 	}
		// }

		private async ValueTask SetOrderAsync(MsgParseResult ordInfo, CancellationToken cancellationToken)
		{
			if(ordInfo.Price == null || ordInfo.Vol == null)
				throw new InvalidOperationException($"SetOrder: invalid order. mmid={ordInfo.MarketMakerId}, action={ordInfo.Action}, vol={ordInfo.Vol}, price={ordInfo.Price}, time={ordInfo.ServerTime}, prio={ordInfo.Priority}");

			var bidchanges = new List<QuoteChange>();
			var askchanges = new List<QuoteChange>();
			SortedList<decimal, List<OrderInfo>> levels; //, oppositeLevels;
			List<QuoteChange> changes; //, oppositeChanges;

			if (ordInfo.Side == Sides.Buy)
			{
				levels = _bids;
				// oppositeLevels = _asks;
				changes = bidchanges;
				// oppositeChanges = askchanges;
			}
			else
			{
				levels = _asks;
				// oppositeLevels = _bids;
				changes = askchanges;
				// oppositeChanges = bidchanges;
			}

			if(!_ordersByMmId.TryGetValue(ordInfo.MarketMakerId, out var mmOrders))
				_ordersByMmId[ordInfo.MarketMakerId] = mmOrders = [];

			var oi = mmOrders.FirstOrDefault(i => i.Side == ordInfo.Side && i.Priority == ordInfo.Priority);

			List<OrderInfo> ensureGetLevel(decimal price)
			{
				if(!levels.TryGetValue(price, out var po))
					levels[price] = po = [];
				return po;
			}

			var oldLevel = oi == null ? null : ensureGetLevel(oi.Price);
			var newLevel = ensureGetLevel(ordInfo.Price.Value);

			if (oldLevel != newLevel && oldLevel != null)
			{
				oldLevel.Remove(oi);
				var newSum = oldLevel.Sum(oi2 => oi2.Volume);

				changes.Add(new(oi.Price, newSum));

				if(newSum == 0)
					levels.Remove(oi.Price);

				oi.Price = ordInfo.Price.Value;
				newLevel.Add(oi);
			}

			if(oi == null)
			{
				oi = new()
				{
					MarketMakerId = ordInfo.MarketMakerId,
					Side = ordInfo.Side,
					Priority = ordInfo.Priority,
					Price = ordInfo.Price.Value,
					Volume = ordInfo.Vol.Value
				};

				mmOrders.Add(oi);
				// ReSharper disable once PossibleNullReferenceException
				newLevel.Add(oi);
			}
			else
			{
				oi.Volume = ordInfo.Vol.Value;
			}

			var newVolume = newLevel.Sum(oi2 => oi2.Volume);
			changes.Add(new(oi.Price, newVolume));

			//TryMatchAndRemove(oi.Price, oi.Side, oppositeLevels, oppositeChanges);

			var quotesMsg = new QuoteChangeMessage
			{
				SecurityId = _subscription.SecurityId,
				ServerTime = ordInfo.ServerTime,
				State = QuoteChangeStates.Increment,
			};

			if (bidchanges.Any())
				quotesMsg.Bids = [.. bidchanges];

			if (askchanges.Any())
				quotesMsg.Asks = [.. askchanges];

			await _msgHandler(quotesMsg, cancellationToken);
		}

		public async ValueTask ApplyAsync(IQFeedMessage msg, CancellationToken cancellationToken)
		{
			var parsed = ParseData(msg);

			switch (msg.Type)
			{
				case IQFeedMessage.MsgType.L2OrderDelete:
					await RemoveOrdersAsync(parsed.MarketMakerId, parsed.Side, cancellationToken);
					break;
				case IQFeedMessage.MsgType.L2PriceLevel:
				case IQFeedMessage.MsgType.L2OrderAdd:
				case IQFeedMessage.MsgType.L2OrderUpdate:
				case IQFeedMessage.MsgType.L2OrderSummary:
					await SetOrderAsync(parsed, cancellationToken);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(msg), msg.Type, LocalizedStrings.InvalidValue);
			}
		}
	}

	private async ValueTask SendOrderLogClearAsync(MarketDataMessage mdMsg, Sides side, CancellationToken cancellationToken)
	{
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.OrderLog,
			SecurityId = mdMsg.SecurityId,
			ServerTime = CurrentTime,
			Side = side,
			OrderId = ClearDepthId,
			OrderVolume = 0,
			OrderState = OrderStates.Done,
		};

		await RaiseNewMessageAsync(message, mdMsg, cancellationToken);
	}

	private async ValueTask SendOrderLogAsync(MarketDataMessage mdMsg, IQFeedMessage msg, CancellationToken cancellationToken)
	{
		var parsed = ParseData(msg);

		if(parsed.OrderId == null)
			throw new InvalidOperationException($"no order id in message '{msg.Message}'");

		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.OrderLog,
			SecurityId = mdMsg.SecurityId,
			ServerTime = parsed.ServerTime,
			OrderId = (long)parsed.OrderId.Value,
			OrderPrice = parsed.Price ?? 0m,
			OrderVolume = parsed.Vol,
			OrderState = parsed.Action == QuoteChangeActions.Delete ? OrderStates.Done : OrderStates.Active,
			Side = parsed.Side,
		};

		await RaiseNewMessageAsync(message, mdMsg, cancellationToken);
	}

	static bool FilterMbo(IQFeedMessage m, string code)
	{
		if (m.Type is not (
			   IQFeedMessage.MsgType.L2PriceLevel
			or IQFeedMessage.MsgType.L2OrderAdd
			or IQFeedMessage.MsgType.L2OrderUpdate
			or IQFeedMessage.MsgType.L2OrderDelete
			or IQFeedMessage.MsgType.L2OrderSummary
			or IQFeedMessage.MsgType.NotFound
			or IQFeedMessage.MsgType.NoDepthAvailableYet
			or IQFeedMessage.MsgType.SystemClearDepth
		))
		{
			return false;
		}

		return m[0] == code;
	}

	public async ValueTask SubscribeMbo(MarketDataMessage mdMsg, CancellationToken token)
	{
		var symbol = GetSymbol(mdMsg.SecurityId);
		var req = new FeedRequest(this, $"WOR,{symbol}")
		{
			Filter = m => FilterMbo(m, symbol),
			GetUnsubscribeCmd = _ => $"ROR,{symbol}",
		};

		bool? streamIsLevel2 = null;
		NasdaqLevel2Book book = null;

		await foreach (var m in req.ExecuteAsync(token).WithEnforcedCancellation(token))
		{
			switch (m.Type)
			{
				case IQFeedMessage.MsgType.NotFound:
					this.AddWarningLog("(MBO)not found: {0}", m.Message);
					break;
				case IQFeedMessage.MsgType.NoDepthAvailableYet:
					this.AddWarningLog("(MBO)no mdepth yet: {0}", m.Message);
					break;

				case IQFeedMessage.MsgType.L2OrderAdd:
				case IQFeedMessage.MsgType.L2OrderUpdate:
				case IQFeedMessage.MsgType.L2OrderDelete:
				case IQFeedMessage.MsgType.L2OrderSummary:
					var msgIsLevel2 = m[1].IsEmptyOrWhiteSpace(); // m[1] is order id. empty for nasdaq level2, non-empty for CME order log

					if (streamIsLevel2 == null)
					{
						streamIsLevel2 = msgIsLevel2;

						if (msgIsLevel2)
							await SendQuoteChangeClearAsync(mdMsg, CurrentTime, token);
					}

					if(msgIsLevel2 != streamIsLevel2)
						throw new InvalidOperationException($"unexpected WOR message type. expected level2={streamIsLevel2}");

					if (msgIsLevel2)
					{
						book ??= new NasdaqLevel2Book(mdMsg, (m2, ct) => RaiseNewMessageAsync(m2, mdMsg, ct));
						await book.ApplyAsync(m, token);
					}
					else
					{
						await SendOrderLogAsync(mdMsg, m, token);
					}

					break;

				case IQFeedMessage.MsgType.SystemClearDepth:
					var side = m[1] == "B" ? Sides.Buy : Sides.Sell;
					await SendOrderLogClearAsync(mdMsg, side, token);
					break;

				default:
					this.AddWarningLog("{0}: unhandled message '{1}': '{2}'", req.Request, m.Type, m.Message);
					break;
			}
		}
	}

	private async ValueTask SendQuoteChangeClearAsync(MarketDataMessage mdMsg, DateTime serverTime, CancellationToken cancellationToken)
	{
		var message = new QuoteChangeMessage
		{
			SecurityId = mdMsg.SecurityId,
			ServerTime = serverTime,
			Bids = [],
			Asks = [],
			State = QuoteChangeStates.SnapshotComplete,
		};

		await RaiseNewMessageAsync(message, mdMsg, cancellationToken);
	}

	private async ValueTask<bool> SendQuoteChangeAsync(MarketDataMessage mdMsg, IQFeedMessage msg, bool snapshotSent, CancellationToken cancellationToken)
	{
		var parsed = ParseData(msg);

		if(parsed.Price == null)
			throw new InvalidOperationException($"MBP message without price: '{msg.Message}'");

		if (!snapshotSent)
		{
			snapshotSent = true;
			await SendQuoteChangeClearAsync(mdMsg, parsed.ServerTime, cancellationToken);
		}

		var change = parsed.Action switch
		{
			QuoteChangeActions.New or QuoteChangeActions.Update => new QuoteChange(parsed.Price.Value, parsed.Vol ?? throw new InvalidOperationException($"MBP '{parsed.Action}' message without volume: '{msg.Message}'")),
			QuoteChangeActions.Delete => new QuoteChange(parsed.Price.Value, 0),
			_ => throw new InvalidOperationException(parsed.Action.To<string>()),
		};

		var message = new QuoteChangeMessage
		{
			SecurityId = mdMsg.SecurityId,
			ServerTime = parsed.ServerTime,
			//OriginalTransactionId = mdMsg.TransactionId,
			State = QuoteChangeStates.Increment,
		};

		if (parsed.Side == Sides.Buy)
			message.Bids = [change];
		else
			message.Asks = [change];

		await RaiseNewMessageAsync(message, mdMsg, cancellationToken);

		return snapshotSent;
	}

	static bool FilterMbp(IQFeedMessage m, string code)
	{
		if (m.Type is not (
			   IQFeedMessage.MsgType.L2PriceLevel
			or IQFeedMessage.MsgType.L2PriceLevelSummary
			or IQFeedMessage.MsgType.L2PriceLevelUpdate
			or IQFeedMessage.MsgType.L2PriceLevelDelete
			or IQFeedMessage.MsgType.End
			or IQFeedMessage.MsgType.NotFound
			or IQFeedMessage.MsgType.NoDepthAvailableYet
			or IQFeedMessage.MsgType.SystemClearDepth
		))
		{
			return false;
		}

		return m[0] == code;
	}

	public async ValueTask SubscribeMbp(MarketDataMessage mdMsg, CancellationToken token)
	{
		var snapshotSent = false;
		var maxDepth = mdMsg.MaxDepth == null ? (int?)null : 1.Max(mdMsg.MaxDepth.Value.Min(40));
		var symbol = GetSymbol(mdMsg.SecurityId);

		var req = new FeedRequest(this, $"WPL,{symbol},{maxDepth}")
		{
			Filter = m => FilterMbp(m, symbol),
			GetUnsubscribeCmd = _ => $"RPL,{symbol}",
		};

		await foreach (var m in req.ExecuteAsync(token).WithEnforcedCancellation(token))
		{
			switch (m.Type)
			{
				case IQFeedMessage.MsgType.NotFound:
					this.AddWarningLog("(MBP)not found: {0}", m.Message);
					break;
				case IQFeedMessage.MsgType.NoDepthAvailableYet:
					this.AddWarningLog("(MBP)no mdepth yet: {0}", m.Message);
					break;

				case IQFeedMessage.MsgType.L2PriceLevel:
				case IQFeedMessage.MsgType.L2PriceLevelSummary:
				case IQFeedMessage.MsgType.L2PriceLevelUpdate:
				case IQFeedMessage.MsgType.L2PriceLevelDelete:
					snapshotSent = await SendQuoteChangeAsync(mdMsg, m, snapshotSent, token);
					break;

				case IQFeedMessage.MsgType.SystemClearDepth:
					await SendQuoteChangeClearAsync(mdMsg, CurrentTime, token);
					break;

				default:
					this.AddWarningLog("{0}: unhandled message '{1}': '{2}'", req.Request, m.Type, m.Message);
					break;
			}
		}
	}

	private static MsgParseResult ParseData(IQFeedMessage m)
	{
		string date, time;
		var result = new MsgParseResult();

		static Sides SideFromNative(string s)
			=> s == "B"
				? Sides.Buy
				: s == "A"
					? Sides.Sell
					: throw new ArgumentOutOfRangeException(nameof(s), s, LocalizedStrings.InvalidValue);

		result.Action = m.Type switch
		{
			IQFeedMessage.MsgType.L2OrderAdd or
			IQFeedMessage.MsgType.L2PriceLevelSummary or
			IQFeedMessage.MsgType.L2OrderSummary
				=> QuoteChangeActions.New,

			IQFeedMessage.MsgType.L2PriceLevel or
			IQFeedMessage.MsgType.L2OrderUpdate or
			IQFeedMessage.MsgType.L2PriceLevelUpdate
				=> QuoteChangeActions.Update,

			IQFeedMessage.MsgType.L2OrderDelete or
			IQFeedMessage.MsgType.L2PriceLevelDelete
				=> QuoteChangeActions.Delete,

			_ => throw new ArgumentOutOfRangeException(nameof(m), m.Type, LocalizedStrings.InvalidValue),
		};

		switch (m.Type)
		{
			case IQFeedMessage.MsgType.L2PriceLevel:
			case IQFeedMessage.MsgType.L2OrderAdd:
			case IQFeedMessage.MsgType.L2OrderUpdate:
			case IQFeedMessage.MsgType.L2OrderSummary:
				time = m[8];
				date = m[9];
				result.Side = SideFromNative(m[3]);
				result.Price = m[4].To<decimal?>();
				result.Vol = m[5].To<uint?>();
				result.Priority = m[6].To<ulong?>();
				result.OrderId = m[1].To<ulong?>();
				result.MarketMakerId = m[2];
				break;

			case IQFeedMessage.MsgType.L2OrderDelete:
				time = m[4];
				date = m[5];
				result.Side = SideFromNative(m[3]);
				result.Price = null;
				result.Vol = null;
				result.OrderId = m[1].To<ulong?>();
				result.MarketMakerId = m[2]; // the api doc says this field is always empty. not true. group delete message contains mmid and Side to delete all orders for certain mmid/side combination
				break;
			case IQFeedMessage.MsgType.L2PriceLevelSummary:
			case IQFeedMessage.MsgType.L2PriceLevelUpdate:
				time = m[6];
				date = m[7];
				result.Side = SideFromNative(m[1]);
				result.Price = m[2].To<decimal?>();
				result.Vol = m[3].To<uint?>();
				result.OrderId = null;
				break;

			case IQFeedMessage.MsgType.L2PriceLevelDelete:
				time = m[3];
				date = m[4];
				result.Side = SideFromNative(m[1]);
				result.Price = m[2].To<decimal?>();
				result.Vol = null;
				result.OrderId = null;
				break;

			default:
				throw new ArgumentOutOfRangeException();
		}

		var serverTime = time.IsEmptyOrWhiteSpace() ?
			date.TryToDateTime(DateFormat)?.FromEst() :
				time.Contains("99:") ? IQFeedHelper.CurrentTimeUtc : $"{date} {time}".TryToDateTime(TimeFormatFull)?.FromEst();

		result.ServerTime = serverTime ?? throw new InvalidOperationException("invalid datetime: " + m.Message);

		return result;
	}
}

class IQFeedLookup(IQFeedMessageAdapter parent) : IQFeed(parent, "Lookup")
{
	protected override EndPoint GetAddress() => Adapter.LookupAddress;

	protected override async Task InitConnection(CancellationToken token)
	{
		await base.InitConnection(token);

		await foreach (var m in RequestListedMarkets(token).WithEnforcedCancellation(token))
			await RaiseNewMessageAsync(m, null, token);

		await foreach (var m in RequestSecurityTypes(token).WithEnforcedCancellation(token))
			await RaiseNewMessageAsync(m, null, token);
	}

	public async ValueTask RequestNewsStory(MarketDataMessage mdMsg, CancellationToken token)
	{
		if(mdMsg.NewsId.IsEmptyOrWhiteSpace())
			throw new ArgumentException("no news id");
		var newsId = mdMsg.NewsId.EnsureProtocolField(nameof(mdMsg.NewsId));

		var builder = new StringBuilder();

		var req = new FeedRequest(this, id => $"NSY,{newsId},t,,{id}")
		{
			Filter = m => m.Type == IQFeedMessage.MsgType.LookupNews
		};

		await foreach (var msg in req.ExecuteAsync(token).WithEnforcedCancellation(token))
		{
			var line = msg.RejoinFrom(0);
			if(!line.StartsWith("<BEGIN>") && !line.StartsWith("<END>"))
				builder.AppendLine(line);
		}

		var message = new NewsMessage
		{
			Id = mdMsg.NewsId,
			Story = builder.ToString(),
			ServerTime = CurrentTime
		};

		await RaiseNewMessageAsync(message, mdMsg, token);
	}

	private IAsyncEnumerable<IQFeedListedMarketMessage> RequestListedMarkets(CancellationToken token)
	{
		var req = new FeedRequest(this, id => $"SLM,{id}") { Filter = m => m.Type == IQFeedMessage.MsgType.LookupSymbols };

		return req.ExecuteAsync(parts => new IQFeedListedMarketMessage(parts[0].To<int>(), parts[1], parts[2], parts[3].To<int>()), token);
	}

	private IAsyncEnumerable<IQFeedSecurityTypeMessage> RequestSecurityTypes(CancellationToken token)
	{
		var req = new FeedRequest(this, id => $"SST,{id}") { Filter = m => m.Type == IQFeedMessage.MsgType.LookupSymbols };

		return req.ExecuteAsync(parts => new IQFeedSecurityTypeMessage(parts[0].To<int>(), parts[1], parts[2]), token);
	}

	/// <summary>
	/// To send the request for the news description.
	/// </summary>
	public async ValueTask RequestNewsHeadlines(MarketDataMessage mdMsg, CancellationToken token)
	{
		var req = new FeedRequest(this, id => $"NHL,,,t,,{mdMsg.From?.ToEst():yyyyMMdd},{id}") { Filter = m => m.Type == IQFeedMessage.MsgType.LookupNews };

		await foreach (var m in req.ExecuteAsync(ToNewsMessage, token).WithEnforcedCancellation(token))
			await RaiseNewMessageAsync(m, mdMsg, token);
	}

	public IAsyncEnumerable<SecurityMessage> RequestSecurities(SecurityLookupMessage lookupMsg, HashSet<SecurityTypes> securityTypes, CancellationToken token)
	{
		var filterValue = Adapter.ToNativeSecurityTypes(securityTypes).Select(t => t.To<string>()).ToArray();

		var code = lookupMsg.SecurityId.SecurityCode;
		if (code.IsEmpty())
			code = "*";
		code.EnsureProtocolField(nameof(lookupMsg.SecurityId.SecurityCode));

		//var searchFieldStr = searchField == IQFeedSearchField.Symbol ? "s" : "d";
		//var filterTypeStr = filterType == IQFeedFilterType.Market ? "e" : "t";
		const string searchFieldStr = "s";
		const string filterTypeStr = "t";

		SecurityMessage convert(IQFeedMessage m)
		{
			return new()
			{
				SecurityId = Adapter.CreateSecurityId(m[0], m[1].To<int>()),
				Name = m[3],
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityType = Adapter.FromNativeSecurityType(m[2].To<int>()),
			};
		}

		var req = new FeedRequest(this, id => $"SBF,{searchFieldStr},{code},{filterTypeStr},{filterValue.JoinSpace()},{id}") { Filter = m => m.Type == IQFeedMessage.MsgType.LookupSymbols };

		return req.ExecuteAsync(convert, token);
	}

	public async ValueTask RequestTicks(MarketDataMessage mdMsg, CancellationToken token)
	{
		var symbol = GetSymbol(mdMsg.SecurityId);
		ExecutionMessage convert(IQFeedMessage m)
		{
			var type = m[7];

			// C - Last Qualified Trade.
			// E - Extended Trade = Form T trade.
			// O - Other Trade = Any trade not accounted for by C or E.
			// S - Settle = Daily settle, only applicable to commodities.
			if(type is "E" or "O")
			{
				if(mdMsg.IsRegularTradingHours == true)
					return null;
			}
			else if(type != "C")
			{
				return null;
			}

			return new()
			{
				SecurityId = mdMsg.SecurityId,
				OriginalTransactionId = mdMsg.TransactionId,
				DataTypeEx = DataType.Ticks,
				ServerTime   = m[0].ToDateTime(TimeFormatFull).FromEst(),
				TradePrice   = m[1].To<decimal>(),
				TradeVolume  = m[2].To<decimal>(),
				TradeId      = m[6].To<long>(),
				OriginSide   = m[10].To<int>() switch
				{
					1 => Sides.Buy,
					2 => Sides.Sell,
					_ => null
				}
			};
		}

		Func<string, string> getCmd = mdMsg.Count != null ?
			id => $"HTX,{symbol},{mdMsg.Count.Value},1,{id}" :
			id => $"HTT,{symbol},{mdMsg.From?.ToEst():yyyyMMdd HHmmss},{mdMsg.To?.ToEst():yyyyMMdd HHmmss},,,,1,{id}";

		var req = new FeedRequest(this, getCmd) { Filter = m => m.Type == IQFeedMessage.MsgType.LookupHistory };

		await foreach (var m in req.ExecuteAsync(convert, token).WithEnforcedCancellation(token))
			await RaiseNewMessageAsync(m, mdMsg, token);
	}

	public async IAsyncEnumerable<CandleMessage> RequestIntradayCandles(MarketDataMessage mdMsg, [EnumeratorCancellation] CancellationToken token)
	{
		var symbol = GetSymbol(mdMsg.SecurityId);
		static TimeFrameCandleMessage convert(IQFeedMessage m)
		{
			return new()
			{
				CloseTime = m[0].ToDateTime("yyyy-MM-dd HH:mm:ss").FromEst(),
				HighPrice = m[1].To<decimal>(),
				LowPrice = m[2].To<decimal>(),
				OpenPrice = m[3].To<decimal>(),
				ClosePrice = m[4].To<decimal>(),
				TotalVolume = m[6].To<decimal>(),
				TotalTicks = m[7].To<int?>() ?? 0,
				State = CandleStates.Finished,
			};
		}

		mdMsg.DataType2.GetCandleParams(out var arg, out var intervalType);

		Func<string, string> createCmd = mdMsg.Count != null ?
			id => $"HIX,{symbol},{arg},{mdMsg.Count},1,{id},,{intervalType}" :
			id => $"HIT,{symbol},{arg},{mdMsg.From?.ToEst():yyyyMMdd HHmmss},{mdMsg.To?.ToEst():yyyyMMdd HHmmss},,,,1,{id},,{intervalType}";


		var req = new FeedRequest(this, createCmd) { Filter = m => m.Type == IQFeedMessage.MsgType.LookupHistory };

		await foreach (var m in req.ExecuteAsync(convert, token).WithEnforcedCancellation(token))
			yield return m;
	}

	private static TimeFrameCandleMessage BigCandleParser(IQFeedMessage m)
	{
		return new()
		{
			CloseTime = m[0].ToDateTime("yyyy-MM-dd").FromEst(),
			HighPrice = m[1].To<decimal>(),
			LowPrice = m[2].To<decimal>(),
			OpenPrice = m[3].To<decimal>(),
			ClosePrice = m[4].To<decimal>(),
			TotalVolume = m[5].To<decimal>(),
			OpenInterest = m[6] != "0" ? m[6].To<decimal?>() : null,
			State = CandleStates.Finished,
		};
	}

	public async IAsyncEnumerable<CandleMessage> RequestDailyCandles(MarketDataMessage mdMsg, [EnumeratorCancellation] CancellationToken token)
	{
		var symbol = GetSymbol(mdMsg.SecurityId);
		Func<string, string> createCmd = mdMsg.Count != null ?
			id => $"HDX,{symbol},{mdMsg.Count},1,{id}" :
			id => $"HDT,{symbol},{mdMsg.From?.ToEst():yyyyMMdd},{mdMsg.To?.ToEst():yyyyMMdd},,1,{id}";

		var req = new FeedRequest(this, createCmd) { Filter = m => m.Type == IQFeedMessage.MsgType.LookupHistory };

		await foreach (var m in req.ExecuteAsync(BigCandleParser, token).WithEnforcedCancellation(token))
			yield return m;
	}

	public IAsyncEnumerable<CandleMessage> RequestWeeklyCandles(MarketDataMessage mdMsg, CancellationToken token)
		=> RequestBigCandles(mdMsg, true, token);

	public IAsyncEnumerable<CandleMessage> RequestMonthlyCandles(MarketDataMessage mdMsg, CancellationToken token)
		=> RequestBigCandles(mdMsg, false, token);

	private async IAsyncEnumerable<CandleMessage> RequestBigCandles(MarketDataMessage mdMsg, bool week, [EnumeratorCancellation] CancellationToken token)
	{
		var cmd = week ? "HWX" : "HMX";
		var count = mdMsg.Count ?? new Range<DateTime>(mdMsg.From ?? DateTime.MinValue, mdMsg.To ?? DateTime.MaxValue).GetTimeFrameCount(mdMsg.GetArg<TimeSpan>());
		var symbol = GetSymbol(mdMsg.SecurityId);

		string createCmd(string id) => $"{cmd},{symbol},{count},1,{id}";

		var req = new FeedRequest(this, createCmd) { Filter = m => m.Type == IQFeedMessage.MsgType.LookupHistory };

		await foreach (var m in req.ExecuteAsync(BigCandleParser, token).WithEnforcedCancellation(token))
			yield return m;
	}
}

class IQFeedDerivatives(IQFeedMessageAdapter parent) : IQFeed(parent, "Derivatives")
{
	protected override Task InitConnection(CancellationToken token)
	{
		RunSysHandler(token);
		return Task.CompletedTask;
	}

	protected override EndPoint GetAddress() => Adapter.DerivativeAddress;

	public async IAsyncEnumerable<CandleMessage> SubscribeCandles(MarketDataMessage mdMsg, [EnumeratorCancellation] CancellationToken token)
	{
		var symbol = GetSymbol(mdMsg.SecurityId);
		TimeFrameCandleMessage convert(IQFeedMessage m)
		{
			return new()
			{
				CloseTime = m[1].ToDateTime("yyyy-MM-dd HH:mm:ss").FromEst(),
				OpenPrice = m[2].To<decimal>(),
				HighPrice = m[3].To<decimal>(),
				LowPrice = m[4].To<decimal>(),
				ClosePrice = m[5].To<decimal>(),
				TotalVolume = m[7].To<decimal>(),
				TotalTicks = m[8].To<int?>() ?? 0,
				State = m.Type == IQFeedMessage.MsgType.StreamCandleUpdated ? CandleStates.Active : CandleStates.Finished
			};
		}

		var histBack = TimeSpan.FromMinutes(2);

		if (mdMsg.GetArg() is TimeSpan tf)
		{
			histBack = TimeSpan.FromTicks(tf.Ticks * 2);
			if(tf >= TimeSpan.FromDays(1))
				throw new InvalidOperationException(LocalizedStrings.IntervalNotSupported.Put(tf));
		}

		mdMsg.DataType2.GetCandleParams(out var arg, out var intervalType);

		DateTime? from = mdMsg.From ?? DateTime.UtcNow - histBack;

		string createCmd(string id) => $"BW,{symbol},{arg},{from?.ToEst():yyyyMMdd HHmmss},,,,,{id},{intervalType},,1";

		bool filter(IQFeedMessage m) =>
			m[0] == symbol &&
			m.Type is IQFeedMessage.MsgType.StreamCandleHistory or IQFeedMessage.MsgType.StreamCandleCompleted or IQFeedMessage.MsgType.StreamCandleUpdated;

		var req = new FeedRequest(this, createCmd)
		{
			Filter = filter,
			GetUnsubscribeCmd = id => $"BR,{symbol},{id}"
		};

		await foreach (var m in req.ExecuteAsync(convert, token).WithEnforcedCancellation(token))
			yield return m;
	}
}

namespace StockSharp.Ctp.Native;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class CtpNativeClient : IDisposable
{
	static CtpNativeClient()
	{
		NativeLibrary.SetDllImportResolver(typeof(CtpNativeClient).Assembly, ResolveLibrary);
	}

	private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (!libraryName.Equals(CtpNativeMethods.LibraryName, StringComparison.Ordinal))
			return nint.Zero;

		var fileName = OperatingSystem.IsWindows()
			? $"{CtpNativeMethods.LibraryName}.dll"
			: $"lib{CtpNativeMethods.LibraryName}.so";
		var architecture = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
		var platform = OperatingSystem.IsWindows() ? "win" : "linux";
		var assemblyDirectory = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
		var candidates = new[]
		{
			Path.Combine(AppContext.BaseDirectory, fileName),
			Path.Combine(AppContext.BaseDirectory, "runtimes", $"{platform}-{architecture}", "native", fileName),
			Path.Combine(assemblyDirectory, fileName),
			Path.Combine(assemblyDirectory, "runtimes", $"{platform}-{architecture}", "native", fileName),
		};

		foreach (var candidate in candidates)
		{
			if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
				return handle;
		}

		return nint.Zero;
	}

	private readonly CtpNativeMethods.Callbacks _callbacks;
	private readonly Lock _sync = new();
	private readonly Dictionary<CtpChannels, TaskCompletionSource> _readyWaiters = [];
	private nint _handle;

	public CtpNativeClient()
	{
		ValidateStruct<CtpNativeError>(1);
		ValidateStruct<CtpNativeInstrument>(2);
		ValidateStruct<CtpNativeDepth>(3);
		ValidateStruct<CtpNativeOrderRequest>(4);
		ValidateStruct<CtpNativeCancelRequest>(5);
		ValidateStruct<CtpNativeOrder>(6);
		ValidateStruct<CtpNativeTrade>(7);
		ValidateStruct<CtpNativePosition>(8);
		ValidateStruct<CtpNativeAccount>(9);

		_callbacks = new()
		{
			State = OnState,
			Error = OnError,
			Instrument = OnInstrument,
			Depth = OnDepth,
			Order = OnOrder,
			Trade = OnTrade,
			Position = OnPosition,
			Account = OnAccount,
		};

		_handle = CtpNativeMethods.ctp_create(_callbacks, nint.Zero);
		if (_handle == nint.Zero)
			throw new InvalidOperationException("The CTP native client could not be created. Check the native runtime architecture and dependencies.");
	}

	public event Action<CtpChannels, CtpNativeConnectionStates, int, CtpNativeError?> StateChanged;
	public event Action<CtpChannels, CtpNativeError> Error;
	public event Action<CtpNativeInstrument?, CtpNativeError?, int, bool> Instrument;
	public event Action<CtpNativeDepth> Depth;
	public event Action<CtpNativeOrder?, CtpNativeError?, int, bool, bool> Order;
	public event Action<CtpNativeTrade?, CtpNativeError?, int, bool, bool> Trade;
	public event Action<CtpNativePosition?, CtpNativeError?, int, bool> Position;
	public event Action<CtpNativeAccount?, CtpNativeError?, int, bool> Account;

	public string MarketVersion => GetVersion(CtpChannels.MarketData);
	public string TraderVersion => GetVersion(CtpChannels.Trader);

	public async Task ConnectMarketAsync(string front, string dataPath, string brokerId, string userId, SecureString password, bool productionMode, TimeSpan timeout, CancellationToken cancellationToken)
	{
		var waiter = PrepareWaiter(CtpChannels.MarketData);
		var flowPath = PrepareFlowPath(dataPath, "market");
		try
		{
			EnsureResult(CtpNativeMethods.ctp_connect_market(Handle, front, flowPath, brokerId, userId, password.UnSecure(), productionMode ? 1 : 0), "connect market data");
			await waiter.Task.WaitAsync(timeout, cancellationToken);
		}
		finally
		{
			RemoveWaiter(CtpChannels.MarketData, waiter);
		}
	}

	public async Task ConnectTraderAsync(string front, string dataPath, string brokerId, string userId, string investorId, SecureString password, string appId, SecureString authCode, string productInfo, CtpResumeTypes resumeType, bool productionMode, TimeSpan timeout, CancellationToken cancellationToken)
	{
		var waiter = PrepareWaiter(CtpChannels.Trader);
		var flowPath = PrepareFlowPath(dataPath, "trader");
		try
		{
			EnsureResult(CtpNativeMethods.ctp_connect_trader(Handle, front, flowPath, brokerId, userId, investorId, password.UnSecure(), appId ?? string.Empty, authCode?.UnSecure() ?? string.Empty, productInfo ?? string.Empty, (int)resumeType, productionMode ? 1 : 0), "connect trader");
			await waiter.Task.WaitAsync(timeout, cancellationToken);
		}
		finally
		{
			RemoveWaiter(CtpChannels.Trader, waiter);
		}
	}

	public void DisconnectMarket() => EnsureResult(CtpNativeMethods.ctp_disconnect_market(Handle), "disconnect market data");
	public void DisconnectTrader() => EnsureResult(CtpNativeMethods.ctp_disconnect_trader(Handle), "disconnect trader");

	public void SubscribeMarketData(string instrumentId, bool subscribe)
		=> EnsureResult(CtpNativeMethods.ctp_subscribe_market_data(Handle, instrumentId, subscribe ? 1 : 0), subscribe ? "subscribe market data" : "unsubscribe market data");

	public string NextOrderRef()
	{
		var value = new StringBuilder(14);
		EnsureResult(CtpNativeMethods.ctp_next_order_ref(Handle, value, value.Capacity), "allocate order reference");
		return value.ToString();
	}

	public void QueryInstruments(int requestId, string exchangeId, string instrumentId, string productId)
		=> EnsureResult(CtpNativeMethods.ctp_query_instruments(Handle, requestId, exchangeId ?? string.Empty, instrumentId ?? string.Empty, productId ?? string.Empty), "query instruments");

	public void InsertOrder(in CtpNativeOrderRequest request)
		=> EnsureResult(CtpNativeMethods.ctp_insert_order(Handle, request), "insert order");

	public void CancelOrder(in CtpNativeCancelRequest request)
		=> EnsureResult(CtpNativeMethods.ctp_cancel_order(Handle, request), "cancel order");

	public void QueryOrders(int requestId)
		=> EnsureResult(CtpNativeMethods.ctp_query_orders(Handle, requestId), "query orders");

	public void QueryTrades(int requestId)
		=> EnsureResult(CtpNativeMethods.ctp_query_trades(Handle, requestId), "query trades");

	public void QueryPositions(int requestId, string instrumentId = null)
		=> EnsureResult(CtpNativeMethods.ctp_query_positions(Handle, requestId, instrumentId ?? string.Empty), "query positions");

	public void QueryAccount(int requestId, string currencyId = null)
		=> EnsureResult(CtpNativeMethods.ctp_query_account(Handle, requestId, currencyId ?? string.Empty), "query account");

	private nint Handle => _handle != nint.Zero ? _handle : throw new ObjectDisposedException(nameof(CtpNativeClient));

	private static string PrepareFlowPath(string root, string channel)
	{
		var path = Path.Combine(root, channel);
		Directory.CreateDirectory(path);
		return Path.GetFullPath(path) + Path.DirectorySeparatorChar;
	}

	private TaskCompletionSource PrepareWaiter(CtpChannels channel)
	{
		var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_sync)
		{
			if (_readyWaiters.ContainsKey(channel))
				throw new InvalidOperationException($"A CTP {channel} connection attempt is already active.");
			_readyWaiters.Add(channel, waiter);
		}
		return waiter;
	}

	private void RemoveWaiter(CtpChannels channel, TaskCompletionSource waiter)
	{
		lock (_sync)
		{
			if (_readyWaiters.TryGetValue(channel, out var current) && ReferenceEquals(current, waiter))
				_readyWaiters.Remove(channel);
		}
	}

	private void CompleteWaiter(CtpChannels channel, CtpNativeConnectionStates state, int reason, CtpNativeError? error)
	{
		TaskCompletionSource waiter;
		lock (_sync)
			_readyWaiters.TryGetValue(channel, out waiter);

		if (waiter == null)
			return;
		if (state == CtpNativeConnectionStates.Ready)
			waiter.TrySetResult();
		else if (state == CtpNativeConnectionStates.Failed)
			waiter.TrySetException((error ?? default).ToException($"{channel} connection"));
		else if (state == CtpNativeConnectionStates.Disconnected)
			waiter.TrySetException(new InvalidOperationException($"CTP {channel} disconnected before login completed. Reason: {reason}."));
	}

	private static void EnsureResult(int result, string operation)
	{
		if (result == 0)
			return;
		var reason = result switch
		{
			-1 => "network connection is unavailable",
			-2 => "too many requests are waiting",
			-3 => "request rate limit was exceeded",
			_ => "the native API rejected the request",
		};
		throw new InvalidOperationException($"CTP {operation} failed ({result}): {reason}.");
	}

	private static T? Read<T>(nint pointer) where T : struct
		=> pointer == nint.Zero ? null : Marshal.PtrToStructure<T>(pointer);

	private static string GetVersion(CtpChannels channel)
		=> Marshal.PtrToStringAnsi(CtpNativeMethods.ctp_get_version((int)channel)) ?? string.Empty;

	private static void ValidateStruct<T>(int type) where T : struct
	{
		var managedSize = Marshal.SizeOf<T>();
		var nativeSize = CtpNativeMethods.ctp_get_struct_size(type);
		if (nativeSize != managedSize)
			throw new InvalidOperationException($"CTP native ABI mismatch for {typeof(T).Name}: managed size {managedSize}, native size {nativeSize}.");
	}

	private void OnState(int channel, int state, int reason, nint error, nint userData)
		=> InvokeSafely(() =>
		{
			var channelValue = (CtpChannels)channel;
			var stateValue = (CtpNativeConnectionStates)state;
			var errorValue = Read<CtpNativeError>(error);
			CompleteWaiter(channelValue, stateValue, reason, errorValue);
			StateChanged?.Invoke(channelValue, stateValue, reason, errorValue);
		});

	private void OnError(int channel, nint error, nint userData)
		=> InvokeSafely(() => Error?.Invoke((CtpChannels)channel, Read<CtpNativeError>(error) ?? default));

	private void OnInstrument(nint instrument, nint error, int requestId, int isLast, nint userData)
		=> InvokeSafely(() => Instrument?.Invoke(Read<CtpNativeInstrument>(instrument), Read<CtpNativeError>(error), requestId, isLast != 0));

	private void OnDepth(nint depth, nint userData)
		=> InvokeSafely(() => { if (Read<CtpNativeDepth>(depth) is { } value) Depth?.Invoke(value); });

	private void OnOrder(nint order, nint error, int requestId, int isLast, int isQuery, nint userData)
		=> InvokeSafely(() => Order?.Invoke(Read<CtpNativeOrder>(order), Read<CtpNativeError>(error), requestId, isLast != 0, isQuery != 0));

	private void OnTrade(nint trade, nint error, int requestId, int isLast, int isQuery, nint userData)
		=> InvokeSafely(() => Trade?.Invoke(Read<CtpNativeTrade>(trade), Read<CtpNativeError>(error), requestId, isLast != 0, isQuery != 0));

	private void OnPosition(nint position, nint error, int requestId, int isLast, nint userData)
		=> InvokeSafely(() => Position?.Invoke(Read<CtpNativePosition>(position), Read<CtpNativeError>(error), requestId, isLast != 0));

	private void OnAccount(nint account, nint error, int requestId, int isLast, nint userData)
		=> InvokeSafely(() => Account?.Invoke(Read<CtpNativeAccount>(account), Read<CtpNativeError>(error), requestId, isLast != 0));

	private static void InvokeSafely(Action action)
	{
		try
		{
			action();
		}
		catch
		{
			// Exceptions must never cross the native callback boundary.
		}
	}

	public void Dispose()
	{
		var handle = Interlocked.Exchange(ref _handle, nint.Zero);
		if (handle != nint.Zero)
			CtpNativeMethods.ctp_destroy(handle);
	}
}

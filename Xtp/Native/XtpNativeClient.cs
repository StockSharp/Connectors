namespace StockSharp.Xtp.Native;

using System.Reflection;
using System.Runtime.InteropServices;

internal sealed class XtpNativeClient : IDisposable
{
	static XtpNativeClient()
	{
		NativeLibrary.SetDllImportResolver(typeof(XtpNativeClient).Assembly, ResolveLibrary);
	}

	private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (!libraryName.Equals(XtpNativeMethods.LibraryName, StringComparison.Ordinal))
			return nint.Zero;

		var fileName = OperatingSystem.IsWindows()
			? $"{XtpNativeMethods.LibraryName}.dll"
			: $"lib{XtpNativeMethods.LibraryName}.so";
		var architecture = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
		var candidates = new[]
		{
			Path.Combine(AppContext.BaseDirectory, fileName),
			Path.Combine(AppContext.BaseDirectory, "runtimes", $"{(OperatingSystem.IsWindows() ? "win" : "linux")}-{architecture}", "native", fileName),
			Path.Combine(Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory, fileName),
		};

		foreach (var candidate in candidates)
		{
			if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
				return handle;
		}

		return nint.Zero;
	}

	private readonly XtpNativeMethods.Callbacks _callbacks;
	private nint _handle;

	public XtpNativeClient(byte clientId, string dataPath, string softwareVersion, string softwareKey)
	{
		ArgumentOutOfRangeException.ThrowIfZero(clientId);
		ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);
		Directory.CreateDirectory(dataPath);

		_callbacks = new()
		{
			Disconnected = OnDisconnected,
			Error = OnError,
			Security = OnSecurity,
			Depth = OnDepth,
			Tick = OnTick,
			Order = OnOrder,
			Trade = OnTrade,
			Cancel = OnCancel,
			Position = OnPosition,
			Asset = OnAsset,
		};

		_handle = XtpNativeMethods.xtp_create(clientId, dataPath, softwareVersion ?? string.Empty, softwareKey ?? string.Empty, _callbacks, nint.Zero);
		if (_handle == nint.Zero)
			throw new InvalidOperationException("The XTP native clients could not be created. Check the native runtime architecture and data directory permissions.");
	}

	public event Action<XtpChannel, int> Disconnected;
	public event Action<XtpChannel, XtpNativeError> Error;
	public event Action<XtpNativeSecurity?, XtpNativeError, bool> Security;
	public event Action<XtpNativeDepth> Depth;
	public event Action<XtpNativeTick> Tick;
	public event Action<XtpNativeOrder?, XtpNativeError, int, bool, bool> Order;
	public event Action<XtpNativeTrade?, XtpNativeError, int, bool, bool> Trade;
	public event Action<ulong, ulong, XtpNativeError> CancelError;
	public event Action<XtpNativePosition?, XtpNativeError, int, bool> Position;
	public event Action<XtpNativeAsset?, XtpNativeError, int, bool> Asset;

	public string QuoteVersion => GetVersion(XtpChannel.Quote);
	public string TraderVersion => GetVersion(XtpChannel.Trader);

	public void LoginQuote(EndPoint address, string user, SecureString password, XtpProtocols protocol, string localIp)
	{
		var result = XtpNativeMethods.xtp_quote_login(Handle, address.GetHost(), address.GetPort(), user, password.UnSecure(), (int)protocol, localIp ?? string.Empty);
		if (result != 0)
			throw GetException(XtpChannel.Quote, result);
	}

	public void LoginTrader(EndPoint address, string user, SecureString password, XtpProtocols protocol, string localIp)
	{
		var session = XtpNativeMethods.xtp_trader_login(Handle, address.GetHost(), address.GetPort(), user, password.UnSecure(), (int)protocol, localIp ?? string.Empty);
		if (session == 0)
			throw GetException(XtpChannel.Trader);
	}

	public void LogoutQuote() => XtpNativeMethods.xtp_quote_logout(Handle);
	public void LogoutTrader() => XtpNativeMethods.xtp_trader_logout(Handle);

	public void SubscribeMarketData(string ticker, XtpExchange exchange, bool subscribe)
		=> EnsureResult(XtpNativeMethods.xtp_subscribe_market_data(Handle, ticker, (int)exchange, subscribe ? 1 : 0), XtpChannel.Quote);

	public void SubscribeTicks(string ticker, XtpExchange exchange, bool subscribe)
		=> EnsureResult(XtpNativeMethods.xtp_subscribe_ticks(Handle, ticker, (int)exchange, subscribe ? 1 : 0), XtpChannel.Quote);

	public void QuerySecurities(XtpExchange exchange)
		=> EnsureResult(XtpNativeMethods.xtp_query_securities(Handle, (int)exchange), XtpChannel.Quote);

	public ulong InsertOrder(in XtpNativeOrderRequest request)
	{
		var id = XtpNativeMethods.xtp_insert_order(Handle, request);
		return id == 0 ? throw GetException(XtpChannel.Trader) : id;
	}

	public ulong CancelOrder(ulong orderId)
	{
		var id = XtpNativeMethods.xtp_cancel_order(Handle, orderId);
		return id == 0 ? throw GetException(XtpChannel.Trader) : id;
	}

	public void QueryOrders(int requestId) => EnsureResult(XtpNativeMethods.xtp_query_orders(Handle, requestId), XtpChannel.Trader);
	public void QueryTrades(int requestId) => EnsureResult(XtpNativeMethods.xtp_query_trades(Handle, requestId), XtpChannel.Trader);
	public void QueryPositions(int requestId) => EnsureResult(XtpNativeMethods.xtp_query_positions(Handle, requestId), XtpChannel.Trader);
	public void QueryAssets(int requestId) => EnsureResult(XtpNativeMethods.xtp_query_assets(Handle, requestId), XtpChannel.Trader);

	private nint Handle => _handle != nint.Zero ? _handle : throw new ObjectDisposedException(nameof(XtpNativeClient));

	private string GetVersion(XtpChannel channel)
		=> Marshal.PtrToStringAnsi(XtpNativeMethods.xtp_get_version(Handle, (int)channel)) ?? string.Empty;

	private Exception GetException(XtpChannel channel, int fallback = -1)
	{
		XtpNativeMethods.xtp_get_last_error(Handle, (int)channel, out var error);
		var id = error.Id == 0 ? fallback : error.Id;
		return new InvalidOperationException($"XTP {channel} error {id}: {error.Message}");
	}

	private void EnsureResult(int result, XtpChannel channel)
	{
		if (result != 0)
			throw GetException(channel, result);
	}

	private static T? Read<T>(nint pointer) where T : struct
		=> pointer == nint.Zero ? null : Marshal.PtrToStructure<T>(pointer);

	private void OnDisconnected(int channel, int reason, nint userData)
		=> InvokeSafely(() => Disconnected?.Invoke((XtpChannel)channel, reason));

	private void OnError(int channel, nint error, nint userData)
		=> InvokeSafely(() => Error?.Invoke((XtpChannel)channel, Read<XtpNativeError>(error) ?? default));

	private void OnSecurity(nint security, nint error, int isLast, nint userData)
		=> InvokeSafely(() => Security?.Invoke(Read<XtpNativeSecurity>(security), Read<XtpNativeError>(error) ?? default, isLast != 0));

	private void OnDepth(nint depth, nint userData)
		=> InvokeSafely(() => { if (Read<XtpNativeDepth>(depth) is { } value) Depth?.Invoke(value); });

	private void OnTick(nint tick, nint userData)
		=> InvokeSafely(() => { if (Read<XtpNativeTick>(tick) is { } value) Tick?.Invoke(value); });

	private void OnOrder(nint order, nint error, int requestId, int isLast, int isQuery, nint userData)
		=> InvokeSafely(() => Order?.Invoke(Read<XtpNativeOrder>(order), Read<XtpNativeError>(error) ?? default, requestId, isLast != 0, isQuery != 0));

	private void OnTrade(nint trade, nint error, int requestId, int isLast, int isQuery, nint userData)
		=> InvokeSafely(() => Trade?.Invoke(Read<XtpNativeTrade>(trade), Read<XtpNativeError>(error) ?? default, requestId, isLast != 0, isQuery != 0));

	private void OnCancel(ulong orderId, ulong cancelOrderId, nint error, nint userData)
		=> InvokeSafely(() => CancelError?.Invoke(orderId, cancelOrderId, Read<XtpNativeError>(error) ?? default));

	private void OnPosition(nint position, nint error, int requestId, int isLast, nint userData)
		=> InvokeSafely(() => Position?.Invoke(Read<XtpNativePosition>(position), Read<XtpNativeError>(error) ?? default, requestId, isLast != 0));

	private void OnAsset(nint asset, nint error, int requestId, int isLast, nint userData)
		=> InvokeSafely(() => Asset?.Invoke(Read<XtpNativeAsset>(asset), Read<XtpNativeError>(error) ?? default, requestId, isLast != 0));

	private void InvokeSafely(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Error?.Invoke(XtpChannel.Trader, new XtpNativeError { Id = -1, Message = ex.Message });
		}
	}

	public void Dispose()
	{
		var handle = Interlocked.Exchange(ref _handle, nint.Zero);
		if (handle != nint.Zero)
			XtpNativeMethods.xtp_destroy(handle);
	}
}

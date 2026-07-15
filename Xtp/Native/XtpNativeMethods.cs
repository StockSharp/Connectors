namespace StockSharp.Xtp.Native;

using System.Runtime.InteropServices;

internal static class XtpNativeMethods
{
	internal const string LibraryName = "StockSharp.Xtp.Native";

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void DisconnectedCallback(int channel, int reason, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void ErrorCallback(int channel, nint error, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void SecurityCallback(nint security, nint error, int isLast, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void DepthCallback(nint depth, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void TickCallback(nint tick, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void OrderCallback(nint order, nint error, int requestId, int isLast, int isQuery, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void TradeCallback(nint trade, nint error, int requestId, int isLast, int isQuery, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void CancelCallback(ulong orderId, ulong cancelOrderId, nint error, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void PositionCallback(nint position, nint error, int requestId, int isLast, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void AssetCallback(nint asset, nint error, int requestId, int isLast, nint userData);

	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	internal struct Callbacks
	{
		public DisconnectedCallback Disconnected;
		public ErrorCallback Error;
		public SecurityCallback Security;
		public DepthCallback Depth;
		public TickCallback Tick;
		public OrderCallback Order;
		public TradeCallback Trade;
		public CancelCallback Cancel;
		public PositionCallback Position;
		public AssetCallback Asset;
	}

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern nint xtp_create(byte clientId, string dataPath, string softwareVersion, string softwareKey, in Callbacks callbacks, nint userData);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern void xtp_destroy(nint context);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int xtp_quote_login(nint context, string host, int port, string user, string password, int protocol, string localIp);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern ulong xtp_trader_login(nint context, string host, int port, string user, string password, int protocol, string localIp);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int xtp_quote_logout(nint context);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int xtp_trader_logout(nint context);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int xtp_subscribe_market_data(nint context, string ticker, int exchange, int subscribe);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int xtp_subscribe_ticks(nint context, string ticker, int exchange, int subscribe);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int xtp_query_securities(nint context, int exchange);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern ulong xtp_insert_order(nint context, in XtpNativeOrderRequest request);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern ulong xtp_cancel_order(nint context, ulong orderId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int xtp_query_orders(nint context, int requestId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int xtp_query_trades(nint context, int requestId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int xtp_query_positions(nint context, int requestId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int xtp_query_assets(nint context, int requestId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int xtp_get_last_error(nint context, int channel, out XtpNativeError error);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern nint xtp_get_version(nint context, int channel);
}

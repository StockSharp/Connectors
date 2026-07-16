namespace StockSharp.Ctp.Native;

using System.Runtime.InteropServices;
using System.Text;

internal static class CtpNativeMethods
{
	internal const string LibraryName = "StockSharp.Ctp.Native";

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void StateCallback(int channel, int state, int reason, nint error, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void ErrorCallback(int channel, nint error, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void InstrumentCallback(nint instrument, nint error, int requestId, int isLast, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void DepthCallback(nint depth, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void OrderCallback(nint order, nint error, int requestId, int isLast, int isQuery, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void TradeCallback(nint trade, nint error, int requestId, int isLast, int isQuery, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void PositionCallback(nint position, nint error, int requestId, int isLast, nint userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void AccountCallback(nint account, nint error, int requestId, int isLast, nint userData);

	[StructLayout(LayoutKind.Sequential, Pack = 8)]
	internal struct Callbacks
	{
		public StateCallback State;
		public ErrorCallback Error;
		public InstrumentCallback Instrument;
		public DepthCallback Depth;
		public OrderCallback Order;
		public TradeCallback Trade;
		public PositionCallback Position;
		public AccountCallback Account;
	}

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern nint ctp_create(in Callbacks callbacks, nint userData);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern void ctp_destroy(nint context);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int ctp_connect_market(nint context, string front, string flowPath, string brokerId, string userId, string password, int productionMode);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int ctp_connect_trader(nint context, string front, string flowPath, string brokerId, string userId, string investorId, string password, string appId, string authCode, string productInfo, int resumeType, int productionMode);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int ctp_disconnect_market(nint context);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int ctp_disconnect_trader(nint context);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int ctp_subscribe_market_data(nint context, string instrumentId, int subscribe);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int ctp_next_order_ref(nint context, StringBuilder orderRef, int capacity);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int ctp_query_instruments(nint context, int requestId, string exchangeId, string instrumentId, string productId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int ctp_insert_order(nint context, in CtpNativeOrderRequest request);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int ctp_cancel_order(nint context, in CtpNativeCancelRequest request);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int ctp_query_orders(nint context, int requestId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int ctp_query_trades(nint context, int requestId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int ctp_query_positions(nint context, int requestId, string instrumentId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	internal static extern int ctp_query_account(nint context, int requestId, string currencyId);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern nint ctp_get_version(int channel);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
	internal static extern int ctp_get_struct_size(int type);
}

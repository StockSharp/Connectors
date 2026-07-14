namespace StockSharp.InteractiveBrokers.Native;

/// <summary>
/// System order states.
/// </summary>
enum OrderStatus
{
	/// <summary>
	/// The transaction is sent to the server.
	/// </summary>
	SentToServer,

	/// <summary>
	/// The transaction is received by the server.
	/// </summary>
	ReceiveByServer,

	/// <summary>
	/// Sending transaction error.
	/// </summary>
	GateError,

	/// <summary>
	/// The order is accepted by the exchange.
	/// </summary>
	Accepted,

	/// <summary>
	/// Cancel pending.
	/// </summary>
	SentToCanceled,

	/// <summary>
	/// Cancelled.
	/// </summary>
	Cancelled,

	/// <summary>
	/// Matched.
	/// </summary>
	Matched,
}
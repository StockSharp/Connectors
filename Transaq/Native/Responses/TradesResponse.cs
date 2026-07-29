namespace StockSharp.Transaq.Native.Responses;

class TradesResponse : BaseResponse
{
	public IEnumerable<TransaqMyTrade> Trades { get; set; }
}

class TransaqMyTrade : Tick
{
	public long OrderNo { get; set; }
	public string Client { get; set; }
	public string Union { get; set; }
	public DateTime Time { get; set; }
	public string BrokerRef { get; set; }
	public double? Value { get; set; }
	public double? Commission { get; set; }
	public double? Yield { get; set; }
	public double? AccrueEdint { get; set; }
	public TradeTypes TradeType { get; set; }
	public string SettleCode { get; set; }
	public long CurrentPos { get; set; }
}

enum TradeTypes
{
	/// <summary>
	/// Обычная.
	/// </summary>
	T,

	/// <summary>
	/// Внебиржевая/Адресная.
	/// </summary>
	N,

	/// <summary>
	/// Первичное размещение.
	/// </summary>
	P,

	/// <summary>
	/// Расчетная по операции своп.
	/// </summary>
	S,

	/// <summary>
	/// Расчетная по внебиржевой операции своп.
	/// </summary>
	W,

	/// <summary>
	/// Перевод денег/бумаг.
	/// </summary>
	F,

	/// <summary>
	/// Расчетная сделка бивалютной корзины.
	/// </summary>
	E,

	/// <summary>
	/// Расчетная внебиржевая сделка бивалютной корзины.
	/// </summary>
	K,

	/// <summary>
	/// Адресная сделка первой части РЕПО.
	/// </summary>
	R,

	/// <summary>
	/// Сделка по операции РЕПО с ЦК.
	/// </summary>
	G,

	/// <summary>
	/// Первая часть сделки по операции РЕПО с ЦК.
	/// </summary>
	H,

	/// <summary>
	/// Вторая часть сделки по операции РЕПО с ЦК.
	/// </summary>
	h,

	/// <summary>
	/// Адресная сделка по операции РЕПО с ЦК.
	/// </summary>
	I,

	/// <summary>
	/// Первая часть адресной сделки по операции РЕПО с ЦК.
	/// </summary>
	J,

	/// <summary>
	/// Вторая часть адресной сделки по операции РЕПО с ЦК.
	/// </summary>
	j,

	/// <summary>
	/// Техническая сделка по возврату активов РЕПО с ЦК.
	/// </summary>
	L,

	/// <summary>
	/// Первая часть адресной сделки РЕПО с корзиной.
	/// </summary>
	M,

	/// <summary>
	/// Вторая часть адресной сделки РЕПО с корзиной.
	/// </summary>
	n,
}
namespace StockSharp.LCX.Native.Model;

sealed class LcxWsMessage
{
	public string Type { get; init; }

	public string Topic { get; init; }

	public string Pair { get; init; }

	public LcxTicker[] Tickers { get; init; } = [];

	public LcxBook Book { get; init; }

	public LcxPublicTrade[] Trades { get; init; } = [];

	public LcxBalance[] Balances { get; init; } = [];

	public LcxOrder Order { get; init; }

	public LcxUserTrade UserTrade { get; init; }
}

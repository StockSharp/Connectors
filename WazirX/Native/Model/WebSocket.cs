namespace StockSharp.WazirX.Native.Model;

sealed class WazirXWsMessage
{
	public string Stream { get; init; }

	public WazirXTicker[] Tickers { get; init; } = [];

	public WazirXBook Book { get; init; }

	public WazirXTrade[] Trades { get; init; } = [];

	public WazirXCandle Candle { get; init; }

	public WazirXBalance[] Balances { get; init; } = [];

	public WazirXOrder Order { get; init; }

	public WazirXUserTrade UserTrade { get; init; }
}

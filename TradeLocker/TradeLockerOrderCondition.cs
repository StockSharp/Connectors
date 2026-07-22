namespace StockSharp.TradeLocker;

/// <summary>TradeLocker-specific order parameters.</summary>
public sealed class TradeLockerOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Absolute stop-loss price.</summary>
	[Display(
		Name = "Stop loss",
		GroupName = LocalizedStrings.GeneralKey)]
	public decimal? StopLoss { get; set; }

	/// <summary>Absolute take-profit price.</summary>
	[Display(
		Name = "Take profit",
		GroupName = LocalizedStrings.GeneralKey)]
	public decimal? TakeProfit { get; set; }

	/// <summary>Strategy identifier visible in TradeLocker.</summary>
	[Display(
		Name = "Strategy ID",
		GroupName = LocalizedStrings.GeneralKey)]
	public string StrategyId { get; set; }
}

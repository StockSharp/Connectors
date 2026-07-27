namespace StockSharp.TradeLocker;

/// <summary>TradeLocker-specific order parameters.</summary>
public sealed class TradeLockerOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Absolute stop-loss price.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossLabelKey,
		GroupName = LocalizedStrings.GeneralKey)]
	public decimal? StopLoss { get; set; }

	/// <summary>Absolute take-profit price.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitLabelKey,
		GroupName = LocalizedStrings.GeneralKey)]
	public decimal? TakeProfit { get; set; }

	/// <summary>Strategy identifier visible in TradeLocker.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StrategyIdKey,
		GroupName = LocalizedStrings.GeneralKey)]
	public string StrategyId { get; set; }
}

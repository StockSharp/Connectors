namespace StockSharp.MatchTrader;

/// <summary>Match-Trader-specific order parameters.</summary>
public sealed class MatchTraderOrderCondition : BaseWithdrawOrderCondition
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

	/// <summary>Native position identifier used when <see cref="BaseWithdrawOrderCondition.IsWithdraw"/> is enabled.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionKey,
		GroupName = LocalizedStrings.GeneralKey)]
	public string PositionId { get; set; }
}

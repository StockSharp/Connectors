namespace StockSharp.XOpenHub;

/// <summary>X Open Hub-specific order parameters.</summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.XOpenHubKey)]
public sealed class XOpenHubOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Absolute stop-loss price.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopLossKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public decimal? StopLoss { get; set; }

	/// <summary>Absolute take-profit price.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public decimal? TakeProfit { get; set; }

	/// <summary>Trailing-stop offset in points.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TrailingDeltaKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public int Offset { get; set; }

	/// <summary>Native position identifier used for a full or partial close.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PositionKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public string PositionId { get; set; }

	/// <summary>Comment stored by the broker with the order.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CommentKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 4)]
	public string Comment { get; set; }
}

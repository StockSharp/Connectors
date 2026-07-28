namespace StockSharp.Coincall;

/// <summary>
/// Coincall order condition.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoincallKey)]
public sealed class CoincallOrderCondition : BaseWithdrawOrderCondition
{
	private const string _reduceOnly = "ReduceOnly";
	private const string _triggerPrice = "TriggerPrice";

	/// <summary>
	/// Reduce an existing position only.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReducePositionKey,
		Description = LocalizedStrings.ReducePositionKey)]
	public bool ReduceOnly
	{
		get => Parameters.TryGetValue(_reduceOnly, out var value) &&
			value is true;
		set => Parameters[_reduceOnly] = value;
	}

	/// <summary>
	/// Stop trigger price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey)]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(_triggerPrice, out var value)
			? (decimal?)value
			: null;
		set => Parameters[_triggerPrice] = value;
	}
}

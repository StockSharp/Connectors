namespace StockSharp.IG;

/// <summary>IG API environments.</summary>
public enum IgEnvironments
{
	/// <summary>Demo environment.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey)]
	Demo,

	/// <summary>Live environment.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RealKey)]
	Live,
}

/// <summary>IG native deal kinds.</summary>
public enum IgDealKinds
{
	/// <summary>An open OTC position.</summary>
	Position,

	/// <summary>A pending OTC working order.</summary>
	WorkingOrder,
}

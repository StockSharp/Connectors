namespace StockSharp.Daishin;

/// <summary>Stock order routes supported by Daishin CYBOS Plus.</summary>
[DataContract]
[Serializable]
public enum DaishinOrderMarkets
{
	/// <summary>Use the adapter market setting; consolidated quotes route to KRX.</summary>
	[EnumMember]
	Adapter,
	/// <summary>Route to Korea Exchange.</summary>
	[EnumMember]
	Krx,
	/// <summary>Route to Nextrade.</summary>
	[EnumMember]
	Nxt,
}

/// <summary>Additional parameters for Daishin stock orders.</summary>
[DataContract]
[Serializable]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DaishinKey)]
public sealed class DaishinOrderCondition : OrderCondition
{
	private DaishinOrderMarkets _market;

	/// <summary>Explicit KRX or NXT order route.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DaishinMarketKey,
		Description = LocalizedStrings.DaishinMarketDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	public DaishinOrderMarkets Market
	{
		get => _market;
		set
		{
			_market = value;
			Parameters[nameof(Market)] = value;
		}
	}
}

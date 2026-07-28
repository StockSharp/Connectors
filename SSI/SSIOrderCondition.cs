namespace StockSharp.SSI;

/// <summary>SSI exchange order type override.</summary>
[DataContract]
[Serializable]
public enum SSIOrderConditionTypes
{
	/// <summary>Select LO or MTL from the StockSharp order type.</summary>
	[EnumMember]
	Auto,

	/// <summary>At the open.</summary>
	[EnumMember]
	ATO,

	/// <summary>At the close.</summary>
	[EnumMember]
	ATC,

	/// <summary>Market-to-limit.</summary>
	[EnumMember]
	MTL,

	/// <summary>Match or kill.</summary>
	[EnumMember]
	MOK,

	/// <summary>Match and kill.</summary>
	[EnumMember]
	MAK,

	/// <summary>Post-limit order.</summary>
	[EnumMember]
	PLO,
}

/// <summary>SSI-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SSIKey)]
public sealed class SSIOrderCondition : OrderCondition
{
	/// <summary>SSI exchange order type.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderTypeKey,
		Description = LocalizedStrings.OrderTypeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public SSIOrderConditionTypes Type
	{
		get => (SSIOrderConditionTypes?)Parameters.TryGetValue(
			nameof(Type)) ?? SSIOrderConditionTypes.Auto;
		set => Parameters[nameof(Type)] = value;
	}
}

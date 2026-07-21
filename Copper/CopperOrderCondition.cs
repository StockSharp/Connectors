namespace StockSharp.Copper;

/// <summary>Copper withdrawal and portfolio-transfer parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CopperKey)]
public sealed class CopperOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Destination kind.</summary>
	[DataMember]
	[Display(Name = "Destination type",
		Description = "Copper withdrawal destination type.",
		GroupName = LocalizedStrings.WithdrawKey, Order = 0)]
	public CopperDestinationTypes DestinationType
	{
		get => (CopperDestinationTypes?)Parameters.TryGetValue(
			nameof(DestinationType)) ?? CopperDestinationTypes.ExternalAddress;
		set => Parameters[nameof(DestinationType)] = value;
	}

	/// <summary>Address-book or destination portfolio identifier.</summary>
	[DataMember]
	[Display(Name = "Destination ID",
		Description = "Copper address-book or destination portfolio ID.",
		GroupName = LocalizedStrings.WithdrawKey, Order = 1)]
	public string DestinationId
	{
		get => (string)Parameters.TryGetValue(nameof(DestinationId));
		set => Parameters[nameof(DestinationId)] = value?.Trim();
	}

	/// <summary>Blockchain network or main currency.</summary>
	[DataMember]
	[Display(Name = "Main currency",
		Description = "Copper main currency identifying the blockchain network.",
		GroupName = LocalizedStrings.WithdrawKey, Order = 2)]
	public string MainCurrency
	{
		get => (string)Parameters.TryGetValue(nameof(MainCurrency));
		set => Parameters[nameof(MainCurrency)] = value?.Trim();
	}

	/// <summary>Optional network fee level.</summary>
	[DataMember]
	[Display(Name = "Fee level",
		Description = "Copper network fee level.",
		GroupName = LocalizedStrings.WithdrawKey, Order = 3)]
	public CopperFeeLevels? FeeLevel
	{
		get => (CopperFeeLevels?)Parameters.TryGetValue(nameof(FeeLevel));
		set => Parameters[nameof(FeeLevel)] = value;
	}

	/// <summary>Whether the fee is deducted from the withdrawal amount.</summary>
	[DataMember]
	[Display(Name = "Include fee",
		Description = "Deduct the network fee from the withdrawal amount.",
		GroupName = LocalizedStrings.WithdrawKey, Order = 4)]
	public bool IsFeeIncluded
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsFeeIncluded)) ?? false;
		set => Parameters[nameof(IsFeeIncluded)] = value;
	}

	/// <summary>Destination memo or tag.</summary>
	[DataMember]
	[Display(Name = "Memo",
		Description = "Destination memo or tag.",
		GroupName = LocalizedStrings.WithdrawKey, Order = 5)]
	public string Memo
	{
		get => (string)Parameters.TryGetValue(nameof(Memo));
		set => Parameters[nameof(Memo)] = value?.Trim();
	}

	/// <summary>Operator-visible transfer description.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DescriptionKey,
		Description = LocalizedStrings.DescriptionKey,
		GroupName = LocalizedStrings.WithdrawKey, Order = 6)]
	public string Description
	{
		get => (string)Parameters.TryGetValue(nameof(Description));
		set => Parameters[nameof(Description)] = value?.Trim();
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new CopperOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}

namespace StockSharp.Anchorage;

/// <summary>Anchorage trading, transfer, and staking parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AnchorageKey)]
public sealed class AnchorageOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Native operation kind.</summary>
	[DataMember]
	[Display(
		Name = "Operation",
		GroupName = "Anchorage",
		Order = 0)]
	public AnchorageOperations Operation
	{
		get => (AnchorageOperations?)Parameters.TryGetValue(nameof(Operation)) ??
			AnchorageOperations.Trade;
		set => Parameters[nameof(Operation)] = value;
	}

	/// <summary>Optional native trading order type.</summary>
	[DataMember]
	[Display(
		Name = "Native order type",
		GroupName = "Anchorage",
		Order = 1)]
	public AnchorageNativeOrderTypes? NativeOrderType
	{
		get => (AnchorageNativeOrderTypes?)Parameters.TryGetValue(
			nameof(NativeOrderType));
		set => Parameters[nameof(NativeOrderType)] = value;
	}

	/// <summary>Currency in which trading quantity is specified.</summary>
	[DataMember]
	[Display(
		Name = "Quantity currency",
		GroupName = "Anchorage",
		Order = 2)]
	public string QuantityCurrency
	{
		get => (string)Parameters.TryGetValue(nameof(QuantityCurrency));
		set => Parameters[nameof(QuantityCurrency)] = value?.Trim();
	}

	/// <summary>Stop or take-profit trigger price.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey,
		GroupName = "Anchorage", Order = 3)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Optional UTC expiration for advanced orders.</summary>
	[DataMember]
	[Display(
		Name = "End time",
		GroupName = "Anchorage",
		Order = 4)]
	public DateTime? EndTime
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(EndTime));
		set => Parameters[nameof(EndTime)] = value;
	}

	/// <summary>Explicit source wallet ID for custody operations.</summary>
	[DataMember]
	[Display(Name = "Source wallet", GroupName = LocalizedStrings.WithdrawKey,
		Order = 0)]
	public string SourceWalletId
	{
		get => (string)Parameters.TryGetValue(nameof(SourceWalletId));
		set => Parameters[nameof(SourceWalletId)] = value?.Trim();
	}

	/// <summary>Destination resource kind.</summary>
	[DataMember]
	[Display(Name = "Destination type",
		GroupName = LocalizedStrings.WithdrawKey, Order = 1)]
	public AnchorageResourceTypes DestinationType
	{
		get => (AnchorageResourceTypes?)Parameters.TryGetValue(
			nameof(DestinationType)) ?? AnchorageResourceTypes.Wallet;
		set => Parameters[nameof(DestinationType)] = value;
	}

	/// <summary>Destination resource identifier.</summary>
	[DataMember]
	[Display(Name = "Destination ID",
		GroupName = LocalizedStrings.WithdrawKey, Order = 2)]
	public string DestinationId
	{
		get => (string)Parameters.TryGetValue(nameof(DestinationId));
		set => Parameters[nameof(DestinationId)] = value?.Trim();
	}

	/// <summary>Transfer memo.</summary>
	[DataMember]
	[Display(Name = "Memo", GroupName = LocalizedStrings.WithdrawKey,
		Order = 3)]
	public string Memo
	{
		get => (string)Parameters.TryGetValue(nameof(Memo));
		set => Parameters[nameof(Memo)] = value?.Trim();
	}

	/// <summary>Deduct a same-asset network fee from the amount.</summary>
	[DataMember]
	[Display(Name = "Deduct fee", GroupName = LocalizedStrings.WithdrawKey,
		Order = 4)]
	public bool IsFeeDeducted
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsFeeDeducted)) ?? false;
		set => Parameters[nameof(IsFeeDeducted)] = value;
	}

	/// <summary>Use the Anchorage gas station when available.</summary>
	[DataMember]
	[Display(Name = "Use gas station",
		GroupName = LocalizedStrings.WithdrawKey, Order = 5)]
	public bool IsGasStationUsed
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsGasStationUsed)) ?? false;
		set => Parameters[nameof(IsGasStationUsed)] = value;
	}

	/// <summary>Existing staking position identifier.</summary>
	[DataMember]
	[Display(
		Name = "Staking position",
		GroupName = "Staking",
		Order = 0)]
	public string StakingPositionId
	{
		get => (string)Parameters.TryGetValue(nameof(StakingPositionId));
		set => Parameters[nameof(StakingPositionId)] = value?.Trim();
	}

	/// <summary>Staking provider.</summary>
	[DataMember]
	[Display(
		Name = "Staking provider",
		GroupName = "Staking",
		Order = 1)]
	public AnchorageStakingProviders StakingProvider
	{
		get => (AnchorageStakingProviders?)Parameters.TryGetValue(
			nameof(StakingProvider)) ?? AnchorageStakingProviders.Unknown;
		set => Parameters[nameof(StakingProvider)] = value;
	}

	/// <summary>Provider delegation address.</summary>
	[DataMember]
	[Display(
		Name = "Provider address",
		GroupName = "Staking",
		Order = 2)]
	public string StakingProviderAddress
	{
		get => (string)Parameters.TryGetValue(nameof(StakingProviderAddress));
		set => Parameters[nameof(StakingProviderAddress)] = value?.Trim();
	}

	/// <summary>Ethereum validator credential type.</summary>
	[DataMember]
	[Display(
		Name = "Validator type",
		GroupName = "Staking",
		Order = 3)]
	public AnchorageValidatorTypes? ValidatorType
	{
		get => (AnchorageValidatorTypes?)Parameters.TryGetValue(
			nameof(ValidatorType));
		set => Parameters[nameof(ValidatorType)] = value;
	}

	/// <summary>Unstake the complete position.</summary>
	[DataMember]
	[Display(
		Name = "Full amount",
		GroupName = "Staking",
		Order = 4)]
	public bool IsFullAmount
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsFullAmount)) ?? false;
		set => Parameters[nameof(IsFullAmount)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new AnchorageOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}

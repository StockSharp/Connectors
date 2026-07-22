namespace StockSharp.Deriv;

/// <summary>Deriv contract parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DerivKey)]
public sealed class DerivOrderCondition : OrderCondition,
	IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>Native Deriv contract type.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionContractTypeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public DerivContractTypes? ContractType
	{
		get => Parameters.TryGetValue(nameof(ContractType))?.To<DerivContractTypes?>();
		set => Parameters[nameof(ContractType)] = value;
	}

	/// <summary>Whether amount represents stake or payout.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderVolumeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public DerivBasisTypes Basis
	{
		get => Parameters.TryGetValue(nameof(Basis))?.To<DerivBasisTypes?>() ?? DerivBasisTypes.Stake;
		set => Parameters[nameof(Basis)] = value;
	}

	/// <summary>Account currency used to price the contract.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CurrencyKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public string Currency
	{
		get => Parameters.TryGetValue(nameof(Currency))?.To<string>().IsEmpty("USD");
		set => Parameters[nameof(Currency)] = value;
	}

	/// <summary>Contract duration.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DurationKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public int Duration
	{
		get => Parameters.TryGetValue(nameof(Duration))?.To<int?>() ?? 1;
		set => Parameters[nameof(Duration)] = value;
	}

	/// <summary>Contract duration unit.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DurationKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 4)]
	public DerivDurationUnits DurationUnit
	{
		get => Parameters.TryGetValue(nameof(DurationUnit))?.To<DerivDurationUnits?>() ?? DerivDurationUnits.Days;
		set => Parameters[nameof(DurationUnit)] = value;
	}

	/// <summary>Explicit UTC expiry time. When set, it takes precedence over duration.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExpiryDateKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 5)]
	public DateTime? ExpiryDate
	{
		get => Parameters.TryGetValue(nameof(ExpiryDate))?.To<DateTime?>();
		set => Parameters[nameof(ExpiryDate)] = value?.ToUniversalTime();
	}

	/// <summary>Primary absolute or relative barrier.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ActivationPriceKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 6)]
	public string Barrier
	{
		get => Parameters.TryGetValue(nameof(Barrier))?.To<string>();
		set => Parameters[nameof(Barrier)] = value;
	}

	/// <summary>Second absolute or relative barrier.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ActivationPriceKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 7)]
	public string Barrier2
	{
		get => Parameters.TryGetValue(nameof(Barrier2))?.To<string>();
		set => Parameters[nameof(Barrier2)] = value;
	}

	/// <summary>Multiplier for multiplier contracts.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MultiplierKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 8)]
	public decimal? Multiplier
	{
		get => Parameters.TryGetValue(nameof(Multiplier))?.To<decimal?>();
		set => Parameters[nameof(Multiplier)] = value;
	}

	/// <summary>Accumulator growth rate.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ChangeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 9)]
	public decimal? GrowthRate
	{
		get => Parameters.TryGetValue(nameof(GrowthRate))?.To<decimal?>();
		set => Parameters[nameof(GrowthRate)] = value;
	}

	/// <summary>Deal-cancellation duration for supported multiplier contracts.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CancellationKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 10)]
	public string Cancellation
	{
		get => Parameters.TryGetValue(nameof(Cancellation))?.To<string>();
		set => Parameters[nameof(Cancellation)] = value;
	}

	/// <summary>Selected tick for tick-high and tick-low contracts.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SelectedKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 11)]
	public int? SelectedTick
	{
		get => Parameters.TryGetValue(nameof(SelectedTick))?.To<int?>();
		set => Parameters[nameof(SelectedTick)] = value;
	}

	/// <summary>Payout per point for supported contracts.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceStepKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 12)]
	public decimal? PayoutPerPoint
	{
		get => Parameters.TryGetValue(nameof(PayoutPerPoint))?.To<decimal?>();
		set => Parameters[nameof(PayoutPerPoint)] = value;
	}

	/// <summary>Maximum loss amount at which supported contracts are closed.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 13)]
	public decimal? StopLoss
	{
		get => Parameters.TryGetValue(nameof(StopLoss))?.To<decimal?>();
		set => Parameters[nameof(StopLoss)] = value;
	}

	/// <summary>Target profit amount at which supported contracts are closed.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 14)]
	public decimal? TakeProfit
	{
		get => Parameters.TryGetValue(nameof(TakeProfit))?.To<decimal?>();
		set => Parameters[nameof(TakeProfit)] = value;
	}

	/// <summary>Optional proposal identifier obtained outside the adapter.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IdentifierKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 15)]
	public string ProposalId
	{
		get => Parameters.TryGetValue(nameof(ProposalId))?.To<string>();
		set => Parameters[nameof(ProposalId)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopLoss;
		set => StopLoss = value;
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set
		{
			if (value)
				throw new NotSupportedException("Deriv contracts do not expose trailing stop-loss orders.");
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => TakeProfit;
		set => TakeProfit = value;
	}
}

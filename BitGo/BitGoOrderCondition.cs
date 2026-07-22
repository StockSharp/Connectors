namespace StockSharp.BitGo;

/// <summary>BitGo Prime order-specific parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitGoKey)]
public sealed class BitGoOrderCondition : OrderCondition
{
	/// <summary>Native order type. Leave empty for automatic mapping.</summary>
	[DataMember]
	[Display(
		Name = "Native type",
		GroupName = "BitGo",
		Order = 0)]
	public BitGoOrderTypes? NativeType
	{
		get => (BitGoOrderTypes?)Parameters.TryGetValue(nameof(NativeType));
		set => Parameters[nameof(NativeType)] = value;
	}

	/// <summary>Funding source.</summary>
	[DataMember]
	[Display(
		Name = "Funding type",
		GroupName = "BitGo",
		Order = 1)]
	public BitGoFundingTypes FundingType
	{
		get => (BitGoFundingTypes?)Parameters.TryGetValue(nameof(FundingType)) ??
			BitGoFundingTypes.Funded;
		set => Parameters[nameof(FundingType)] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey,
		GroupName = "BitGo", Order = 2)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>TWAP duration.</summary>
	[DataMember]
	[Display(
		Name = "TWAP duration",
		GroupName = "BitGo",
		Order = 3)]
	public TimeSpan? TwapDuration
	{
		get => (TimeSpan?)Parameters.TryGetValue(nameof(TwapDuration));
		set => Parameters[nameof(TwapDuration)] = value;
	}

	/// <summary>Use time-sliced TWAP execution.</summary>
	[DataMember]
	[Display(
		Name = "Time sliced",
		GroupName = "BitGo",
		Order = 4)]
	public bool IsTimeSliced
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTimeSliced)) ?? false;
		set => Parameters[nameof(IsTimeSliced)] = value;
	}

	/// <summary>TWAP slice interval.</summary>
	[DataMember]
	[Display(
		Name = "TWAP interval",
		GroupName = "BitGo",
		Order = 5)]
	public TimeSpan? TwapInterval
	{
		get => (TimeSpan?)Parameters.TryGetValue(nameof(TwapInterval));
		set => Parameters[nameof(TwapInterval)] = value;
	}

	/// <summary>Regular TWAP progression bounds.</summary>
	[DataMember]
	[Display(
		Name = "Bounds control",
		GroupName = "BitGo",
		Order = 6)]
	public BitGoBoundsControls BoundsControl
	{
		get => (BitGoBoundsControls?)Parameters.TryGetValue(
			nameof(BoundsControl)) ?? BitGoBoundsControls.Standard;
		set => Parameters[nameof(BoundsControl)] = value;
	}

	/// <summary>Steady Pace interval value.</summary>
	[DataMember]
	[Display(
		Name = "Steady Pace interval",
		GroupName = "BitGo",
		Order = 7)]
	public int? SteadyPaceInterval
	{
		get => (int?)Parameters.TryGetValue(nameof(SteadyPaceInterval));
		set => Parameters[nameof(SteadyPaceInterval)] = value;
	}

	/// <summary>Steady Pace interval unit.</summary>
	[DataMember]
	[Display(
		Name = "Interval unit",
		GroupName = "BitGo",
		Order = 8)]
	public BitGoIntervalUnits IntervalUnit
	{
		get => (BitGoIntervalUnits?)Parameters.TryGetValue(nameof(IntervalUnit)) ??
			BitGoIntervalUnits.Minute;
		set => Parameters[nameof(IntervalUnit)] = value;
	}

	/// <summary>Steady Pace child-order size.</summary>
	[DataMember]
	[Display(
		Name = "Sub-order size",
		GroupName = "BitGo",
		Order = 9)]
	public decimal? SubOrderSize
	{
		get => (decimal?)Parameters.TryGetValue(nameof(SubOrderSize));
		set => Parameters[nameof(SubOrderSize)] = value;
	}

	/// <summary>Steady Pace size variance from zero to one.</summary>
	[DataMember]
	[Display(
		Name = "Variance",
		GroupName = "BitGo",
		Order = 10)]
	public decimal? Variance
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Variance));
		set => Parameters[nameof(Variance)] = value;
	}

	/// <summary>Optional UTC execution schedule.</summary>
	[DataMember]
	[Display(
		Name = "Scheduled date",
		GroupName = "BitGo",
		Order = 11)]
	public DateTime? ScheduledDate
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(ScheduledDate));
		set => Parameters[nameof(ScheduledDate)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new BitGoOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}

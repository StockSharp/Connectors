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
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NativeTypeKey,
		GroupName = LocalizedStrings.BitGoKey,
		Order = 0)]
	public BitGoOrderTypes? NativeType
	{
		get => (BitGoOrderTypes?)Parameters.TryGetValue(nameof(NativeType));
		set => Parameters[nameof(NativeType)] = value;
	}

	/// <summary>Funding source.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FundingTypeKey,
		GroupName = LocalizedStrings.BitGoKey,
		Order = 1)]
	public BitGoFundingTypes FundingType
	{
		get => (BitGoFundingTypes?)Parameters.TryGetValue(nameof(FundingType)) ??
			BitGoFundingTypes.Funded;
		set => Parameters[nameof(FundingType)] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey,
		GroupName = LocalizedStrings.BitGoKey,
		Order = 2)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>TWAP duration.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TwapDurationKey,
		GroupName = LocalizedStrings.BitGoKey,
		Order = 3)]
	public TimeSpan? TwapDuration
	{
		get => (TimeSpan?)Parameters.TryGetValue(nameof(TwapDuration));
		set => Parameters[nameof(TwapDuration)] = value;
	}

	/// <summary>Use time-sliced TWAP execution.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeSlicedKey,
		GroupName = LocalizedStrings.BitGoKey,
		Order = 4)]
	public bool IsTimeSliced
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTimeSliced)) ?? false;
		set => Parameters[nameof(IsTimeSliced)] = value;
	}

	/// <summary>TWAP slice interval.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TwapIntervalLabelKey,
		GroupName = LocalizedStrings.BitGoKey,
		Order = 5)]
	public TimeSpan? TwapInterval
	{
		get => (TimeSpan?)Parameters.TryGetValue(nameof(TwapInterval));
		set => Parameters[nameof(TwapInterval)] = value;
	}

	/// <summary>Regular TWAP progression bounds.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoundsControlKey,
		GroupName = LocalizedStrings.BitGoKey,
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
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SteadyPaceIntervalKey,
		GroupName = LocalizedStrings.BitGoKey,
		Order = 7)]
	public int? SteadyPaceInterval
	{
		get => (int?)Parameters.TryGetValue(nameof(SteadyPaceInterval));
		set => Parameters[nameof(SteadyPaceInterval)] = value;
	}

	/// <summary>Steady Pace interval unit.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalUnitKey,
		GroupName = LocalizedStrings.BitGoKey,
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
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SubOrderSizeKey,
		GroupName = LocalizedStrings.BitGoKey,
		Order = 9)]
	public decimal? SubOrderSize
	{
		get => (decimal?)Parameters.TryGetValue(nameof(SubOrderSize));
		set => Parameters[nameof(SubOrderSize)] = value;
	}

	/// <summary>Steady Pace size variance from zero to one.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VarianceKey,
		GroupName = LocalizedStrings.BitGoKey,
		Order = 10)]
	public decimal? Variance
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Variance));
		set => Parameters[nameof(Variance)] = value;
	}

	/// <summary>Optional UTC execution schedule.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ScheduledDateKey,
		GroupName = LocalizedStrings.BitGoKey,
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

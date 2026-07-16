namespace StockSharp.CQG;

/// <summary>CQG-specific order condition.</summary>
[DataContract]
[Serializable]
public class CqgOrderCondition : OrderCondition
{
	private const string _nativeOrderType = "NativeOrderType";
	private const string _stopPrice = "StopPrice";
	private const string _duration = "Duration";
	private const string _instructions = "Instructions";
	private const string _visibleVolume = "VisibleVolume";
	private const string _triggerVolume = "TriggerVolume";
	private const string _trailOffset = "TrailOffset";

	/// <summary>Optional native order type override.</summary>
	[DataMember]
	public CqgOrderTypes? NativeOrderType
	{
		get => Parameters.TryGetValue(_nativeOrderType)?.To<CqgOrderTypes?>();
		set => Parameters[_nativeOrderType] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[DataMember]
	public decimal? StopPrice
	{
		get => Parameters.TryGetValue(_stopPrice)?.To<decimal?>();
		set => Parameters[_stopPrice] = value;
	}

	/// <summary>Native order duration.</summary>
	[DataMember]
	public CqgOrderDurations Duration
	{
		get => Parameters.TryGetValue(_duration)?.To<CqgOrderDurations>() ?? CqgOrderDurations.Day;
		set => Parameters[_duration] = value;
	}

	/// <summary>Additional native execution instructions.</summary>
	[DataMember]
	public CqgExecutionInstructions Instructions
	{
		get => Parameters.TryGetValue(_instructions)?.To<CqgExecutionInstructions>() ?? CqgExecutionInstructions.None;
		set => Parameters[_instructions] = value;
	}

	/// <summary>Visible iceberg volume.</summary>
	[DataMember]
	public decimal? VisibleVolume
	{
		get => Parameters.TryGetValue(_visibleVolume)?.To<decimal?>();
		set => Parameters[_visibleVolume] = value;
	}

	/// <summary>Quantity-trigger threshold.</summary>
	[DataMember]
	public decimal? TriggerVolume
	{
		get => Parameters.TryGetValue(_triggerVolume)?.To<decimal?>();
		set => Parameters[_triggerVolume] = value;
	}

	/// <summary>Trailing offset in scaled price units.</summary>
	[DataMember]
	public decimal? TrailOffset
	{
		get => Parameters.TryGetValue(_trailOffset)?.To<decimal?>();
		set => Parameters[_trailOffset] = value;
	}
}

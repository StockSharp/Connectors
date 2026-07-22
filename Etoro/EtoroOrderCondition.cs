namespace StockSharp.Etoro;

/// <summary>eToro unified-order parameters.</summary>
[DataContract]
[Serializable]
public class EtoroOrderCondition : OrderCondition
{
	private const string _settlementType = "SettlementType";
	private const string _leverage = "Leverage";
	private const string _volumeMode = "VolumeMode";
	private const string _orderCurrency = "OrderCurrency";
	private const string _stopLossRate = "StopLossRate";
	private const string _takeProfitRate = "TakeProfitRate";
	private const string _isTrailingStopLoss = "IsTrailingStopLoss";
	private const string _positionId = "PositionId";
	private const string _additionalMargin = "AdditionalMargin";

	/// <summary>Settlement type used when opening a position.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SettlementKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public EtoroSettlementTypes SettlementType
	{
		get => Parameters.TryGetValue(_settlementType)?.To<EtoroSettlementTypes>() ?? EtoroSettlementTypes.Real;
		set => Parameters[_settlementType] = value;
	}

	/// <summary>Position leverage used when opening a position.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	public int Leverage
	{
		get => Parameters.TryGetValue(_leverage)?.To<int?>() ?? 1;
		set => Parameters[_leverage] = value;
	}

	/// <summary>Interpretation of the StockSharp order volume.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ModeKey,
		GroupName = LocalizedStrings.VolumeKey,
		Order = 2)]
	public EtoroVolumeModes VolumeMode
	{
		get => Parameters.TryGetValue(_volumeMode)?.To<EtoroVolumeModes>() ?? EtoroVolumeModes.Units;
		set => Parameters[_volumeMode] = value;
	}

	/// <summary>ISO currency used when <see cref="VolumeMode"/> is <see cref="EtoroVolumeModes.Amount"/>.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CurrencyKey,
		GroupName = LocalizedStrings.VolumeKey,
		Order = 3)]
	public string OrderCurrency
	{
		get => Parameters.TryGetValue(_orderCurrency)?.To<string>().IsEmpty("USD");
		set => Parameters[_orderCurrency] = value;
	}

	/// <summary>Absolute stop-loss rate.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey,
		GroupName = LocalizedStrings.StopKey,
		Order = 4)]
	public decimal? StopLossRate
	{
		get => Parameters.TryGetValue(_stopLossRate)?.To<decimal?>();
		set => Parameters[_stopLossRate] = value;
	}

	/// <summary>Absolute take-profit rate.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.LimitKey,
		Order = 5)]
	public decimal? TakeProfitRate
	{
		get => Parameters.TryGetValue(_takeProfitRate)?.To<decimal?>();
		set => Parameters[_takeProfitRate] = value;
	}

	/// <summary>Use a trailing stop-loss.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TrailingKey,
		GroupName = LocalizedStrings.StopKey,
		Order = 6)]
	public bool IsTrailingStopLoss
	{
		get => Parameters.TryGetValue(_isTrailingStopLoss)?.To<bool>() == true;
		set => Parameters[_isTrailingStopLoss] = value;
	}

	/// <summary>Position identifier to close. Leave empty to open a new position.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 7)]
	public long? PositionId
	{
		get => Parameters.TryGetValue(_positionId)?.To<long?>();
		set => Parameters[_positionId] = value;
	}

	/// <summary>Additional margin in account currency.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarginKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 8)]
	public decimal? AdditionalMargin
	{
		get => Parameters.TryGetValue(_additionalMargin)?.To<decimal?>();
		set => Parameters[_additionalMargin] = value;
	}
}

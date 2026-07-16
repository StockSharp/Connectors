namespace StockSharp.Longbridge;

/// <summary>Longbridge-specific order condition.</summary>
[DataContract]
[Serializable]
public class LongbridgeOrderCondition : OrderCondition
{
	private const string _orderType = "OrderType";
	private const string _triggerPrice = "TriggerPrice";
	private const string _limitOffset = "LimitOffset";
	private const string _trailingAmount = "TrailingAmount";
	private const string _trailingPercent = "TrailingPercent";
	private const string _outsideRth = "OutsideRth";
	private const string _timeInForce = "TimeInForce";
	private const string _remark = "Remark";

	/// <summary>Optional native order type override.</summary>
	[DataMember]
	public LongbridgeOrderTypes? NativeOrderType
	{
		get => Parameters.TryGetValue(_orderType)?.To<LongbridgeOrderTypes?>();
		set => Parameters[_orderType] = value;
	}

	/// <summary>Conditional-order trigger price.</summary>
	[DataMember]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
		set => Parameters[_triggerPrice] = value;
	}

	/// <summary>Trailing limit offset.</summary>
	[DataMember]
	public decimal? LimitOffset
	{
		get => Parameters.TryGetValue(_limitOffset)?.To<decimal?>();
		set => Parameters[_limitOffset] = value;
	}

	/// <summary>Trailing distance expressed as an amount.</summary>
	[DataMember]
	public decimal? TrailingAmount
	{
		get => Parameters.TryGetValue(_trailingAmount)?.To<decimal?>();
		set => Parameters[_trailingAmount] = value;
	}

	/// <summary>Trailing distance expressed as a percentage.</summary>
	[DataMember]
	public decimal? TrailingPercent
	{
		get => Parameters.TryGetValue(_trailingPercent)?.To<decimal?>();
		set => Parameters[_trailingPercent] = value;
	}

	/// <summary>Outside-regular-hours policy.</summary>
	[DataMember]
	public LongbridgeOutsideRths OutsideRth
	{
		get => Parameters.TryGetValue(_outsideRth)?.To<LongbridgeOutsideRths>() ?? LongbridgeOutsideRths.RegularOnly;
		set => Parameters[_outsideRth] = value;
	}

	/// <summary>Native time-in-force policy.</summary>
	[DataMember]
	public LongbridgeTimeInForces NativeTimeInForce
	{
		get => Parameters.TryGetValue(_timeInForce)?.To<LongbridgeTimeInForces>() ?? LongbridgeTimeInForces.Day;
		set => Parameters[_timeInForce] = value;
	}

	/// <summary>Optional broker-visible order remark.</summary>
	[DataMember]
	public string Remark
	{
		get => Parameters.TryGetValue(_remark)?.ToString();
		set => Parameters[_remark] = value;
	}
}

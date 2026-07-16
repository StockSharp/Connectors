namespace StockSharp.Questrade;

/// <summary>Questrade specific order condition.</summary>
[DataContract]
[Serializable]
public class QuestradeOrderCondition : OrderCondition
{
	private const string _stopPrice = "StopPrice";
	private const string _duration = "Duration";
	private const string _side = "Side";
	private const string _icebergQuantity = "IcebergQuantity";
	private const string _allOrNone = "AllOrNone";
	private const string _anonymous = "Anonymous";
	private const string _primaryRoute = "PrimaryRoute";
	private const string _secondaryRoute = "SecondaryRoute";

	/// <summary>Stop activation price.</summary>
	[DataMember]
	public decimal? StopPrice
	{
		get => Parameters.TryGetValue(_stopPrice)?.To<decimal?>();
		set => Parameters[_stopPrice] = value;
	}

	/// <summary>Native order duration.</summary>
	[DataMember]
	public QuestradeOrderDurations Duration
	{
		get => Parameters.TryGetValue(_duration)?.To<QuestradeOrderDurations>() ?? QuestradeOrderDurations.Day;
		set => Parameters[_duration] = value;
	}

	/// <summary>Native side used for short sales and option position effects.</summary>
	[DataMember]
	public QuestradeOrderSides? NativeSide
	{
		get => Parameters.TryGetValue(_side)?.To<QuestradeOrderSides?>();
		set => Parameters[_side] = value;
	}

	/// <summary>Visible quantity for an iceberg order.</summary>
	[DataMember]
	public decimal? IcebergQuantity
	{
		get => Parameters.TryGetValue(_icebergQuantity)?.To<decimal?>();
		set => Parameters[_icebergQuantity] = value;
	}

	/// <summary>Whether the order is all-or-none.</summary>
	[DataMember]
	public bool AllOrNone
	{
		get => Parameters.TryGetValue(_allOrNone)?.To<bool>() == true;
		set => Parameters[_allOrNone] = value;
	}

	/// <summary>Whether the order is anonymous.</summary>
	[DataMember]
	public bool Anonymous
	{
		get => Parameters.TryGetValue(_anonymous)?.To<bool>() == true;
		set => Parameters[_anonymous] = value;
	}

	/// <summary>Primary route. The default is <c>AUTO</c>.</summary>
	[DataMember]
	public string PrimaryRoute
	{
		get => Parameters.TryGetValue(_primaryRoute)?.ToString();
		set => Parameters[_primaryRoute] = value;
	}

	/// <summary>Secondary route. The default is <c>AUTO</c>.</summary>
	[DataMember]
	public string SecondaryRoute
	{
		get => Parameters.TryGetValue(_secondaryRoute)?.ToString();
		set => Parameters[_secondaryRoute] = value;
	}
}

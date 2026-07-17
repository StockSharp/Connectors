namespace StockSharp.MotilalOswal;

/// <summary>Motilal Oswal specific order condition.</summary>
[DataContract]
[Serializable]
public class MotilalOswalOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _duration = "Duration";
	private const string _triggerPrice = "TriggerPrice";
	private const string _disclosedVolume = "DisclosedVolume";
	private const string _isAfterMarket = "IsAfterMarket";
	private const string _goodTillDate = "GoodTillDate";
	private const string _tag = "Tag";
	private const string _participantCode = "ParticipantCode";

	/// <summary>Order product.</summary>
	[DataMember]
	public MotilalOswalProducts? Product
	{
		get => Parameters.TryGetValue(_product)?.To<MotilalOswalProducts?>();
		set => Parameters[_product] = value;
	}

	/// <summary>Order duration.</summary>
	[DataMember]
	public MotilalOswalOrderDurations? Duration
	{
		get => Parameters.TryGetValue(_duration)?.To<MotilalOswalOrderDurations?>();
		set => Parameters[_duration] = value;
	}

	/// <summary>Stop-loss trigger price.</summary>
	[DataMember]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
		set => Parameters[_triggerPrice] = value;
	}

	/// <summary>Quantity disclosed to the market.</summary>
	[DataMember]
	public decimal? DisclosedVolume
	{
		get => Parameters.TryGetValue(_disclosedVolume)?.To<decimal?>();
		set => Parameters[_disclosedVolume] = value;
	}

	/// <summary>Whether the order is an after-market order.</summary>
	[DataMember]
	public bool IsAfterMarket
	{
		get => Parameters.TryGetValue(_isAfterMarket)?.To<bool>() == true;
		set => Parameters[_isAfterMarket] = value;
	}

	/// <summary>Good-till date in the Indian market calendar.</summary>
	[DataMember]
	public DateTime? GoodTillDate
	{
		get => Parameters.TryGetValue(_goodTillDate)?.To<DateTime?>();
		set => Parameters[_goodTillDate] = value;
	}

	/// <summary>Client tag echoed by the API. The protocol limit is ten characters.</summary>
	[DataMember]
	public string Tag
	{
		get => Parameters.TryGetValue(_tag)?.ToString();
		set => Parameters[_tag] = value;
	}

	/// <summary>Exchange participant code.</summary>
	[DataMember]
	public string ParticipantCode
	{
		get => Parameters.TryGetValue(_participantCode)?.ToString();
		set => Parameters[_participantCode] = value;
	}
}

namespace StockSharp.Breeze;

/// <summary>ICICI Direct Breeze order condition.</summary>
[Serializable]
[DataContract]
public class BreezeOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _triggerPrice = "TriggerPrice";
	private const string _disclosedVolume = "DisclosedVolume";
	private const string _userRemark = "UserRemark";

	/// <summary>Order product. When omitted, it is inferred from the instrument.</summary>
	[DataMember]
	public BreezeProducts? Product
	{
		get => Parameters.TryGetValue(_product)?.To<BreezeProducts?>();
		set => Parameters[_product] = value;
	}

	/// <summary>Stop-loss trigger price.</summary>
	[DataMember]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
		set => Parameters[_triggerPrice] = value;
	}

	/// <summary>Disclosed volume.</summary>
	[DataMember]
	public decimal? DisclosedVolume
	{
		get => Parameters.TryGetValue(_disclosedVolume)?.To<decimal?>();
		set => Parameters[_disclosedVolume] = value;
	}

	/// <summary>User remark sent with the order.</summary>
	[DataMember]
	public string UserRemark
	{
		get => Parameters.TryGetValue(_userRemark)?.ToString();
		set => Parameters[_userRemark] = value;
	}
}

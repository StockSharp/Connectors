namespace StockSharp.Zebu;

/// <summary>Zebu MYNT-specific order condition.</summary>
[DataContract]
[Serializable]
public class ZebuOrderCondition : NorenOrderCondition
{
	/// <summary>Order product.</summary>
	[DataMember]
	public NorenProducts? Product
	{
		get => NorenProduct;
		set => NorenProduct = value;
	}
}

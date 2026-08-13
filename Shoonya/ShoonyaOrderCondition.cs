namespace StockSharp.Shoonya;

/// <summary>Shoonya-specific order condition.</summary>
[DataContract]
[Serializable]
public class ShoonyaOrderCondition : NorenOrderCondition
{
	/// <summary>Order product.</summary>
	[DataMember]
	public ShoonyaProducts? Product
	{
		get => NorenProduct is { } product ? (ShoonyaProducts)product : null;
		set => NorenProduct = value is { } product ? (NorenProducts)product : null;
	}
}

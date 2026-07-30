namespace StockSharp.Shoonya;

/// <summary>Shoonya-specific order condition.</summary>
[DataContract]
[Serializable]
public class ShoonyaOrderCondition : NorenOrderCondition
{
	/// <summary>Order product.</summary>
	public new ShoonyaProducts? Product
	{
		get => base.Product is { } product ? (ShoonyaProducts)product : null;
		set => base.Product = value is { } product ? (NorenProducts)product : null;
	}
}

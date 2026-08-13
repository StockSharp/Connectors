namespace StockSharp.Noren;

/// <summary>Noren-specific order condition.</summary>
[DataContract]
[Serializable]
public abstract class NorenOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _triggerPrice = "TriggerPrice";
	private const string _disclosedVolume = "DisclosedVolume";
	private const string _isAfterMarket = "IsAfterMarket";
	private const string _stopLossPrice = "StopLossPrice";
	private const string _profitPrice = "ProfitPrice";
	private const string _trailingPrice = "TrailingPrice";
	private const string _remarks = "Remarks";

	/// <summary>Initializes a new instance of the <see cref="NorenOrderCondition"/>.</summary>
	protected NorenOrderCondition()
	{
	}

	/// <summary>Order product represented by the shared Noren protocol.</summary>
	protected internal NorenProducts? NorenProduct
	{
		get
		{
			var product = Parameters.TryGetValue(_product);

			if (product is null)
				return null;
			if (product is NorenProducts norenProduct)
				return norenProduct;
			if (Enum.TryParse<NorenProducts>(
				product.ToString(), true, out norenProduct))
				return norenProduct;

			return product.To<NorenProducts?>();
		}
		set => Parameters[_product] = value;
	}

	/// <summary>Stop-order trigger price.</summary>
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

	/// <summary>Whether the order is placed after regular market hours.</summary>
	[DataMember]
	public bool IsAfterMarket
	{
		get => Parameters.TryGetValue(_isAfterMarket)?.To<bool>() == true;
		set => Parameters[_isAfterMarket] = value;
	}

	/// <summary>Differential stop-loss price for cover and bracket orders.</summary>
	[DataMember]
	public decimal? StopLossPrice
	{
		get => Parameters.TryGetValue(_stopLossPrice)?.To<decimal?>();
		set => Parameters[_stopLossPrice] = value;
	}

	/// <summary>Differential profit price for bracket orders.</summary>
	[DataMember]
	public decimal? ProfitPrice
	{
		get => Parameters.TryGetValue(_profitPrice)?.To<decimal?>();
		set => Parameters[_profitPrice] = value;
	}

	/// <summary>Trailing stop-loss price.</summary>
	[DataMember]
	public decimal? TrailingPrice
	{
		get => Parameters.TryGetValue(_trailingPrice)?.To<decimal?>();
		set => Parameters[_trailingPrice] = value;
	}

	/// <summary>Client remarks echoed by Noren.</summary>
	[DataMember]
	public string Remarks
	{
		get => Parameters.TryGetValue(_remarks)?.ToString();
		set => Parameters[_remarks] = value;
	}
}

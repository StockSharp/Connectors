namespace StockSharp.PhillipPoems;

/// <summary>POEMS funding source for a stock order.</summary>
[DataContract]
public enum PhillipPoemsPaymentModes
{
	/// <summary>Cash.</summary>
	[EnumMember(Value = "0")]
	Cash = 0,

	/// <summary>Central Provident Fund.</summary>
	[EnumMember(Value = "1")]
	Cpf = 1,

	/// <summary>Supplementary Retirement Scheme.</summary>
	[EnumMember(Value = "2")]
	Srs = 2,
}

/// <summary>Additional parameters for Phillip POEMS stock orders.</summary>
[DataContract]
[Serializable]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PhillipPoemsKey)]
public sealed class PhillipPoemsOrderCondition : OrderCondition
{
	private decimal? _stopPrice;
	private PhillipPoemsPaymentModes _paymentMode;
	private string _settlementCurrency;
	private bool _isLimitIfTouched;
	private bool _isShortSale;

	/// <summary>Trigger price for SLO and LIT orders.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public decimal? StopPrice
	{
		get => _stopPrice;
		set
		{
			_stopPrice = value;
			Parameters[nameof(StopPrice)] = value;
		}
	}

	/// <summary>Order funding source.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PhillipPoemsPaymentKey,
		Description = LocalizedStrings.PhillipPoemsPaymentDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	public PhillipPoemsPaymentModes PaymentMode
	{
		get => _paymentMode;
		set
		{
			_paymentMode = value;
			Parameters[nameof(PaymentMode)] = value;
		}
	}

	/// <summary>ISO 4217 settlement currency overriding the connector default.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PhillipPoemsCurrencyKey,
		Description = LocalizedStrings.PhillipPoemsCurrencyDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	public string SettlementCurrency
	{
		get => _settlementCurrency;
		set
		{
			_settlementCurrency = value;
			Parameters[nameof(SettlementCurrency)] = value;
		}
	}

	/// <summary>Use LIT instead of SLO for a conditional order.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PhillipPoemsLimitIfTouchedKey,
		Description = LocalizedStrings.PhillipPoemsLimitIfTouchedDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public bool IsLimitIfTouched
	{
		get => _isLimitIfTouched;
		set
		{
			_isLimitIfTouched = value;
			Parameters[nameof(IsLimitIfTouched)] = value;
		}
	}

	/// <summary>Use the native short-sell action.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PhillipPoemsShortSaleKey,
		Description = LocalizedStrings.PhillipPoemsShortSaleDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 4)]
	public bool IsShortSale
	{
		get => _isShortSale;
		set
		{
			_isShortSale = value;
			Parameters[nameof(IsShortSale)] = value;
		}
	}
}

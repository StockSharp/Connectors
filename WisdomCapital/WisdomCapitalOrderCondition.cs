namespace StockSharp.WisdomCapital;

/// <summary>Wisdom Capital Trading API order condition.</summary>
public class WisdomCapitalOrderCondition : BaseWithdrawOrderCondition
{
    /// <summary>Trading product.</summary>
    [Display(
        Name = "Product",
        Description = "Wisdom Capital XTS trading product.",
        GroupName = LocalizedStrings.GeneralKey,
        Order = 0)]
    public WisdomCapitalProducts Product { get; set; } =
        WisdomCapitalProducts.Intraday;

    /// <summary>Stop trigger price.</summary>
    [Display(
        Name = "Trigger price",
        Description = "Stop-market or stop-limit trigger price.",
        GroupName = LocalizedStrings.GeneralKey,
        Order = 1)]
    public decimal? TriggerPrice { get; set; }

    /// <summary>Quantity disclosed to the exchange.</summary>
    [Display(
        Name = "Disclosed quantity",
        Description = "Quantity disclosed publicly for the order.",
        GroupName = LocalizedStrings.GeneralKey,
        Order = 2)]
    public decimal? DisclosedVolume { get; set; }

    /// <summary>Client-generated idempotency and audit identifier.</summary>
    [Display(
        Name = "Unique identifier",
        Description = "Order identifier passed to XTS for audit and duplicate tracking.",
        GroupName = LocalizedStrings.GeneralKey,
        Order = 3)]
    public string UniqueIdentifier { get; set; }
}

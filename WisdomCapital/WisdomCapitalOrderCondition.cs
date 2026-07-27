namespace StockSharp.WisdomCapital;

/// <summary>Wisdom Capital Trading API order condition.</summary>
public class WisdomCapitalOrderCondition : BaseWithdrawOrderCondition
{
    /// <summary>Trading product.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ProductKey,
        Description = LocalizedStrings.WisdomCapitalXtsTradingProductDescKey,
        GroupName = LocalizedStrings.GeneralKey,
        Order = 0)]
    public WisdomCapitalProducts Product { get; set; } =
        WisdomCapitalProducts.Intraday;

    /// <summary>Stop trigger price.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TriggerPriceKey,
        Description = LocalizedStrings.StopMarketOrStopLimitTriggerPriceDescKey,
        GroupName = LocalizedStrings.GeneralKey,
        Order = 1)]
    public decimal? TriggerPrice { get; set; }

    /// <summary>Quantity disclosed to the exchange.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DisclosedQuantityKey,
        Description = LocalizedStrings.QuantityDisclosedPubliclyForTheOrderDescKey,
        GroupName = LocalizedStrings.GeneralKey,
        Order = 2)]
    public decimal? DisclosedVolume { get; set; }

    /// <summary>Client-generated idempotency and audit identifier.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.UniqueIdentifierKey,
        Description = LocalizedStrings.OrderIdentifierPassedToXtsForAuditAndDuplicateTrackingDescKey,
        GroupName = LocalizedStrings.GeneralKey,
        Order = 3)]
    public string UniqueIdentifier { get; set; }
}

namespace StockSharp.Dnse;

/// <summary>
/// DNSE-specific order condition.
/// </summary>
[DataContract]
[Serializable]
public class DnseOrderCondition : OrderCondition
{
    private const string _loanPackageId = "LoanPackageId";
    private const string _nativeOrderType = "NativeOrderType";

    /// <summary>
    /// Loan package identifier returned by
    /// <c>GET /accounts/{accountNo}/loan-packages</c>.
    /// </summary>
    [DataMember]
    public int? LoanPackageId
    {
        get => Parameters.TryGetValue(_loanPackageId)?.To<int?>();
        set => Parameters[_loanPackageId] = value;
    }

    /// <summary>Native order type.</summary>
    [DataMember]
    public DnseOrderTypes NativeOrderType
    {
        get => Parameters.TryGetValue(_nativeOrderType)?
            .To<DnseOrderTypes?>() ?? DnseOrderTypes.Auto;
        set => Parameters[_nativeOrderType] = value;
    }
}

namespace StockSharp.Ppi;

/// <summary>PPI-specific order settings.</summary>
[DataContract]
[Serializable]
public class PpiOrderCondition : OrderCondition
{
    private const string _quantityType = "QuantityType";
    private const string _operationTerm = "OperationTerm";
    private const string _settlement = "Settlement";
    private const string _activationPrice = "ActivationPrice";

    /// <summary>How the order quantity is interpreted.</summary>
    [DataMember]
    public PpiQuantityTypes QuantityType
    {
        get => Parameters.TryGetValue(_quantityType)?.To<PpiQuantityTypes?>() ??
            PpiQuantityTypes.Papers;
        set => Parameters[_quantityType] = value;
    }

    /// <summary>Native validity term.</summary>
    [DataMember]
    public PpiOperationTerms OperationTerm
    {
        get => Parameters.TryGetValue(_operationTerm)?.To<PpiOperationTerms?>() ??
            PpiOperationTerms.UntilExecution;
        set => Parameters[_operationTerm] = value;
    }

    /// <summary>Native settlement code, for example A-24HS or INMEDIATA.</summary>
    [DataMember]
    public string Settlement
    {
        get => Parameters.TryGetValue(_settlement)?.To<string>();
        set => Parameters[_settlement] = value;
    }

    /// <summary>Activation price for a stop order.</summary>
    [DataMember]
    public decimal? ActivationPrice
    {
        get => Parameters.TryGetValue(_activationPrice)?.To<decimal?>();
        set => Parameters[_activationPrice] = value;
    }
}

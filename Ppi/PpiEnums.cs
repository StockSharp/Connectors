namespace StockSharp.Ppi;

/// <summary>PPI order quantity interpretation.</summary>
[DataContract]
[Serializable]
public enum PpiQuantityTypes
{
    /// <summary>Quantity is expressed in securities.</summary>
    [EnumMember]
    Papers,

    /// <summary>Quantity is expressed as an amount of money.</summary>
    [EnumMember]
    Money,

    /// <summary>Use the complete available position.</summary>
    [EnumMember]
    Total,
}

/// <summary>PPI order validity terms.</summary>
[DataContract]
[Serializable]
public enum PpiOperationTerms
{
    /// <summary>Valid for the current trading day.</summary>
    [EnumMember]
    Day,

    /// <summary>Valid until execution.</summary>
    [EnumMember]
    UntilExecution,

    /// <summary>Valid until the specified date.</summary>
    [EnumMember]
    UntilDate,

    /// <summary>Valid for 72 hours.</summary>
    [EnumMember]
    SeventyTwoHours,
}

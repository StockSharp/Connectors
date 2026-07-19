namespace StockSharp.Coinhako.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum CoinhakoSides
{
    [EnumMember(Value = "buy")]
    Buy,

    [EnumMember(Value = "sell")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinhakoExecutionTypes
{
    [EnumMember(Value = "rfq")]
    Rfq,

    [EnumMember(Value = "limit")]
    Limit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinhakoOrderStatuses
{
    [EnumMember(Value = "Open")]
    Open,

    [EnumMember(Value = "Cancelling")]
    Cancelling,

    [EnumMember(Value = "Completed")]
    Completed,

    [EnumMember(Value = "Cancelled")]
    Cancelled,

    [EnumMember(Value = "Pending")]
    Pending,
}

sealed class CoinhakoError
{
    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class CoinhakoErrorEnvelope
{
    [JsonProperty("errors")]
    public CoinhakoError[] Errors { get; set; }
}

abstract class CoinhakoQueryParameters
{
    public abstract void Append(CoinhakoQueryWriter writer);
}

sealed class CoinhakoQueryWriter
{
    private readonly StringBuilder _builder = new();

    public CoinhakoQueryWriter Add(string name, string value)
    {
        if (name.IsEmpty() || value.IsEmpty())
            return this;
        if (_builder.Length > 0)
            _builder.Append('&');
        _builder.Append(Uri.EscapeDataString(name));
        _builder.Append('=');
        _builder.Append(Uri.EscapeDataString(value));
        return this;
    }

    public CoinhakoQueryWriter Add(string name, long? value)
        => value is null ? this : Add(name,
            value.Value.ToString(CultureInfo.InvariantCulture));

    public CoinhakoQueryWriter Add(string name, int? value)
        => value is null ? this : Add(name,
            value.Value.ToString(CultureInfo.InvariantCulture));

    public override string ToString() => _builder.ToString();
}

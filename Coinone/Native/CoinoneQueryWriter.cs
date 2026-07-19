namespace StockSharp.Coinone.Native;

sealed class CoinoneQueryWriter
{
    private readonly List<string> _values = [];

    public CoinoneQueryWriter Add(string name, string value)
    {
        if (!value.IsEmpty())
            _values.Add($"{Escape(name)}={Escape(value)}");
        return this;
    }

    public CoinoneQueryWriter Add(string name, int? value)
        => value is null
            ? this
            : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public CoinoneQueryWriter Add(string name, long? value)
        => value is null
            ? this
            : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public CoinoneQueryWriter Add(string name, decimal? value)
        => value is null
            ? this
            : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public override string ToString() => string.Join("&", _values);

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));
}

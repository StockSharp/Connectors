namespace StockSharp.PintuPro.Native;

sealed class PintuProQueryWriter
{
    private readonly List<string> _values = [];

    public PintuProQueryWriter Add(string name, string value)
    {
        if (!value.IsEmpty())
            _values.Add($"{Escape(name)}={Escape(value)}");
        return this;
    }

    public PintuProQueryWriter Add(string name, int? value)
        => value is null
            ? this
            : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public PintuProQueryWriter Add(string name, long? value)
        => value is null
            ? this
            : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public PintuProQueryWriter Add(string name, decimal? value)
        => value is null
            ? this
            : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public PintuProQueryWriter Add(string name, bool? value)
        => value is null
            ? this
            : Add(name, value.Value ? "true" : "false");

    public override string ToString() => string.Join("&", _values);

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));
}

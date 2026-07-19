namespace StockSharp.Korbit.Native;

sealed class KorbitQueryWriter
{
    private readonly List<string> _values = [];

    public KorbitQueryWriter Add(string name, string value)
    {
        if (!value.IsEmpty())
            _values.Add($"{Escape(name)}={Escape(value)}");
        return this;
    }

    public KorbitQueryWriter Add(string name, int? value)
        => value is null
            ? this
            : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public KorbitQueryWriter Add(string name, long? value)
        => value is null
            ? this
            : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public KorbitQueryWriter Add(string name, decimal? value)
        => value is null
            ? this
            : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public KorbitQueryWriter Add(string name, bool? value)
        => value is null
            ? this
            : Add(name, value.Value ? "true" : "false");

    public override string ToString() => string.Join("&", _values);

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));
}

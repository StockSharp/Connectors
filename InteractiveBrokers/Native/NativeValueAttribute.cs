namespace StockSharp.InteractiveBrokers.Native;

class NativeValueAttribute : Attribute
{
	public NativeValueAttribute(string value)
	{
		if (value.IsEmpty())
			throw new ArgumentNullException(nameof(value));

		Value = value;
	}

	public string Value { get; }
}
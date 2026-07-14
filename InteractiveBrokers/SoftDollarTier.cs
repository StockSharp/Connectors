namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Soft Dollar Tier information.
/// </summary>
public class SoftDollarTier : IPersistable
{
	/// <summary>
	/// Name.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Value.
	/// </summary>
	public string Value { get; set; }

	/// <summary>
	/// Display name.
	/// </summary>
	public string DisplayName { get; set; }

	void IPersistable.Load(SettingsStorage storage)
	{
		Name = storage.GetValue<string>(nameof(Name));
		Value = storage.GetValue<string>(nameof(Value));
		DisplayName = storage.GetValue<string>(nameof(DisplayName));
	}

	void IPersistable.Save(SettingsStorage storage)
	{
		storage.SetValue(nameof(Name), Name);
		storage.SetValue(nameof(Value), Value);
		storage.SetValue(nameof(DisplayName), DisplayName);
	}
}
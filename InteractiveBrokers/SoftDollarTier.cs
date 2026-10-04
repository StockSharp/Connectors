namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Soft Dollar Tier information.
/// </summary>
public class SoftDollarTier : Cloneable<SoftDollarTier>, IAsyncPersistable
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

	/// <summary>
	/// Create a copy of <see cref="SoftDollarTier"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override SoftDollarTier Clone() => (SoftDollarTier)MemberwiseClone();

	Task IAsyncPersistable.LoadAsync(SettingsStorage storage, CancellationToken cancellationToken)
	{
		Name = storage.GetValue<string>(nameof(Name));
		Value = storage.GetValue<string>(nameof(Value));
		DisplayName = storage.GetValue<string>(nameof(DisplayName));

		return Task.CompletedTask;
	}

	Task IAsyncPersistable.SaveAsync(SettingsStorage storage, CancellationToken cancellationToken)
	{
		storage.SetValue(nameof(Name), Name);
		storage.SetValue(nameof(Value), Value);
		storage.SetValue(nameof(DisplayName), DisplayName);

		return Task.CompletedTask;
	}
}
namespace StockSharp.InteractiveBrokers;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Server-side logging levels.
/// </summary>
public enum ServerLogLevels
{
	/// <summary>
	/// System messages.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SystemKey)]
	System = 1,

	/// <summary>
	/// Errors.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ErrorsKey)]
	Error = 2,

	/// <summary>
	/// Warnings.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WarningsKey)]
	Warning = 3,

	/// <summary>
	/// Information messages.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InfoKey)]
	Information = 4,

	/// <summary>
	/// Detailed messages.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DebugKey)]
	Detail = 5
}
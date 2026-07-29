namespace StockSharp.Transaq.Native.Commands;

class ChangePassMessage : BaseCommandMessage
{
	public ChangePassMessage() : base(ApiCommands.ChangePass)
	{
	}

	public string NewPass { get; set; }
	public string OldPass { get; set; }
}
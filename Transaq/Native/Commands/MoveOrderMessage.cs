namespace StockSharp.Transaq.Native.Commands;

class MoveOrderMessage : BaseCommandMessage
{
	public MoveOrderMessage() : base(ApiCommands.MoveOrder)
	{
	}

	public long TransactionId { get; set; }
	public double Price { get; set; }
	public int Quantity { get; set; }
	public MoveOrderFlag MoveFlag { get; set; }
}

enum MoveOrderFlag
{
	DontChangeQuantity = 0,
	ChangeQuantity,
	IfNotEqualRemoveOrder
}
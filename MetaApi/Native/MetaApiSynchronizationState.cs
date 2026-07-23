namespace StockSharp.MetaApi.Native;

sealed class MetaApiSynchronizationState
{
	private readonly Lock _sync = new();
	private string _synchronizationId;
	private long? _lastSequence;
	private bool _ordersFinished;
	private bool _dealsFinished;
	private bool _requiresResynchronization;

	public bool IsReady
	{
		get
		{
			using (_sync.EnterScope())
				return _ordersFinished && _dealsFinished && !_requiresResynchronization;
		}
	}

	public bool RequiresResynchronization
	{
		get
		{
			using (_sync.EnterScope())
				return _requiresResynchronization;
		}
	}

	public void Begin(string synchronizationId)
	{
		using (_sync.EnterScope())
		{
			_synchronizationId = synchronizationId.ThrowIfEmpty(nameof(synchronizationId));
			_lastSequence = null;
			_ordersFinished = false;
			_dealsFinished = false;
			_requiresResynchronization = false;
		}
	}

	public bool TryAccept(MetaApiSynchronizationPacket packet)
	{
		if (packet is null)
			throw new ArgumentNullException(nameof(packet));

		using (_sync.EnterScope())
		{
			var synchronizationId = packet.SynchronizationId;
			if (!synchronizationId.IsEmpty() &&
				!synchronizationId.EqualsIgnoreCase(_synchronizationId))
				return false;

			var sequence = packet.SequenceNumber;
			if (sequence is not null && _lastSequence is not null)
			{
				if (sequence <= _lastSequence)
					return false;
				if (sequence != _lastSequence + 1)
				{
					_requiresResynchronization = true;
					return false;
				}
			}
			if (sequence is not null)
				_lastSequence = sequence;

			switch (packet.Type)
			{
				case "orderSynchronizationFinished":
					_ordersFinished = true;
					break;
				case "dealSynchronizationFinished":
					_dealsFinished = true;
					break;
			}
			return true;
		}
	}
}

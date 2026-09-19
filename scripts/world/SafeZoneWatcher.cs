using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Lives on the cabin. Once the player has the lantern and compass (picked up
/// after the door is boarded) and steps outside this radius, the storm
/// begins. One-way: checked every frame only until it fires once.
/// </summary>
public partial class SafeZoneWatcher : Node
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float Radius = 16f;

	private Node3D _cabin;
	private PlayerController _player;
	private bool _fired;

	public override void _Ready() => _cabin = GetNode<Node3D>(CabinPath);

	public override void _Process(double delta)
	{
		if (_fired || StoryManager.Instance == null || StoryManager.Instance.Current < Checkpoint.Act3DoorBoarded) return;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		var inv = _player?.GetNodeOrNull<PlayerInventory>("Inventory");
		if (inv is not { HasLantern: true, HasCompass: true }) return;
		float d = new Vector2(_player.GlobalPosition.X - _cabin.GlobalPosition.X, _player.GlobalPosition.Z - _cabin.GlobalPosition.Z).Length();
		if (d <= Radius) return;
		_fired = true;
		StormController.Instance?.Activate();
	}
}

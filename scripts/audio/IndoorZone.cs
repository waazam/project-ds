using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// An interior volume (the cabin, the bunker, the maze). While the player is
/// inside it, <see cref="ForestAmbienceManager.SetIndoor"/> muffles and ducks
/// the forest bed and the weather, as heard through walls. Give it one or more
/// CollisionShape3D children; it only detects the player (physics layer 2).
/// Several zones may overlap: the listener counts as indoors while inside any.
/// </summary>
[GlobalClass]
public partial class IndoorZone : Area3D
{
	private int _inside;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = 1u << 1;
		Monitorable = false;
		BodyEntered += b => { if (b.IsInGroup("player")) { _inside++; Apply(); } };
		BodyExited += b => { if (b.IsInGroup("player")) { _inside = Mathf.Max(0, _inside - 1); Apply(); } };
	}

	public override void _ExitTree()
	{
		_inside = 0;
		ForestAmbienceManager.Instance?.SetIndoor(this, false);
	}

	private void Apply() => ForestAmbienceManager.Instance?.SetIndoor(this, _inside > 0);
}

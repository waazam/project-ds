using Godot;

namespace ProjectDS.World;

/// <summary>Keeps procedural scatter (trees, rocks, foliage) out of a circle around this node.</summary>
[GlobalClass]
public partial class ClearZone : Node3D
{
	[Export] public float Radius = 4f;
	/// <summary>Also clear small ground foliage (grass/ferns), not just trees and rocks.</summary>
	[Export] public bool ClearFoliage = true;

	public override void _EnterTree() => AddToGroup("clear_zones");
}

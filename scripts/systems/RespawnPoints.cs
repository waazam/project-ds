using Godot;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// Where Continue puts the player for each checkpoint. The raw position saved at a checkpoint
/// can be somewhere unplayable on reload (inside the cabin, mid-way through a scripted teleport,
/// inside the bunker interior), so every checkpoint gets a safe spot derived from the level's
/// own markers, always outdoors, on the ground, and facing where the story leads next.
///
/// A level can override any of these by placing a Node3D in group "respawn_&lt;Checkpoint&gt;"
/// (e.g. "respawn_Act7CabinBurning"); its -Z is the facing.
/// </summary>
public static class RespawnPoints
{
	public static (Vector3 pos, float yaw) For(Node n, SaveData save)
	{
		var tree = n.GetTree();
		var cp = save.Checkpoint;
		Vector3 saved = new(save.PosX, save.PosY, save.PosZ);
		// Dev launch (--start-act=8): stand 14 m in front of the bunker's hatch, facing it.
		if (System.Array.IndexOf(save.Flags, "dev_spawn_bunker") >= 0 && tree.GetFirstNodeInGroup("bunker_marker") is Node3D hatch)
		{
			Vector3 front = hatch.GlobalTransform * new Vector3(0, 0, 14f);
			return OnGround(n, front, YawToward(front, hatch.GlobalPosition));
		}

		var cabin = tree.GetFirstNodeInGroup("cabin") as Cabin;
		// Post in hand but not yet carried out: just inside the (open) doorway, facing out, so stepping
		// outside plays the rain-stops beat exactly as it would have. Before any level marker: an outdoor
		// respawn would skip the doorway, and with it that beat.
		if (cp == Checkpoint.Act5CabinEntered && cabin != null
			&& StoryManager.Instance is { NewelPostTaken: true } s5 && !s5.HasFlag(StoryManager.Flag.DawnBroke))
			return (cabin.InsidePoint + Vector3.Up * 0.1f, YawToward(cabin.InsidePoint, cabin.ApproachPoint));

		if (tree.GetFirstNodeInGroup($"respawn_{cp}") is Node3D marker)
			return OnGround(n, marker.GlobalPosition, YawOf(-marker.GlobalBasis.Z));

		var top = tree.GetFirstNodeInGroup("stairs_top_trigger") as Node3D;
		var bridge = tree.GetFirstNodeInGroup("bridge_marker") as Node3D;
		var bunker = tree.GetFirstNodeInGroup("bunker_marker") as Node3D;
		var spawn = tree.GetFirstNodeInGroup("player_spawn") as Node3D;

		switch (cp)
		{
			case Checkpoint.Act1Start when spawn != null:
				return (spawn.GlobalPosition + Vector3.Up * 0.1f, YawOf(-spawn.GlobalBasis.Z));
			case Checkpoint.Act2StairsClimbed when spawn != null:
				// Where the stairs let go of them: the hollow's own spawn point (its wake spot).
				return OnGround(n, spawn.GlobalPosition, YawOf(-spawn.GlobalBasis.Z));
			case Checkpoint.Act3DoorBoarded when cabin != null:
			case Checkpoint.Act5CabinEntered when cabin != null:
				// Outside the door, facing it: never inside the cabin, whatever state the door is in.
				return OnGround(n, cabin.ApproachPoint, YawToward(cabin.ApproachPoint, cabin.DoorCenter));
			case Checkpoint.Act6BridgeCrossed when bridge != null:
				// Just past the bridge on the far (north) bank, facing on up the trail.
				return OnGround(n, bridge.GlobalPosition + new Vector3(0, 0, -8f), 0f);
			case Checkpoint.Act7CabinBurning when cabin != null:
				// Well out in front of the burning cabin, looking at it.
				return OnGround(n, cabin.WideApproachPoint, YawToward(cabin.WideApproachPoint, cabin.GlobalPosition));
			case Checkpoint.Act8BunkerEntered when bunker is Bunker b8:
				// The apron in front of the open door, facing it (the interior is re-entered by walking in).
				return (b8.ApproachPointWorld, b8.ApproachYaw);
			case Checkpoint.Act10WalkieFound when bunker != null:
			{
				// Where Act 11's radio exchange carries the player: just outside the bunker, facing away from it.
				Vector3 p = bunker.GlobalTransform * new Vector3(0, 0, 16f);
				return OnGround(n, p, YawToward(bunker.GlobalPosition, p));
			}
			default:
				// Act 11's wake spot (chosen for clearance when it was saved) and any missing marker.
				return OnGround(n, saved, save.Yaw);
		}
	}

	private static (Vector3, float) OnGround(Node n, Vector3 p, float yaw)
	{
		var terrain = GroundSnap.FindTerrain(n);
		if (terrain != null) p.Y = Mathf.Max(p.Y, terrain.HeightAt(p.X, p.Z));
		return (p + Vector3.Up * 0.15f, yaw);
	}

	private static float YawOf(Vector3 fwd) => Mathf.Atan2(-fwd.X, -fwd.Z);
	private static float YawToward(Vector3 from, Vector3 to) => YawOf(to - from);
}

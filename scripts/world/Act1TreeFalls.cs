using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Act 1's way to the stairs, made by the woods themselves (the owner, 2026-10-03: "remove the stone pathway, and have
/// trees fall left and right along that path to the stairs forcing the player to go into that direction ... we can
/// even have trees fall behind them after they pass the first fallen tree to make sure they dont deviate from the
/// path. This will be a more organic way to get the player to the stairs and feel more supernatural and creepy").
///
/// The line is the old path's (<see cref="FriendTrail"/>, its stones gone): from the trail's end round the dead fir's
/// crown to the stairs' foot. Once the fir is down across the trail, trees standing either side of the line come
/// down as the player nears them, lengthwise along its edges, a wall on the left, then on the right, so the way on is
/// the only way. Behind the player, once they're well past, trees come down across the line, closing it. None of them
/// comes down within reach of the player; near the stairs they fall outward, never into the clearing.
/// </summary>
[GlobalClass]
public partial class Act1TreeFalls : Node3D
{
	[Export] public NodePath FallenTreePath = "../FallenTree";
	[Export] public NodePath LinePath = "../FriendTrail";
	/// <summary>Along the line (m): where the trees beside it stand, alternately left and right.</summary>
	[Export] public float[] SideAt = { 20f, 27f, 34f, 41f, 48f, 55f, 62f, 69f };
	/// <summary>Along the line (m): where trees come down across it behind the player.</summary>
	[Export] public float[] BehindAt = { 13f, 33f, 54f };
	/// <summary>How far ahead of the player (m along the line) a side tree comes down; how far past a behind tree the
	/// player has to be before it closes the way.</summary>
	[Export] public float AheadTrigger = 15f, BehindTrigger = 12f;
	[Export] public float SideOffset = 4.4f;

	private FallenTree _fir;
	private FriendTrail _line;
	private ForestTerrain _terrain;
	private readonly List<(FallingTree tree, float at, bool behind)> _trees = new();
	private static Mesh[] _meshes;

	/// <summary>For the tests: how many have come down beside the way, and across it behind.</summary>
	public int SideDown { get; private set; }
	public int BehindDown { get; private set; }
	public int SideCount => SideAt.Length;
	/// <summary>For the tests: how many have started to come down, beside the way and behind.</summary>
	public int SideStarted { get; private set; }
	/// <summary>For tests: the highest any of them lies off the ground (they lie flat on it: the owner saw them propped on
	/// their branches).</summary>
	public float MaxRestTip { get { float m = 0f; foreach (var (t, _, _) in _trees) m = Mathf.Max(m, t.RestTipHeight); return m; } }
	public int BehindStarted { get; private set; }

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_fir = GetNodeOrNull<FallenTree>(FallenTreePath);
		_line = GetNodeOrNull<FriendTrail>(LinePath);
		_terrain = GroundSnap.FindTerrain(this);
		if (_line == null || _terrain == null || _line.Length < 10f) return;
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		_meshes ??= new[]
		{
			ForestScatter.FirMesh(13, 18f, 0.31f, 0.26f, 11, 3.0f, 0.15f, 0.4f),
			ForestScatter.FirMesh(14, 22f, 0.34f, 0.30f, 15, 2.4f, 0.08f, 0.4f),
			ForestScatter.FirMesh(12, 26f, 0.42f, 0.36f, 13, 3.7f, 0.12f, 0.4f),
		};
		float L = _line.Length;
		for (int i = 0; i < SideAt.Length; i++)
		{
			float s = SideAt[i];
			if (s > L - 6f) continue;
			var p = _line.At(s, out Vector2 d2);
			var tan = new Vector3(d2.X, 0, d2.Y);
			var left = new Vector3(d2.Y, 0, -d2.X);
			float sideSign = i % 2 == 0 ? 1f : -1f;
			Vector3 outward = left * sideSign;
			int mi = i % 3;
			float h = mi == 0 ? 18f : mi == 1 ? 22f : 26f;
			// lengthwise along the edge, forward, a little outward; near the stairs (it would reach the clearing) outward
			Vector3 fall = s + h * 0.95f < L - 4f ? (tan + outward * 0.22f).Normalized() : (outward + tan * 0.35f).Normalized();
			Add(p + new Vector2(outward.X, outward.Z) * SideOffset, fall, mi, h, s, false, i + 1);
		}
		for (int i = 0; i < BehindAt.Length; i++)
		{
			float s = BehindAt[i];
			if (s > L - 10f) continue;
			var p = _line.At(s, out Vector2 d2);
			var left = new Vector3(d2.Y, 0, -d2.X);
			float sideSign = i % 2 == 0 ? -1f : 1f;
			// across the line from one side to the other, slanting back the way they came
			var tan = new Vector3(d2.X, 0, d2.Y);
			Vector3 fall = (-left * sideSign - tan * 0.25f).Normalized();
			Add(p + new Vector2(left.X, left.Z) * sideSign * (SideOffset + 0.6f), fall, 2, 26f, s, true, 20 + i);
		}
	}

	private void Add(Vector2 at, Vector3 fall, int mesh, float h, float s, bool behind, int seed)
	{
		var foot = new Vector3(at.X, _terrain.HeightAt(at.X, at.Y) - 0.05f, at.Y);
		var t = new FallingTree { Name = $"{(behind ? "Behind" : "Side")}Tree{seed}", Mesh = _meshes[mesh], Height = h, TrunkR = 0.36f + mesh * 0.04f, FallDir = fall, Seed = seed };
		t.Reserve(this, _terrain, foot);
		AddChild(t);
		t.GlobalPosition = foot;
		_trees.Add((t, s, behind));
	}

	/// <summary>Where the player is along the line (m), by the nearest point of it.</summary>
	private float Progress(Vector3 p)
	{
		float best = float.MaxValue, bestS = 0f;
		var q = new Vector2(p.X, p.Z);
		for (float s = 0f; s <= _line.Length; s += 0.5f)
		{
			float d = _line.At(s, out _).DistanceSquaredTo(q);
			if (d < best) { best = d; bestS = s; }
		}
		return bestS;
	}

	public override void _Process(double delta)
	{
		if (_trees.Count == 0 || GetTree().GetFirstNodeInGroup("player") is not Node3D player) return;
		// it all begins with the dead fir
		if (_fir != null && !_fir.Fallen) return;
		float sp = Progress(player.GlobalPosition);
		SideDown = BehindDown = 0;
		foreach (var (tree, at, behind) in _trees)
		{
			if (tree.Down || tree.Falling) { if (tree.Down) { if (behind) BehindDown++; else SideDown++; } continue; }
			bool go = behind ? sp >= at + BehindTrigger : sp >= at - AheadTrigger;
			// never within reach of the player as it comes down
			if (go && player.GlobalPosition.DistanceTo(tree.GlobalPosition) > 7f)
			{
				tree.Fall();
				if (behind) BehindStarted++; else SideStarted++;
				GD.Print($"[story] Act 1: a tree comes down {(behind ? "behind them" : "beside the way")} ({at:0} m along)");
			}
		}
	}
}

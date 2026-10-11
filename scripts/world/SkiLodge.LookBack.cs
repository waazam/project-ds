using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// Things that are different when they look back (2026-10-11, the owner's list: "Look-back moments. Some things change
/// only when you turn your back on them ... a chair now facing you, a door that was shut now open, footprints that weren't
/// there"). In Act 23, in the lobby, each once, never while it can be seen, and only after it has been seen as it was:
/// <list type="bullet">
/// <item><b>the wing chair</b> by the west wall, turned to the room, is turned to face them;</item>
/// <item><b>wet bare footprints</b> across the boards from the front doors to where they were standing a moment ago;</item>
/// </list>
/// (with the corridor's portrait and 204's door, SkiLodge.Haunts.cs and SkiLodge.Hide.cs). No sound when it happens:
/// they'll only find it.
/// </summary>
public partial class SkiLodge
{
	private sealed class LookBackThing
	{
		public string Name;
		public Node3D Node;
		public System.Action<PlayerController> Change;
		public bool SeenAsWas, Done;
		public double UnseenFor;
	}

	private readonly List<LookBackThing> _lookBacks = new();
	public int LookBacksDone { get; private set; }
	private double _lastLookBack = -100;

	private void BuildLookBacks()
	{
		// the wing chair, in the lobby by the west wall, its back to the wall
		var at = new Vector3(-8.6f, FloorY, 1.4f);
		var chair = FurnitureKit.Place(this, "wing_chair", new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f), at),
			new Dictionary<string, Material>(Woodwork) { ["upholstery"] = LodgeTextures.VelvetVioletMat }, "LookBackChair");
		if (chair != null) LodgeKit.Solid(_inBody, at + Vector3.Up * 0.45f, new Vector3(0.85f, 0.9f, 0.85f));
		if (chair != null)
			_lookBacks.Add(new LookBackThing
			{
				Name = "chair", Node = chair,
				Change = p =>
				{
					var to = (ToLocal(p.GlobalPosition) - chair.Position) with { Y = 0 };
					chair.Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), 0);
				},
			});
		// the footprints: laid now, shown when it happens
		var prints = new Node3D { Name = "LookBackPrints", Visible = false };
		AddChild(prints);
		_lookBacks.Add(new LookBackThing { Name = "prints", Node = prints, Change = p => LayWetPrints(prints, p) });
	}

	private static StandardMaterial3D _wetPrint;

	/// <summary>Bare feet, wet, from inside the front doors to just behind where they stand.</summary>
	private void LayWetPrints(Node3D holder, PlayerController p)
	{
		_wetPrint ??= new StandardMaterial3D
		{
			ResourceName = "wet_print", AlbedoColor = new Color(0.05f, 0.04f, 0.035f, 0.55f), Roughness = 0.08f, MetallicSpecular = 0.8f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		var from = FrontDoorInside with { Y = FloorY };
		var to = ToLocal(p.GlobalPosition) with { Y = FloorY };
		var dir = (to - from) with { Y = 0 };
		float len = dir.Length();
		if (len < 2f) return;
		dir /= len;
		var side = dir.Cross(Vector3.Up);
		int n = Mathf.FloorToInt((len - 1.2f) / 0.62f);
		var k = new MeshKit();
		k.Mat(_wetPrint);
		k.Color = Colors.White;
		for (int i = 0; i < n; i++)
		{
			var c = from + dir * (0.4f + i * 0.62f) + side * (i % 2 == 0 ? 0.12f : -0.12f) + Vector3.Up * 0.006f;
			var b = Basis.LookingAt(dir, Vector3.Up);
			// a long narrow foot: heel, sole and toes
			k.Blob(c, new Vector3(0.05f, 0.002f, 0.12f), 7700 + i, 0.15f, true, 1f);
			k.Blob(c + dir * 0.11f, new Vector3(0.055f, 0.002f, 0.05f), 7800 + i, 0.2f, true, 1f);
		}
		k.CommitTo(holder, "Prints", false);
		holder.Visible = true;
	}

	/// <summary>Each frame of Act 23: whether one of them can happen now.</summary>
	private void LookBackTick(PlayerController player, double delta)
	{
		if (_lookBacks.Count == 0 || Frozen) return;
		if (StoryManager.Instance is not { Current: >= Checkpoint.Act22Finished and < Checkpoint.Act23Finished }) return;
		var cam = player.CameraRig?.Camera;
		if (cam == null) return;
		double now = Time.GetTicksMsec() / 1000.0;
		foreach (var lb in _lookBacks)
		{
			if (lb.Done || !IsInstanceValid(lb.Node)) continue;
			var pos = lb.Name == "prints" ? ToGlobal(new Vector3(0, FloorY + 0.5f, 3f)) : lb.Node.GlobalPosition + Vector3.Up * 0.8f;
			float d = pos.DistanceTo(player.GlobalPosition);
			bool inView = d < 22f && cam.IsPositionInFrustum(pos) && (-cam.GlobalBasis.Z).Dot((pos - cam.GlobalPosition).Normalized()) > 0.55f;
			if (inView) { lb.SeenAsWas = true; lb.UnseenFor = 0; continue; }
			lb.UnseenFor += delta;
			bool lobby = ToLocal(player.GlobalPosition) is var l && l.Y < UpperY - 1f && new Vector2(l.X, l.Z).Length() < HexIn;
			if (lb.SeenAsWas && lobby && lb.UnseenFor > 2.5 && d > 3f && d < 14f && now - _lastLookBack > 60 && now - _lastHaunt > 15)
			{
				lb.Change(player);
				lb.Done = true;
				LookBacksDone++;
				_lastLookBack = now;
				GD.Print($"[story] Act 23: they look back - {lb.Name}");
			}
		}
	}
}

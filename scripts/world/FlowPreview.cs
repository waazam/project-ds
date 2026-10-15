using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (dev only, the gushy-water pass 2026-10-15) <see cref="LookShots"/>' <c>--look-fx=name</c>: puts on a water or blood
/// effect that an event normally makes, so it can be photographed (and its frames sequenced) from a fixed pose, and logs
/// where the level's flowing water is. Reaches into the levels' private state by reflection: nothing here is used in play.
/// Names: <c>where</c> (log only), <c>torrent</c>, <c>torrentblood</c>, <c>drain</c>, <c>bowl</c>, <c>skull</c>, <c>stairs</c>,
/// <c>bloodrain</c>.
/// </summary>
public static class FlowPreview
{
	private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

	private static T Find<T>(Node from) where T : Node => Walk<T>(from.GetTree().Root);

	private static T Walk<T>(Node n) where T : Node
	{
		if (n is T t) return t;
		foreach (var c in n.GetChildren()) if (Walk<T>(c) is { } f) return f;
		return null;
	}

	private static object Get(object o, string name) =>
		o.GetType().GetField(name, Any)?.GetValue(o) ?? o.GetType().GetProperty(name, Any)?.GetValue(o);

	private static void Set(object o, string name, object v)
	{
		var f = o.GetType().GetField(name, Any);
		if (f != null) { f.SetValue(o, v); return; }
		o.GetType().GetProperty(name, Any)?.SetValue(o, v);
	}

	private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Any)?.Invoke(o, args);

	private static async Task Frames(Node n, int count)
	{
		for (int i = 0; i < count; i++) await n.ToSignal(n.GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	public static async Task Apply(Node from, string fx)
	{
		Where(from);
		switch (fx)
		{
			case "torrent":
			case "torrentblood":
				if (Find<StationRoom2>(from) is { } r2)
				{
					((Node3D)Get(r2, "_torrent")).Visible = true;
					var tm = (ShaderMaterial)Get(r2, "_torrentMat");
					var wm = (ShaderMaterial)Get(r2, "_waterMat");
					var water = (Node3D)Get(r2, "_water");
					Set(r2, "Level", 0.55f);
					water.Visible = true;
					water.Position = water.Position with { Y = 0.55f };
					var c = fx == "torrentblood" ? new Color(0.28f, 0.01f, 0.01f) : new Color(0.07f, 0.1f, 0.08f);
					tm.SetShaderParameter("tint", c);
					tm.SetShaderParameter("flow", 1f);
					wm.SetShaderParameter("water_color", c);
					if (fx == "torrentblood")
					{
						wm.SetShaderParameter("scum_color", c.Lerp(new Color(0.5f, 0.06f, 0.05f), 0.5f));
						Call(r2, "TorrentViscous", 1f, c);
					}
					GD.Print($"[flow] torrent on, room 2 at {r2.GlobalPosition} yaw {Mathf.RadToDeg(r2.GlobalRotation.Y):0}");
				}
				break;
			case "pitold":
			case "drainold":
				// (the before shots: with viscous 0 basement_water.gdshader is exactly the old shader)
				if (Find<BossRoom>(from) is { } bo)
				{
					var m = (ShaderMaterial)Get(bo, "_bloodMat");
					m.SetShaderParameter("viscous", 0f);
					if (fx == "drainold") m.SetShaderParameter("swirl", 0.6f);
				}
				break;
			case "drain":
				if (Find<BossRoom>(from) is { } br)
				{
					((ShaderMaterial)Get(br, "_bloodMat")).SetShaderParameter("swirl", 0.6f);
					GD.Print($"[flow] pit drain swirl on at {br.GlobalPosition}");
				}
				break;
			case "lodge":
				if (Find<SkiLodge>(from) is { } lb)
				{
					// the blood dragged down table 1, the head on its platter on table 3, the bowl on table 5 (then melted)
					foreach (var (t, n) in new[] { (1, 2), (3, 4), (5, 6) })
					{
						if (lb.GetNodeOrNull($"Under{t}") is Node old) old.Free();
						Call(lb, "Reveal", t, n, false, false);
						if (lb.GetNodeOrNull<Node3D>($"Sheet{t}") is { } sheet) sheet.Visible = false;
						if (lb.GetNodeOrNull<Node3D>($"Under{t}") is { } u) GD.Print($"[flow] lodge Under{t} (sheet {n}) at {u.GlobalPosition} mesh {u.GetNodeOrNull<MeshInstance3D>("Mesh")?.GetAabb()}");
					}
					await Frames(from, 20);
					_ = (Task)Call(lb, "MeltToBlood");
					await Frames(from, 240);
					GD.Print("[flow] lodge: streak, platter, bowl melted");
				}
				break;
			case "stairs":
				if (Find<SnowMaze.EndingDirector>(from) is { } ed && Get(ed, "_stairs") is Node3D stairs)
				{
					stairs.Visible = true;
					if (ed.GetType().GetMethod("BleedStairs", Any) != null && Get(ed, "_car") is Node3D car) car.Visible = false;   // (as in the ending; kept in the before shots)
					stairs.GlobalPosition = ed.CarAt;
					stairs.GlobalBasis = Basis.Identity;
					GD.Print($"[flow] stairs at {ed.CarAt}");
					if (ed.GetType().GetMethod("BleedStairs", Any) is { } bleed)
						await (Task)bleed.Invoke(ed, new object[] { System.Threading.CancellationToken.None });
					else
					{
						// (the old one, for the before shots: a flat glossy box on each step)
						var blood = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.02f, 0.02f), Roughness = 0.1f, MetallicSpecular = 0.8f };
						for (int i = 11; i >= 0; i--)
						{
							var step = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.01f, 0.32f) }, MaterialOverride = blood };
							stairs.AddChild(step);
							step.Position = new Vector3(0, 0.2f + i * 0.2f, -0.3f - i * 0.3f);
						}
					}
				}
				break;
			case "bloodrain":
				if (Find<Act6ClearingEvent>(from) is { } a6)
				{
					Call(a6, "StartBlood");
					var rain = (BloodRain)Get(a6, "_blood");
					if (rain != null) { rain.Presence = 1f; rain.Intensity = 1f; Set(a6, "BloodPresence", 1f); }
					await Frames(from, 400);
					// the creek, as it is after a while of it (it reddens over some fifteen seconds)
					if (rain != null) { Set(rain, "_creekRed", 0.85f); GroundSnap.FindTerrain(rain)?.SetStreamBlood(0.85f); }
					GD.Print("[flow] blood rain on");
				}
				break;
		}
		await Frames(from, 30);
	}

	/// <summary>Logs the level's flowing water: the streams' ends and middles, the waterfall, the bridge.</summary>
	public static void Where(Node from)
	{
		foreach (var n in from.GetTree().Root.FindChildren("*", nameof(ForestTerrain), true, false))
			if (n is ForestTerrain t && t.Stream is { } s && s.Points.Count > 1)
			{
				var o = t.GlobalPosition;
				for (float f = 0f; f <= 1.001f; f += 0.125f)
				{
					var p = s.At(s.Length * f, out _);
					GD.Print($"[flow] {t.GetParent()?.Name}/{t.Name} stream {f:0.000}: ({p.X + o.X:0.0}, {t.WaterLevel(s.Length * f):0.0}, {p.Y + o.Z:0.0})");
				}
				if (t.TryGetStreamCrossing(out var cross, out var dir, out _)) GD.Print($"[flow]   crossing {cross} trail dir {dir}");
			}
		if (Find<Waterfall>(from) is { } wf) GD.Print($"[flow] waterfall curtain {wf.CurtainCenter} lip {wf.LipCenter}");
		if (Find<Sewer>(from) is { } sw) GD.Print($"[flow] sewer at {sw.GlobalPosition} yaw {Mathf.RadToDeg(sw.GlobalRotation.Y):0}");
		if (Find<BossRoom>(from) is { } b) GD.Print($"[flow] pit at {b.GlobalPosition} yaw {Mathf.RadToDeg(b.GlobalRotation.Y):0} blood y {b.BloodY}");
	}
}

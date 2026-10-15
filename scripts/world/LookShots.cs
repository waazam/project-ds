using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The look audit's camera (dev only). Run as the main scene with the test save slots:
/// `godot --path . res://scenes/levels/look_shots.tscn -- --autotest --look-act=N [--look-out=dir] [--look-wait=8]
///  [--look-pos="x,y,z,yawDeg,pitchDeg;..."] [--look-turns=4] [--look-nohud]`.
/// Writes act N's save (the story test's own state for that act), Continues into it (Act 1: a new game),
/// keeps the story test out of it, waits, then photographs the view: from the spawn at --look-turns headings,
/// then from each --look-pos. Shots go to test-output/look/&lt;dir&gt;/. Quits when done.
/// </summary>
public partial class LookShots : Node
{
	private int _act = 1, _turns = 4;
	private float _wait = 8f;
	private string _out = "res://test-output/look";
	private readonly List<(Vector3 p, float yaw, float pitch)> _poses = new();

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		string dir = "";
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			string V(string k) => a[k.Length..];
			if (a.StartsWith("--look-act=")) _act = int.Parse(V("--look-act="));
			else if (a.StartsWith("--look-out=")) dir = V("--look-out=");
			else if (a.StartsWith("--look-wait=")) _wait = float.Parse(V("--look-wait="), CultureInfo.InvariantCulture);
			else if (a.StartsWith("--look-turns=")) _turns = int.Parse(V("--look-turns="));
			else if (a.StartsWith("--look-pos="))
				foreach (var pose in V("--look-pos=").Split(';', StringSplitOptions.RemoveEmptyEntries))
				{
					var f = Array.ConvertAll(pose.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
					_poses.Add((new Vector3(f[0], f[1], f[2]), f.Length > 3 ? f[3] : 0f, f.Length > 4 ? f[4] : 0f));
				}
		}
		_out += "/" + (dir == "" ? $"act{_act:00}" : dir);
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_out));
		using (FileAccess.Open("res://test-output/look/.gdignore", FileAccess.ModeFlags.Write)) { }
		// the test save slots (--autotest) without the story test: GameFlow is told it isn't a test while it sets up
		GetTree().NodeAdded += n =>
		{
			if (n is not GameFlow gf) return;
			SetAutoTest(false);
			gf.Ready += () => SetAutoTest(true);
		};
		Callable.From(Begin).CallDeferred();
	}

	private static void SetAutoTest(bool v) =>
		typeof(GameSettings).GetProperty(nameof(GameSettings.AutoTest))!.SetValue(GameSettings.Instance, v);

	private void Begin()
	{
		// survive the level change
		var root = GetTree().Root;
		GetParent().RemoveChild(this);
		root.AddChild(this);
		if (_act <= 1) StoryManager.Instance.StartNewGame();
		else
		{
			var m = typeof(StoryTest).GetMethod("StateFor", BindingFlags.NonPublic | BindingFlags.Static);
			var st = ((Checkpoint cp, string[] flags, string inventory))m.Invoke(null, new object[] { _act });
			SaveSystem.Save(new SaveData { Checkpoint = st.cp, Flags = st.flags, Inventory = st.inventory });
			StoryManager.Instance.ContinueGame();
		}
		Run();
	}

	public override void _Process(double delta)
	{
		// the story test isn't wanted here (GameFlow adds it under --autotest)
		foreach (var n in GetTree().Root.FindChildren("*", nameof(StoryTest), true, false)) n.QueueFree();
	}

	private async void Run()
	{
		var tree = GetTree();
		PlayerController p = null;
		double t = 0;
		while (t < 90)
		{
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			t += GetProcessDeltaTime();
			p = tree.GetFirstNodeInGroup("player") as PlayerController;
			if (p != null && GameFlow.Instance is { Started: true }) break;
		}
		if (p == null) { GD.PrintErr("[look] no player"); tree.Quit(2); return; }
		p.PlayerInput.Scripted = true;
		await Wait(_wait);
		var start = p.GlobalPosition;
		float yaw0 = p.CameraRig.Yaw;
		GD.Print($"[look] act {_act} at {start} yaw {Mathf.RadToDeg(yaw0):0}");
		if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-noshafts") >= 0 && FindAtmo() is { } atS) atS.ShaftScale = 0f;
		if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-nosky") >= 0 && GetViewport().World3D?.Environment is { } envS) { envS.BackgroundMode = Godot.Environment.BGMode.Color; envS.BackgroundColor = Colors.Black; }
		if (GetViewport().World3D?.Environment is { } env0)
			GD.Print($"[look]   sky energy {env0.BackgroundEnergyMultiplier:0.00} exposure {env0.TonemapExposure:0.00} fog {env0.FogMode} sky-fog {env0.FogSkyAffect:0.00} horizon {(env0.Sky?.SkyMaterial as ShaderMaterial)?.GetShaderParameter("horizon_color")}");
		int shot = 0;
		for (int i = 0; i < _turns; i++)
		{
			_pin = null;
			p.Teleport(start, yaw0 + Mathf.Tau * i / _turns);
			p.CameraRig.SetPitch(0f);
			await Hold(p, yaw0 + Mathf.Tau * i / _turns, 0f, 1.2f);
			Save($"{shot++:00}_spawn_{i * 360 / Math.Max(1, _turns):000}");
		}
		// --look-rel=NodeName: the poses are in that node's own frame (a building's interior)
		Transform3D rel = Transform3D.Identity;
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--look-rel=") && tree.Root.FindChild(a["--look-rel=".Length..], true, false) is Node3D rn)
			{
				rel = rn.GlobalTransform.Orthonormalized();
				GD.Print($"[look] relative to {rn.Name} at {rn.GlobalPosition}");
			}
		float relYaw = rel.Basis.GetEuler().Y;
		foreach (var (lpos, lyaw, pitch) in _poses)
		{
			var pos = rel * lpos;
			// a y of 9999 or more: dropped onto whatever is under that x, z (a long road, its heights unknown)
			if (lpos.Y >= 9999f)
			{
				await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
				var hit = p.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(
					new Vector3(pos.X, 3000f, pos.Z), new Vector3(pos.X, -3000f, pos.Z), 1));
				pos.Y = hit.Count > 0 ? ((Vector3)hit["position"]).Y + 0.3f : 0f;
			}
			float yaw = lyaw + Mathf.RadToDeg(relYaw);
			_pin = null;
			p.Teleport(pos, Mathf.DegToRad(yaw));
			p.CameraRig.SetPitch(Mathf.DegToRad(pitch));
			await Hold(p, Mathf.DegToRad(yaw), Mathf.DegToRad(pitch), 2.5f);
			Save($"{shot++:00}_pos_{pos.X:0}_{pos.Z:0}_{yaw:0}");
			if (_perf > 0f)
			{
				await Perf(p, yaw, pitch, "with");
				// --look-perfhide=A,B: the same again with those nodes hidden (what a room costs)
				var hidden = new List<Node3D>();
				foreach (var a in OS.GetCmdlineUserArgs())
					if (a.StartsWith("--look-perfhide="))
						foreach (var name in a["--look-perfhide=".Length..].Split(','))
							if (tree.Root.FindChild(name, true, false) is Node3D hn && hn.Visible) { hn.Visible = false; hidden.Add(hn); }
				if (hidden.Count > 0)
				{
					await Perf(p, yaw, pitch, "without " + string.Join("+", hidden.ConvertAll(h => h.Name.ToString())));
					foreach (var hn in hidden) hn.Visible = true;
				}
			}
			if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-outline-perf") >= 0)
			{
				// the white outlines' cost here: frame time with them, then without, then with again
				int lit = 0;
				foreach (var n in tree.GetNodesInGroup(ItemOutline.Group)) if (n is ItemOutline io && io.Showing) lit++;
				var ms = new double[3];
				for (int k = 0; k < 3; k++)
				{
					ItemOutline.AllOff = k == 1;
					await Hold(p, Mathf.DegToRad(yaw), Mathf.DegToRad(pitch), 1.0f);
					ulong t0 = Time.GetTicksUsec();
					int frames = 0;
					while (Time.GetTicksUsec() - t0 < 3_000_000) { await ToSignal(tree, SceneTree.SignalName.ProcessFrame); frames++; }
					ms[k] = (Time.GetTicksUsec() - t0) / 1000.0 / frames;
				}
				ItemOutline.AllOff = false;
				GD.Print($"[perf-outline] {lit} outlines showing: {ms[0]:0.00} ms on, {ms[1]:0.00} ms off, {ms[2]:0.00} ms on again");
			}
			if (_ab)
			{
				SetTreeLook(false);
				await Hold(p, Mathf.DegToRad(yaw), Mathf.DegToRad(pitch), 0.4f);
				Save($"{shot - 1:00}_pos_{pos.X:0}_{pos.Z:0}_{yaw:0}_OFF");
				SetTreeLook(true);
			}
			if (FindAtmo() is { } at0)
				GD.Print($"[look]   space {Audio.AudioDirector.CurrentSpace?.ToString() ?? "open"} enclosed {at0.Enclosed:0.00} under {at0.Underground:0.00} interior {at0.Interior:0.00} lodge {at0.Lodge:0.00} banks {at0.FogBanksOn} vol {at0.GetViewport().World3D?.Environment?.VolumetricFogDensity ?? -1f:0.0000}");
			else GD.Print("[look]   no ForestAtmosphere");
			if (_fogAb && FindAtmo() is { } atmo)
			{
				atmo.EnclosedClearsFog = false;
				await Hold(p, Mathf.DegToRad(yaw), Mathf.DegToRad(pitch), 2.5f);
				Save($"{shot - 1:00}_pos_{pos.X:0}_{pos.Z:0}_{yaw:0}_OFF");
				atmo.EnclosedClearsFog = true;
			}
		}
		if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-pickups") >= 0) await ShootPickups(p, start);
		if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-endings") >= 0) await ShootEndings(p, shot);
		GD.Print("[look] done");
		tree.Quit(0);
	}

	/// <summary>
	/// The pickup audit (2026-10-14): every item with a white outline (<see cref="ItemOutline"/>) that is there to take in
	/// this act, photographed from a standing spot about 3.5 m off (the side nearest the spawn, with a clear line to it,
	/// on a floor near its own) and from 1.5 m, the outline on; with --look-ab also the far shot without it. Each is
	/// logged with what's under it (the gap to the surface below: floating, sunk) and its size.
	/// </summary>
	private async System.Threading.Tasks.Task ShootPickups(PlayerController p, Vector3 spawn)
	{
		var tree = GetTree();
		float eyeH = p.CameraRig.Camera.GlobalPosition.Y - p.GlobalPosition.Y;
		var space = p.GetWorld3D().DirectSpaceState;
		var self = new Godot.Collections.Array<Rid> { p.GetRid() };
		string only = null;
		float[] dists = { 3.5f, 1.5f };
		Vector3? from = null;
		int around = 0;
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			if (a.StartsWith("--look-around=")) around = int.Parse(a["--look-around=".Length..]);
			if (a.StartsWith("--look-only=")) only = a["--look-only=".Length..];
			// --look-dists=2.5,1: the shot distances; --look-from=dx,dz: the side of the item to stand on (not the spawn's)
			else if (a.StartsWith("--look-dists=")) dists = Array.ConvertAll(a["--look-dists=".Length..].Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
			else if (a.StartsWith("--look-from="))
			{
				var f = Array.ConvertAll(a["--look-from=".Length..].Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
				from = new Vector3(f[0], 0, f[1]);
			}
		}

		// every interactable, and whether it has the white outline (doors and the like don't): the scope's audit
		if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-interactables") >= 0)
			foreach (var n in tree.GetNodesInGroup("interactables"))
				if (n is Interactable it && IsInstanceValid(it))
				{
					string prompt;
					try { prompt = it.GetPrompt(p); } catch (Exception) { prompt = "(throws)"; }
					var ol = it.GetNodeOrNull<ItemOutline>(ItemOutline.NodeName)
						?? ItemOutline.Find(it.HighlightRoot.IsEmpty ? it.GetParent() : it.GetNodeOrNull(it.HighlightRoot));
					GD.Print($"[ia] {it.GetParent()?.Name}/{it.Name} ({it.GetType().Name} in {it.GetParent()?.GetType().Name}) '{prompt}' enabled {it.Enabled} opening {it.IsOpening()} outline {(ol == null ? "none" : $"{ol.MeshCount} meshes active {ol.IsActive}")} at {it.GlobalPosition.X:0.0},{it.GlobalPosition.Y:0.0},{it.GlobalPosition.Z:0.0}");
				}
		int shot = 100;
		foreach (var n in tree.GetNodesInGroup(ItemOutline.Group))
		{
			if (n is not ItemOutline o || !IsInstanceValid(o) || !o.TryBounds(out Aabb box)) continue;
			string name = o.GetParent()?.Name ?? "?";
			if (o.GetParent() is Node par && par.Name == "Item") name = par.GetParent()?.Name ?? name;
			else if (o.GetParent() is Interactable ip && ip.GetParent() is Node ipp) name = $"{ipp.Name}.{ip.Name}".Replace("@", "");
			if (only != null && !Array.Exists(only.Split(','), s => name.ToString().Contains(s))) continue;
			Vector3 c = box.GetCenter();
			var down = space.IntersectRay(new PhysicsRayQueryParameters3D { From = c, To = c + Vector3.Down * 3f, CollisionMask = 1u, Exclude = self });
			float floorY = down.Count > 0 ? ((Vector3)down["position"]).Y : float.NaN;
			string under = down.Count > 0 && down["collider"].As<Node>() is { } col ? col.Name : "-";
			string vis = o.GetParent() is Interactable ? "" : VisualCover(tree.Root, o, box);
			GD.Print($"[pick] {name} at {c.X:0.00},{c.Y:0.00},{c.Z:0.00} size {box.Size.X:0.00}x{box.Size.Y:0.00}x{box.Size.Z:0.00} active {o.IsActive} gap {box.Position.Y - floorY:0.000} on {under}{vis}");
			if (!o.IsActive) continue;
			// --look-around=N: the first distance from N sides (an item whose way in isn't the spawn's side)
			var shots = new List<(float dist, Vector3 toward)>();
			if (around > 0)
				for (int k = 0; k < around; k++)
				{
					float ang = Mathf.Tau * k / around;
					shots.Add((dists[0], c + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 10f));
				}
			else foreach (float dd in dists) shots.Add((dd, from is Vector3 fd ? c + fd * 10f : spawn));
			foreach (var (dist, toward) in shots)
			{
				var pose = Pose(space, self, c, box, dist, toward, eyeH, around > 0);
				if (pose == null) { GD.Print($"[pick]   no standing spot {dist} m from {name}"); continue; }
				var (at, eye) = pose.Value;
				Vector3 d = c - eye;
				float yaw = Mathf.Atan2(-d.X, -d.Z), pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
				_pin = null;
				p.Teleport(at + Vector3.Up * 0.05f, yaw);
				await Hold(p, yaw, pitch, 2.2f);
				string label = $"{shot++:000}_{name}_{dist:0.0}m";
				Save(label);
				GD.Print($"[pick]   shot {label} from {at.X:0.0},{at.Y:0.0},{at.Z:0.0} dist {eye.DistanceTo(c):0.0}");
				if (_ab && dist > 2f)
				{
					ItemOutline.AllOff = true;
					await Hold(p, yaw, pitch, 0.6f);
					Save(label + "_OFF");
					ItemOutline.AllOff = false;
					await Hold(p, yaw, pitch, 0.6f);
				}
			}
		}
	}

	/// <summary>What the eye sees above and below the item (not the colliders: the drawn meshes): the first surface a ray
	/// from 0.6 m over its centre meets coming down, and the one under its centre. " covered by X" when something drawn
	/// lies over it (a snow drift heaped on a key), " visual gap N" to the drawn surface under it.</summary>
	private static string VisualCover(Node root, ItemOutline o, Aabb box)
	{
		Vector3 c = box.GetCenter();
		float top = box.End.Y, bottom = box.Position.Y;
		float coverY = float.MinValue, underY = float.MinValue;
		string coverName = "", underName = "";
		foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
		{
			if (n is not MeshInstance3D mi || mi.Mesh == null || !mi.IsVisibleInTree() || mi.HasMeta(ItemOutline.Meta)) continue;
			if (o.MeshRoot != null && (o.MeshRoot.IsAncestorOf(mi) || mi == o.MeshRoot)) continue;
			Aabb b = mi.GlobalTransform * mi.GetAabb();
			if (c.X < b.Position.X || c.X > b.End.X || c.Z < b.Position.Z || c.Z > b.End.Z || b.Position.Y > c.Y + 0.6f || b.End.Y < c.Y - 0.6f) continue;
			var tm = mi.Mesh.GenerateTriangleMesh();
			if (tm == null) continue;
			var inv = mi.GlobalTransform.AffineInverse();
			foreach (float start in new[] { c.Y + 0.6f, c.Y })
			{
				Vector3 from = inv * (c with { Y = start }), dir = (inv.Basis * Vector3.Down).Normalized();
				var hit = tm.IntersectRay(from, dir);
				if (hit.Count == 0) continue;
				float y = (mi.GlobalTransform * (Vector3)hit["position"]).Y;
				if (start > c.Y) { if (y > top - 0.002f && y > coverY && y < start) { coverY = y; coverName = mi.Name; } }
				else if (y > underY && y < c.Y + 0.001f) { underY = y; underName = mi.Name; }
			}
		}
		string s = "";
		if (coverY > float.MinValue && coverY < top + 0.6f && coverY - top < 0.25f) s += $" covered by {coverName} {coverY - top:+0.000;-0.000} over its top";
		if (underY > float.MinValue) s += $" visual gap {bottom - underY:0.000} on {underName}";
		return s;
	}

	/// <summary>A standing spot <paramref name="dist"/> m from the item with a clear view of it: floor under it near the
	/// item's own level, room to stand, nothing between the eye and the item. Toward the spawn first.</summary>
	private static (Vector3 at, Vector3 eye)? Pose(PhysicsDirectSpaceState3D space, Godot.Collections.Array<Rid> self, Vector3 c, Aabb box, float dist, Vector3 spawn, float eyeH, bool strict = false)
	{
		Vector3 toSpawn = (spawn - c) with { Y = 0 };
		toSpawn = toSpawn.LengthSquared() > 0.01f ? toSpawn.Normalized() : Vector3.Back;
		// a thin upright thing (a key hung on a wall) is seen face on, not edge on
		int thinAxis = (int)box.GetShortestAxisIndex();
		Vector3 thin = Vector3.Zero;
		if (thinAxis != 1 && box.Size[thinAxis] < 0.3f * box.Size[(int)box.GetLongestAxisIndex()]) thin[thinAxis] = 1f;
		(Vector3, Vector3)? best = null;
		float bestScore = float.MinValue;
		foreach (float dd in new[] { dist, dist * 0.75f, dist * 1.25f, dist * 0.5f })
		{
			for (int i = 0; i < 24; i++)
			{
				float a = Mathf.Tau * i / 24f;
				var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
				Vector3 xz = c + dir * dd;
				var hit = space.IntersectRay(new PhysicsRayQueryParameters3D { From = xz with { Y = c.Y + 1.2f }, To = xz with { Y = c.Y - 2.6f }, CollisionMask = 1u, Exclude = self });
				if (hit.Count == 0 || ((Vector3)hit["normal"]).Y < 0.7f) continue;
				Vector3 at = (Vector3)hit["position"];
				Vector3 eye = at + Vector3.Up * eyeH;
				// room to stand
				if (space.IntersectRay(new PhysicsRayQueryParameters3D { From = at + Vector3.Up * 0.25f, To = eye + Vector3.Up * 0.15f, CollisionMask = 1u, Exclude = self }).Count > 0) continue;
				// a clear line to it (stopping just short of it)
				Vector3 to = c - eye;
				// (a thing with its own collider, the font, a candle stand: meeting it is seeing it)
				var los = space.IntersectRay(new PhysicsRayQueryParameters3D { From = eye, To = eye + to * (1f - 0.12f / Mathf.Max(to.Length(), 0.2f)), CollisionMask = 1u, Exclude = self });
				if (los.Count > 0 && !box.Grow(0.1f).HasPoint((Vector3)los["position"])) continue;
				if (strict && dir.Dot(toSpawn) < 0.5f) continue;
				float score = dir.Dot(toSpawn) * 0.6f - Mathf.Abs(eye.Y - (c.Y + 1.0f)) * 0.8f - Mathf.Abs(dd - dist) * 0.3f + Mathf.Abs(dir.Dot(thin)) * 0.8f;
				if (score > bestScore) { bestScore = score; best = (at, eye); }
			}
			if (best != null) return best;
		}
		return best;
	}

	private Vector3? _pin;

	/// <summary>--look-perf=S (2026-10-14g): at each pose, the frame time averaged over S seconds, the draw calls and
	/// the primitives (a room's cost, with --look-perfhide).</summary>
	private float _perf
	{
		get
		{
			foreach (var a in OS.GetCmdlineUserArgs())
				if (a.StartsWith("--look-perf=")) return float.Parse(a["--look-perf=".Length..], CultureInfo.InvariantCulture);
			return 0f;
		}
	}

	private async System.Threading.Tasks.Task Perf(PlayerController p, float yaw, float pitch, string label)
	{
		await Hold(p, yaw, pitch, 0.6f);
		double t = 0, worst = 0;
		int frames = 0;
		long draws = 0, prims = 0;
		while (t < _perf)
		{
			await Hold(p, yaw, pitch, 0f);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			double dt = GetProcessDeltaTime();
			t += dt;
			worst = Math.Max(worst, dt);
			frames++;
			draws += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
			prims += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
		}
		GD.Print($"[look]   perf {label}: {t * 1000.0 / frames:0.00} ms/frame avg ({frames / t:0} fps), worst {worst * 1000.0:0.0} ms, draws {draws / frames}, prims {prims / frames}");
	}
	private bool _fogAb => Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-fogab") >= 0;
	private ForestAtmosphere FindAtmo()
	{
		foreach (var n in GetTree().Root.FindChildren("*", nameof(ForestAtmosphere), true, false)) return n as ForestAtmosphere;
		return null;
	}
	private bool _ab => Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-ab") >= 0;

	/// <summary>A/B: the 2026-10-13 tree shading on or off (the trunks' roundness, the crowns' depth).</summary>
	private static void SetTreeLook(bool on)
	{
		var bark = ProcTextures.TreeBarkMat;
		bark.SetShaderParameter("round_shade", on ? 0.62f : 0f);
		bark.SetShaderParameter("under_shade", on ? 0.35f : 0f);
		bark.SetShaderParameter("cavity", on ? 0.3f : 0f);
		bark.SetShaderParameter("tint", on ? new Color(1f, 0.88f, 0.77f) : new Color(0.6f, 0.53f, 0.47f));
		bark.SetShaderParameter("light_falloff", on ? 1.6f : 1f);
		bark.SetShaderParameter("light_cavity", on ? 0.5f : 0f);
		bark.SetShaderParameter("light_gain", on ? 0.7f : 1f);
		var fir = ProcTextures.FirBranchMat;
		fir.SetShaderParameter("inner_ao", on ? 0.55f : 0f);
		fir.SetShaderParameter("translucency", on ? 0.3f : 0f);
	}

	private async System.Threading.Tasks.Task Hold(PlayerController p, float yaw, float pitch, float s)
	{
		double t = 0;
		while (t < s)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			t += GetProcessDeltaTime();
			// settle on the ground for a moment, then stay put (A/B shots must frame the same thing)
			if (_pin is Vector3 pin) { p.GlobalPosition = pin; p.Velocity = Vector3.Zero; }
			else if (t > 0.8 && p.IsOnFloor()) _pin = p.GlobalPosition;
			p.CameraRig.SnapBehind(yaw);
			p.CameraRig.SetPitch(pitch);
			if (_noHud) HideHud();
		}
	}

	/// <summary>--look-endings (Act 1's level, at the car): the car as the endings show it, posed as EndingDirector poses it.
	/// The fifth: the eye at the driver's seat (EyeHeight 1.0), looking out of the windscreen. The third: the car's meshes
	/// squashed about its origin and the wendigo stood on it, seen from the lot.</summary>
	private async System.Threading.Tasks.Task ShootEndings(PlayerController p, int shot)
	{
		if (GetTree().Root.FindChild("Car", true, false) is not ParkProp car) { GD.PrintErr("[look] no car"); return; }
		var fwd = -car.GlobalBasis.Z;
		float yaw = Mathf.Atan2(-fwd.X, -fwd.Z);
		float eye = p.CameraRig.EyeHeight;
		var seat = car.GlobalPosition + car.GlobalBasis * new Vector3(-0.38f, 1.15f, 0.25f);
		p.CameraRig.EyeHeight = 1.0f;
		_pin = seat + Vector3.Down * 1.0f;
		p.GlobalPosition = _pin.Value;
		await Hold(p, yaw, -0.05f, 2.0f);
		Save($"{shot++:00}_ending5_seat");
		await Hold(p, yaw + 0.6f, -0.1f, 1.0f);
		Save($"{shot++:00}_ending5_seat_left");
		p.CameraRig.EyeHeight = eye;
		var squash = new Transform3D(Basis.FromScale(new Vector3(1.05f, 0.55f, 1f)), Vector3.Zero);
		var carXf = car.GlobalTransform;
		foreach (var g in car.FindChildren("*", "GeometryInstance3D", true, false))
			if (g is GeometryInstance3D gi && gi.GetParent() is not GeometryInstance3D && g is not Decal)
				gi.GlobalTransform = carXf * (squash * (carXf.AffineInverse() * gi.GlobalTransform));
		var view = car.GlobalTransform * new Vector3(-5f, 0f, -5.5f);
		var w = new Entities.Wendigo { Name = "LookWendigo" };
		car.GetParent().AddChild(w);
		w.StandAt(car.GlobalPosition + Vector3.Up * 0.9f, view);
		_pin = null;
		var d = car.GlobalPosition - view;
		p.Teleport(view + Vector3.Up * 0.3f, Mathf.Atan2(-d.X, -d.Z));
		await Hold(p, Mathf.Atan2(-d.X, -d.Z), Mathf.DegToRad(4f), 3.0f);
		Save($"{shot++:00}_ending3_crushed");
	}

	/// <summary>--look-nohud: the hands and what they hold, the chapter card, the subtitles and the item HUD hidden (a
	/// clean look at the set, a prop's before and after).</summary>
	private readonly bool _noHud = Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-nohud") >= 0;
	private void HideHud()
	{
		if (GetViewport().GetCamera3D() is { } cam) cam.CullMask &= ~HeldItem.HeldLayer;
		foreach (var n in GetTree().Root.FindChildren("*", "", true, false))
			if (n is FirstPersonHand or HeldItem) ((Node3D)n).Visible = false;
			else if (n is UI.ChapterCard or UI.Subtitle or UI.EquippedItemHud) ((CanvasLayer)n).Visible = false;
	}

	private async System.Threading.Tasks.Task Wait(float s)
	{
		double t = 0;
		while (t < s) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); t += GetProcessDeltaTime(); }
	}

	private void Save(string label)
	{
		var img = GetViewport().GetTexture().GetImage();
		if (img.GetWidth() > 1280) img.Resize(1280, img.GetHeight() * 1280 / img.GetWidth(), Image.Interpolation.Bilinear);
		img.SavePng(ProjectSettings.GlobalizePath($"{_out}/{label}.png"));
		GD.Print($"[look] {label}  fps {Engine.GetFramesPerSecond():0}  prims {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)}");
	}
}

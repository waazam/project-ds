using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for buildings_preview.tscn: first-person (1.62 m eye, FOV 70, the
/// game's PS2 post) screenshots of the cabin, shed, tent/map, storm, night fire and
/// the Act 6 veins into test-output/buildings/, plus a triangle count per building.
/// Pass "-- --only=cabin,shed,storm,night,fire,burnt,clutter,veins" to limit. Not used by the game.
/// </summary>
public partial class BuildingsPreview : Node3D
{
	[Export] public NodePath CameraPath = "Camera3D";
	private const float Eye = 1.62f;
	private Camera3D _cam;
	private ForestTerrain _terrain;
	private ForestAtmosphere _atmo;
	private string _out;
	private HashSet<string> _only;

	public override void _Ready()
	{
		_cam = GetNode<Camera3D>(CameraPath);
		_cam.MakeCurrent();
		_out = ProjectSettings.GlobalizePath("res://test-output/buildings");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--only=")) _only = new HashSet<string>(a.Substring(7).Split(','));
		Run();
	}

	private bool Want(string s) => _only == null || _only.Contains(s);
	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Secs(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
	private void Log(string s) => GD.Print("[bld] " + s);
	private Vector3 G(Vector3 p) { p.Y = _terrain.HeightAt(p.X, p.Z); return p; }

	private async Task Shot(string name, Vector3 eye, Vector3 at, int settle = 6)
	{
		_cam.GlobalPosition = eye;
		_cam.LookAt(at, Vector3.Up);
		await Frames(settle);
		GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
		Log($"shot {name}");
	}

	/// <summary>Eye standing at local (x,z) of a building (on terrain, or on the given floor height), looking at local point.</summary>
	private async Task Local(string name, Node3D b, Vector2 standXZ, Vector3 lookLocal, float? floorLocalY = null)
	{
		Vector3 w = b.GlobalTransform * new Vector3(standXZ.X, 0, standXZ.Y);
		float y = floorLocalY.HasValue ? b.GlobalPosition.Y + floorLocalY.Value : _terrain.HeightAt(w.X, w.Z);
		await Shot(name, new Vector3(w.X, y + Eye, w.Z), b.GlobalTransform * lookLocal);
	}

	private static int Tris(Node n)
	{
		int t = 0;
		if (n is MeshInstance3D mi && mi.Mesh != null)
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
			{
				var arr = mi.Mesh.SurfaceGetArrays(s);
				var idx = arr[(int)Mesh.ArrayType.Index].AsInt32Array();
				t += idx.Length > 0 ? idx.Length / 3 : arr[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
			}
		foreach (var c in n.GetChildren()) t += Tris(c);
		return t;
	}

	private async Task ShotTo(string dir, string name, Vector3 eye, Vector3 at, int settle = 8)
	{
		_cam.GlobalPosition = eye;
		_cam.LookAt(at, Vector3.Up);
		await Frames(settle);
		GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}.png");
		Log($"after {name} eye {eye} look {at}");
	}

	/// <summary>
	/// Re-shoots the pre-rework review (test-output/review, visual_review.log) from the exact same
	/// world eye positions into test-output/after/ under the same names. Look targets on the
	/// buildings keep their offset from the building origin (the cabin now stands at sill height,
	/// so targets move up with it); interior eyes move up by the same amount (the floor is raised).
	/// </summary>
	private async Task AfterShots(Cabin cabin, Shed shed)
	{
		string dir = ProjectSettings.GlobalizePath("res://test-output/after");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		float dy = cabin.GlobalPosition.Y - 0.48193133f;           // old cabin origin Y from the review log
		float dyS = shed.GlobalPosition.Y - 8.907389f;              // old shed origin (look targets were origin + 0.1..1.1)
		Vector3 C(float x, float y, float z) => new(x, y + dy, z);
		Vector3 S(float x, float y, float z) => new(x, y + dyS, z);
		Vector3 W(float x, float y, float z) => new(x, y, z);

		await Frames(30);
		cabin.SetBoarded(true);
		await ShotTo(dir, "cabin_01_front_16m", W(17.473246f, 9.141799f, -32.32106f), C(2.5340867f, 2.2819314f, -26.398935f));
		await ShotTo(dir, "cabin_02_threequarter_9m", W(7.5729885f, 4.82671f, -34.11965f), C(2.5340867f, 1.9819313f, -26.398935f));
		await ShotTo(dir, "cabin_03_back_chimney", W(-3.464775f, 1.8047285f, -18.398083f), C(2.5340867f, 2.6819315f, -26.398935f));
		await ShotTo(dir, "cabin_04_boarded_door_3m", W(7.7978086f, 4.3171396f, -28.351685f), C(5.0299826f, 1.4819313f, -27.12729f));
		foreach (var n in new[] { "LanternPickup", "CompassPickup" })
			if (cabin.GetNodeOrNull(n) is Pickup p) p.Reveal();
		Vector3 spots = (cabin.GetNode<Node3D>("LanternSpot").GlobalPosition + cabin.GetNode<Node3D>("CompassSpot").GlobalPosition) * 0.5f;
		await ShotTo(dir, "cabin_05_porch_pickups", W(6.893876f, 4.00518f, -27.879555f), spots);
		cabin.OpenDoor();
		await ShotTo(dir, "cabin_06_open_door_6m", W(10.789745f, 5.374615f, -28.80811f), C(2.5340867f, 1.5819314f, -26.398935f));
		await ShotTo(dir, "cabin_07_friend_15m", W(17.357443f, 8.87206f, -30.933044f), C(1.1061016f, 1.5819314f, -26.294733f));
		await ShotTo(dir, "cabin_08_interior_friend_3m", C(3.8780308f, 2.6843257f, -26.791126f), C(1.1061016f, 1.4819313f, -26.294733f));
		await ShotTo(dir, "cabin_09_friend_close_1_5m", C(2.1299405f, 2.1019313f, -27.426878f), C(1.0649018f, 1.6819314f, -26.293127f));
		await ShotTo(dir, "cabin_10_interior_corner_up", C(3.5459704f, 2.1019313f, -27.215078f), C(0.62647736f, 3.3819313f, -23.654665f));
		await ShotTo(dir, "cabin_11_interior_lookback_door", C(0.6541352f, 2.1019313f, -26.058668f), C(5.0299826f, 1.6819314f, -27.12729f));
		await ShotTo(dir, "cabin_12_interior_floor", C(3.718156f, 2.1019313f, -25.911104f), C(1.8542632f, 0.48193133f, -25.158838f));

		await ShotTo(dir, "shed_01_front_8m", W(28.032806f, 13.66013f, -35.402565f), S(20.773329f, 9.607389f, -31.72153f));
		await ShotTo(dir, "shed_02_threequarter_5m", W(23.012644f, 12.610833f, -36.541847f), S(20.773329f, 9.707389f, -31.72153f));
		await ShotTo(dir, "shed_03_inside_look", W(22.597254f, 11.616414f, -32.25379f), S(20.773329f, 9.007389f, -32.121532f));
		await ShotTo(dir, "shed_04_back_side", W(15.133118f, 7.756484f, -33.20073f), S(20.773329f, 10.007389f, -31.72153f));

		await ShotTo(dir, "clutter_ForgottenTent_1_5m", W(-1.327173f, 6.883222f, -303.0688f), W(-2.377173f, 5.3944216f, -304.11877f));
		await ShotTo(dir, "clutter_ForgottenTent_5m", W(1.122827f, 6.926479f, -300.61877f), W(-2.377173f, 5.494422f, -304.11877f));
		await ShotTo(dir, "clutter_ForgottenTent_side", W(-4.627173f, 7.1231585f, -303.11877f), W(-2.377173f, 5.3944216f, -304.11877f));
		await ShotTo(dir, "clutter_TornMap_1_5m", W(4.656727f, 3.3720171f, -100.35215f), W(3.6067271f, 1.8753992f, -101.40215f));
		await ShotTo(dir, "clutter_TornMap_5m", W(7.106727f, 3.924039f, -97.90215f), W(3.6067271f, 1.9753993f, -101.40215f));
		await ShotTo(dir, "clutter_TornMap_side", W(1.3567271f, 3.2988386f, -100.40215f), W(3.6067271f, 1.8753992f, -101.40215f));

		// Act 3 storm (the autotest's shot: 30 m behind the cabin, just outside the safe zone)
		var rain = RainVfx.Instance;
		if (rain != null)
		{
			rain.Intensity = 1f;
			await Secs(3.0);
			Vector3 p = G(cabin.GlobalPosition + new Vector3(0, 0, -30f));
			await ShotTo(dir, "24_storm_active", p + Vector3.Up * Eye, G(p + new Vector3(0.5f, 0, -10f)) + Vector3.Up * 3.2f, 20);
			rain.Flash(1f);
			await Secs(0.16);
			GetViewport().GetTexture().GetImage().SavePng($"{dir}/24_storm_active_lightning.png");
			await Frames(90);
			rain.Intensity = 0f;
			await Secs(2.0);
		}

		// Act 6: menacing mood + the clearing's dressing (giant firs); the veins as the new skin
		var clearing = GetTree().Root.FindChild("Act6Clearing", true, false) as Node3D;
		var dressing = GetTree().Root.FindChild("DeepZoneDressing", true, false) as DeepZoneDressing ?? FindDressing(GetTree().Root);
		if (clearing != null)
		{
			_atmo?.SetMood(ForestAtmosphere.Mood.Menacing, 0.1f);
			if (dressing != null)
			{
				dressing.Reveal(clearing.GlobalPosition, 55f);
				// replace the old flat overlay plane (if DeepZoneDressing still builds it) with the terrain skin
				foreach (var c in dressing.GetChildren())
					if (c is MeshInstance3D mi && mi.Mesh is PlaneMesh) mi.QueueFree();
				if (dressing.FindChild("VeinyGround", true, false) == null)
					dressing.AddChild(VeinyGround.Create(_terrain, clearing.GlobalPosition, 55f));
			}
			await Frames(40);
			await ShotTo(dir, "act6_01_clearing_menacing", W(43, 10.988216f, -583.4f), W(40, 11.739843f, -599.4f));
			await ShotTo(dir, "act6_02_giant_firs_up", W(40, 10.739702f, -579.4f), W(40, 39.739845f, -539.4f));
			await ShotTo(dir, "act6_03_veins_ground", W(34, 11.339597f, -589.4f), W(34, 9.662568f, -595.4f));
			await ShotTo(dir, "act6_04_look_out", W(40, 11.487506f, -591.4f), W(60, 10.302527f, -549.4f));
			await ShotTo(dir, "act6_05_slope_veins", W(55, 16.259445f, -624.4f), W(50, 9.792591f, -614.4f));
		}

		// Act 7 night: the player's lantern (Lantern.cs glow values) held low right of the camera
		_atmo?.SetMood(ForestAtmosphere.Mood.Night, 0.1f);
		var lantern = new OmniLight3D
		{
			LightColor = new Color(1f, 0.72f, 0.42f), LightEnergy = 1.1f, OmniRange = 7.5f, OmniAttenuation = 1.4f,
			ShadowEnabled = true, Position = new Vector3(0.18f, -0.25f, -0.25f),
		};
		_cam.AddChild(lantern);
		await Frames(40);
		await ShotTo(dir, "night_01_cabin_lantern_12m", W(14.053608f, 6.957228f, -29.760574f), C(2.5340867f, 1.8819313f, -26.398935f));
		cabin.SetBurning(1f);
			await Secs(5.0);
		await ShotTo(dir, "night_02_fire_25m", W(25.132408f, 13.723988f, -38.202152f), C(2.5340867f, 3.4819312f, -26.398935f));
		await ShotTo(dir, "night_03_fire_10m", W(11.573415f, 5.696616f, -31.120222f), C(2.5340867f, 3.2819314f, -26.398935f));
		cabin.SetBurning(0f);
		await ShotTo(dir, "night_04_trail_lantern", W(-4.0019126f, 3.0665724f, -93.2142f), W(2.7836576f, 2.6221237f, -103.10874f));
		lantern.QueueFree();
	}

	private static DeepZoneDressing FindDressing(Node n)
	{
		if (n is DeepZoneDressing d) return d;
		foreach (var c in n.GetChildren()) { var r = FindDressing(c); if (r != null) return r; }
		return null;
	}

	private async void Run()
	{
		await Frames(10);
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		_atmo = GetTree().GetFirstNodeInGroup("atmosphere") as ForestAtmosphere;
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		var shed = GetTree().Root.FindChild("Shed", true, false) as Shed;
		Log($"cabin steps {cabin.StepCount}");
		Log($"cabin at {cabin.GlobalPosition} (terrain at centre {_terrain.HeightAt(cabin.GlobalPosition.X, cabin.GlobalPosition.Z):0.00})");
		Log($"cabin tris shell {Tris(cabin.GetNode("Generated/CabinMesh"))}, interior {Tris(cabin.GetNode("Generated/InteriorMesh"))}, total generated {Tris(cabin.GetNode("Generated"))}");
		Log($"shed at {shed.GlobalPosition}, tris {Tris(shed.GetNode("Generated"))}");
		foreach (var m in new[] { "LanternSpot", "CompassSpot" })
			Log($"{m} {cabin.GetNode<Node3D>(m).GlobalPosition}  ground there {_terrain.HeightAt(cabin.GetNode<Node3D>(m).GlobalPosition.X, cabin.GetNode<Node3D>(m).GlobalPosition.Z):0.00}");
		Log($"HammerSpot {shed.GetNode<Node3D>("HammerSpot").GlobalPosition}");
		float hd = cabin.Depth * 0.5f;

		if (_only != null && _only.Contains("stairtrail"))
		{
			var trail = GetTree().Root.FindChild("FriendTrail", true, false) as FriendTrail;
			var stairs = (GetTree().GetFirstNodeInGroup("stairs_top_trigger") as Node3D)?.GetParent() as Node3D;
			if (trail != null && stairs != null)
			{
				await Frames(30);
				Vector3 E(Vector2 p) => new Vector3(p.X, _terrain.HeightAt(p.X, p.Y) + Eye, p.Y);
				Vector3 foot = stairs.GlobalPosition;
				Log($"trail length {trail.Length:0.0} m -> {trail.Length / 1.9f:0} s at walk speed 1.9 m/s; stairs foot {foot}");
				for (float s = 0; s <= trail.Length + 0.1f; s += 5f)
				{
					var p = trail.At(s, out _);
					float sil = 0f;
					foreach (var z in ProjectDS.Audio.SilenceZone.All) sil = Mathf.Max(sil, z.SilenceAt(new Vector3(p.X, 0, p.Y)));
					Log($"s={s:00} dist to stairs {new Vector2(foot.X - p.X, foot.Z - p.Y).Length():0.0} m  silence {sil:0.00}");
				}
				string[] names = { "hat", "bottle", "map", "glove", "pole", "bootprint" };
				var p0 = trail.At(0f, out Vector2 d0);
				await Shot("stairs_trail_01_path_end", E(p0 - d0 * 1.5f), E(trail.At(10f, out _)) + Vector3.Down * 0.9f);
				for (int i = 0; i < trail.ItemAt.Length; i++)
				{
					float from = i == 0 ? 0f : trail.ItemAt[i - 1];
					var pf = trail.At(from, out _);
					var item = trail.GetNode<Node3D>($"Belonging{i}");
					float dist = new Vector2(item.GlobalPosition.X - pf.X, item.GlobalPosition.Z - pf.Y).Length();
					Log($"item {names[i]} at {item.GlobalPosition}, {dist:0.0} m from previous cue");
					await Shot($"stairs_trail_{i + 2:00}_to_{names[i]}", E(pf), item.GlobalPosition + Vector3.Up * (i == 0 ? 1.1f : 0.1f));
				}
				foreach (float back in new[] { 40f, 30f, 22f })
				{
					float s = Mathf.Max(0f, trail.Length + 9f - back);
					var p = trail.At(s, out _);
					float d = new Vector2(foot.X - p.X, foot.Z - p.Y).Length();
					await Shot($"stairs_trail_{(int)back}m_look_stairs", E(p), foot + new Vector3(0, 2.5f, -3f));
					Log($"stairs view from {d:0.0} m");
				}
			}
		}

		if (Want("firetest"))
		{
			Vector3 at = _terrain.TrailPoint(20f, out Vector3 tt);
			var fx = new FireVfx { Extent = new Vector3(1.2f, 1.4f, 1.2f), Seed = 5 };
			AddChild(fx);
			fx.GlobalPosition = at;
			await Secs(3.0);
			foreach (var c in fx.GetChildren()) Log($"firetest child {c.Name} {c.GetType().Name} {(c is GpuParticles3D gp ? $"emitting {gp.Emitting} amount {gp.Amount} ratio {gp.AmountRatio}" : "")}");
			Vector3 side = tt.Cross(Vector3.Up).Normalized();
			await Shot("firetest_4m", G(at + side * 4f) + Vector3.Up * Eye, at + Vector3.Up * 1.5f);
			await Shot("firetest_8m_up", G(at + side * 8f) + Vector3.Up * Eye, at + Vector3.Up * 3.5f);
			fx.QueueFree();
		}

		if (_only != null && _only.Contains("after"))
		{
			await AfterShots(cabin, shed);
			Log("done");
			GetTree().Quit();
			return;
		}

		if (Want("cabin"))
		{
			await Frames(20);
			await Local("cabin_01_front_16m", cabin, new Vector2(0, 16f), new Vector3(0, 1.5f, 0));
			await Local("cabin_02_threequarter_9m", cabin, new Vector2(6.5f, 8f), new Vector3(0, 1.4f, 0));
			await Local("cabin_03_back_chimney", cabin, new Vector2(-8f, -7f), new Vector3(-1f, 1.8f, -0.5f));
			await Local("cabin_03b_side_foundation", cabin, new Vector2(8.5f, -1f), new Vector3(0, 0.3f, 0));
			await Local("cabin_04_boarded_door_3m", cabin, new Vector2(0.3f, hd + 3.8f), new Vector3(0, 1.1f, hd));
			await Local("cabin_05_porch_pickups", cabin, new Vector2(0.2f, hd + 3.4f), new Vector3(0, 0.2f, hd + 0.5f));
			cabin.SetBoarded(true);
			await Local("cabin_05b_boarded_4m", cabin, new Vector2(-0.8f, hd + 5f), new Vector3(0, 1.1f, hd));
			cabin.OpenDoor();
			await Local("cabin_06_open_door_6m", cabin, new Vector2(1.5f, hd + 6f), new Vector3(0, 1.1f, hd));
			await Local("cabin_08_interior_friend_3m", cabin, new Vector2(0.1f, hd - 0.5f), new Vector3(0.3f, 0.7f, -1.6f), 0f);
			await Local("cabin_10_interior_corner_up", cabin, new Vector2(1.3f, 1.8f), new Vector3(-1.2f, 2.6f, -1.8f), 0f);
			await Local("cabin_11_interior_lookback_door", cabin, new Vector2(0.6f, -1.9f), new Vector3(-0.2f, 1.1f, hd), 0f);
			await Local("cabin_12_interior_stove_cot", cabin, new Vector2(0.2f, 1.2f), new Vector3(-1.2f, 0.6f, -1.8f), 0f);
			await Local("cabin_13_interior_cot_shelf", cabin, new Vector2(-1.2f, 0.9f), new Vector3(1.5f, 0.8f, -0.4f), 0f);
		}

		if (Want("shed"))
		{
			await Local("shed_01_front_8m", shed, new Vector2(0.5f, 8f), new Vector3(0, 1.1f, 0));
			await Local("shed_02_threequarter_5m", shed, new Vector2(3.5f, 4f), new Vector3(0, 1f, 0));
			await Local("shed_03_inside_look", shed, new Vector2(0.1f, shed.Depth * 0.5f + 1.2f), new Vector3(0.1f, 0.9f, -0.8f));
			await Local("shed_04_back_side", shed, new Vector2(-4f, -4f), new Vector3(0, 1f, 0));
		}

		if (Want("clutter"))
		{
			foreach (var name in new[] { "ForgottenTent", "TornMap" })
			{
				var p = GetTree().Root.FindChild(name, true, false) as Node3D;
				if (p == null) continue;
				Vector3 f = p.GlobalBasis.Z; f.Y = 0; f = f.Normalized();
				Vector3 r = p.GlobalBasis.X; r.Y = 0; r = r.Normalized();
				Vector3 a = G(p.GlobalPosition + f * 1.5f + r * 0.4f), b = G(p.GlobalPosition + f * 5f + r * 1f), c = G(p.GlobalPosition + r * 2.5f);
				await Shot($"clutter_{name}_1_5m", a + Vector3.Up * Eye, p.GlobalPosition + Vector3.Up * 0.2f);
				await Shot($"clutter_{name}_5m", b + Vector3.Up * Eye, p.GlobalPosition + Vector3.Up * 0.3f);
				await Shot($"clutter_{name}_side", c + Vector3.Up * Eye, p.GlobalPosition + Vector3.Up * 0.3f);
			}
		}

		var rain = RainVfx.Instance;
		if (Want("storm") && rain != null)
		{
			rain.Intensity = 1f;
			await Secs(3.0);
			Vector3 trail = _terrain.TrailPoint(70f, out Vector3 t);
			await Shot("storm_01_trail", trail + Vector3.Up * Eye, trail + t * 10f + Vector3.Up * 1.3f, 30);
			await Local("storm_02_cabin_9m", cabin, new Vector2(5.5f, 8f), new Vector3(0, 1.4f, 0));
			rain.Flash(1f);
			await Secs(0.16);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/storm_03_cabin_lightning.png");
			Log("shot storm_03_cabin_lightning");
			await Frames(90);
			_cam.GlobalPosition = trail + Vector3.Up * Eye; _cam.LookAt(trail + t * 10f + Vector3.Up * 1.3f, Vector3.Up);
			await Secs(0.5);
			rain.Flash(1f);
			await Secs(0.16);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/storm_04_trail_lightning.png");
			Log("shot storm_04_trail_lightning");
			await Frames(60);
			rain.Intensity = 0f;
			await Secs(2.0);
		}

		if (Want("night") || Want("fire"))
		{
			_atmo?.SetMood(ForestAtmosphere.Mood.Night, 0.1f);
			await Frames(30);
			if (Want("night"))
			{
				await Local("night_01_cabin_12m", cabin, new Vector2(3f, 12f), new Vector3(0, 1.4f, 0));
				Vector3 trail = _terrain.TrailPoint(60f, out Vector3 t);
				await Shot("night_04_trail", trail + Vector3.Up * Eye, trail + t * 10f + Vector3.Up * 1.2f);
				Vector3 deep = _terrain.TrailPoint(330f, out Vector3 t2);
				await Shot("night_05_deep_trail", deep + Vector3.Up * Eye, deep + t2 * 10f + Vector3.Up * 1.2f);
			}
			if (Want("fire"))
			{
				cabin.SetBoarded(false);
				cabin.OpenDoor();
				cabin.SetBurning(1f);
			await Secs(5.0);
				await Local("fire_01_25m", cabin, new Vector2(8f, 23f), new Vector3(0, 2f, 0));
				await Local("fire_02_10m", cabin, new Vector2(3f, 10f), new Vector3(0, 2f, 0));
				await Local("fire_03_side_14m", cabin, new Vector2(13f, 3f), new Vector3(0, 2.2f, 0));
				cabin.SetBurning(0f);
			}
		}

		if (Want("burnt"))
		{
			_atmo?.SetMood(ForestAtmosphere.Mood.Dawn, 0.1f);
			await Frames(20);
			cabin.SetBurnt(true);
			cabin.SetBurning(0.25f);
			await Frames(60);
			await Local("burnt_01_front_10m", cabin, new Vector2(3f, 10f), new Vector3(0, 1.8f, 0));
			await Local("burnt_02_threequarter", cabin, new Vector2(-7f, 7f), new Vector3(0, 1.8f, 0));
		}

		if (Want("veins"))
		{
			var clearing = GetTree().Root.FindChild("Act6Clearing", true, false) as Node3D;
			if (clearing != null)
			{
				Vector3 c = clearing.GlobalPosition;
				AddChild(VeinyGround.Create(_terrain, c, 55f));
				_atmo?.SetMood(ForestAtmosphere.Mood.Menacing, 0.1f);
				await Frames(30);
				Vector3 rim = G(c + new Vector3(-44f, 0, 20f));
				await Shot("veins_01_rim_worms", rim + Vector3.Up * Eye, G(c + new Vector3(-38f, 0, 17f)));
				Vector3 mid = G(c + new Vector3(-22f, 0, 10f));
				await Shot("veins_02_mid", mid + Vector3.Up * Eye, G(c + new Vector3(-10f, 0, 4f)) + Vector3.Up * 0.3f);
				Vector3 inner = G(c + new Vector3(-8f, 0, 6f));
				await Shot("veins_03_inner_down", inner + Vector3.Up * Eye, G(c + new Vector3(-6f, 0, 3.5f)));
			}
		}

		Log("done");
		GetTree().Quit();
	}
}

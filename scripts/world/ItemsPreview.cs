using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for items_preview.tscn: the real forest, the real player and HUD,
/// and the game's own lighting (not studio light, except the two lineup shots
/// that were studio-lit in the review).
///
/// 1. After shots matching test-output/review/ (same eye/look points, taken from
///    visual_review.log; items that moved are framed by the same offset from
///    their new position) into test-output/after/ under the same names.
/// 2. Per pickup: far, 2 m with the crosshair beside it (no highlight) and on it
///    (highlight + prompt), through the real PlayerInteraction; copies go to
///    after/highlight_off_*.png and after/highlight_on_*.png. Prompt checks,
///    blocked states included.
/// 3. The friend and the birds in place (test-output/characters).
///
/// Run with --autotest so any checkpoint a trigger reaches goes to the test
/// save slots, never the owner's save. Not used by the game.
/// </summary>
public partial class ItemsPreview : Node3D
{
	[Export] public bool QuitWhenDone = true;

	private PlayerController _player;
	private PlayerInteraction _interaction;
	private PlayerInventory _inv;
	private ForestTerrain _terrain;
	private Camera3D _free;
	private SpotLight3D _freeLamp;
	private string _props, _chars, _after;
	private int _fails;

	public override void _Ready()
	{
		_props = ProjectSettings.GlobalizePath("res://test-output/props");
		_chars = ProjectSettings.GlobalizePath("res://test-output/characters");
		_after = ProjectSettings.GlobalizePath("res://test-output/after");
		foreach (var d in new[] { _props, _chars, _after }) DirAccess.MakeDirRecursiveAbsolute(d);
		Run();
	}

	private void Log(string s) => GD.Print("[items] " + s);
	private void Check(string what, bool ok, string detail = "")
	{
		if (!ok) _fails++;
		Log($"{(ok ? "PASS" : "FAIL")} {what} {detail}");
	}

	private async Task Frames(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}

	private Vector3 Floor(Vector3 p, float above = 1.2f)
	{
		var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * above, p + Vector3.Down * 6f, 1u);
		q.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
		if (hit.Count > 0) return (Vector3)hit["position"];
		p.Y = _terrain.HeightAt(p.X, p.Z);
		return p;
	}

	/// <summary>Player stands at <paramref name="feet"/> (snapped to the floor) and looks at <paramref name="target"/>.</summary>
	private async Task Pose(Vector3 feet, Vector3 target, float floorAbove = 1.2f)
	{
		_player.CameraRig.Camera.MakeCurrent();
		feet = Floor(feet, floorAbove) + Vector3.Up * 0.02f;
		Vector3 eye = feet + Vector3.Up * 1.62f;
		Vector3 d = target - eye;
		float yaw = Mathf.Atan2(-d.X, -d.Z);
		float pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
		_player.Teleport(feet, yaw);
		_player.CameraRig.SetPitch(pitch);
		await Frames(3);
		_player.CameraRig.SetPitch(pitch);
		_player.CameraRig.SnapBehind(yaw);
		await Frames(25);
	}

	/// <summary>Free camera (FOV 70) at an exact eye/look pair, optionally carrying a lantern-like beam.</summary>
	private async Task Free(string name, Vector3 eye, Vector3 look, bool lamp = false)
	{
		// Park the player out of the way so its interaction can't highlight anything in frame.
		_player.Teleport(new Vector3(60f, _terrain.HeightAt(60f, 60f) + 0.1f, 60f), 0f);
		_free.GlobalPosition = eye;
		Vector3 up = Mathf.Abs((look - eye).Normalized().Y) > 0.98f ? Vector3.Forward : Vector3.Up;
		_free.LookAt(look, up);
		_freeLamp.Visible = lamp;
		_free.MakeCurrent();
		await Frames(12);
		Shot(_after, name);
	}

	private void Shot(string dir, string name)
	{
		GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}.png");
		Log("shot " + name);
	}

	private static Vector3 Flat(Vector3 v) { v.Y = 0; return v.Normalized(); }

	private static int Tris(Node n)
	{
		int t = 0;
		foreach (var c in n.FindChildren("*", "MeshInstance3D", true, false))
			t += ItemMeshes.CountTriangles(((MeshInstance3D)c).Mesh);
		return t;
	}

	/// <summary>True if nothing solid sits between the eye and the target.</summary>
	private bool Clear(Vector3 eye, Vector3 target, float slack)
	{
		var q = PhysicsRayQueryParameters3D.Create(eye, target, 1u);
		q.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
		return hit.Count == 0 || eye.DistanceTo((Vector3)hit["position"]) > eye.DistanceTo(target) - slack;
	}

	/// <summary>Far, 2 m crosshair-off and crosshair-on shots of one pickup, approached from <paramref name="fromDir"/> (or the nearest clear direction).</summary>
	private async Task ShootItem(string name, Node3D node, Vector3 fromDir, Interactable use, float far = 5f)
	{
		if (node == null || !IsInstanceValid(node)) { Check($"{name} present", false); return; }
		Vector3 c = use != null ? use.GlobalTransform * use.PickOffset : node.GlobalPosition;
		fromDir = Flat(fromDir);
		foreach (float deg in new[] { 0f, 40f, -40f, 80f, -80f, 130f, -130f, 180f })
		{
			Vector3 d = new Basis(Vector3.Up, Mathf.DegToRad(deg)) * fromDir;
			Vector3 eye = Floor(node.GlobalPosition + d * 1.8f) + Vector3.Up * 1.64f;
			if (Clear(eye, c, use?.PickRadius ?? 0.2f)) { fromDir = d; break; }
		}
		Log($"{name} at {node.GlobalPosition} tris {Tris(node.GetNodeOrNull("Item") ?? node)}");

		await Pose(node.GlobalPosition + fromDir * far, c);
		Shot(_props, $"{name}_{far:0}m");
		Check($"{name} not focused at {far:0} m", _interaction.Focused != use, _interaction.PromptText);

		Vector3 side = fromDir.Cross(Vector3.Up).Normalized();
		await Pose(node.GlobalPosition + fromDir * 1.8f, c + side * 0.75f);
		Shot(_props, $"{name}_2m_crosshair_off");
		Shot(_after, $"highlight_off_{name}");
		Check($"{name} not focused with the crosshair beside it", _interaction.Focused != use, _interaction.PromptText);

		await Pose(node.GlobalPosition + fromDir * 1.8f, c);
		Shot(_props, $"{name}_2m_crosshair_on");
		Shot(_after, $"highlight_on_{name}");
		Check($"{name} focused under the crosshair", use != null && _interaction.Focused == use, $"prompt \"{_interaction.PromptText}\"");
	}

	private static Vector3 V(float x, float y, float z) => new(x, y, z);

	private async void Run()
	{
		await Frames(12);
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_terrain == null || _player == null) { GD.PushError("items preview: no terrain/player"); GetTree().Quit(2); return; }
		_interaction = _player.GetNode<PlayerInteraction>("Interaction");
		_inv = _player.GetNode<PlayerInventory>("Inventory");
		_player.PlayerInput.Scripted = true;
		var root = GetTree().Root;

		_free = new Camera3D { Name = "FreeCam", Fov = 70f, Near = 0.05f, Far = 400f };
		AddChild(_free);
		_freeLamp = new SpotLight3D
		{
			LightColor = new Color(1f, 0.8f, 0.52f), SpotRange = 7f, SpotAngle = 50f, SpotAngleAttenuation = 0.6f,
			LightEnergy = 1.6f, ShadowEnabled = true, Visible = false,
		};
		_free.AddChild(_freeLamp);

		// A reference crosshair dot, unless the HUD already draws one.
		if (root.FindChild("Crosshair", true, false) == null)
		{
			var layer = new CanvasLayer { Layer = 50 };
			AddChild(layer);
			var dot = new ColorRect { Color = new Color(0.9f, 0.88f, 0.8f, 0.8f), Size = new Vector2(2, 2) };
			dot.Position = GetViewport().GetVisibleRect().Size * 0.5f - Vector2.One;
			layer.AddChild(dot);
		}

		if (root.FindChild("ScreenFader", true, false) is ProjectDS.UI.ScreenFader fader) fader.SetBlack(false);
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		var shed = root.FindChild("Shed", true, false) as Node3D;
		var friend = cabin?.GetNodeOrNull<Node3D>("Friend");
		if (friend?.GetNodeOrNull<Area3D>("Reveal") is Area3D reveal) reveal.SetDeferred(Area3D.PropertyName.Monitoring, false);
		cabin?.OpenDoor();

		var pickups = new Dictionary<string, Pickup>();
		foreach (var n in root.FindChildren("*", "Area3D", true, false))
			if (n is Pickup p) { pickups[p.Name] = p; p.Reveal(); }
		await Frames(50);
		foreach (var (n, p) in pickups) Log($"pickup {n} {p.Kind} at {p.GlobalPosition}");

		// ── prompts, including blocked states ──
		if (pickups.TryGetValue("KeyPickup", out var key) && pickups.TryGetValue("HammerPickup", out var hammer))
		{
			_inv.TryPickup(ToolKind.Axe);
			Check("carrying a tool doesn't block another", key.Use.CanInteract(_player) && key.Use.GetPrompt(_player) == "Take the key", key.Use.GetPrompt(_player));
			Check("locked prompt", hammer.Use.GetPrompt(_player) == "Locked: it needs the key", hammer.Use.GetPrompt(_player));
			Check("locked blocks the take", !hammer.Use.CanInteract(_player));
			_inv.TryPickup(ToolKind.Key);
			Check("axe and key carried together", _inv.HasTool(ToolKind.Axe) && _inv.HasTool(ToolKind.Key), _inv.Serialize());
			Check("unlock prompt", hammer.Use.CanInteract(_player), hammer.Use.GetPrompt(_player));
			_inv.Consume(ToolKind.Axe);
			_inv.Consume(ToolKind.Key);
		}

		await AfterShots(cabin, pickups);

		// ── daytime (Act 1-2): the camera near the trailhead, the birds ──
		if (pickups.TryGetValue("CameraPickup", out var cam))
		{
			Vector3 tp = _terrain.TrailPoint(4f, out _);
			await ShootItem("camera", cam, tp - cam.GlobalPosition, cam.Use);
			await Pose(tp, cam.GlobalPosition);
			Shot(_props, "camera_from_trail");
		}
		foreach (var n in root.FindChildren("*", "Node3D", true, false))
		{
			if (n is not Bird bird || bird.Name.ToString().StartsWith("Lineup")) continue;
			Log($"bird {bird.Color} perched={bird.Perched} tris {bird.TriangleCount} at {bird.GlobalPosition}");
		}

		// ── Act 3: the porch, before the lantern is in hand ──
		if (cabin != null)
		{
			Vector3 front = Flat(cabin.GlobalBasis.Z);
			if (pickups.TryGetValue("LanternPickup", out var lantern)) await ShootItem("lantern", lantern, front, lantern.Use);
			if (pickups.TryGetValue("CompassPickup", out var compass)) await ShootItem("compass", compass, front, compass.Use);
			await Pose(cabin.ApproachPoint, cabin.DoorCenter + Vector3.Down * 1.0f);
			Shot(_props, "porch_lantern_compass_from_approach");
		}
		// ── Acts 4-5: the player carries the lantern from here on ──
		_inv.TryPickup(ToolKind.Lantern);
		if (shed != null && pickups.TryGetValue("HammerPickup", out var ham))
			await ShootItem("hammer", ham, Flat(shed.GlobalBasis.Z), ham.Use, 4f);
		if (pickups.TryGetValue("AxePickup", out var axe))
		{
			var anchor = axe.GetNodeOrNull<TrailAnchor>("TrailAnchor");
			Vector3 tp = _terrain.TrailPoint(anchor?.Distance ?? 158f, out _);
			await ShootItem("axe", axe, tp - axe.GlobalPosition, axe.Use);
			await Pose(tp, axe.GlobalPosition + Vector3.Up * 0.3f);
			Shot(_props, "axe_from_trail");
		}
		if (pickups.TryGetValue("KeyPickup", out var k2))
		{
			var anchor = k2.GetNodeOrNull<TrailAnchor>("TrailAnchor");
			float s = anchor?.Distance ?? 402f;
			await ShootItem("key", k2, _terrain.TrailPoint(s - 3f, out _) - k2.GlobalPosition, k2.Use);
			await Pose(_terrain.TrailPoint(s - 8f, out _), k2.GlobalPosition);
			Shot(_props, "key_from_trail_8m");
		}

		// ── Act 5: inside the cabin ──
		if (friend != null && cabin != null)
		{
			var body = friend.GetNodeOrNull<FriendBody>("Body");
			Log($"friend tris {body?.TriangleCount}");
			Vector3 front = Flat(cabin.GlobalBasis.Z);
			Vector3 chest = friend.GlobalPosition + Vector3.Up * 0.95f;
			await Pose(friend.GlobalPosition + front * 15f, chest);
			Shot(_chars, "friend_15m_through_door");
			_free.GlobalPosition = _player.CameraRig.Camera.GlobalPosition;
			_free.Fov = 70f * 0.72f;   // the game's right-click focus zoom
			_free.LookAt(chest, Vector3.Up);
			_free.MakeCurrent();
			await Frames(10);
			Shot(_chars, "friend_15m_through_door_focus_zoom");
			_free.Fov = 70f;
			await Pose(friend.GlobalPosition + front * 3f, chest);
			Shot(_chars, "friend_3m");
			await Pose(friend.GlobalPosition + front * 1.6f + cabin.GlobalBasis.X * 0.35f, friend.GlobalPosition + Vector3.Up * 0.8f + front * 0.4f);
			Shot(_chars, "friend_1_5m");
			await Pose(friend.GlobalPosition + front * 0.9f + cabin.GlobalBasis.X * 1.4f, chest);
			Shot(_chars, "friend_side_1_5m");
			if (pickups.TryGetValue("NewelPostPickup", out var newel))
				await ShootItem("newel_post", newel, front, newel.Use, 3.6f);
		}

		// ── Act 10: the walkie at the maze exit ──
		if (_walkie != null && IsInstanceValid(_walkie))
		{
			var use = _walkie.GetNodeOrNull<Interactable>("Interactable");
			await ShootItem("walkie", _walkie, V(1816f, 0, -3036f) - _walkie.GlobalPosition, use, 4f);
		}

		Log(_fails == 0 ? "ALL CHECKS PASSED" : $"{_fails} CHECK(S) FAILED");
		if (QuitWhenDone) GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	private WalkiePickup _walkie;

	/// <summary>The review's viewpoints (visual_review.log), re-shot after the rework into test-output/after/.</summary>
	private async Task AfterShots(Cabin cabin, Dictionary<string, Pickup> pickups)
	{
		// Items: same eye offset from the item as in the review, measured from where it sits now.
		var old = new Dictionary<string, Vector3>
		{
			["LanternPickup"] = V(5.4580393f, 0.48193133f, -26.731352f),
			["CompassPickup"] = V(5.1779027f, 0.48193133f, -27.69131f),
			["NewelPostPickup"] = V(1.772096f, 1.1319313f, -26.332827f),
			["HammerPickup"] = V(20.293348f, 8.507389f, -31.581463f),
			["KeyPickup"] = V(-4.9534025f, 5.9123855f, -363.93326f),
			["AxePickup"] = V(-7.9858027f, 4.861085f, -128.2607f),
			["CameraPickup"] = V(-0.92183065f, -0.07403053f, 9.974629f),
		};
		var shots = new (string node, string name, Vector3 eye, Vector3 look)[]
		{
			("LanternPickup", "item_lantern_close_1_2m", V(6.4180393f, 3.6364708f, -26.011353f), V(5.4580393f, 0.60193133f, -26.731352f)),
			("LanternPickup", "item_lantern_5m", V(9.458039f, 4.6099105f, -23.731352f), V(5.4580393f, 0.6819313f, -26.731352f)),
			("CompassPickup", "item_compass_close_1_2m", V(6.1379027f, 3.5913734f, -26.971312f), V(5.1779027f, 0.60193133f, -27.69131f)),
			("CompassPickup", "item_compass_5m", V(9.177902f, 4.618787f, -24.69131f), V(5.1779027f, 0.6819313f, -27.69131f)),
			("NewelPostPickup", "item_newel_post_close", V(2.372096f, 2.0819314f, -25.732826f), V(1.772096f, 1.2319313f, -26.332827f)),
			("HammerPickup", "item_hammer_close_1_2m", V(21.25335f, 10.766535f, -30.861464f), V(20.293348f, 8.627389f, -31.581463f)),
			("HammerPickup", "item_hammer_5m", V(24.293348f, 11.4776f, -28.581463f), V(20.293348f, 8.707389f, -31.581463f)),
			("KeyPickup", "item_key_close_1_2m", V(-3.9934025f, 7.5606723f, -363.21326f), V(-4.9534025f, 6.0323853f, -363.93326f)),
			("KeyPickup", "item_key_5m", V(-0.9534025f, 7.632675f, -360.93326f), V(-4.9534025f, 6.1123853f, -363.93326f)),
			("AxePickup", "item_axe_close_1_2m", V(-7.0258026f, 6.217737f, -127.540695f), V(-7.9858027f, 4.981085f, -128.2607f)),
			("AxePickup", "item_axe_5m", V(-3.9858027f, 4.827332f, -125.2607f), V(-7.9858027f, 5.0610847f, -128.2607f)),
			("CameraPickup", "item_camera_close_1_2m", V(0.038169384f, 1.4959495f, 10.69463f), V(-0.92183065f, 0.045969464f, 9.974629f)),
			("CameraPickup", "item_camera_5m", V(3.0781693f, 1.5820577f, 12.974629f), V(-0.92183065f, 0.12596947f, 9.974629f)),
		};
		foreach (var (node, name, eye, look) in shots)
		{
			if (!pickups.TryGetValue(node, out var p)) continue;
			Vector3 now = p.GlobalPosition, was = old[node];
			Vector3 e, l;
			if (node == "NewelPostPickup") { e = now + (eye - was); l = now + (look - was); }
			else
			{
				// Keep the review's horizontal offset and aim; stand at eye height on whatever is really there.
				e = new Vector3(now.X + eye.X - was.X, 0, now.Z + eye.Z - was.Z);
				e = Floor(e, 3f) + Vector3.Up * 1.62f;
				l = now + (look - was) - new Vector3(0, look.Y - was.Y, 0) + Vector3.Up * Mathf.Min(look.Y - was.Y, 0.2f);
			}
			await Free(name, e, l);
		}

		// Cabin shots: the cabin was re-grounded; x/z are unchanged (same correction the buildings preview uses).
		if (cabin != null)
		{
			float dy = cabin.GlobalPosition.Y - 0.48193133f;
			Vector3 C(float x, float y, float z) => new(x, y + dy, z);
			Vector3 spots = pickups.TryGetValue("LanternPickup", out var la) && pickups.TryGetValue("CompassPickup", out var co)
				? (la.GlobalPosition + co.GlobalPosition) * 0.5f : C(5.317971f, 0.58193135f, -27.21133f);
			await Free("cabin_05_porch_pickups", V(6.893876f, 4.00518f, -27.879555f), spots);
			await Free("cabin_08_interior_friend_3m", C(3.8780308f, 2.6843257f, -26.791126f), C(1.1061016f, 1.4819313f, -26.294733f));
			await Free("cabin_09_friend_close_1_5m", C(2.1299405f, 2.1019313f, -27.426878f), C(1.0649018f, 1.6819314f, -26.293127f));
		}

		// Birds: they sit exactly where they did (same anchors), now on a stump.
		await Free("bird_Red_2_5m", V(1.954534f, 1.9667667f, -18.557856f), V(0.20706797f, 1.1221722f, -20.345694f));
		await Free("bird_Red_8m", V(5.7989593f, 2.581276f, -14.624615f), V(0.20706797f, 1.1221722f, -20.345694f));
		await Free("bird_Blue_2_5m", V(-20.780378f, 3.112125f, -70.99723f), V(-21.268736f, 2.3493237f, -73.449066f));
		await Free("bird_Blue_8m", V(-19.705992f, 2.6996584f, -65.60319f), V(-21.268736f, 2.3493237f, -73.449066f));
		await Free("bird_Purple_2_5m", V(13.868108f, 3.7901402f, -117.607414f), V(12.741511f, 2.7727675f, -119.83918f));
		await Free("bird_Purple_8m", V(16.346619f, 3.6666694f, -112.697525f), V(12.741511f, 2.7727675f, -119.83918f));
		await Free("bird_BlackOmen_2_5m", V(-5.6078277f, 4.6861334f, -184.45575f), V(-7.7032375f, 4.0658684f, -185.81929f));
		await Free("bird_BlackOmen_8m", V(-0.99792624f, 3.8125563f, -181.45595f), V(-7.7032375f, 4.0658684f, -185.81929f));

		await Lineup();
		await FriendInTheOpen();

		// Maze exit: the walkie where the review found it, lit by a lantern beam as in the review.
		if (BunkerInterior.Instance is BunkerInterior bunker)
		{
			_walkie = new WalkiePickup { Name = "WalkiePreview" };
			bunker.AddChild(_walkie);
			_walkie.GlobalPosition = V(1821.2f, -79.4f, -3037.5f);
			await Frames(30);
			Log($"walkie at {_walkie.GlobalPosition}");
			await Free("maze_06_exit_walkie", V(1816f, -78.38f, -3036f), V(1821.2f, -79.7f, -3037.5f), true);
			await Free("maze_07_walkie_close", V(1820.2999f, -78.399994f, -3036.7f), V(1821.2f, -79.6f, -3037.5f), true);
			await Free("maze_08_walkie_side_level", V(1820f, -79.65f, -3037.5f), V(1821.2f, -79.7f, -3037.5f), true);
		}
	}

	/// <summary>Every item and bird in a row on the leaf litter by the trailhead, as in the review's lineup.</summary>
	private async Task Lineup()
	{
		var kinds = new[] { ToolKind.Lantern, ToolKind.Compass, ToolKind.Axe, ToolKind.Key, ToolKind.Hammer, ToolKind.Camera, ToolKind.NewelPost, ToolKind.Radio };
		var made = new List<Node3D>();
		for (int i = 0; i < kinds.Length; i++)
		{
			float x = -1.47f + i * 0.42f;
			Node3D n = kinds[i] == ToolKind.Radio
				? new WalkiePickup { Name = "LineupWalkie" }
				: new Pickup { Name = $"Lineup{kinds[i]}", Kind = kinds[i], UseSpot = false };
			AddChild(n);
			n.GlobalPosition = new Vector3(x, _terrain.HeightAt(x, 14.7f) + 0.2f, 14.7f);
			made.Add(n);
		}
		var colors = new[] { BirdColor.Red, BirdColor.Blue, BirdColor.Purple, BirdColor.BlackOmen };
		for (int i = 0; i < colors.Length; i++)
		{
			float x = -0.48f + i * 0.32f;
			var b = new Bird { Name = $"Lineup{colors[i]}", Color = colors[i] };
			b.Position = new Vector3(x, _terrain.HeightAt(x, 14.1f), 14.1f);
			b.Rotation = new Vector3(0, 0.35f - i * 0.2f, 0);
			AddChild(b);
			made.Add(b);
		}
		await Frames(50);
		foreach (var n in made) if (n is Bird b) b.SetProcess(false);   // hold a clean pose for the lineup
		foreach (var n in made) Log($"lineup {n.Name} tris {Tris(n)}");

		await Free("lineup_01_items_ingame_light", V(0f, 1.3897134f, 16.6f), V(0f, -0.23028663f, 14.7f));
		await Free("lineup_02_items_low_angle", V(0f, 0.31971338f, 16.1f), V(0f, -0.13028663f, 14.7f));
		// Studio light, as in the review: a key light and a fill over the lineup.
		var key = new DirectionalLight3D { LightEnergy = 1.6f, ShadowEnabled = true, Rotation = new Vector3(Mathf.DegToRad(-50f), Mathf.DegToRad(30f), 0) };
		var fill = new OmniLight3D { LightEnergy = 1.4f, OmniRange = 6f, Position = new Vector3(0, 1.6f, 16.2f) };
		AddChild(key); AddChild(fill);
		await Free("lineup_03_items_studio_light", V(0f, 0.31971338f, 16.1f), V(0f, -0.13028663f, 14.7f));
		await Free("lineup_04_birds_studio_close", V(0f, 0.119713366f, 14.75f), V(0f, -0.13028663f, 14.1f));
		key.QueueFree(); fill.QueueFree();
		foreach (var n in made) n.QueueFree();
		await Frames(3);
	}

	/// <summary>The friend placed on the open ground by the trailhead, as in the review's friend_open_* shots.</summary>
	private async Task FriendInTheOpen()
	{
		var scene = GD.Load<PackedScene>("res://scenes/entities/friend.tscn");
		var f = scene.Instantiate<Node3D>();
		f.Name = "FriendOpen";
		f.Position = new Vector3(0, _terrain.HeightAt(0, 13f), 13f);
		AddChild(f);
		f.GetNode<Area3D>("Reveal").SetDeferred(Area3D.PropertyName.Monitoring, false);
		await Frames(40);
		if (f.FindChild("NewelPostPickup", true, false) is Pickup np) np.Reveal();
		await Frames(5);
		await Free("friend_open_03m", V(0f, 1.373631f, 16f), V(0f, 0.7131902f, 13f));
		await Free("friend_open_15m", V(0f, 1.198f, 28f), V(0f, 0.7131902f, 13f));
		await Free("friend_open_side_2m", V(2f, 1.5004996f, 13.3f), V(0f, 0.7131902f, 13f));
		f.QueueFree();
		await Frames(3);
	}
}

using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// The lodge has a personality (2026-10-10b, the owner: "we need the lodge to have its own kind of personality where weird
/// events can occur inside of it just like the novels: the shining and 1408"). In Act 23, while the lodge is still warm,
/// each of these happens once, where it's set, never two within half a minute of each other, and none of them ever hurts:
/// <list type="bullet">
/// <item><b>The ball</b>: up in the corridor, a ball comes rolling down it out of the dark toward them, slows, and stops at
/// their feet. Nobody threw it.</item>
/// <item><b>The portrait</b>: one of the corridor's portraits, passed and looked away from, is someone else when they look
/// back.</item>
/// <item><b>The party</b>: from the shut dining hall, voices: a party, murmuring, a glass set down, a laugh. Come near the
/// doors and it's silent.</item>
/// <item><b>The keys</b>: at the front desk, every room key on its hook swings at once, and rings, and settles.</item>
/// <item><b>The lamps</b>: on the stairs, every lamp in the hall dims slowly to half, holds, and comes back (no flicker:
/// as if something drew the power for a moment).</item>
/// <item><b>The knock</b>: in 204, three knocks from inside its wardrobe. Open it, and there's nothing.</item>
/// </list>
/// </summary>
public partial class SkiLodge
{
	public readonly HashSet<string> HauntsDone = new();
	private double _lastHaunt = -100;
	private readonly List<Node3D> _keys = new();
	private bool _hauntsBuilt;

	private void LodgeHaunts()
	{
		_hauntsBuilt = true;
		BuildLookBacks();
		// the room keys, each on its own hook so it can swing (they were part of the desk's mesh)
		var tagMat = LodgeTextures.BrassMat;
		foreach (float x in new[] { 5.35f, 5.0f, 4.65f, 4.3f })
		{
			var key = new Node3D { Name = "Key", Position = new Vector3(x, 2.84f, HexIn - 0.27f) };
			AddChild(key);
			var k = new MeshKit();
			k.Mat(tagMat);
			k.Color = Colors.White;
			k.Cylinder(Vector3.Zero, new Vector3(0, -0.09f, 0), 0.006f, 0.006f, 4, true);
			k.Box(new Vector3(0, -0.13f, 0), new Vector3(0.035f, 0.07f, 0.008f), 1f);
			k.CommitTo(key, "Key", false);
			_keys.Add(key);
		}
	}

	/// <summary>Each frame of Act 23: is it time for one of them?</summary>
	private void HauntTick(PlayerController player, Vector3 l)
	{
		if (!_hauntsBuilt || player == null || Frozen) return;
		if (StoryManager.Instance is not { Current: >= Checkpoint.Act22Finished and < Checkpoint.Act23Finished }) return;
		double now = Time.GetTicksMsec() / 1000.0;
		if (now - _lastHaunt < 35.0 || !player.PlayerInput.Enabled) return;
		bool upstairs = l.Y > UpperY - 0.5f;
		bool corridor = upstairs && Mathf.Abs(l.Z) < CorrHalf + 0.3f && l.X > CorrX0 && l.X < CorrX1;
		bool lobby = !upstairs && l.Y < UpperY - 1f && new Vector2(l.X, l.Z).Length() < HexIn;
		bool stairs = l.Y > FloorY + 1f && l.Y < UpperY - 0.6f && new Vector2(l.X, l.Z).Length() < HexIn;
		if (corridor && l.X > CorrX0 + 6f && Once("ball")) _ = Ball(player, l);
		else if (corridor && Once("portrait", false) && PortraitSwap(player)) Did("portrait");
		else if (lobby && !Has(LodgeFlag.DiningOpen) && l.X > 2f && l.X < 9f && Once("party")) _ = Party(player);
		else if (lobby && new Vector2(l.X - 5.1f, l.Z - 7.3f).Length() < 2.6f && Once("keys")) Keys();
		else if (stairs && Once("lamps")) Lamps();
		else if (upstairs && InRoom(204, l) && Once("knock")) Knock();
		// the music box again, in the room next to theirs (its tune winding down, the last note never coming)
		else if (upstairs && !corridor && Once("musicbox"))
			AudioDirector.OneShot(this, "musicbox_lullaby", 1, ToGlobal(new Vector3(l.X < RoomSplitX ? RoomSplitX + 3f : RoomSplitX - 3f, UpperY + 1f, l.Z)), 0f, "Events", 5f, 0f);
	}

	private bool Once(string what, bool mark = true)
	{
		if (HauntsDone.Contains(what)) return false;
		if (mark) Did(what);
		return true;
	}

	private void Did(string what)
	{
		HauntsDone.Add(what);
		_lastHaunt = Time.GetTicksMsec() / 1000.0;
		GD.Print($"[story] Act 23: the lodge - {what}");
	}

	private async System.Threading.Tasks.Task Ball(PlayerController player, Vector3 l)
	{
		var ball = new MeshInstance3D
		{
			Name = "Ball", Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 14, Rings = 8 },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.1f, 0.08f), Roughness = 0.4f },
		};
		AddChild(ball);
		var from = new Vector3(CorrX0 + 0.6f, UpperY + 0.09f, 0f);
		var to = new Vector3(l.X - 0.6f, UpperY + 0.09f, Mathf.Clamp(l.Z, -0.5f, 0.5f));
		ball.Position = from;
		float dist = from.DistanceTo(to);
		double t = 0, dur = dist / 2.2;
		double bump = 0;
		while (t < dur && IsInstanceValid(ball))
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / dur);
			float eased = 1f - (1f - u) * (1f - u);   // (slowing as it comes)
			var p = from.Lerp(to, eased);
			ball.Rotation += new Vector3(0, 0, -(p - ball.Position).Length() / 0.09f);
			ball.Position = p;
			bump -= dt;
			if (bump <= 0) { bump = 0.5 + u; AudioDirector.OneShot(this, "wood_bump", 3, ToGlobal(p), -20f + 6f * u, "Events", 2f, 0.1f); }
		}
		PlayerBreathing.Startle(0.6f);
		var gone = GetTree().CreateTimer(60);
		gone.Timeout += () => { if (IsInstanceValid(ball)) ball.QueueFree(); };
	}

	/// <summary>A corridor portrait behind them and out of sight: someone else in it now.</summary>
	private bool PortraitSwap(PlayerController player)
	{
		var cam = player.CameraRig?.Camera;
		if (cam == null) return false;
		foreach (var (node, canvas) in Paintings)
		{
			if (!IsInstanceValid(node)) continue;
			var lp = ToLocal(node.GlobalPosition);
			if (lp.Y < UpperY || Mathf.Abs(lp.Z) > CorrHalf + 0.4f) continue;
			float d = node.GlobalPosition.DistanceTo(player.GlobalPosition);
			if (d < 4f || d > 12f || cam.IsPositionInFrustum(node.GlobalPosition)) continue;
			var faces = System.Array.FindAll(Arts, a => a.Kind == "face");
			var cur = (canvas.MaterialOverride as StandardMaterial3D)?.AlbedoTexture?.ResourcePath ?? "";
			foreach (var a in faces)
			{
				string path = $"res://assets/textures/paintings/av_{a.Id}.jpg";
				if (path == cur || !ResourceLoader.Exists(path)) continue;
				var m = (StandardMaterial3D)canvas.MaterialOverride.Duplicate();
				m.AlbedoTexture = GD.Load<Texture2D>(path);
				canvas.MaterialOverride = m;
				return true;
			}
		}
		return false;
	}

	private async System.Threading.Tasks.Task Party(PlayerController player)
	{
		var at = ToGlobal(new Vector3(WingX0 + 6f, FloorY + 1.5f, 0f));
		var doors = ToGlobal(new Vector3(WingX0, FloorY + 1.2f, 0f));
		for (int i = 0; i < 8; i++)
		{
			if (player.GlobalPosition.DistanceTo(doors) < 5.5f) { GD.Print("[story] Act 23: the party goes silent as they come to the doors"); return; }
			var p = at + new Vector3(GD.Randf() * 6f, 0, (GD.Randf() - 0.5f) * 6f);
			AudioDirector.OneShot(this, "whisper_voice", 3, p, -16f, "Voice", 4f, 0.15f);
			if (i % 3 == 1) AudioDirector.OneShot(this, "bottle_clink", 3, p, -18f, "Events", 3f, 0.1f);
			await ToSignal(GetTree().CreateTimer(GD.RandRange(0.7, 1.3)), SceneTreeTimer.SignalName.Timeout);
		}
	}

	private void Keys()
	{
		foreach (var key in _keys)
		{
			var tw = key.CreateTween();
			float a = (float)GD.RandRange(0.3, 0.5);
			for (int i = 0; i < 6; i++) { tw.TweenProperty(key, "rotation:z", a * (i % 2 == 0 ? 1f : -1f), 0.18f + i * 0.04f).SetTrans(Tween.TransitionType.Sine); a *= 0.7f; }
			tw.TweenProperty(key, "rotation:z", 0f, 0.4f).SetTrans(Tween.TransitionType.Sine);
		}
		AudioDirector.OneShot(this, "ice_tinkle", 3, ToGlobal(new Vector3(4.8f, 2.8f, HexIn - 0.3f)), -8f, "Events", 2f, 0.05f);
		AudioDirector.OneShot(this, "ice_tinkle", 3, ToGlobal(new Vector3(4.6f, 2.8f, HexIn - 0.3f)), -12f, "Events", 2f, 0.1f);
		PlayerBreathing.Startle(0.4f);
	}

	private void Lamps()
	{
		foreach (var c in GetChildren())
		{
			if (c is not OmniLight3D l || l == _fireLight || !(l.Name.ToString().StartsWith("Light") || l.Name.ToString().StartsWith("Sconce"))) continue;
			if (ToLocal(l.GlobalPosition).Length() > HexR + 2f) continue;
			float e = l.LightEnergy;
			var tw = l.CreateTween();
			tw.TweenProperty(l, "light_energy", e * 0.4f, 2.0f).SetTrans(Tween.TransitionType.Sine);
			tw.TweenInterval(1.4f);
			tw.TweenProperty(l, "light_energy", e, 2.5f).SetTrans(Tween.TransitionType.Sine);
		}
	}

	private void Knock()
	{
		var w = Hides.Find(h => h.Room == 204);
		var at = w != null ? w.GlobalPosition : ToGlobal(new Vector3(CorrX0 + 1f, UpperY + 1.2f, -InnerZ + 1.4f));
		var tw = CreateTween();
		for (int i = 0; i < 3; i++)
		{
			tw.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "wall_knock", 3, at, -4f, "Events", 3f, 0.05f)));
			tw.TweenInterval(0.6f);
		}
		tw.TweenCallback(Callable.From(() => PlayerBreathing.Startle(0.7f)));
	}
}

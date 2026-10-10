using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.HallwayParts;

namespace ProjectDS.World;

/// <summary>
/// The safe lights (2026-10-10): at a few of the saves down in the dark (the station's second room, the sewer's way in, the
/// lodge's upstairs corridor, halfway through the crawlspace) a caged work light hangs on its cord, warm, humming: a place to
/// breathe. On a rare run (about one in three, and then at only one of them, once), standing under it a few seconds there's a
/// creak behind you, and in the dark past the light's edge something is standing there, watching. Turn to look and it's gone.
/// Nothing else: it doesn't follow, it doesn't come again.
/// </summary>
public static class SaveLamps
{
	private static readonly string[] At = { "respawn_Act13Room1Solved", "respawn_Act16Finished", "respawn_Act23Room203Done", "respawn_Act23Crawlspace" };
	/// <summary>This run's one lamp with something behind it (-1: none this run).</summary>
	private static int _haunted = -2;
	public static readonly List<SaveLamp> Lamps = new();

	public static void Place(Node3D host)
	{
		if (_haunted == -2) _haunted = GD.Randf() < 0.35f ? (int)(GD.Randi() % At.Length) : -1;
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--fake-safety") >= 0) _haunted = 3;
		Lamps.RemoveAll(l => !GodotObject.IsInstanceValid(l));
		for (int i = 0; i < At.Length; i++)
		{
			if (host.GetTree().GetFirstNodeInGroup(At[i]) is not Node3D m) continue;
			var lamp = new SaveLamp { Name = "SaveLamp_" + At[i]["respawn_".Length..], Haunted = i == _haunted };
			host.AddChild(lamp);
			lamp.GlobalPosition = m.GlobalPosition;
			Lamps.Add(lamp);
		}
	}
}

public partial class SaveLamp : Node3D
{
	public bool Haunted;
	public bool Hung { get; private set; }
	public bool Watched { get; private set; }
	private OmniLight3D _light;
	private Node3D _bulb;
	private float _under, _t;
	private ShadowMan _figure;
	private float _figureT;

	private float _retry;

	/// <summary>(Hung when they come near: the rooms round it may be built later than the save is placed.)</summary>
	private void Hang()
	{
		// up to the ceiling over the save (no ceiling within four metres: no lamp)
		var from = GlobalPosition + Vector3.Up * 1.0f;
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Up * 4f, 1));
		if (hit.Count == 0) return;
		var ceil = (Vector3)hit["position"];
		float drop = Mathf.Clamp(ceil.Y - GlobalPosition.Y - 2.15f, 0.25f, 1.2f);
		_bulb = new Node3D { Name = "Bulb" };
		AddChild(_bulb);
		_bulb.GlobalPosition = ceil - Vector3.Up * drop;
		var k = new MeshKit();
		k.Mat(new StandardMaterial3D { ResourceName = "lamp_cord", AlbedoColor = new Color(0.06f, 0.06f, 0.06f), Roughness = 0.8f });
		k.Color = Colors.White;
		k.Cylinder(Vector3.Zero, Vector3.Up * drop, 0.006f, 0.006f, 5, false);
		// the cage: a few wires round the bulb, a cap
		k.Cylinder(new Vector3(0, 0.02f, 0), new Vector3(0, 0.07f, 0), 0.03f, 0.028f, 8, true);
		for (int i = 0; i < 6; i++)
		{
			float a = i / 6f * Mathf.Tau;
			var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			k.Cylinder(new Vector3(0, 0.02f, 0) + d * 0.03f, new Vector3(0, -0.07f, 0) + d * 0.055f, 0.003f, 0.003f, 4, false);
			k.Cylinder(new Vector3(0, -0.07f, 0) + d * 0.055f, new Vector3(0, -0.15f, 0) + d * 0.01f, 0.003f, 0.003f, 4, false);
		}
		k.CommitTo(_bulb, "Cage", false);
		_bulb.AddChild(new MeshInstance3D
		{
			Name = "Glass", Mesh = new SphereMesh { Radius = 0.045f, Height = 0.1f, RadialSegments = 12, Rings = 6 }, Position = new Vector3(0, -0.05f, 0),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.6f), EmissionEnabled = true, Emission = new Color(1f, 0.72f, 0.42f), EmissionEnergyMultiplier = 1.4f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		_light = new OmniLight3D { Name = "Light", LightColor = new Color(1f, 0.82f, 0.58f), LightEnergy = 1.05f, OmniRange = 5.5f, OmniAttenuation = 1.4f, ShadowEnabled = true, Position = new Vector3(0, -0.12f, 0) };
		_bulb.AddChild(_light);
		if (ResourceLoader.Exists("res://assets/audio/ambient/save_hum_loop.wav"))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/save_hum_loop.wav").Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			_bulb.AddChild(new AudioStreamPlayer3D { Name = "Hum", Stream = wav, Bus = "Events", VolumeDb = -20f, UnitSize = 1.5f, MaxDistance = 14f, Autoplay = true });
		}
		Hung = true;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (!Hung)
		{
			_retry -= dt;
			if (_retry > 0f) return;
			_retry = 1f;
			if (StoryBeat.Player(this) is { } pl && pl.GlobalPosition.DistanceTo(GlobalPosition) < 40f) Hang();
			return;
		}
		_t += dt;
		// a slow sway on its cord
		_bulb.Rotation = new Vector3(Mathf.Sin(_t * 0.9f) * 0.03f, 0, Mathf.Sin(_t * 0.7f + 1f) * 0.025f);
		if (_figure != null) { WatchFigure(dt); return; }
		if (!Haunted || Watched) return;
		var player = StoryBeat.Player(this);
		if (player == null || !player.PlayerInput.Enabled) { _under = 0f; return; }
		var flat = (player.GlobalPosition - GlobalPosition) with { Y = 0 };
		_under = flat.Length() < 2.5f ? _under + dt : 0f;
		if (_under > 2.5f) Behind(player);
	}

	/// <summary>A creak behind them, and something standing in the dark past the light, watching.</summary>
	private void Behind(PlayerController player)
	{
		var cam = player.CameraRig?.Camera;
		if (cam == null) return;
		var back = cam.GlobalBasis.Z with { Y = 0 };
		if (back.LengthSquared() < 0.01f) return;
		back = back.Normalized();
		var chest = player.GlobalPosition + Vector3.Up * 1.2f;
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(chest, chest + back * 5f, 1, new Godot.Collections.Array<Rid> { player.GetRid() }));
		float d = hit.Count > 0 ? chest.DistanceTo((Vector3)hit["position"]) - 0.6f : 4.6f;
		if (d < 2.4f) { Watched = true; return; }   // (a wall right behind: nowhere for it to stand)
		Watched = true;
		_figure = new ShadowMan { Name = "BehindYou", Scale = Vector3.One * 0.72f, Glare = 0f };
		(GetTree().CurrentScene ?? GetTree().Root).AddChild(_figure);
		var feet = player.GlobalPosition + back * d;
		var down = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(feet + Vector3.Up, feet + Vector3.Down * 3f, 1));
		if (down.Count > 0) feet = (Vector3)down["position"];
		_figure.StandAt(feet, player.GlobalPosition);
		AudioDirector.OneShot(this, "stair_groan", 3, feet + Vector3.Up * 0.2f, -6f, "Events", 3f, 0.05f);
		GD.Print($"[story] the safe light: something is standing behind them ({d:0.0} m)");
	}

	private void WatchFigure(float dt)
	{
		_figureT += dt;
		var cam = GetViewport().GetCamera3D();
		bool seen = false;
		if (cam != null && IsInstanceValid(_figure))
		{
			var to = (_figure.GlobalPosition + Vector3.Up * 1.3f - cam.GlobalPosition).Normalized();
			seen = (-cam.GlobalBasis.Z).Dot(to) > 0.77f;
		}
		// turned to look: gone (it never lets itself be looked at); or after a while, unseen, gone anyway
		if (seen || _figureT > 8f)
		{
			if (seen) { PlayerBreathing.Startle(0.8f); GD.Print("[story] the safe light: they turned - nothing there"); }
			if (IsInstanceValid(_figure)) _figure.QueueFree();
			_figure = null;
		}
	}
}

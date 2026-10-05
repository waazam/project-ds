using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Player;

/// <summary>
/// The flamethrower (Act 24; the owner: "the only weapon in the entire game ... infinite fuel amount, but it will have an
/// overheating gauge where if you hold the flame open for too long it will need a 7 second cool down. It will encourage
/// the player to use bursts"; "we need to see the flamethrower in the player's pov"). A WW2 portable flamethrower's wand
/// in the hands, low on the right of the view: its pistol grips, the long barrel, the igniter's drum and the nozzle; the
/// hose running off it over the shoulder to the tanks on the back.
/// <list type="bullet">
/// <item>Left mouse, held: the fuel valve opens and the igniter lights it: a stream of burning napalm
/// (<see cref="NapalmStream"/>) arcing out to where the eye is on, roaring;</item>
/// <item>the barrel heats as it fires (a full burn of about five and a half seconds) and cools when it doesn't; heat it all
/// the way and it locks: seven seconds venting before it will fire again (a gauge at the foot of the view);</item>
/// <item>infinite fuel.</item>
/// </list>
/// The wand kicks a little and sways with the walk; it lifts out of the way while the camera is up.
/// </summary>
public partial class Flamethrower : Node3D
{
	[Export] public float HeatSeconds = 5.5f, CoolSeconds = 3.2f, LockSeconds = 7f;

	public static Flamethrower Instance { get; private set; }
	public NapalmStream Stream { get; private set; }
	/// <summary>0..1, and whether it's locked out venting.</summary>
	public float Heat { get; private set; }
	public bool Overheated { get; private set; }
	public bool Firing { get; private set; }
	public bool Active => _active;
	/// <summary>For tests: fire it as if the button were held.</summary>
	public bool ScriptedFire;

	private PlayerController _player;
	private Node3D _wand, _nozzle;
	private bool _active;
	private float _lock, _kick, _sway, _pilotT;
	private AudioStreamPlayer _roar;
	private OmniLight3D _pilot;
	private GpuParticles3D _vent;
	private UI.FlamethrowerGauge _gauge;

	public override void _Ready()
	{
		Instance = this;
		_player = GetParent<PlayerController>();
		Stream = new NapalmStream { Name = "Napalm" };
		AddChild(Stream);
		_gauge = new UI.FlamethrowerGauge { Name = "FlameGauge", Thrower = this };
		AddChild(_gauge);
		var roar = ResourceLoader.Exists("res://assets/audio/ambient/flame_roar_loop.wav") ? GD.Load<AudioStreamWav>("res://assets/audio/ambient/flame_roar_loop.wav") : null;
		if (roar != null)
		{
			roar = (AudioStreamWav)roar.Duplicate();
			roar.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			roar.LoopBegin = 0;
			roar.LoopEnd = Mathf.RoundToInt(roar.GetLength() * roar.MixRate);
		}
		_roar = new AudioStreamPlayer { Name = "Roar", Stream = roar, Bus = "Player", VolumeDb = -80f };
		AddChild(_roar);
	}

	public override void _ExitTree() { if (Instance == this) Instance = null; }

	/// <summary>The wand in the hands, on the camera (built the first time it's held).</summary>
	private void BuildWand()
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return;
		_wand = new Node3D { Name = "FlamethrowerWand", Position = WandRest };
		cam.AddChild(_wand);
		var model = FurnitureKit.Clean(() => FurnitureKit.Place(_wand, "flamethrower_wand", new Transform3D(Basis.Identity, Vector3.Zero), WandRoles, "Wand"));
		if (model == null) BuildFallback(_wand);
		if (model != null) model.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		// (the wand's model runs along -Z, its nozzle's mouth 0.82 m out)
		_nozzle = new Node3D { Name = "Nozzle", Position = new Vector3(0f, 0.035f, -0.86f) };
		_wand.AddChild(_nozzle);
		// the igniter's little flame, always lit while it's held, its light on the hands
		_pilot = new OmniLight3D { Name = "Pilot", Position = _nozzle.Position + new Vector3(0, 0.02f, 0.02f), LightColor = new Color(1f, 0.6f, 0.25f), LightEnergy = 0.25f, OmniRange = 1.4f, ShadowEnabled = false };
		_wand.AddChild(_pilot);
		var pilotFlame = new FireVfx { Name = "PilotFlame", Extent = new Vector3(0.025f, 0.05f, 0.025f), FlameScale = 0.4f, Smoke = false, Embers = false, EmitLight = false, Haze = false, Position = _nozzle.Position + new Vector3(0, 0.015f, 0.01f) };
		_wand.AddChild(pilotFlame);
		_vent = new GpuParticles3D
		{
			Name = "Vent", Amount = 24, Lifetime = 1.4f, Emitting = false, Position = _nozzle.Position, LocalCoords = false,
			ProcessMaterial = new ParticleProcessMaterial { Direction = Vector3.Up, Spread = 25f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.5f, Gravity = new Vector3(0, 0.3f, 0), ScaleMin = 1f, ScaleMax = 2.5f, Color = new Color(0.8f, 0.82f, 0.85f, 0.25f) },
			DrawPass1 = new QuadMesh { Size = new Vector2(0.05f, 0.05f), Material = new StandardMaterial3D { AlbedoTexture = World.LakeParts.LakeFx.SoftDot(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_wand.AddChild(_vent);
	}

	private static readonly Vector3 WandRest = new(0.2f, -0.24f, -0.3f);

	private static System.Collections.Generic.Dictionary<string, Material> _roles;
	private static System.Collections.Generic.Dictionary<string, Material> WandRoles => _roles ??= new()
	{
		// (painted steel, worn: matte olive drab; glossier and more metal, under its own warm pilot light it read as gold)
		["olive"] = new StandardMaterial3D { ResourceName = "ft_olive", AlbedoColor = new Color(0.17f, 0.19f, 0.11f), Roughness = 0.82f, Metallic = 0.05f, AlbedoTexture = ProcTextures.Grime() },
		["steel"] = new StandardMaterial3D { ResourceName = "ft_steel", AlbedoColor = new Color(0.16f, 0.16f, 0.16f), Roughness = 0.6f, Metallic = 0.6f },
		["brass"] = new StandardMaterial3D { ResourceName = "ft_brass", AlbedoColor = new Color(0.36f, 0.28f, 0.14f), Roughness = 0.6f, Metallic = 0.6f, AlbedoTexture = ProcTextures.Grime() },
		["rubber"] = new StandardMaterial3D { ResourceName = "ft_rubber", AlbedoColor = new Color(0.035f, 0.035f, 0.035f), Roughness = 0.8f },
		["grip"] = new StandardMaterial3D { ResourceName = "ft_grip", AlbedoColor = new Color(0.08f, 0.07f, 0.06f), Roughness = 0.7f },
		["soot"] = new StandardMaterial3D { ResourceName = "ft_soot", AlbedoColor = new Color(0.05f, 0.045f, 0.04f), Roughness = 0.9f },
	};

	/// <summary>(no model built: a plain wand of boxes and tubes, so it still works)</summary>
	private static void BuildFallback(Node3D at)
	{
		var k = new MeshKit();
		k.Mat(WandRoles["olive"]);
		k.Color = Colors.White;
		k.Cylinder(new Vector3(0, 0.03f, 0.12f), new Vector3(0, 0.03f, -0.66f), 0.018f, 0.018f, 8, true);
		k.Cylinder(new Vector3(0, 0.035f, -0.66f), new Vector3(0, 0.035f, -0.84f), 0.032f, 0.026f, 10, true);
		k.Mat(WandRoles["grip"]);
		k.Box(new Vector3(0, -0.04f, 0.06f), new Vector3(0.03f, 0.12f, 0.045f), 1f, new Basis(Vector3.Right, 0.25f));
		k.Box(new Vector3(0, -0.035f, -0.38f), new Vector3(0.03f, 0.11f, 0.045f), 1f, new Basis(Vector3.Right, 0.2f));
		k.CommitTo(at, "Wand", false);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		var inv = _player.Inventory;
		bool owned = inv != null && inv.HasTool(ToolKind.Flamethrower);
		var hud = GetTree().GetFirstNodeInGroup("equipped_item_hud") as UI.EquippedItemHud;
		bool held = owned && (hud == null || hud.Held == ToolKind.Flamethrower || hud.Held == ToolKind.None);
		bool cameraUp = UI.CameraViewfinder.Current is { } vf && IsInstanceValid(vf) && vf.Raise > 0.01f;
		_active = held && !cameraUp;
		if (owned && _wand == null) BuildWand();
		if (_wand != null) _wand.Visible = held;
		var input = _player.PlayerInput;
		bool want = _active && input.Enabled && (ScriptedFire || input.PhotoHeld) && !_player.PlayerInput.Focus;
		// the heat: up as it fires, down as it rests; all the way up, it locks and vents for seven seconds
		if (Overheated)
		{
			_lock -= dt;
			Heat = Mathf.Max(0f, _lock / LockSeconds);
			if (_lock <= 0f) { Overheated = false; Heat = 0f; AudioDirector.OneShot(this, "flame_ready", 1, null, -10f, "Player"); }
			want = false;
		}
		else if (want)
		{
			Heat += dt / HeatSeconds;
			if (Heat >= 1f)
			{
				Heat = 1f;
				Overheated = true;
				_lock = LockSeconds;
				want = false;
				AudioDirector.OneShot(this, "flame_overheat", 1, null, -4f, "Player");
				GD.Print("[flamethrower] overheated: venting for seven seconds");
			}
		}
		else Heat = Mathf.Max(0f, Heat - dt / CoolSeconds);
		if (want && !Firing) AudioDirector.OneShot(this, "flame_ignite", 2, null, -3f, "Player");
		if (!want && Firing) AudioDirector.OneShot(this, "flame_stop", 2, null, -8f, "Player");
		Firing = want;
		if (_vent != null) _vent.Emitting = Overheated;
		// the roar
		if (_roar.Stream != null)
		{
			_roar.VolumeDb = Mathf.MoveToward(_roar.VolumeDb, Firing ? -2f : -80f, dt * (Firing ? 300f : 120f));
			if (_roar.VolumeDb > -79f && !_roar.Playing) _roar.Play();
			else if (_roar.VolumeDb <= -79f && _roar.Playing) _roar.Stop();
		}
		// the stream: from the nozzle to where the eye is on (a ray from the eye picks the point)
		var cam = _player.CameraRig?.Camera;
		if (cam != null && _nozzle != null)
		{
			var from = cam.GlobalPosition;
			var fwd = -cam.GlobalBasis.Z;
			var q = PhysicsRayQueryParameters3D.Create(from, from + fwd * 24f, 1);
			var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
			var aim = hit.Count > 0 ? (Vector3)hit["position"] : from + fwd * 24f;
			var nozzle = _nozzle.GlobalPosition;
			var dir = (aim - nozzle).Normalized();
			// (aimed a touch high: the stream falls as it flies)
			dir = (dir + Vector3.Up * 0.05f * Mathf.Clamp(nozzle.DistanceTo(aim) / 20f, 0f, 1f)).Normalized();
			Stream.Emit(Firing, nozzle, dir, _player.Velocity * 0.5f, dt);
		}
		else Stream.Emit(false, Vector3.Zero, Vector3.Forward, Vector3.Zero, dt);
		// the wand: a kick while it fires, a sway with the walk, out of the way while the camera's up
		if (_wand != null)
		{
			_kick = Mathf.Lerp(_kick, Firing ? 1f : 0f, 1f - Mathf.Exp(-dt * 10f));
			_sway += dt * _player.GroundSpeed * 1.6f;
			_pilotT += dt;
			var bob = new Vector3(Mathf.Sin(_sway) * 0.008f, Mathf.Abs(Mathf.Cos(_sway)) * 0.006f, 0f) * Mathf.Clamp(_player.GroundSpeed / 3f, 0f, 1f);
			var shake = Firing ? new Vector3(Mathf.Sin(_pilotT * 31f), Mathf.Sin(_pilotT * 27f + 1f), 0f) * 0.0018f : Vector3.Zero;
			_wand.Position = WandRest + bob + shake + new Vector3(0f, 0.004f, 0.025f) * _kick + (cameraUp ? new Vector3(0.05f, -0.3f, 0.1f) : Vector3.Zero);
			_wand.Rotation = new Vector3(0.03f * _kick, 0.04f, 0f);
			if (_pilot != null) _pilot.LightEnergy = Firing ? 0.8f : 0.25f;
		}
	}
}

using Godot;
using ProjectDS.Audio;
using ProjectDS.World;

namespace ProjectDS.Player;

/// <summary>
/// A green road flare in the right hand (2026-10-10; see <see cref="Flare"/>): struck as it's taken, held out low and a
/// little ahead, burning for the time it was given (four-fifths of the walk to the next one), guttering over its last
/// three seconds and going out. Throw it (G / B on a pad): it's lobbed ahead, tumbling, and burns out where it lands,
/// and whatever hunts down here goes to look at it.
/// Its light is a real one, in the world, from its tip; the hand and the stick are lit by their own soft green fill.
/// </summary>
public partial class HeldFlare : HeldItem
{
	protected override Vector3 Rest => new(0.26f, -0.24f, -0.5f);
	protected override Vector3 Lowered => new(0.3f, -0.75f, -0.45f);

	/// <summary>Seconds left to burn; 0 when there's none in hand.</summary>
	public float BurnLeft { get; private set; }
	public bool Lit => BurnLeft > 0f;
	public int Thrown { get; private set; }
	/// <summary>A flare is burning in someone's hand (for the hunter: it sees them further off by it).</summary>
	public static bool AnyLit => _instance is { Lit: true } f && IsInstanceValid(f);

	private static HeldFlare _instance;
	private Node3D _model;
	private MeshInstance3D _core;
	private OmniLight3D _light, _fill;
	private GpuParticles3D _sparks;
	private AudioStreamPlayer _fizz;
	private FastNoiseLite _waver;
	private float _t, _strike;
	private bool _hinted;

	public static HeldFlare For(PlayerController player)
	{
		var cam = player?.CameraRig?.Camera;
		if (cam == null) return null;
		return cam.GetNodeOrNull<HeldFlare>(nameof(HeldFlare)) ?? Attach<HeldFlare>(player);
	}

	protected override bool Wanted => Lit && !CameraUp;

	public override void _EnterTree() => _instance = this;
	public override void _ExitTree() { if (_instance == this) _instance = null; }

	protected override void Build()
	{
		_model = new Node3D { Name = "Model", Rotation = new Vector3(Mathf.DegToRad(-28f), 0, Mathf.DegToRad(-8f)) };
		AddChild(_model);
		Flare.BuildModel(_model, out _core);
		_model.Position = new Vector3(0, -0.06f, 0);
		BuildFist(this, new Vector3(0, -0.02f, 0), new Color(0.09f, 0.075f, 0.065f), new Color(0.07f, 0.075f, 0.07f));
		SetLayer(this);
		_sparks = Flare.Sparks();
		_sparks.Position = new Vector3(0, 0.27f, 0);
		_model.AddChild(_sparks);
		_light = new OmniLight3D { Name = "FlareLight", LightColor = Flare.Green, LightEnergy = 0f, OmniRange = 11f, OmniAttenuation = 1.1f, ShadowEnabled = true, Position = new Vector3(0, 0.3f, 0) };
		_model.AddChild(_light);
		_fill = new OmniLight3D { Name = "HandLight", LightColor = new Color(0.55f, 1f, 0.6f), LightEnergy = 0.5f, OmniRange = 0.6f, ShadowEnabled = false, LightCullMask = HeldLayer, Position = new Vector3(0.03f, 0.3f, 0.08f) };
		_model.AddChild(_fill);
		_waver = new FastNoiseLite { Seed = 2410, Frequency = 2.2f };
		if (ResourceLoader.Exists("res://assets/audio/ambient/flare_burn_loop.wav"))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/flare_burn_loop.wav").Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			_fizz = new AudioStreamPlayer { Name = "Fizz", Stream = wav, Bus = "Player", VolumeDb = -80f };
			AddChild(_fizz);
		}
	}

	/// <summary>A flare taken up and struck, to burn <paramref name="seconds"/>. One already burning in the hand is
	/// dropped at the feet, still burning.</summary>
	public void Take(float seconds)
	{
		if (Lit) Drop();
		BurnLeft = Mathf.Max(seconds, 3f);
		_strike = 0.35f;
		AudioDirector.OneShot(this, "flare_strike", 2, null, -6f, "Player");
		GD.Print($"[flare] taken: burns {BurnLeft:0.0} s");
		if (!_hinted)
		{
			_hinted = true;
			_ = Systems.StoryBeat.Caption(this, "[G] Throw it", 0.4f, 2.6f, 0.8f);
		}
	}

	private void Drop()
	{
		var at = Player.GlobalPosition + (-Player.GlobalBasis.Z) * 0.4f + Vector3.Up * 0.3f;
		Spawn(at, Vector3.Down * 1f);
		BurnLeft = 0f;
	}

	/// <summary>Lobbed ahead (test bots call it too).</summary>
	public void Throw()
	{
		if (!Lit) return;
		var cam = Player.CameraRig?.Camera;
		if (cam == null) return;
		var fwd = -cam.GlobalBasis.Z;
		var at = cam.GlobalPosition + fwd * 0.4f + cam.GlobalBasis.X * 0.2f - cam.GlobalBasis.Y * 0.1f;
		var vel = fwd * 9.5f + Vector3.Up * 2.6f + Player.Velocity * 0.5f;
		Spawn(at, vel);
		AudioDirector.OneShot(this, "cloth", 4, null, -14f, "Player");
		BurnLeft = 0f;
		Thrown++;
		GD.Print("[flare] thrown");
	}

	private void Spawn(Vector3 at, Vector3 vel)
	{
		var f = new Flare { Name = "ThrownFlare", Mode = Flare.Kind.Thrown, BurnLeft = BurnLeft, Velocity = vel };
		(GetTree().CurrentScene ?? GetTree().Root).AddChild(f);
		f.GlobalPosition = at;
	}

	public override void _Process(double delta)
	{
		float dt = Mathf.Min((float)delta, 0.05f);
		if (Lit)
		{
			BurnLeft = Mathf.Max(0f, BurnLeft - dt);
			if (BurnLeft <= 0f)
			{
				AudioDirector.OneShot(this, "flare_out", 1, null, -8f, "Player");
				GD.Print("[flare] went out in the hand");
			}
			else if (Player?.PlayerInput is { ThrowPressed: true } && Raise > 0.6f) Throw();
		}
		base._Process(delta);
		if (_light == null) return;
		_t += dt;
		_strike = Mathf.Max(0f, _strike - dt);
		float k = !Lit ? 0f : BurnLeft < 3f ? BurnLeft / 3f * (0.75f + 0.25f * _waver.GetNoise1D(_t * 3f)) : 1f;
		// (struck: it catches over a third of a second, never a pop of light)
		k *= 1f - _strike / 0.35f * 0.8f;
		_light.LightEnergy = 0.7f * k * (0.9f + 0.1f * _waver.GetNoise1D(_t * 1.3f)) * Raise;
		_fill.LightEnergy = 0.5f * k;
		_core.Visible = Lit;
		_sparks.Emitting = Lit && Visible;
		if (_fizz != null)
		{
			_fizz.VolumeDb = Mathf.MoveToward(_fizz.VolumeDb, Lit ? -14f + Mathf.LinearToDb(Mathf.Max(k, 0.05f)) : -80f, dt * 60f);
			if (_fizz.VolumeDb > -79f && !_fizz.Playing) _fizz.Play();
			else if (_fizz.VolumeDb <= -79f && _fizz.Playing) _fizz.Stop();
		}
	}

	protected override void Animate(float dt)
	{
		// held a little out and up, wavering with the hand; lifted as it's struck
		Hold = Rest + new Vector3(0, _strike * 0.12f, 0);
		_model.Rotation = new Vector3(Mathf.DegToRad(-28f) + Mathf.Sin(_t * 1.1f) * 0.02f, 0, Mathf.DegToRad(-8f) + Mathf.Sin(_t * 0.8f) * 0.02f);
	}
}

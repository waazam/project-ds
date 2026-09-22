using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// Act 1 shot list: a doe grazing in a small glade off the trail. Low-poly, built in code
/// (body, neck and head on a pivot, ears, four legs, a short tail; brown with a pale belly
/// and rump). It grazes: head down for a while, head up to look round, ears flicking.
///
/// It is shy. When the player comes within <see cref="SpookRadius"/>, runs within
/// <see cref="RunSpookRadius"/>, or a shutter goes off within <see cref="ShutterSpookRadius"/>,
/// it throws its head up, holds a moment, then bounds away from the player into the trees
/// and is gone for good: hidden, never back. Fleeing sets <see cref="FledFlag"/> (saved), and on
/// Continue a deer that fled or is already on the roll stays gone.
///
/// Photographable while it is visible: the <see cref="PhotoSubject"/> rides on it. The node's
/// origin is on the ground between its feet; it faces -Z.
/// </summary>
[GlobalClass]
public partial class Deer : Node3D
{
	public const string FledFlag = "deer_fled";

	[Export] public float SpookRadius = 12f;
	[Export] public float RunSpookRadius = 20f;
	[Export] public float ShutterSpookRadius = 30f;
	/// <summary>Head up, looking, before it bolts.</summary>
	[Export] public float AlertSeconds = 0.55f;
	/// <summary>Bounding at least this long; then it carries on until out of sight (or 6 s) and is gone.</summary>
	[Export] public float FleeSeconds = 2.2f;
	[Export] public float FleeSpeed = 8.5f;
	[Export] public float BoundHeight = 0.55f;
	[Export] public float BoundsPerSecond = 2.3f;
	[Export] public float ModelScale = 1.05f;
	[Export] public int Seed = 5;

	public enum State { Grazing, Alert, Fleeing, Gone }
	public State Current { get; private set; } = State.Grazing;

	private Node3D _model, _neck, _earL, _earR, _tail;
	private readonly Node3D[] _legs = new Node3D[4];   // front L, front R, hind L, hind R
	private ForestTerrain _terrain;
	private PlayerController _player;
	private readonly RandomNumberGenerator _rng = new();

	private bool _headDown = true;
	private float _headTimer, _earTimerL, _earTimerR, _tailTimer, _earFlickL, _earFlickR, _tailFlick;
	private float _neckPitch, _neckYaw, _neckYawTarget;
	private float _stateTime;
	private Vector3 _fleeDir;
	private float _yaw;

	private const float GrazePitch = -2.25f;   // neck swung down to the grass

	public override void _Ready()
	{
		_rng.Seed = (ulong)(Seed * 7919 + 3);
		BuildModel();
		AddChild(new PhotoSubject
		{
			Name = "PhotoSubject",
			Id = "deer",
			LookPoints = new[] { new Vector3(0, 0.82f, 0.05f), new Vector3(0, 1.05f, -0.5f) },
			MinDistance = 3f,
			MaxDistance = 45f,
			ConeDegrees = 12f,
			OwnerPath = "..",
		});
		_headTimer = _rng.RandfRange(2f, 5f);
		_earTimerL = _rng.RandfRange(1f, 4f);
		_earTimerR = _rng.RandfRange(1f, 4f);
		_tailTimer = _rng.RandfRange(2f, 6f);
		_neckPitch = GrazePitch;
		CameraTool.PhotoTaken += OnPhotoTaken;
		Callable.From(Restore).CallDeferred();
	}

	public override void _ExitTree() => CameraTool.PhotoTaken -= OnPhotoTaken;

	/// <summary>Restore contract: a deer that fled, or that is already on the roll, is not here any more.</summary>
	private void Restore()
	{
		_terrain = GroundSnap.FindTerrain(this);
		_yaw = GlobalRotation.Y;
		var story = StoryManager.Instance;
		if (story != null && (story.HasFlag(FledFlag) || story.HasFlag(StoryManager.Flag.Photo("deer")))) Vanish();
	}

	private void OnPhotoTaken(Camera3D cam)
	{
		if (Current != State.Grazing || cam == null || !IsInsideTree()) return;
		if (cam.GlobalPosition.DistanceTo(GlobalPosition) < ShutterSpookRadius) Spook();
	}

	/// <summary>Starts the flight (head up, then away). Harmless once it is already going.</summary>
	public void Spook()
	{
		if (Current != State.Grazing) return;
		Current = State.Alert;
		_stateTime = 0f;
		StoryManager.Instance?.SetFlag(FledFlag);
		GD.Print("[deer] spooked");
	}

	private void Vanish()
	{
		Current = State.Gone;
		Visible = false;
		ProcessMode = ProcessModeEnum.Disabled;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_stateTime += dt;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		switch (Current)
		{
			case State.Grazing: Graze(dt); break;
			case State.Alert: Alert(dt); break;
			case State.Fleeing: Flee(dt); break;
		}
		Ears(dt);
		_neck.Rotation = new Vector3(_neckPitch, _neckYaw, 0);
	}

	private void Graze(float dt)
	{
		if (_player != null && IsInstanceValid(_player))
		{
			float d = _player.GlobalPosition.DistanceTo(GlobalPosition);
			if (d < SpookRadius || (_player.IsRunning && d < RunSpookRadius)) { Spook(); return; }
		}
		_headTimer -= dt;
		if (_headTimer <= 0f)
		{
			_headDown = !_headDown;
			_headTimer = _headDown ? _rng.RandfRange(4f, 9f) : _rng.RandfRange(1.8f, 4f);
			_neckYawTarget = _headDown ? _rng.RandfRange(-0.15f, 0.15f) : _rng.RandfRange(-0.5f, 0.5f);
		}
		// chewing: a small bob while the head is down
		float target = _headDown ? GrazePitch + 0.05f * Mathf.Sin(_stateTime * 5.5f) : -0.05f;
		_neckPitch = Mathf.Lerp(_neckPitch, target, 1f - Mathf.Exp(-dt * 2.2f));
		_neckYaw = Mathf.Lerp(_neckYaw, _neckYawTarget, 1f - Mathf.Exp(-dt * 1.5f));
		if (!_headDown && _rng.Randf() < dt * 0.6f) _neckYawTarget = _rng.RandfRange(-0.5f, 0.5f);
		_tailTimer -= dt;
		if (_tailTimer <= 0f) { _tailFlick = 0.35f; _tailTimer = _rng.RandfRange(3f, 8f); }
		_tailFlick = Mathf.Max(0f, _tailFlick - dt);
		_tail.Rotation = new Vector3(_tailFlick > 0f ? -0.9f * Mathf.Sin(_tailFlick / 0.35f * Mathf.Pi) : 0f, 0, 0);
	}

	private void Alert(float dt)
	{
		// head snaps up and turns toward the noise; ears forward; tail up (the white flag)
		_neckPitch = Mathf.Lerp(_neckPitch, 0.12f, 1f - Mathf.Exp(-dt * 12f));
		float look = 0f;
		if (_player != null && IsInstanceValid(_player))
		{
			Vector3 to = ToLocal(_player.GlobalPosition);
			look = Mathf.Clamp(Mathf.Atan2(-to.X, -to.Z), -0.9f, 0.9f);
		}
		_neckYaw = Mathf.Lerp(_neckYaw, look, 1f - Mathf.Exp(-dt * 10f));
		_tail.Rotation = new Vector3(-1.2f, 0, 0);
		if (_stateTime < AlertSeconds) return;
		// away from the player, bent a little so it doesn't run straight down the line of sight
		Vector3 away = GlobalPosition - (_player?.GlobalPosition ?? GlobalPosition + GlobalBasis.Z);
		away.Y = 0;
		if (away.LengthSquared() < 0.01f) away = GlobalBasis.Z;
		_fleeDir = away.Normalized().Rotated(Vector3.Up, _rng.RandfRange(0.25f, 0.5f) * (_rng.Randf() < 0.5f ? -1f : 1f));
		Current = State.Fleeing;
		_stateTime = 0f;
	}

	private void Flee(float dt)
	{
		// turn onto the escape line fast, then bound
		float targetYaw = Mathf.Atan2(-_fleeDir.X, -_fleeDir.Z);
		_yaw = Mathf.LerpAngle(_yaw, targetYaw, 1f - Mathf.Exp(-dt * 9f));
		float speed = FleeSpeed * Mathf.SmoothStep(0f, 0.35f, _stateTime);
		Vector3 p = GlobalPosition + _fleeDir * speed * dt;
		float phase = _stateTime * BoundsPerSecond;
		float arc = Mathf.Abs(Mathf.Sin(phase * Mathf.Pi)) * BoundHeight * Mathf.SmoothStep(0f, 0.3f, _stateTime);
		float ground = _terrain?.HeightAt(p.X, p.Z) ?? p.Y;
		p.Y = ground + arc;
		// nose up on the way up, down on the way down
		float pitch = -0.35f * Mathf.Cos(phase * Mathf.Pi) * Mathf.Sign(Mathf.Sin(phase * Mathf.Pi));
		GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(pitch * 0.5f, _yaw, 0)), p);
		// legs: fore pair reach forward while the hind pair push back, then swap
		float s = Mathf.Sin(phase * Mathf.Tau);
		for (int i = 0; i < 4; i++)
		{
			bool fore = i < 2;
			float swing = (fore ? 0.75f : -0.7f) * s + (i % 2 == 0 ? 0.05f : -0.05f);
			_legs[i].Rotation = new Vector3(-swing, 0, 0);
		}
		_neckPitch = Mathf.Lerp(_neckPitch, -0.15f + 0.15f * s, 1f - Mathf.Exp(-dt * 8f));
		_neckYaw = Mathf.Lerp(_neckYaw, 0f, 1f - Mathf.Exp(-dt * 6f));
		_tail.Rotation = new Vector3(-1.2f, 0, 0);
		if (_stateTime < FleeSeconds) return;
		if (_stateTime > 6f || !InView()) Vanish();
	}

	/// <summary>Whether the player's camera could still see it (in front, near enough, not behind a trunk).</summary>
	private bool InView()
	{
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return false;
		Vector3 c = GlobalPosition + Vector3.Up * 0.8f;
		if (cam.GlobalPosition.DistanceTo(c) > 70f || !cam.IsPositionInFrustum(c)) return false;
		var q = PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, c, 1u);
		if (_player != null) q.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		return GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
	}

	private void Ears(float dt)
	{
		_earTimerL -= dt; _earTimerR -= dt;
		if (_earTimerL <= 0f) { _earFlickL = 0.18f; _earTimerL = _rng.RandfRange(1.5f, 5f); }
		if (_earTimerR <= 0f) { _earFlickR = 0.18f; _earTimerR = _rng.RandfRange(1.5f, 5f); }
		_earFlickL = Mathf.Max(0f, _earFlickL - dt);
		_earFlickR = Mathf.Max(0f, _earFlickR - dt);
		bool alert = Current != State.Grazing;
		float fl = _earFlickL > 0f ? Mathf.Sin(_earFlickL / 0.18f * Mathf.Pi) * 0.7f : 0f;
		float fr = _earFlickR > 0f ? Mathf.Sin(_earFlickR / 0.18f * Mathf.Pi) * 0.7f : 0f;
		_earL.Rotation = new Vector3(alert ? -0.3f : 0f, 0, fl);
		_earR.Rotation = new Vector3(alert ? -0.3f : 0f, 0, -fr);
	}

	// ------------------------------------------------------------------ the model

	private static Color C(float r, float g, float b) => new Color(r, g, b).SrgbToLinear();

	private void BuildModel()
	{
		_model = new Node3D { Name = "Model", Scale = Vector3.One * ModelScale };
		AddChild(_model);
		var mat = PropTextures.FurMat;
		Color hide = C(0.5f, 0.39f, 0.29f), back = C(0.4f, 0.31f, 0.23f), belly = C(0.84f, 0.78f, 0.66f);
		Color dark = C(0.12f, 0.1f, 0.09f), muzzle = C(0.36f, 0.28f, 0.21f), inner = C(0.72f, 0.6f, 0.52f);

		// body: torso, chest, haunch, the pale belly and rump patch under and behind
		var k = new MeshKit();
		k.Mat(mat);
		k.Color = hide;
		k.Blob(new Vector3(0, 0.8f, 0.02f), new Vector3(0.18f, 0.2f, 0.5f), Seed + 1, 0.05f, false, 2f, 0.25f);
		k.Blob(new Vector3(0, 0.8f, -0.33f), new Vector3(0.17f, 0.23f, 0.2f), Seed + 2, 0.05f, false, 2f, 0.25f);
		k.Blob(new Vector3(0, 0.83f, 0.36f), new Vector3(0.18f, 0.21f, 0.2f), Seed + 3, 0.05f, false, 2f, 0.25f);
		k.Color = back;
		k.Blob(new Vector3(0, 0.93f, 0.02f), new Vector3(0.11f, 0.08f, 0.46f), Seed + 4, 0.04f, false, 2f);
		k.Color = belly;
		k.Blob(new Vector3(0, 0.66f, -0.02f), new Vector3(0.13f, 0.09f, 0.4f), Seed + 5, 0.04f, false, 2f);
		k.Blob(new Vector3(0, 0.84f, 0.52f), new Vector3(0.1f, 0.13f, 0.05f), Seed + 6, 0.04f, false, 2f);
		k.CommitTo(_model, "Body");

		// neck and head on one pivot at the base of the neck
		_neck = new Node3D { Name = "Neck", Position = new Vector3(0, 0.88f, -0.44f) };
		_model.AddChild(_neck);
		k = new MeshKit();
		k.Mat(mat);
		k.Color = hide;
		k.Cylinder(new Vector3(0, -0.04f, 0.04f), new Vector3(0, 0.45f, -0.15f), 0.1f, 0.062f, 7, false, 2f);
		k.Color = belly;
		k.Cylinder(new Vector3(0, 0.0f, -0.03f), new Vector3(0, 0.36f, -0.17f), 0.06f, 0.045f, 5, false, 2f);   // pale throat
		k.Color = hide;
		k.Blob(new Vector3(0, 0.49f, -0.2f), new Vector3(0.075f, 0.08f, 0.1f), Seed + 7, 0.04f, false, 2f);      // skull
		k.Color = muzzle;
		k.Cylinder(new Vector3(0, 0.48f, -0.25f), new Vector3(0, 0.43f, -0.41f), 0.055f, 0.03f, 6, true, 2f);
		k.Color = dark;
		k.Blob(new Vector3(0, 0.435f, -0.415f), new Vector3(0.027f, 0.022f, 0.02f), Seed + 8, 0.02f, false, 2f); // nose
		k.Blob(new Vector3(0.062f, 0.51f, -0.25f), new Vector3(0.014f, 0.016f, 0.014f), Seed + 9, 0.02f, false, 2f);
		k.Blob(new Vector3(-0.062f, 0.51f, -0.25f), new Vector3(0.014f, 0.016f, 0.014f), Seed + 10, 0.02f, false, 2f);
		k.CommitTo(_neck, "Head");
		_earL = Ear(_neck, -1f, mat, hide, inner);
		_earR = Ear(_neck, 1f, mat, hide, inner);

		// legs: pivots at the shoulder and hip; hind legs bend back at the hock
		Vector3[] tops = { new(-0.1f, 0.72f, -0.36f), new(0.1f, 0.72f, -0.36f), new(-0.1f, 0.74f, 0.4f), new(0.1f, 0.74f, 0.4f) };
		for (int i = 0; i < 4; i++)
		{
			bool hind = i >= 2;
			var leg = new Node3D { Name = $"Leg{i}", Position = tops[i] };
			_model.AddChild(leg);
			_legs[i] = leg;
			var lk = new MeshKit();
			lk.Mat(mat);
			float h = tops[i].Y;
			Vector3 knee = hind ? new Vector3(0, -0.34f, 0.09f) : new Vector3(0, -0.34f, 0.01f);
			lk.Color = hide;
			if (hind) lk.Blob(new Vector3(0, -0.08f, 0.0f), new Vector3(0.075f, 0.16f, 0.11f), Seed + 20 + i, 0.04f, false, 2f);
			lk.Cylinder(Vector3.Zero, knee, hind ? 0.06f : 0.05f, 0.032f, 6, false, 2f);
			lk.Color = hide * 0.92f;
			lk.Cylinder(knee, new Vector3(0, -h + 0.04f, 0.0f), 0.026f, 0.019f, 5, false, 2f);
			lk.Color = dark;
			lk.Cylinder(new Vector3(0, -h + 0.045f, 0.0f), new Vector3(0, -h, -0.01f), 0.02f, 0.025f, 5, true, 2f);
			lk.CommitTo(leg, "Mesh");
		}

		// tail: short, brown on top, white underneath
		_tail = new Node3D { Name = "Tail", Position = new Vector3(0, 0.92f, 0.55f) };
		_model.AddChild(_tail);
		k = new MeshKit();
		k.Mat(mat);
		k.Color = back;
		k.Blob(new Vector3(0, -0.06f, 0.035f), new Vector3(0.04f, 0.09f, 0.025f), Seed + 30, 0.04f, false, 2f);
		k.Color = belly;
		k.Blob(new Vector3(0, -0.07f, 0.02f), new Vector3(0.034f, 0.08f, 0.022f), Seed + 31, 0.04f, false, 2f);
		k.CommitTo(_tail, "Mesh");
	}

	private Node3D Ear(Node3D neck, float side, Material mat, Color hide, Color inner)
	{
		var ear = new Node3D { Name = side < 0 ? "EarL" : "EarR", Position = new Vector3(side * 0.05f, 0.55f, -0.16f) };
		neck.AddChild(ear);
		var k = new MeshKit();
		k.Mat(mat);
		k.Xf = new Transform3D(Basis.FromEuler(new Vector3(0.15f, 0, side * -0.75f)), Vector3.Zero);
		k.Color = hide;
		k.Blob(new Vector3(0, 0.075f, 0), new Vector3(0.035f, 0.085f, 0.014f), Seed + (side < 0 ? 40 : 41), 0.03f, false, 2f);
		k.Color = inner;
		k.Blob(new Vector3(0, 0.07f, -0.008f), new Vector3(0.024f, 0.065f, 0.008f), Seed + (side < 0 ? 42 : 43), 0.03f, false, 2f);
		k.CommitTo(ear, "Mesh");
		return ear;
	}
}

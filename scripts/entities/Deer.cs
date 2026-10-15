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
///
/// (2026-10-15, the owner: "the doe ... could use some graphic updates") The model rebuilt as an animal, not a sausage on
/// sticks: a deep chest, the withers up, the waist tucked, a round rump; the neck rising out of the shoulders (it started
/// from a flat cut); a tapered head and muzzle with eyes, big cupped ears up and out; legs with an elbow, a knee and a
/// hock, fetlocks, dew claws and split hooves; a flat tail white beneath. One coat, painted per vertex: a darker back, the
/// pale belly, throat, inner legs and rump patch blended in (they were stuck-on white blobs), a faint dapple. Alive while
/// it grazes: it breathes, shifts its weight from foot to foot, and steps now and then.
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

	/// <summary>For the preview: the triangles in the model.</summary>
	public int TriangleCount { get; private set; }

	private Node3D _model, _body, _neck, _earL, _earR, _tail;
	private readonly Node3D[] _legs = new Node3D[4];   // front L, front R, hind L, hind R
	private ForestTerrain _terrain;
	private PlayerController _player;
	private readonly RandomNumberGenerator _rng = new();

	private bool _headDown = true;
	private float _headTimer, _earTimerL, _earTimerR, _tailTimer, _earFlickL, _earFlickR, _tailFlick;
	private float _neckPitch, _neckYaw, _neckYawTarget;
	private float _stateTime, _clock;
	private Vector3 _fleeDir;
	private float _yaw;
	// grazing life: a step now and then (one leg lifted and put down a little on), the weight shifting
	private float _stepTimer, _stepT = -1f;
	private int _stepLeg;

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
		_stepTimer = _rng.RandfRange(3f, 7f);
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
		_clock += dt;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		switch (Current)
		{
			case State.Grazing: Graze(dt); break;
			case State.Alert: Alert(dt); break;
			case State.Fleeing: Flee(dt); break;
		}
		Ears(dt);
		_neck.Rotation = new Vector3(_neckPitch, _neckYaw, 0);
		// breathing: the barrel swells a little (quicker once it's frightened)
		float breath = Mathf.Sin(_clock * (Current == State.Grazing ? 1.6f : 4.5f)) * 0.012f;
		_body.Scale = new Vector3(1f + breath, 1f + breath * 0.8f, 1f);
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
		// a step now and then while it grazes: one leg lifted, folded a little and set down again (head down, moving on
		// along the grass); the weight shifting over the others
		_stepTimer -= dt;
		if (_stepT < 0f && _stepTimer <= 0f) { _stepT = 0f; _stepLeg = _rng.RandiRange(0, 3); _stepTimer = _rng.RandfRange(2.5f, 7f); }
		for (int i = 0; i < 4; i++)
		{
			float lift = 0f;
			if (_stepT >= 0f && i == _stepLeg) lift = Mathf.Sin(Mathf.Clamp(_stepT / 0.55f, 0f, 1f) * Mathf.Pi);
			bool fore = i < 2;
			_legs[i].Rotation = new Vector3((fore ? -0.35f : 0.25f) * lift, 0, 0);
			if (_legs[i].GetNodeOrNull<Node3D>("Lower") is { } lower) lower.Rotation = new Vector3((fore ? -0.9f : 0.7f) * lift, 0, 0);
		}
		if (_stepT >= 0f) { _stepT += dt; if (_stepT > 0.55f) _stepT = -1f; }
		float shift = Mathf.Sin(_clock * 0.45f + Seed) * 0.012f;
		_model.Position = new Vector3(shift, 0, 0);
		_model.Rotation = new Vector3(0, 0, shift * 0.8f);
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
		// frozen, every leg planted (a step in progress set down)
		for (int i = 0; i < 4; i++)
		{
			_legs[i].Rotation = _legs[i].Rotation.Lerp(Vector3.Zero, 1f - Mathf.Exp(-dt * 14f));
			if (_legs[i].GetNodeOrNull<Node3D>("Lower") is { } lower) lower.Rotation = lower.Rotation.Lerp(Vector3.Zero, 1f - Mathf.Exp(-dt * 14f));
		}
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
		_model.Position = Vector3.Zero;
		_model.Rotation = Vector3.Zero;
		// legs: fore pair reach forward while the hind pair push back, then swap
		float s = Mathf.Sin(phase * Mathf.Tau);
		for (int i = 0; i < 4; i++)
		{
			bool fore = i < 2;
			float swing = (fore ? 0.75f : -0.7f) * s + (i % 2 == 0 ? 0.05f : -0.05f);
			_legs[i].Rotation = new Vector3(-swing, 0, 0);
			// (2026-10-12, the animation overhaul) the lower leg folds as the leg comes through (the fore knee forward, the hind
			// hock back), straightening to land: a gather and a reach, not a stick swung from the hip
			float through = fore ? Mathf.Max(0f, s) : Mathf.Max(0f, -s);
			if (_legs[i].GetNodeOrNull<Node3D>("Lower") is { } lower) lower.Rotation = new Vector3((fore ? -1.3f : 0.9f) * through, 0, 0);   // (the fore hoof folds back under, the hind one forward)
		}
		// (2026-10-15) the back flexes with the bound: gathered over the legs as they come under, stretched as they reach
		_body.Rotation = new Vector3(0.06f * s, 0, 0);
		_neckPitch = Mathf.Lerp(_neckPitch, -0.15f + 0.15f * s, 1f - Mathf.Exp(-dt * 8f));
		_neckYaw = Mathf.Lerp(_neckYaw, 0f, 1f - Mathf.Exp(-dt * 6f));
		_tail.Rotation = new Vector3(-1.2f + 0.15f * s, 0, 0);
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
		// alert: both turned forward, cupped at the sound
		_earL.Rotation = new Vector3(alert ? -0.3f : 0f, alert ? 0.5f : 0f, fl);
		_earR.Rotation = new Vector3(alert ? -0.3f : 0f, alert ? -0.5f : 0f, -fr);
	}

	// ------------------------------------------------------------------ the model

	private static Color C(float r, float g, float b) => new Color(r, g, b).SrgbToLinear();

	private static StandardMaterial3D _furTri;
	private static StandardMaterial3D TriFur()
	{
		var m = (StandardMaterial3D)PropTextures.FurMat.Duplicate();
		m.Uv1Triplanar = true;
		m.Uv1WorldTriplanar = false;
		m.Uv1Scale = Vector3.One * 2.2f;
		m.Uv1TriplanarSharpness = 2f;
		m.VertexColorUseAsAlbedo = true;
		return m;
	}

	private static readonly Color Hide = C(0.52f, 0.4f, 0.29f), Back = C(0.36f, 0.27f, 0.19f), Pale = C(0.86f, 0.81f, 0.7f);
	private static readonly Color Dark = C(0.09f, 0.08f, 0.07f), Muzzle = C(0.3f, 0.23f, 0.17f), Inner = C(0.74f, 0.6f, 0.53f);
	private static readonly Color Hoof = C(0.13f, 0.11f, 0.1f);

	private static float SS(float a, float b, float x) => Mathf.SmoothStep(a, b, x);

	/// <summary>A faint dapple on the coat (position-hashed, smooth): the coat isn't one flat brown.</summary>
	private static float Dapple(Vector3 p) =>
		0.5f + 0.25f * Mathf.Sin(p.X * 23f + p.Z * 17f) * Mathf.Sin(p.Y * 19f - p.Z * 11f) + 0.25f * Mathf.Sin(p.Z * 41f + p.Y * 7f);

	private void BuildModel()
	{
		_model = new Node3D { Name = "Model", Scale = Vector3.One * ModelScale };
		AddChild(_model);
		var mat = _furTri ??= TriFur();
		int tris = 0;

		// the barrel, brisket to rump: the chest deep and the withers up, the waist tucked, the haunch round (in model space:
		// the ground at 0, facing -Z; size = half-width, half-height)
		_body = new Node3D { Name = "Body" };
		_model.AddChild(_body);
		var k = new MeshKit();
		k.Mat(mat);
		k.Color = Hide;
		Loft.Tube(k, new[]
			{
				new Vector3(0, 0.80f, -0.58f), new Vector3(0, 0.81f, -0.5f), new Vector3(0, 0.83f, -0.36f), new Vector3(0, 0.85f, -0.16f),
				new Vector3(0, 0.86f, 0.06f), new Vector3(0, 0.88f, 0.26f), new Vector3(0, 0.89f, 0.42f), new Vector3(0, 0.87f, 0.55f), new Vector3(0, 0.85f, 0.63f),
			},
			new[]
			{
				new Vector2(0.06f, 0.08f), new Vector2(0.115f, 0.165f), new Vector2(0.15f, 0.205f), new Vector2(0.16f, 0.19f),
				new Vector2(0.15f, 0.165f), new Vector2(0.158f, 0.17f), new Vector2(0.15f, 0.17f), new Vector2(0.105f, 0.125f), new Vector2(0.04f, 0.06f),
			}, 18, 4);
		// the withers: a low ridge over the shoulders, blended into the back
		Loft.Tube(k, new[] { new Vector3(0, 0.98f, -0.44f), new Vector3(0, 1.02f, -0.32f), new Vector3(0, 1.0f, -0.18f), new Vector3(0, 0.97f, -0.04f) },
			new[] { new Vector2(0.03f, 0.02f), new Vector2(0.075f, 0.05f), new Vector2(0.08f, 0.04f), new Vector2(0.04f, 0.02f) }, 12, 3);
		// the shoulders and the haunches: the big muscles under the coat where the legs come out
		foreach (float sx in new[] { -1f, 1f })
		{
			// (just under the coat's surface: they shape it, they don't stand off it)
			k.Blob(new Vector3(0.09f * sx, 0.8f, -0.36f), new Vector3(0.062f, 0.13f, 0.095f), Seed + 11 + (int)sx, 0.02f, false, 2f, 0f, 2);
			k.Blob(new Vector3(0.095f * sx, 0.83f, 0.38f), new Vector3(0.07f, 0.15f, 0.135f), Seed + 13 + (int)sx, 0.02f, false, 2f, 0f, 2);
		}
		Coat(k, belly: true, rump: true);

		tris += Tris(k.CommitTo(_body, "Mesh"));

		// neck and head on one pivot at the base of the neck: the neck rising out of the chest (its root inside the barrel, so
		// there's no cut), the throat pale, the head tapered to the muzzle
		// (the pivot well inside the chest, the neck's root round it: swung down to graze, the root turns in place inside the
		// barrel instead of swinging up over the withers)
		_neck = new Node3D { Name = "Neck", Position = new Vector3(0, 0.86f, -0.42f) };
		_model.AddChild(_neck);
		k = new MeshKit();
		k.Mat(mat);
		k.Color = Hide;
		Loft.Tube(k, new[] { new Vector3(0, 0f, 0.02f), new Vector3(0, 0.1f, -0.02f), new Vector3(0, 0.24f, -0.08f), new Vector3(0, 0.38f, -0.14f), new Vector3(0, 0.49f, -0.19f) },
			new[] { new Vector2(0.09f, 0.11f), new Vector2(0.075f, 0.1f), new Vector2(0.058f, 0.075f), new Vector2(0.048f, 0.062f), new Vector2(0.05f, 0.06f) }, 14, 4, false);
		// the head: a tapered skull, the brow, the long face, the muzzle and the chin
		Loft.Tube(k, new[] { new Vector3(0, 0.53f, -0.16f), new Vector3(0, 0.52f, -0.22f), new Vector3(0, 0.49f, -0.3f), new Vector3(0, 0.455f, -0.38f), new Vector3(0, 0.43f, -0.43f) },
			new[] { new Vector2(0.05f, 0.05f), new Vector2(0.062f, 0.066f), new Vector2(0.05f, 0.056f), new Vector2(0.034f, 0.042f), new Vector2(0.024f, 0.03f) }, 14, 4);
		k.Color = Dark;
		k.Blob(new Vector3(0, 0.43f, -0.445f), new Vector3(0.024f, 0.019f, 0.014f), Seed + 8, 0.02f, false, 2f, 0f, 2);   // the nose
		foreach (float sx in new[] { -1f, 1f })
		{
			k.Color = Dark;
			k.Blob(new Vector3(0.05f * sx, 0.515f, -0.255f), new Vector3(0.013f, 0.016f, 0.017f), Seed + 9 + (int)sx, 0.01f, false, 2f, 0f, 2);   // the eye
			k.Color = Pale;
			k.Blob(new Vector3(0.047f * sx, 0.515f, -0.255f), new Vector3(0.015f, 0.02f, 0.021f), Seed + 15 + (int)sx, 0.01f, false, 2f, 0f, 1);   // the pale ring round it
		}
		Coat(k, belly: false, rump: false, throat: true);

		tris += Tris(k.CommitTo(_neck, "Head"));
		_earL = Ear(_neck, -1f, mat, ref tris);
		_earR = Ear(_neck, 1f, mat, ref tris);

		// legs: a pivot at the shoulder or the hip, a second at the knee or the hock. The fore: the forearm down to the knee,
		// the cannon to the fetlock, the pastern sloped to the hoof. The hind: the heavy thigh down to the hock (well back),
		// the long cannon to the fetlock. Split hooves, two dew claws behind each fetlock.
		Vector3[] tops = { new(-0.1f, 0.74f, -0.36f), new(0.1f, 0.74f, -0.36f), new(-0.1f, 0.76f, 0.4f), new(0.1f, 0.76f, 0.4f) };
		for (int i = 0; i < 4; i++)
		{
			bool hind = i >= 2;
			var leg = new Node3D { Name = $"Leg{i}", Position = tops[i] };
			_model.AddChild(leg);
			_legs[i] = leg;
			float h = tops[i].Y;
			Vector3 knee = hind ? new Vector3(0, -0.36f, 0.11f) : new Vector3(0, -0.38f, 0.0f);
			var lk = new MeshKit();
			lk.Mat(mat);
			lk.Color = Hide;
			if (hind)
				Loft.Tube(lk, new[] { new Vector3(0, 0.08f, -0.03f), new Vector3(0, -0.06f, -0.02f), new Vector3(0, -0.2f, 0.04f), new Vector3(0, -0.3f, 0.09f), knee },
					new[] { new Vector2(0.07f, 0.12f), new Vector2(0.066f, 0.11f), new Vector2(0.045f, 0.065f), new Vector2(0.026f, 0.04f), new Vector2(0.022f, 0.034f) }, 14, 4);
			else
				Loft.Tube(lk, new[] { new Vector3(0, 0.06f, 0.0f), new Vector3(0, -0.1f, 0.03f), new Vector3(0, -0.24f, 0.01f), knee },
					new[] { new Vector2(0.055f, 0.08f), new Vector2(0.045f, 0.06f), new Vector2(0.03f, 0.036f), new Vector2(0.024f, 0.028f) }, 14, 4);
			// the joint's knob (the knee, the point of the hock)
			lk.Blob(knee + new Vector3(0, 0, hind ? 0.012f : 0.004f), new Vector3(0.024f, 0.03f, 0.026f), Seed + 50 + i, 0.0f, false, 2f, 0f, 2);
			Coat(lk, belly: false, rump: false, leg: true);

			tris += Tris(lk.CommitTo(leg, "Mesh"));
			var lower = new Node3D { Name = "Lower", Position = knee };
			leg.AddChild(lower);
			var lo = new MeshKit();
			lo.Mat(mat);
			lo.Color = Hide * 0.92f;
			Vector3 ground = new Vector3(0, -h, 0) - knee;   // the ground under the knee, in the lower leg's frame
			Vector3 fetlock = new Vector3(0, ground.Y + 0.085f, hind ? -0.04f : 0.0f);
			Vector3 hoofTop = new Vector3(0, ground.Y + 0.045f, fetlock.Z - 0.025f);
			Loft.Tube(lo, new[] { Vector3.Zero, fetlock * 0.5f + new Vector3(0, 0, hind ? -0.012f : 0.004f), fetlock },
				new[] { new Vector2(0.021f, 0.026f), new Vector2(0.015f, 0.02f), new Vector2(0.017f, 0.022f) }, 12, 4);
			// the knee or the hock as a rounded joint on this side too: folded, the leg shows a round joint, not two tube ends
			lo.Blob(Vector3.Zero, new Vector3(0.023f, 0.027f, 0.025f), Seed + 80 + i, 0.0f, false, 2f, 0f, 2);
			lo.Blob(fetlock, new Vector3(0.019f, 0.022f, 0.024f), Seed + 60 + i, 0.0f, false, 2f, 0f, 2);
			Loft.Tube(lo, new[] { fetlock, hoofTop }, new[] { new Vector2(0.016f, 0.02f), new Vector2(0.018f, 0.022f) }, 12, 3, false);
			lo.Color = Hoof;
			foreach (float sx in new[] { -1f, 1f })
			{
				// the two halves of the hoof, pointed at the toe; a dew claw behind the fetlock
				lo.Cylinder(hoofTop + new Vector3(0.008f * sx, 0, 0), new Vector3(0.009f * sx, ground.Y + 0.004f, hoofTop.Z - 0.02f), 0.012f, 0.009f, 10, true, 2f);
				lo.Blob(fetlock + new Vector3(0.012f * sx, -0.01f, 0.024f), new Vector3(0.006f, 0.008f, 0.006f), Seed + 70 + i * 2 + (int)sx, 0.02f, false, 2f, 0f, 1);
			}

			tris += Tris(lo.CommitTo(lower, "Mesh"));
		}

		// tail: short and flat, brown on top, white beneath (the flag when it's raised)
		_tail = new Node3D { Name = "Tail", Position = new Vector3(0, 0.93f, 0.58f) };
		_model.AddChild(_tail);
		k = new MeshKit();
		k.Mat(mat);
		k.Color = Back;
		Loft.Tube(k, new[] { new Vector3(0, 0, -0.01f), new Vector3(0, -0.06f, 0.03f), new Vector3(0, -0.13f, 0.04f) },
			new[] { new Vector2(0.035f, 0.018f), new Vector2(0.045f, 0.022f), new Vector2(0.03f, 0.014f) }, 10, 3);
		k.Shade((Vector3 p, Vector3 n, ref Color c, ref Vector2 uv2) => { c = Back.Lerp(Pale, SS(0.1f, -0.4f, n.Z)); });

		tris += Tris(k.CommitTo(_tail, "Mesh"));
		TriangleCount = tris;
	}

	/// <summary>The coat's colour per vertex: the hide, darker along the back, pale underneath (the belly, the inside of the
	/// legs, the throat and the rump patch) with soft edges, a faint dapple over it.</summary>
	private static void Coat(MeshKit k, bool belly, bool rump, bool throat = false, bool leg = false)
	{
		k.Shade((Vector3 p, Vector3 n, ref Color c, ref Vector2 uv2) =>
		{
			if (c.R < Hide.R * 0.5f && c.G < Hide.G * 0.5f) return;   // (the eyes, the nose: as drawn)
			if (c.R > Hide.R * 1.3f) return;                           // (the eye ring)
			Color col = c.Lerp(Back, SS(0.45f, 0.95f, n.Y) * 0.8f);   // the darker back
			float pale = 0f;
			if (belly) pale = Mathf.Max(pale, SS(-0.25f, -0.7f, n.Y) * SS(0.86f, 0.74f, p.Y));
			if (rump) pale = Mathf.Max(pale, SS(0.48f, 0.6f, p.Z) * SS(0.2f, 0.6f, n.Z) * SS(0.75f, 0.85f, p.Y));
			if (throat) pale = Mathf.Max(pale, SS(-0.05f, -0.6f, n.Z) * SS(0.0f, -0.4f, n.Y + 0.2f) * SS(0.46f, 0.3f, p.Y));
			if (leg) pale = Mathf.Max(pale, SS(0.5f, 0.95f, Mathf.Abs(n.X)) * 0.45f * SS(-0.05f, -0.25f, p.Y));
			col = col.Lerp(Pale, Mathf.Clamp(pale, 0f, 1f));
			float d = Dapple(p);
			c = col * (0.92f + 0.12f * d);
			c.A = 1f;
		});
	}

	private static int Tris(MeshInstance3D mi) => mi?.Mesh == null ? 0 : mi.Mesh.GetFaces().Length / 3;

	private Node3D Ear(Node3D neck, float side, Material mat, ref int tris)
	{
		// up and out from the top of the skull, big and cupped forward: a deer's ears are most of its silhouette
		var ear = new Node3D { Name = side < 0 ? "EarL" : "EarR", Position = new Vector3(side * 0.04f, 0.565f, -0.18f) };
		neck.AddChild(ear);
		var k = new MeshKit();
		k.Mat(mat);
		k.Xf = new Transform3D(Basis.FromEuler(new Vector3(0.25f, side * 0.3f, side * -0.55f)), Vector3.Zero);
		k.Color = Hide;
		Loft.Tube(k, new[] { new Vector3(0, 0, 0), new Vector3(0, 0.05f, 0), new Vector3(0, 0.1f, 0), new Vector3(0, 0.135f, 0.004f) },
			new[] { new Vector2(0.02f, 0.012f), new Vector2(0.04f, 0.014f), new Vector2(0.034f, 0.012f), new Vector2(0.008f, 0.005f) }, 10, 3);
		k.Color = Inner;
		Loft.Tube(k, new[] { new Vector3(0, 0.02f, -0.008f), new Vector3(0, 0.06f, -0.01f), new Vector3(0, 0.11f, -0.008f) },
			new[] { new Vector2(0.016f, 0.005f), new Vector2(0.03f, 0.006f), new Vector2(0.008f, 0.003f) }, 8, 3);
		k.Color = Dark;
		Loft.Tube(k, new[] { new Vector3(0, 0.118f, 0.002f), new Vector3(0, 0.136f, 0.004f) }, new[] { new Vector2(0.02f, 0.012f), new Vector2(0.006f, 0.004f) }, 8, 2, false);   // the dark rim at the tip

		tris += Tris(k.CommitTo(ear, "Mesh"));
		return ear;
	}
}

using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Player;

/// <summary>
/// The player's camera, in one of two modes (GameSettings.Camera):
///
/// FirstPerson (current design): the camera sits at eye height. The body
/// (and its shadow) is hidden, and a small head bob follows the stride.
///
/// ThirdPerson (kept for later): an over-the-shoulder orbit. A SpringArm3D
/// probes for walls, and the camera pulls in instantly on a hit and eases back
/// out, so it never clips but never snaps outward either.
///
/// Either way the rig is top-level, follows the player with smoothing, and
/// yaws/pitches from PlayerInput.
/// </summary>
public partial class PlayerCameraRig : Node3D
{
	[Export] public NodePath TargetPath = "..";
	[Export] public NodePath SpringArmPath = "SpringArm";
	[Export] public NodePath CameraPath = "Camera";

	[ExportGroup("Focus")]
	/// <summary>Field of view while focusing (right mouse), as a fraction of normal.</summary>
	[Export] public float FocusFovScale = 0.72f;
	[Export] public float FocusSharpness = 7f;

	[ExportGroup("First person")]
	[Export] public float EyeHeight = 1.62f;
	[Export] public float FirstPersonFov = 70f;
	/// <summary>Degrees added to the field of view by a story beat (the clearing loop's swim); tweened by the beat, 0 at rest.</summary>
	public float FovSwim;
	/// <summary>The camera's zoom while it's up to the eye (CameraTool sets it; 1 when not).</summary>
	public float PhotoZoom = 1f;
	/// <summary>A slow roll of the view in radians (Act 6's daze); 0 normally. Added on top of the pitch/yaw.</summary>
	public float RollSwim;
	/// <summary>A pitch added on top of the look (radians), for a view that rides something: Act 12's
	/// boat rocking under the rower. 0 normally; never touches <see cref="Pitch"/> itself.</summary>
	public float PitchSwim;
	/// <summary>A positional jolt added to the first-person camera (metres, camera-local): Act 12's
	/// breach and slams. 0 normally.</summary>
	public Vector3 Shake;
	[Export] public float FirstPersonMinPitch = -80f;
	[Export] public float FirstPersonMaxPitch = 80f;
	[Export] public float EyeVerticalSharpness = 18f;   // smooths stairs without feeling floaty
	[Export] public float BobHeight = 0.022f;
	[Export] public float BobSway = 0.012f;

	[ExportGroup("Weight (first person)")]
	/// <summary>How closely the view follows the mouse: a slight catch-up, never a lag you'd notice as
	/// such, but a snap round to look behind takes a beat to arrive (the owner). Kept gentle: the owner
	/// gets headaches from strong camera motion.</summary>
	[Export] public float LookSharpness = 24f;
	/// <summary>The head's inertia: metres it trails behind the body per m/s² of acceleration, and the most.</summary>
	[Export] public float HeadLag = 0.012f, HeadLagMax = 0.05f;
	/// <summary>The roll into a strafe (radians at walking speed: about a degree and a quarter).</summary>
	[Export] public float StrafeRoll = 0.022f;
	/// <summary>Peeking: how far the head moves out to the side, and how much it tilts.</summary>
	[Export] public float PeekReach = 0.32f, PeekRoll = 0.06f;

	[ExportGroup("Third person")]
	[Export] public float PivotHeight = 1.5f;
	[Export] public float ShoulderOffset = 0.38f;
	[Export] public float ThirdPersonFov = 62f;
	[Export] public float MinDistance = 1.6f;
	[Export] public float MaxDistance = 5.5f;
	[Export] public float MinPitch = -65f;
	[Export] public float MaxPitch = 50f;
	[Export] public float FollowSharpness = 14f;
	[Export] public float VerticalFollowSharpness = 7f;   // softer, smooths stairs and bumps
	[Export] public float ZoomOutSharpness = 4f;

	public float Yaw { get; private set; }
	public float Pitch { get; private set; }
	// where the look is going (the mouse moves these; the view eases after them)
	private float _yawGoal, _pitchGoal;
	private Vector3 _lag;
	private float _lean, _peek;
	/// <summary>A story beat that needs the view driven past the mode's pitch limit (Act 11: looking up the
	/// giant to its eyes) sets this (degrees) for its duration and clears it after.</summary>
	public float? MaxPitchOverride { get; set; }
	public Basis YawBasis => new Basis(Vector3.Up, Yaw);
	public Camera3D Camera { get; private set; }
	public bool IsFirstPerson => _mode == CameraMode.FirstPerson;

	private PlayerController _target;
	private SpringArm3D _arm;
	private float _currentLength;
	private CameraMode _mode = (CameraMode)(-1);
	private float _bobPhase;
	private float _bobAmount;
	private float _baseFov = 70f;

	public override void _Ready()
	{
		TopLevel = true;
		_target = GetNode<PlayerController>(TargetPath);
		_arm = GetNode<SpringArm3D>(SpringArmPath);
		Camera = GetNode<Camera3D>(CameraPath);
		_arm.AddExcludedObject(_target.GetRid());
		_currentLength = GameSettings.Instance.CameraDistance;
		// Mode (and body visibility) is applied on the first frame: the body isn't ready yet.
		GlobalPosition = PivotPosition();
		ApplyRotation();
	}

	public void SnapBehind(float yaw)
	{
		Yaw = _yawGoal = yaw;
		GlobalPosition = PivotPosition();
		ApplyRotation();
	}

	/// <summary>The body was just moved by <paramref name="d"/> (Act 14's stairwell loop): move the
	/// eye's eased position with it, so the view doesn't glide after it.</summary>
	public void ShiftBy(Vector3 d) => GlobalPosition += d;

	/// <summary>Forces the pitch directly (clamped to the current mode's limits), for scripted
	/// camera moments (the top-of-the-stairs look-down) rather than player input.</summary>
	public void SetPitch(float radians)
	{
		float minPitch = IsFirstPerson ? FirstPersonMinPitch : MinPitch;
		float maxPitch = MaxPitchOverride ?? (IsFirstPerson ? FirstPersonMaxPitch : MaxPitch);
		Pitch = _pitchGoal = Mathf.Clamp(radians, Mathf.DegToRad(minPitch), Mathf.DegToRad(maxPitch));
	}

	private void ApplyMode(CameraMode mode)
	{
		if (mode == _mode) return;
		_mode = mode;
		bool fp = mode == CameraMode.FirstPerson;
		_baseFov = fp ? FirstPersonFov : ThirdPersonFov;
		Camera.Fov = _baseFov;
		Camera.Position = Vector3.Zero;
		Pitch = _pitchGoal = fp ? 0f : Mathf.DegToRad(-12f);
		// First person: the placeholder body is hidden entirely, shadow included.
		_target.Visual.Visible = !fp;
		_arm.ProcessMode = fp ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		// F5: dev toggle for the dormant third-person camera.
		if (OS.IsDebugBuild() && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F5 })   // dev builds only
		{
			var s = GameSettings.Instance;
			s.Camera = s.Camera == CameraMode.FirstPerson ? CameraMode.ThirdPerson : CameraMode.FirstPerson;
			return;
		}
		if (!IsFirstPerson && e is InputEventMouseButton mb && mb.Pressed && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			var s = GameSettings.Instance;
			if (mb.ButtonIndex == MouseButton.WheelUp) s.CameraDistance -= 0.3f;
			else if (mb.ButtonIndex == MouseButton.WheelDown) s.CameraDistance += 0.3f;
			s.CameraDistance = Mathf.Clamp(s.CameraDistance, MinDistance, MaxDistance);
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		ApplyMode(GameSettings.Instance.Camera);

		// Focus: ease the field of view in, and slow the aim to match so it stays steady.
		float fovGoal = _baseFov * (_target.PlayerInput.Focus ? FocusFovScale / Mathf.Max(1f, PhotoZoom) : 1f) + FovSwim;
		Camera.Fov = Mathf.Lerp(Camera.Fov, fovGoal, 1f - Mathf.Exp(-FocusSharpness * dt));

		Vector2 look = _target.PlayerInput.ConsumeLook() * (Camera.Fov / _baseFov);
		// working something with a held use: the look slows (the stance is locked, the eyes still move)
		if (_target.Interaction?.Holding ?? false) look *= 0.4f;
		float minPitch = IsFirstPerson ? FirstPersonMinPitch : MinPitch;
		float maxPitch = MaxPitchOverride ?? (IsFirstPerson ? FirstPersonMaxPitch : MaxPitch);
		// a story beat may have set the view directly (SetPitch/SnapBehind) or clamped it: the goal follows
		_yawGoal = Mathf.Wrap(_yawGoal + look.X, -Mathf.Pi, Mathf.Pi);
		_pitchGoal = Mathf.Clamp(_pitchGoal + look.Y, Mathf.DegToRad(minPitch), Mathf.DegToRad(maxPitch));
		bool tests = GameSettings.Instance?.AutoTest ?? false;
		bool motion = GameSettings.Instance?.HeadMotion ?? true;
		float k = tests || !IsFirstPerson || !motion ? 1f : 1f - Mathf.Exp(-LookSharpness * dt);
		Yaw = Mathf.Wrap(Yaw + Mathf.AngleDifference(Yaw, _yawGoal) * k, -Mathf.Pi, Mathf.Pi);
		Pitch = Mathf.Clamp(Mathf.Lerp(Pitch, _pitchGoal, k), Mathf.DegToRad(minPitch), Mathf.DegToRad(maxPitch));

		if (IsFirstPerson) UpdateFirstPerson(dt);
		else UpdateThirdPerson(dt);
	}

	private void UpdateFirstPerson(float dt)
	{
		// Head locked to the body horizontally; vertical eased so stairs don't jolt.
		Vector3 goal = PivotPosition();
		Vector3 pos = GlobalPosition;
		pos.X = goal.X;
		pos.Z = goal.Z;
		pos.Y = Mathf.Lerp(pos.Y, goal.Y, 1f - Mathf.Exp(-EyeVerticalSharpness * dt));
		GlobalPosition = pos;
		ApplyRotation();

		// Head bob, driven by the real stride (PlayerFootsteps): the head dips on each footfall and sways
		// toward the foot that's down; its size follows the body's actual speed, not the keys.
		float speed = _target.IsOnFloor() ? _target.GroundSpeed : 0f;
		float motion = (GameSettings.Instance?.HeadMotion ?? true) ? 1f : 0f;
		_bobAmount = Mathf.Lerp(_bobAmount, Mathf.Clamp(speed / 2f, 0f, 1.4f) * motion, 1f - Mathf.Exp(-6f * dt));
		float dip, sway;
		if (_target.Footsteps is { } steps)
		{
			float ph = steps.StepPhase;
			dip = 0.5f + 0.5f * Mathf.Cos(ph * Mathf.Tau);                         // 1 on the footfall, 0 mid-stride
			sway = Mathf.Sin((steps.Steps + ph) * Mathf.Pi);                         // side to side, a step each way
		}
		else
		{
			float stride = _target.IsRunning ? 1.9f : 1.3f;
			_bobPhase += speed / stride * Mathf.Pi * dt;
			dip = Mathf.Abs(Mathf.Cos(_bobPhase));
			sway = Mathf.Sin(_bobPhase);
		}
		// the head's inertia: it trails a little behind the body as it sets off, and runs on as it stops
		Vector3 acc = _target.Acceleration3;
		Vector3 lagGoal = (-acc * HeadLag).LimitLength(HeadLagMax) * motion;
		_lag = _lag.Lerp(lagGoal, 1f - Mathf.Exp(-7f * dt));
		// rolling a touch into a strafe, as the body is pushed sideways
		Vector3 right = GlobalBasis.X with { Y = 0 };
		float lateral = right.LengthSquared() > 0.01f ? new Vector3(_target.Velocity.X, 0, _target.Velocity.Z).Dot(right.Normalized()) : 0f;
		_lean = Mathf.Lerp(_lean, -lateral / Mathf.Max(_target.WalkSpeed, 0.1f) * StrafeRoll * motion, 1f - Mathf.Exp(-4f * dt));
		// peeking round a corner: the head moves out to the side (never through a wall) and tilts with it
		float peekGoal = _target.PlayerInput.Lean;
		if (Mathf.Abs(peekGoal) > 0.01f)
		{
			var space = GetWorld3D().DirectSpaceState;
			Vector3 side = right.Normalized() * Mathf.Sign(peekGoal);
			var q = PhysicsRayQueryParameters3D.Create(GlobalPosition, GlobalPosition + side * (PeekReach + 0.2f), 1u, new Godot.Collections.Array<Rid> { _target.GetRid() });
			var hit = space.IntersectRay(q);
			if (hit.Count > 0)
			{
				float room = Mathf.Max(0f, GlobalPosition.DistanceTo((Vector3)hit["position"]) - 0.2f);
				peekGoal *= Mathf.Clamp(room / PeekReach, 0f, 1f);
			}
		}
		_peek = Mathf.Lerp(_peek, peekGoal, 1f - Mathf.Exp(-6f * dt));
		Rotation = Rotation with { Z = RollSwim + _lean - _peek * PeekRoll };
		Vector3 local = GlobalBasis.Inverse() * (_lag + right.Normalized() * _peek * PeekReach);
		Camera.Position = new Vector3(
			sway * BobSway * _bobAmount,
			-dip * BobHeight * _bobAmount,
			0f) + local + Shake;
	}

	private void UpdateThirdPerson(float dt)
	{
		// Follow: tight horizontally, softer vertically.
		Vector3 goal = PivotPosition();
		Vector3 pos = GlobalPosition;
		float h = 1f - Mathf.Exp(-FollowSharpness * dt);
		float v = 1f - Mathf.Exp(-VerticalFollowSharpness * dt);
		pos.X = Mathf.Lerp(pos.X, goal.X, h);
		pos.Z = Mathf.Lerp(pos.Z, goal.Z, h);
		pos.Y = Mathf.Lerp(pos.Y, goal.Y, v);
		GlobalPosition = pos;
		ApplyRotation();

		// Collision: the arm reports how far it can extend this frame.
		float desired = Mathf.Clamp(GameSettings.Instance.CameraDistance, MinDistance, MaxDistance);
		_arm.SpringLength = desired;
		float allowed = Mathf.Min(desired, _arm.GetHitLength());
		if (allowed < _currentLength) _currentLength = allowed;
		else _currentLength = Mathf.Lerp(_currentLength, allowed, 1f - Mathf.Exp(-ZoomOutSharpness * dt));
		Camera.Position = _arm.Position + new Vector3(0, 0, _currentLength);
	}

	private Vector3 PivotPosition() =>
		_target.GlobalPosition + new Vector3(0, IsFirstPerson ? EyeHeight : PivotHeight, 0);

	private void ApplyRotation()
	{
		Rotation = new Vector3(Pitch + PitchSwim, Yaw, RollSwim);
		_arm.Position = new Vector3(IsFirstPerson ? 0f : ShoulderOffset, 0, 0);
	}
}

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
	[Export] public float FirstPersonMinPitch = -80f;
	[Export] public float FirstPersonMaxPitch = 80f;
	[Export] public float EyeVerticalSharpness = 18f;   // smooths stairs without feeling floaty
	[Export] public float BobHeight = 0.022f;
	[Export] public float BobSway = 0.012f;

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
		Yaw = yaw;
		GlobalPosition = PivotPosition();
		ApplyRotation();
	}

	private void ApplyMode(CameraMode mode)
	{
		if (mode == _mode) return;
		_mode = mode;
		bool fp = mode == CameraMode.FirstPerson;
		_baseFov = fp ? FirstPersonFov : ThirdPersonFov;
		Camera.Fov = _baseFov;
		Camera.Position = Vector3.Zero;
		Pitch = fp ? 0f : Mathf.DegToRad(-12f);
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
		float fovGoal = _baseFov * (_target.PlayerInput.Focus ? FocusFovScale : 1f);
		Camera.Fov = Mathf.Lerp(Camera.Fov, fovGoal, 1f - Mathf.Exp(-FocusSharpness * dt));

		Vector2 look = _target.PlayerInput.ConsumeLook() * (Camera.Fov / _baseFov);
		float minPitch = IsFirstPerson ? FirstPersonMinPitch : MinPitch;
		float maxPitch = IsFirstPerson ? FirstPersonMaxPitch : MaxPitch;
		Yaw = Mathf.Wrap(Yaw + look.X, -Mathf.Pi, Mathf.Pi);
		Pitch = Mathf.Clamp(Pitch + look.Y, Mathf.DegToRad(minPitch), Mathf.DegToRad(maxPitch));

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

		// Head bob: one vertical dip per footstep, one sway per stride pair.
		float speed = _target.IsOnFloor() ? _target.GroundSpeed : 0f;
		float stride = _target.IsRunning ? 1.15f : 0.72f;   // matches PlayerFootsteps
		_bobPhase += speed / stride * Mathf.Pi * dt;
		_bobAmount = Mathf.Lerp(_bobAmount, Mathf.Clamp(speed / 2f, 0f, 1.4f), 1f - Mathf.Exp(-6f * dt));
		Camera.Position = new Vector3(
			Mathf.Sin(_bobPhase) * BobSway * _bobAmount,
			-Mathf.Abs(Mathf.Cos(_bobPhase)) * BobHeight * _bobAmount,
			0f);
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
		Rotation = new Vector3(Pitch, Yaw, 0);
		_arm.Position = new Vector3(IsFirstPerson ? 0f : ShoulderOffset, 0, 0);
	}
}

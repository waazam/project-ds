using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Player;

/// <summary>
/// Over-the-shoulder orbit camera. The rig is top-level: it follows a pivot
/// above the player with smoothing and yaws/pitches from PlayerInput. A
/// SpringArm3D probes for walls, and the camera pulls in instantly on a hit and
/// eases back out, so it never clips but never snaps outward either.
/// </summary>
public partial class PlayerCameraRig : Node3D
{
	[Export] public NodePath TargetPath = "..";
	[Export] public NodePath SpringArmPath = "SpringArm";
	[Export] public NodePath CameraPath = "Camera";
	[Export] public float PivotHeight = 1.5f;
	[Export] public float ShoulderOffset = 0.38f;
	[Export] public float MinDistance = 1.6f;
	[Export] public float MaxDistance = 5.5f;
	[Export] public float MinPitch = -65f;
	[Export] public float MaxPitch = 50f;
	[Export] public float FollowSharpness = 14f;
	[Export] public float VerticalFollowSharpness = 7f;   // softer, smooths stairs and bumps
	[Export] public float ZoomOutSharpness = 4f;

	public float Yaw { get; private set; }
	public float Pitch { get; private set; } = Mathf.DegToRad(-12f);
	public Basis YawBasis => new Basis(Vector3.Up, Yaw);
	public Camera3D Camera { get; private set; }

	private PlayerController _target;
	private SpringArm3D _arm;
	private float _currentLength;

	public override void _Ready()
	{
		TopLevel = true;
		_target = GetNode<PlayerController>(TargetPath);
		_arm = GetNode<SpringArm3D>(SpringArmPath);
		Camera = GetNode<Camera3D>(CameraPath);
		_arm.AddExcludedObject(_target.GetRid());
		GlobalPosition = PivotPosition();
		_currentLength = GameSettings.Instance.CameraDistance;
		ApplyRotation();
	}

	public void SnapBehind(float yaw)
	{
		Yaw = yaw;
		GlobalPosition = PivotPosition();
		ApplyRotation();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is InputEventMouseButton mb && mb.Pressed && Input.MouseMode == Input.MouseModeEnum.Captured)
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
		Vector2 look = _target.PlayerInput.ConsumeLook();
		Yaw = Mathf.Wrap(Yaw + look.X, -Mathf.Pi, Mathf.Pi);
		Pitch = Mathf.Clamp(Pitch + look.Y, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));

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

	private Vector3 PivotPosition() => _target.GlobalPosition + new Vector3(0, PivotHeight, 0);

	private void ApplyRotation()
	{
		Rotation = new Vector3(Pitch, Yaw, 0);
		_arm.Position = new Vector3(ShoulderOffset, 0, 0);
	}
}

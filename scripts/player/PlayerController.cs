using Godot;

namespace ProjectDS.Player;

/// <summary>
/// Player body: camera-relative movement, gravity, and turning the visual
/// toward the direction of travel. Input, camera, and audio live in sibling
/// components. The body only moves.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	[Export] public float WalkSpeed = 2.7f;
	[Export] public float RunSpeed = 5.4f;
	[Export] public float Acceleration = 9f;
	[Export] public float Deceleration = 12f;
	[Export] public float TurnSpeed = 10f;          // how fast the visual faces travel direction
	[Export] public float GravityScale = 1.6f;
	[Export] public NodePath VisualPath = "Visual";
	[Export] public NodePath InputPath = "Input";
	[Export] public NodePath CameraRigPath = "CameraRig";
	[Export] public NodePath InventoryPath = "Inventory";
	[Export] public NodePath FootstepsPath = "Footsteps";
	[Export] public NodePath InteractionPath = "Interaction";
	[Export] public NodePath StaminaPath = "Stamina";

	public PlayerInput PlayerInput { get; private set; }
	public PlayerCameraRig CameraRig { get; private set; }
	public Node3D Visual { get; private set; }
	// The optional components, resolved from their exported paths on first use (so a reader in
	// another node's _Ready never depends on ready order). Null if the scene has none.
	public PlayerInventory Inventory => _inventory ??= GetNodeOrNull<PlayerInventory>(InventoryPath);
	public PlayerFootsteps Footsteps => _footsteps ??= GetNodeOrNull<PlayerFootsteps>(FootstepsPath);
	public PlayerInteraction Interaction => _interaction ??= GetNodeOrNull<PlayerInteraction>(InteractionPath);
	public PlayerStamina Stamina => _stamina ??= GetNodeOrNull<PlayerStamina>(StaminaPath);

	/// <summary>Horizontal speed in m/s.</summary>
	public float GroundSpeed => new Vector2(Velocity.X, Velocity.Z).Length();
	public bool IsRunning { get; private set; }

	private float _gravity;
	private float _bobTime;
	private Vector3 _visualBase;
	private PlayerInventory _inventory;
	private PlayerFootsteps _footsteps;
	private PlayerInteraction _interaction;
	private PlayerStamina _stamina;

	public override void _Ready()
	{
		AddToGroup("player");
		PlayerInput = GetNode<PlayerInput>(InputPath);
		CameraRig = GetNode<PlayerCameraRig>(CameraRigPath);
		Visual = GetNode<Node3D>(VisualPath);
		_visualBase = Visual.Position;
		_gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity") * GravityScale;
		FloorSnapLength = 0.45f;
		FloorMaxAngle = Mathf.DegToRad(48f);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		var v = Velocity;

		if (!IsOnFloor()) v.Y -= _gravity * dt;
		else if (v.Y < 0f) v.Y = 0f;

		// Camera-relative direction, flattened onto the ground plane.
		Vector2 move = PlayerInput.Move;
		Basis cam = CameraRig.YawBasis;
		Vector3 forward = -cam.Z; forward.Y = 0; forward = forward.Normalized();
		Vector3 right = cam.X; right.Y = 0; right = right.Normalized();
		Vector3 wish = right * move.X + forward * move.Y;

		IsRunning = PlayerInput.Run && move.LengthSquared() > 0.04f && (Stamina?.CanRun ?? true);
		float targetSpeed = (IsRunning ? RunSpeed : WalkSpeed) * move.Length();
		Vector3 targetVel = wish.LengthSquared() > 0.0001f ? wish.Normalized() * targetSpeed : Vector3.Zero;

		var horizontal = new Vector3(v.X, 0, v.Z);
		float rate = targetVel.LengthSquared() > horizontal.LengthSquared() ? Acceleration : Deceleration;
		horizontal = horizontal.MoveToward(targetVel, rate * dt);
		v.X = horizontal.X;
		v.Z = horizontal.Z;

		Velocity = v;
		MoveAndSlide();

		UpdateVisual(wish, dt);
	}

	private void UpdateVisual(Vector3 wish, float dt)
	{
		// First person: the body faces where you look. Third person: it faces travel.
		bool faceLook = CameraRig.IsFirstPerson;
		if (faceLook || wish.LengthSquared() > 0.01f)
		{
			float targetYaw = faceLook ? CameraRig.Yaw : Mathf.Atan2(-wish.X, -wish.Z);
			var rot = Visual.Rotation;
			rot.Y = Mathf.LerpAngle(rot.Y, targetYaw, 1f - Mathf.Exp(-TurnSpeed * dt));
			Visual.Rotation = rot;
		}

		// Placeholder life: a small step bob so movement reads before real animation exists.
		float speed = GroundSpeed;
		_bobTime += dt * speed * 3.2f;
		float bob = Mathf.Abs(Mathf.Sin(_bobTime)) * Mathf.Min(speed, 5f) * 0.012f;
		Visual.Position = _visualBase + new Vector3(0, bob, 0);
	}

	/// <summary>Move the player instantly (spawn, respawn, debug).</summary>
	public void Teleport(Vector3 position, float yaw)
	{
		GlobalPosition = position;
		Velocity = Vector3.Zero;
		Visual.Rotation = new Vector3(0, yaw, 0);
		CameraRig.SnapBehind(yaw);
	}
}

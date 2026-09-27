using Godot;

namespace ProjectDS.Player;

/// <summary>
/// Player body: camera-relative movement, gravity, and turning the visual
/// toward the direction of travel. Input, camera, and audio live in sibling
/// components. The body only moves.
///
/// It moves like a body with weight (the owner's movement overhaul): speed builds up over a moment
/// rather than snapping on, and carries on a little when the keys are let go (so a corner can't be
/// "pixel-peeked" in and out of in an instant). Walking backwards is slower (60%) and strafing a little
/// slower (80%), and both gather speed more slowly than walking forward. Changing direction bends the
/// momentum round rather than reversing it at once. The automated tests keep the old, snappy response.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	[Export] public float WalkSpeed = 2.7f;
	[Export] public float RunSpeed = 5.4f;
	/// <summary>0..1 drag on the player's speed from wading (Act 17's sewer water sets it; 1 = none).</summary>
	public float WadeScale { get; set; } = 1f;
	/// <summary>How quickly speed builds toward the target (an exponential rate per second: 3.5 reaches
	/// about 70% in a third of a second, all of it in about a second).</summary>
	[Export] public float Acceleration = 3.5f;
	/// <summary>How quickly speed dies away with the keys let go (the momentum carried).</summary>
	[Export] public float Friction = 5f;
	/// <summary>Walking backwards and strafing: their top speeds, and how much more slowly they build.</summary>
	[Export] public float BackwardScale = 0.6f, StrafeScale = 0.8f, SideAccelScale = 0.75f;
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
	/// <summary>This tick's horizontal change of velocity (m/s²): the camera leans its head against it.</summary>
	public Vector3 Acceleration3 { get; private set; }

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

		// running is forward only: backing away or sidling is never at a run
		IsRunning = PlayerInput.Run && move.Y > 0.3f && (Stamina?.CanRun ?? true);
		// the direction's cap: full forward, 60% backward, 80% sideways, blended for diagonals
		float fw = Mathf.Max(move.Y, 0f), bk = Mathf.Max(-move.Y, 0f), sd = Mathf.Abs(move.X), sum = fw + bk + sd;
		float dirScale = sum > 0.001f ? (fw + bk * BackwardScale + sd * StrafeScale) / sum : 1f;
		float accelScale = sum > 0.001f ? (fw + (bk + sd) * SideAccelScale) / sum : 1f;
		// holding still to work something (a held interaction) plants the feet
		bool planted = Interaction?.Holding ?? false;
		float targetSpeed = planted ? 0f : (IsRunning ? RunSpeed : WalkSpeed) * move.Length() * dirScale * WadeScale;
		Vector3 targetVel = wish.LengthSquared() > 0.0001f ? wish.Normalized() * targetSpeed : Vector3.Zero;

		var horizontal = new Vector3(v.X, 0, v.Z);
		bool tests = Systems.GameSettings.Instance?.AutoTest ?? false;
		if (tests)
		{
			// the tests steer by the frame: they keep the old, snappy response
			float rate = targetVel.LengthSquared() > horizontal.LengthSquared() ? 9f : 12f;
			horizontal = horizontal.MoveToward(targetVel, rate * dt);
		}
		else
		{
			// weight: speed eases toward the target (building up, or dying away with the keys let go)
			float rate = targetVel.LengthSquared() > 0.0001f ? Acceleration * accelScale : Friction;
			horizontal = horizontal.Lerp(targetVel, 1f - Mathf.Exp(-rate * dt));
		}
		Acceleration3 = (horizontal - new Vector3(v.X, 0, v.Z)) / Mathf.Max(dt, 1e-4f);
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

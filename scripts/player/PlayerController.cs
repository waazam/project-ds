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
///
/// Crouching (C / left Ctrl / right stick, a toggle): the body folds to 1.1 m, so it goes under what a standing
/// body can't; the eye drops 0.6 m, eased over a quarter of a second (gently: never a lurch); it walks at half
/// pace with shorter, quieter steps and can't run. Standing back up waits for headroom (under something low it
/// stays down, with a soft knock overhead); pressing run while crouched stands up if there's room. Stealth reads
/// <see cref="Crouching"/>, <see cref="CrouchAmount"/> and <see cref="HeadWorld"/> (a watcher's line of sight to the
/// head, not the feet).
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	[Export] public float WalkSpeed = 2.7f;
	[Export] public float RunSpeed = 5.4f;
	/// <summary>Crouched: the pace (of walking), the body's height, and how far the eye drops.</summary>
	[Export] public float CrouchSpeedScale = 0.5f;
	[Export] public float StandHeight = 1.75f, CrouchHeight = 1.1f, CrouchEyeDrop = 0.6f;
	/// <summary>How long the body takes to go down or come up (seconds).</summary>
	[Export] public float CrouchTime = 0.25f;
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
	/// <summary>Crouched (the toggle's state; the body may still be folding toward it: see <see cref="CrouchAmount"/>).</summary>
	public bool Crouching { get; private set; }
	/// <summary>0 standing .. 1 fully crouched, eased.</summary>
	public float CrouchAmount { get; private set; }
	/// <summary>The top of the head, now (for line of sight: under a table or behind a low wall, it's this that shows).</summary>
	public Vector3 HeadWorld => GlobalPosition + Vector3.Up * Mathf.Lerp(StandHeight, CrouchHeight, CrouchAmount);
	/// <summary>Times it tried to stand up and hit its head (for tests).</summary>
	public int StandBlocked { get; private set; }
	/// <summary>This tick's horizontal change of velocity (m/s²): the camera leans its head against it.</summary>
	public Vector3 Acceleration3 { get; private set; }

	private float _gravity;
	private float _bobTime;
	private Vector3 _visualBase;
	private PlayerInventory _inventory;
	private PlayerFootsteps _footsteps;
	private PlayerInteraction _interaction;
	private PlayerStamina _stamina;
	private CollisionShape3D _collision;
	private CapsuleShape3D _capsule;
	private float _crouchT;   // 0..1 linear, eased into CrouchAmount
	private double _knockAt;

	public override void _Ready()
	{
		AddToGroup("player");
		AddChild(new AirParticles { Name = "Air" });   // dust, spores, breath (the fidelity pass)
		PlayerInput = GetNode<PlayerInput>(InputPath);
		CameraRig = GetNode<PlayerCameraRig>(CameraRigPath);
		Visual = GetNode<Node3D>(VisualPath);
		_visualBase = Visual.Position;
		_gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity") * GravityScale;
		FloorSnapLength = 0.45f;
		FloorMaxAngle = Mathf.DegToRad(48f);
		// our own copy of the capsule (the scene's is shared by every load of it), resized as the body folds
		_collision = GetNodeOrNull<CollisionShape3D>("Collision");
		if (_collision?.Shape is CapsuleShape3D cap)
		{
			_capsule = (CapsuleShape3D)cap.Duplicate();
			_collision.Shape = _capsule;
			StandHeight = _capsule.Height;
		}
	}

	/// <summary>Crouch or stand (the toggle). Standing needs room overhead; returns whether the body is now crouched.</summary>
	public bool SetCrouch(bool crouch)
	{
		if (crouch == Crouching) return Crouching;
		if (!crouch && !RoomToStand()) { StandBlocked++; KnockHead(); return true; }
		Crouching = crouch;
		Audio.AudioDirector.OneShot(this, "cloth", 4, null, crouch ? -20f : -22f, "Player", 3f, 0.08f);
		return Crouching;
	}

	/// <summary>Is there room for the whole standing body where it is? (A standing capsule, lifted a hair off the
	/// floor, against the world; the player's own body left out.)</summary>
	public bool RoomToStand()
	{
		if (_capsule == null || !IsInsideTree()) return true;
		var probe = new CapsuleShape3D { Radius = _capsule.Radius - 0.02f, Height = StandHeight - 0.06f };
		var q = new PhysicsShapeQueryParameters3D
		{
			Shape = probe, CollisionMask = CollisionMask, CollideWithBodies = true, CollideWithAreas = false,
			Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * (StandHeight * 0.5f + 0.04f)),
			Exclude = new Godot.Collections.Array<Rid> { GetRid() },
		};
		return GetWorld3D().DirectSpaceState.IntersectShape(q, 1).Count == 0;
	}

	private void KnockHead()
	{
		// a soft bump overhead (not on every press held against it)
		double now = Time.GetTicksMsec() / 1000.0;
		if (now < _knockAt) return;
		_knockAt = now + 0.6;
		Audio.AudioDirector.OneShot(this, "wood_bump", 1, HeadWorld, -16f, "Player", 3f, 0.06f);
	}

	/// <summary>Folds the body toward the toggle's state: the capsule shrinks from the top (its foot stays on the
	/// floor), eased so the eye settles rather than drops.</summary>
	private void UpdateCrouch(float dt)
	{
		bool tests = Systems.GameSettings.Instance?.AutoTest ?? false;
		float step = dt / (tests ? 0.1f : Mathf.Max(CrouchTime, 0.01f));
		_crouchT = Mathf.MoveToward(_crouchT, Crouching ? 1f : 0f, step);
		CrouchAmount = _crouchT * _crouchT * (3f - 2f * _crouchT);
		if (_capsule == null) return;
		float h = Mathf.Lerp(StandHeight, CrouchHeight, CrouchAmount);
		if (!Mathf.IsEqualApprox(_capsule.Height, h))
		{
			_capsule.Height = h;
			_collision.Position = new Vector3(0, h * 0.5f, 0);
		}
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

		// crouch: the toggle; running from a crouch stands up first (if there's room)
		if (PlayerInput.CrouchPressed) SetCrouch(!Crouching);
		else if (Crouching && PlayerInput.Run && move.Y > 0.3f && _crouchT >= 1f) SetCrouch(false);
		UpdateCrouch(dt);

		// running is forward only: backing away or sidling is never at a run (nor crouched)
		IsRunning = PlayerInput.Run && move.Y > 0.3f && (Stamina?.CanRun ?? true) && !Crouching && CrouchAmount < 0.2f;
		// the direction's cap: full forward, 60% backward, 80% sideways, blended for diagonals
		float fw = Mathf.Max(move.Y, 0f), bk = Mathf.Max(-move.Y, 0f), sd = Mathf.Abs(move.X), sum = fw + bk + sd;
		float dirScale = sum > 0.001f ? (fw + bk * BackwardScale + sd * StrafeScale) / sum : 1f;
		float accelScale = sum > 0.001f ? (fw + (bk + sd) * SideAccelScale) / sum : 1f;
		// holding still to work something (a held interaction) plants the feet
		bool planted = Interaction?.Holding ?? false;
		float pace = IsRunning ? RunSpeed : WalkSpeed * Mathf.Lerp(1f, CrouchSpeedScale, CrouchAmount);
		float targetSpeed = planted ? 0f : pace * move.Length() * dirScale * WadeScale;
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
		// crouched, the body (seen in third person, and in its shadow) folds down with it
		Visual.Scale = new Vector3(1f, Mathf.Lerp(1f, CrouchHeight / StandHeight, CrouchAmount), 1f);
	}

	/// <summary>Move the player instantly (spawn, respawn, debug).</summary>
	public void Teleport(Vector3 position, float yaw)
	{
		GlobalPosition = position;
		Velocity = Vector3.Zero;
		// a move (a respawn, a scene's start) stands the body up, where there's room
		if (Crouching && RoomToStand()) { Crouching = false; _crouchT = 0f; UpdateCrouch(0f); }
		Visual.Rotation = new Vector3(0, yaw, 0);
		CameraRig.SnapBehind(yaw);
	}
}

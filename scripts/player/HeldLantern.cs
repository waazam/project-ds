using Godot;
using ProjectDS.World;

namespace ProjectDS.Player;

/// <summary>
/// The lantern in your hand, seen (the owner, 2026-10-07: "a held lantern we can actually see"; the compass and the
/// lighter are to follow in the same way, see <see cref="HeldItem"/>). Low in the left hand, a gloved fist round its bail,
/// the light of the lantern coming from its glass:
/// <list type="bullet">
/// <item>it hangs from the bail and swings: a damped pendulum (pitch and roll) pushed by how you move: starting off
/// tips it back, stopping swings it on, turning swings it out to the side; it settles slowly, the way a real one does;</item>
/// <item>it bobs with your stride (up and down twice a cycle, a little side to side once), less crouched;</item>
/// <item>lit, it's raised into view; put out (or with the camera up to your eye), it's lowered out of it;</item>
/// <item>gathering its beam (right mouse), it's lifted and brought in toward the middle, held out ahead;</item>
/// <item>the flame flickers with the lantern's own light; with the blacklight on, the glass glows violet instead.</item>
/// </list>
/// All the motion is eased and gentle (the owner's motion comfort): nothing jolts the view, only the lantern moves.
/// </summary>
public partial class HeldLantern : HeldItem
{
	// (2026-10-12, the owner's reference shots: the arm out ahead, the fist up at the top of a tall wire bail, the lantern
	// hanging well down under it; was the fist right down on its cap, clamping it)
	// (2026-10-13, the owner: "the lantern should be higher up on the screen, the handle is too long": a hurricane lantern's own
	// bail, the fist just over its cap, and the whole lantern up in the lower left of the view instead of hanging off its bottom edge)
	// (2026-10-14g, the owner: "the arm itself is too long and unrealistic": the fist was 64 cm out from the eye and nearly at
	// its height, the arm stretched straight out past its own length from a shoulder 25 cm below the eye. Held as a man
	// holds a lantern up to see by: the elbow bent, the fist about half a metre out, low in the left of the view, the
	// lantern's foot just under the view's edge; only the forearm in view, coming in from the lower left corner)
	protected override Vector3 Rest => new(-0.22f, -0.085f, -0.48f);
	protected override Vector3 Lowered => new(-0.27f, -0.8f, -0.4f);
	/// <summary>The beam gathered: lifted, in toward the middle, out ahead (the arm reaching, not straight).</summary>
	private static readonly Vector3 Gathered = new(-0.12f, -0.06f, -0.54f);   // (2026-10-14g: nearer, the arm's own reach; was (-0.14, -0.02, -0.68))
	/// <summary>The bail standing up over the cap, and how far under the fist the lantern hangs on it.</summary>
	private const float BailRise = 0.09f, ModelScale = 0.95f;   // (2026-10-13: 0.09, a normal bail; was 0.22, the lantern hung well down under the fist)
	private static float HangDrop => (0.232f + BailRise) * ModelScale;
	/// <summary>Thrust up at something in front of them (Act 23's end: held up into the wendigo's face): high, in the middle,
	/// at arm's length.</summary>
	private static readonly Vector3 ThrustPose = new(-0.04f, 0.06f, -0.5f);   // (2026-10-14g: within the arm's reach; was y 0.1)
	/// <summary>0..1: how far it's thrust up (set by a cutscene, eased by it).</summary>
	public float Thrust { get; set; }

	private Lantern _lantern;
	private OmniLight3D _fill;
	private static StandardMaterial3D _amber;
	private static StandardMaterial3D AmberGlass => _amber ??= new StandardMaterial3D
	{
		ResourceName = "held_lantern_glass", AlbedoColor = new Color(1f, 0.72f, 0.4f, 0.4f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		EmissionEnabled = true, Emission = new Color(1f, 0.52f, 0.2f), EmissionEnergyMultiplier = 0.9f, Roughness = 0.15f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};
	/// <summary>The wick smouldering (the flame dead, Acts 14-20): a dark coal glowing red, not a flame.</summary>
	private static StandardMaterial3D _ember;
	private static StandardMaterial3D EmberMat => _ember ??= new StandardMaterial3D
	{
		ResourceName = "held_lantern_ember", AlbedoColor = new Color(0.35f, 0.06f, 0.02f), EmissionEnabled = true, Emission = new Color(1f, 0.26f, 0.05f),
		EmissionEnergyMultiplier = 1f, Roughness = 1f,
	};
	private (MeshInstance3D mi, int surface) _flameSlot;
	private bool _emberShown;
	private Node3D _swing, _model;
	private MeshInstance3D _glass;
	private StandardMaterial3D _uvGlass;
	private Vector2 _ang, _angVel;   // the pendulum: x pitch (fore and aft), y roll (side to side)
	private Vector3 _lastVel, _accSm;
	private float _lastYaw;

	/// <summary>Where its flame is (the lantern's light comes from here while it's held up).</summary>
	public Vector3 FlameWorld => _model != null ? _model.ToGlobal(new Vector3(0, 0.12f, 0)) : GlobalPosition;
	/// <summary>For tests: how far it's swinging now (radians), and whether it's up in view.</summary>
	public float SwingAngle => _ang.Length();
	public bool Up => Raise > 0.9f;

	protected override void Build()
	{
		_lantern = Player.GetNodeOrNull<Lantern>("Lantern");
		// the pivot at the fist on the bail; the lantern hangs below it and swings about it
		_swing = new Node3D { Name = "Swing" };
		AddChild(_swing);
		_model = new Node3D { Name = "Model", Position = new Vector3(0, -HangDrop, 0), Scale = Vector3.One * ModelScale };
		_swing.AddChild(_model);
		ItemMeshes.LanternBailRise = BailRise;
		ItemMeshes.LanternBailGrip = true;   // (2026-10-13d: a wooden grip on the bail, the fist round it)
		ItemMeshes.Built built;
		try { built = ItemMeshes.Build(ToolKind.Lantern, _model); }
		finally { ItemMeshes.LanternBailRise = 0.1f; ItemMeshes.LanternBailGrip = false; }
		if (built.Glow != null) { built.Glow.QueueFree(); }   // (the lantern's own light is the real one)
		foreach (var c in _model.GetChildren())
			if (c is MeshInstance3D mi && mi.Mesh != null)
			{
				_glass ??= mi;
				// its globe: warm amber glass, glowing with the flame inside (the lamp glass's own white, lit from right
				// against it, blew out)
				for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
				{
					if (mi.Mesh.SurfaceGetMaterial(i) == ItemTextures.LampGlassMat) mi.SetSurfaceOverrideMaterial(i, AmberGlass);
					else if (mi.Mesh.SurfaceGetMaterial(i) == ItemTextures.FlameMat) { _flameSlot = (mi, i); }
				}
			}
		// the blacklight's glow in the glass: a violet bulb where the flame is
		_uvGlass = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.15f, 1f), EmissionEnabled = true, Emission = new Color(0.45f, 0.18f, 1f), EmissionEnergyMultiplier = 2.2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		_model.AddChild(new MeshInstance3D { Name = "UvBulb", Mesh = new SphereMesh { Radius = 0.022f, Height = 0.05f, RadialSegments = 8, Rings = 4 }, MaterialOverride = _uvGlass, Position = new Vector3(0, 0.12f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		// the hand: over the bail's top, holding it from above (the owner, 2026-10-11: "the player's hand is holding it from
		// the top instead of this stick thing it has now")
		BuildBailGrip(this);
		// lit by its own soft light only (the lantern's real light, an inch from the glass, blew it all to white): the
		// held things on their own layer, out of the lantern's light, under a warm fill from the flame's place
		SetLayer(this);
		// (2026-10-12, the owner's suggestion A, from the reference shot: the flame's warm light up the underside of the glove
		// and along the sleeve, the arm lit from below by what it carries; reaching up the longer bail to the fist and on)
		_fill = new OmniLight3D { Name = "HandLight", LightColor = new Color(1f, 0.62f, 0.32f), LightEnergy = 1.1f, OmniRange = 1.15f, OmniAttenuation = 1.5f, ShadowEnabled = false, LightCullMask = HeldLayer, LightSpecular = 0.3f };
		_model.AddChild(_fill);
		_fill.Position = new Vector3(0.02f, 0.14f, 0.03f);
		_lastYaw = Player.CameraRig?.Yaw ?? 0f;
	}

	// (2026-10-12) only while it gives light: with the flame dead and the blacklight off it showed a lit flame in the hand that
	// lit nothing (the owner, Act 17's pipe: "your lantern literally doesnt light up anything"); dark, it's lowered
	// (2026-10-13j) and hung at the belt while both hands are on the flamethrower's wand
	protected override bool Wanted => _lantern != null && _lantern.IsOn && (_lantern.Shining || _lantern.Dying) && !CameraUp && !PlayerHands.BothHandsOnWand(Player);

	/// <summary>The left hand round the top of the bail, from above (the pivot is the bail's top, its wire along X): the
	/// back of the gloved hand up, four fingers curled over the wire and under it, the thumb round it from behind; the cuff,
	/// and the jacket's sleeve reaching back from the wrist toward the body, out of the view's lower left corner.</summary>
	private static void BuildBailGrip(Node3D parent)
	{
		if (BuildArm(parent, HandSide.Left, HandPose.Handle, Vector3.Zero) != null) return;   // (the modelled one: one skin, no seams)
		var k = new MeshKit();
		k.Mat(new StandardMaterial3D { ResourceName = "glove", AlbedoColor = new Color(0.09f, 0.075f, 0.065f), Roughness = 0.85f, MetallicSpecular = 0.15f });
		k.Color = Colors.White;
		// the back of the hand, sloping up and back from the knuckles
		k.Blob(new Vector3(-0.004f, 0.03f, 0.03f), new Vector3(0.036f, 0.016f, 0.036f), 4201, 0.08f, true, 1f);
		k.Blob(new Vector3(-0.002f, 0.022f, 0.004f), new Vector3(0.038f, 0.017f, 0.016f), 4202, 0.08f, true, 1f);   // the knuckles' ridge
		// four fingers over the wire: down its front and in under it
		for (int i = 0; i < 4; i++)
		{
			float x = -0.027f + i * 0.018f;
			float len = i == 0 ? 0.85f : i == 3 ? 0.9f : 1f;   // (the little finger and the first a touch short)
			var a = new Vector3(x, 0.02f, -0.004f);
			var b = new Vector3(x, 0.012f, -0.022f * len);
			var c = new Vector3(x, -0.008f, -0.016f * len);
			var d = new Vector3(x, -0.012f, 0.002f);
			k.Cylinder(a, b, 0.0088f, 0.0082f, 7, true);
			k.Cylinder(b, c, 0.0082f, 0.0076f, 7, true);
			k.Cylinder(c, d, 0.0076f, 0.007f, 7, true);
			k.Blob(b, Vector3.One * 0.0084f, 4210 + i, 0.02f, true, 1f);
			k.Blob(c, Vector3.One * 0.0078f, 4220 + i, 0.02f, true, 1f);
		}
		// the thumb, round the wire from behind on the inside
		k.Cylinder(new Vector3(0.03f, 0.022f, 0.03f), new Vector3(0.034f, 0.006f, 0.012f), 0.0105f, 0.0095f, 7, true);
		k.Cylinder(new Vector3(0.034f, 0.006f, 0.012f), new Vector3(0.03f, -0.006f, -0.002f), 0.0095f, 0.0085f, 7, true);
		// the wrist and the glove's cuff
		k.Cylinder(new Vector3(-0.004f, 0.034f, 0.05f), new Vector3(-0.012f, 0.026f, 0.1f), 0.026f, 0.03f, 10, true);
		k.Cylinder(new Vector3(-0.012f, 0.026f, 0.092f), new Vector3(-0.016f, 0.022f, 0.12f), 0.033f, 0.034f, 10, true);
		// the sleeve: the jacket's, reaching back from the cuff to the elbow out of view
		k.Mat(new StandardMaterial3D { ResourceName = "sleeve", AlbedoColor = JacketSleeve, Roughness = 1f, MetallicSpecular = 0f });
		// (up from the view's lower left corner to the hand: run straight back, it lay across the view like a log)
		k.Cylinder(new Vector3(-0.014f, 0.024f, 0.105f), new Vector3(-0.036f, -0.06f, 0.2f), 0.031f, 0.035f, 10, true);
		k.Cylinder(new Vector3(-0.036f, -0.06f, 0.2f), new Vector3(-0.07f, -0.22f, 0.34f), 0.035f, 0.04f, 10, true);
		k.Blob(new Vector3(-0.014f, 0.024f, 0.108f), new Vector3(0.037f, 0.036f, 0.012f), 4230, 0.1f, true, 1f);   // (the cuff's gather)
		var mi = k.CommitTo(parent, "Hand", false);
		mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		mi.Scale = Vector3.One * 1.15f;   // (a gloved hand: a little fuller than bare)
	}

	protected override void Animate(float dt)
	{
		if (_lantern == null) return;
		// where it's held: at rest, or lifted in toward the middle with the beam gathered
		float gather = _lantern.Blacklight ? 0f : _lantern.FocusAmount;
		Hold = Rest.Lerp(Gathered, Mathf.SmoothStep(0f, 1f, gather));
		if (Thrust > 0f) Hold = Hold.Lerp(ThrustPose, Mathf.SmoothStep(0f, 1f, Thrust));
		// knocked from the hand: gone at once (it's flying across the hall, the world's own lantern now)
		_model.Visible = !Lantern.Snuffed;
		// the pendulum, pushed by the body's acceleration (in the view's frame) and the turning
		var vel = Player.Velocity;
		var acc = (vel - _lastVel) / Mathf.Max(dt, 1e-4f);
		_lastVel = vel;
		// (2026-10-12: smoothed: the frame-to-frame velocity at a sprint (steps, slopes, the stride's own surge) kicked it
		// into a jitter; it answers to the body's real starts, stops and turns, not its noise)
		_accSm = _accSm.Lerp(acc.LimitLength(30f), 1f - Mathf.Exp(-dt * 6f));
		acc = _accSm;
		var cam = Player.CameraRig?.Camera;
		if (cam != null)
		{
			var local = cam.GlobalBasis.Inverse() * acc;
			float yaw = Player.CameraRig.Yaw;
			float turn = Mathf.AngleDifference(_lastYaw, yaw) / Mathf.Max(dt, 1e-4f);
			_lastYaw = yaw;
			// (gentle: the push is clamped, the swing damped; it never flails)
			var push = new Vector2(Mathf.Clamp(local.Z * 0.045f, -0.6f, 0.6f), Mathf.Clamp(-local.X * 0.045f + turn * 0.12f, -0.8f, 0.8f));
			const float k = 20f, c = 4f;   // (2026-10-13: the shorter bail swings a little quicker; was 16 and 3.6 on the tall one)
			_angVel += (push * 9f - _ang * k - _angVel * c) * dt;
			_ang += _angVel * dt;
			_ang = _ang.Clamp(new Vector2(-0.45f, -0.5f), new Vector2(0.45f, 0.5f));
		}
		_swing.Rotation = new Vector3(_ang.X, 0f, _ang.Y);
		// the wrist giving a little with the lantern's swing (the hand isn't a clamp: it rides the weight)
		if (GetNodeOrNull<Node3D>("Arm") is { } arm) arm.Rotation = new Vector3(_ang.X * 0.1f, 0f, _ang.Y * 0.08f);   // (2026-10-13d: a firm wrist, "more strong": was 0.22 and 0.18)
		// the flame's light from its glass; the violet bulb with the blacklight
		if (_model.GetNodeOrNull<MeshInstance3D>("UvBulb") is { } uv) uv.Visible = _lantern.Blacklight;
		AmberGlass.Emission = _lantern.Blacklight ? new Color(0.4f, 0.15f, 0.9f) : new Color(1f, 0.52f, 0.2f);
		// (2026-10-13, the owner: the flame brighter, the blacklight as it was: the glass glows and the hand is lit a little
		// more by the flame; the blacklight's 0.9 and 1.1 unchanged)
		AmberGlass.EmissionEnergyMultiplier = _lantern.Blacklight ? 0.9f : 1.2f;
		_fill.LightEnergy = _lantern.Blacklight ? 1.1f : 1.3f;
		_fill.LightColor = _lantern.Blacklight ? new Color(0.5f, 0.3f, 1f) : new Color(1f, 0.66f, 0.36f);
		if (_lantern.Ember)
		{
			// (2026-10-14) the flame dead: only the wick's ember, a dull red glow low in the glass, guttering with it
			AmberGlass.Emission = new Color(1f, 0.3f, 0.08f);
			AmberGlass.EmissionEnergyMultiplier = 0.18f * _lantern.EmberLevel;
			_fill.LightEnergy = 0.25f * _lantern.EmberLevel;
			_fill.LightColor = new Color(1f, 0.42f, 0.18f);
			EmberMat.EmissionEnergyMultiplier = 0.25f + 0.6f * _lantern.EmberLevel;
		}
		// the flame in the glass: a low red coal on the wick while it only smoulders
		if (_flameSlot.mi != null && IsInstanceValid(_flameSlot.mi) && _lantern.Ember != _emberShown)
		{
			_emberShown = _lantern.Ember;
			_flameSlot.mi.SetSurfaceOverrideMaterial(_flameSlot.surface, _emberShown ? EmberMat : null);
		}
		_lantern.HeldFlameAt = Raise > 0.05f ? FlameWorld : null;
	}

	protected override void OnHidden() { if (_lantern != null) _lantern.HeldFlameAt = null; }
}

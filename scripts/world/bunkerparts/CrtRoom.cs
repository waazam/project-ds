using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using static ProjectDS.World.BunkerParts.BunkerLayout;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Act 9's room: a grimy concrete hall (Z -90 .. -122) whose back and side
/// walls are steel shelving stacked unevenly with old CRTs, Matrix-style, all
/// showing grainy night surveillance of the woods. One marked set on the
/// console at the back shows the cabin smouldering; "Turn off the screen"
/// (an <see cref="Interactable"/>) is what the story's CRT sequence hangs on.
/// Caged pendant lamps and the screens' own glow light the room.
/// The sequence itself (timing, caption, flag) lives in <see cref="BunkerFlow"/>;
/// this node only builds the room and switches the tubes.
/// </summary>
public partial class CrtRoom : Node3D
{
	private const float MidZ = (CrtRoomFrontZ + CrtRoomBackZ) * 0.5f;
	private const float Depth = CrtRoomFrontZ - CrtRoomBackZ;
	private const float ConsoleZ = CrtRoomBackZ + 1.45f;
	private const float ConsoleTop = 0.95f;

	private static readonly Color GlowCold = new(0.55f, 0.62f, 0.7f);

	private ShaderMaterial _normalMat, _targetMat;
	private Node3D _target;
	private Vector3 _targetTubeLocal;
	private Interactable _switch;
	private OmniLight3D[] _glow;
	private float[] _glowEnergy;
	private OmniLight3D _targetGlow;
	private StaticBody3D _body;

	/// <summary>Every screen is dark (mid-sequence).</summary>
	public bool ScreensOff { get; private set; }
	/// <summary>The screens show the stairs (sequence done, or restored).</summary>
	public bool ShowingStairs { get; private set; }
	/// <summary>The player is within reach of the marked screen (the autotest reads this).</summary>
	public bool PlayerAtTarget { get; private set; }
	/// <summary>The marked set (group "crt_target_marker": the compass points here).</summary>
	public Node3D Target => _target;
	/// <summary>Where the E pick volume sits on the marked screen (aim here to use it).</summary>
	public Vector3 SwitchWorld => _target != null ? _target.ToGlobal(_targetTubeLocal) : ToGlobal(new Vector3(0, 1.25f, ConsoleZ));

	/// <summary>Raised when the player turns the marked screen off.</summary>
	public event System.Action SwitchUsed;

	public override void _Ready()
	{
		_normalMat = BunkerTextures.NewCrtMat(BunkerTextures.SurveillanceAtlas(), true, 0.93f, 0f);
		_normalMat.SetShaderParameter("tint", new Color(0.72f, 0.78f, 0.74f));
		_targetMat = BunkerTextures.NewCrtMat(BunkerTextures.CabinPicture(), false, 0.9f, 1f);
		_body = new StaticBody3D { Name = "CrtRoomBody", CollisionLayer = 1, CollisionMask = 0 };
		_body.SetMeta("surface", "stone");
		AddChild(_body);

		BuildShell();
		BuildShelving();
		BuildConsoleAndTarget();
		BuildDressing();
		BuildLighting();
	}

	// ------------------------------------------------------------------ tube states

	/// <summary>The story's switch-off: every tube collapses to a line and goes dark.</summary>
	public void TurnAllOff()
	{
		ScreensOff = true;
		var tw = CreateTween().SetParallel();
		foreach (var m in new[] { _normalMat, _targetMat })
			tw.TweenProperty(m, "shader_parameter/power", 0f, 0.35f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		foreach (var l in _glow) tw.TweenProperty(l, "light_energy", 0f, 0.35f);
		tw.TweenProperty(_targetGlow, "light_energy", 0f, 0.35f);
	}

	/// <summary>Every tube comes back on in unison showing one picture, fuzzy at first, clearing to the
	/// stairs over <paramref name="clearSeconds"/>.</summary>
	public void TurnOnStairs(float clearSeconds)
	{
		ScreensOff = false;
		ShowingStairs = true;
		var tw = CreateTween().SetParallel();
		foreach (var m in new[] { _normalMat, _targetMat })
		{
			SetStairsPicture(m);
			m.SetShaderParameter("power", 0f);
			m.SetShaderParameter("clarity", 0f);
			tw.TweenProperty(m, "shader_parameter/power", 1f, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tw.TweenProperty(m, "shader_parameter/clarity", 1f, clearSeconds).SetDelay(0.4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		}
		for (int i = 0; i < _glow.Length; i++) tw.TweenProperty(_glow[i], "light_energy", _glowEnergy[i], 0.5f);
		_targetGlow.LightColor = GlowCold;
		tw.TweenProperty(_targetGlow, "light_energy", 0.5f, 0.5f);
	}

	/// <summary>Restore: the sequence already happened; the tubes simply show the stairs.</summary>
	public void SetStairsInstant()
	{
		ScreensOff = false;
		ShowingStairs = true;
		foreach (var m in new[] { _normalMat, _targetMat })
		{
			SetStairsPicture(m);
			m.SetShaderParameter("power", 1f);
			m.SetShaderParameter("clarity", 1f);
		}
		_targetGlow.LightColor = GlowCold;
		_targetGlow.LightEnergy = 0.5f;
		if (_switch != null) _switch.Enabled = false;
	}

	private static void SetStairsPicture(ShaderMaterial m)
	{
		m.SetShaderParameter("picture", BunkerTextures.StairsPicture());
		m.SetShaderParameter("use_atlas", 0f);
		m.SetShaderParameter("fire", 0f);
		m.SetShaderParameter("tint", new Color(0.7f, 0.74f, 0.76f));
	}

	// ------------------------------------------------------------------ building

	private void AddBox(Vector3 center, Vector3 size, Basis? rot = null)
	{
		var shape = new CollisionShape3D { Position = center, Shape = new BoxShape3D { Size = size } };
		if (rot is { } r) shape.Basis = r;
		_body.AddChild(shape);
	}

	private void BuildShell()
	{
		var k = new MeshKit();
		float w = CrtRoomHalfWidth, h = CrtRoomHeight;
		k.Mat(BunkerTextures.RoomWallMat);
		k.Color = Colors.White;
		k.Box(new Vector3(-w - 0.1f, h * 0.5f, MidZ), new Vector3(0.2f, h, Depth));
		k.Box(new Vector3(w + 0.1f, h * 0.5f, MidZ), new Vector3(0.2f, h, Depth));
		k.Box(new Vector3(0, h * 0.5f, CrtRoomBackZ - 0.1f), new Vector3(w * 2f + 0.4f, h, 0.2f));
		// Front wall = the bulkhead the hallway ends in, with the vine door's opening.
		float fz = CrtRoomFrontZ - BulkheadThickness * 0.5f;
		float sideW = w - DoorHalfWidth;
		k.Box(new Vector3(-(DoorHalfWidth + sideW * 0.5f), h * 0.5f, fz), new Vector3(sideW, h, BulkheadThickness));
		k.Box(new Vector3(DoorHalfWidth + sideW * 0.5f, h * 0.5f, fz), new Vector3(sideW, h, BulkheadThickness));
		k.Box(new Vector3(0, DoorHeight + (h - DoorHeight) * 0.5f, fz), new Vector3(DoorHalfWidth * 2f, h - DoorHeight, BulkheadThickness));
		// Ceiling with cast beams, pilasters down the side walls.
		k.Color = new Color(0.8f, 0.8f, 0.8f);
		k.Box(new Vector3(0, h + 0.05f, MidZ), new Vector3(w * 2f, 0.1f, Depth));
		k.Color = new Color(0.92f, 0.92f, 0.9f);
		for (float z = CrtRoomFrontZ - 4f; z > CrtRoomBackZ + 1f; z -= 4f)
		{
			k.Box(new Vector3(0, h - 0.15f, z), new Vector3(w * 2f, 0.3f, 0.35f));
			k.Box(new Vector3(-w + 0.12f, h * 0.5f, z), new Vector3(0.24f, h, 0.35f));
			k.Box(new Vector3(w - 0.12f, h * 0.5f, z), new Vector3(0.24f, h, 0.35f));
		}
		k.CommitTo(this, "CrtRoomShell");

		var f = new MeshKit();
		f.Mat(BunkerTextures.RoomFloorMat);
		f.Color = Colors.White;
		f.Box(new Vector3(0, -0.05f, MidZ), new Vector3(w * 2f, 0.1f, Depth));
		f.CommitTo(this, "CrtRoomFloor");

		AddBox(new Vector3(-w - 0.1f, h * 0.5f, MidZ), new Vector3(0.2f, h, Depth));
		AddBox(new Vector3(w + 0.1f, h * 0.5f, MidZ), new Vector3(0.2f, h, Depth));
		AddBox(new Vector3(0, h * 0.5f, CrtRoomBackZ - 0.1f), new Vector3(w * 2f, h, 0.2f));
		AddBox(new Vector3(0, -0.05f, MidZ), new Vector3(w * 2f, 0.1f, Depth));
		AddBox(new Vector3(0, h + 0.05f, MidZ), new Vector3(w * 2f, 0.1f, Depth));
		AddBox(new Vector3(-(DoorHalfWidth + sideW * 0.5f), h * 0.5f, fz), new Vector3(sideW, h, BulkheadThickness));
		AddBox(new Vector3(DoorHalfWidth + sideW * 0.5f, h * 0.5f, fz), new Vector3(sideW, h, BulkheadThickness));
		AddBox(new Vector3(0, DoorHeight + (h - DoorHeight) * 0.5f, fz), new Vector3(DoorHalfWidth * 2f, h - DoorHeight, BulkheadThickness));

		// Damp and rust down the walls, puddles on the floor.
		var rng = new RandomNumberGenerator { Seed = 909 };
		for (int i = 0; i < 12; i++)
		{
			float side = rng.Randf() < 0.5f ? -1f : 1f;
			BunkerKit.AddDecal(this, rng.Randf() < 0.6f ? BunkerTextures.RustRun() : BunkerTextures.Streak(),
				new Vector3(side * w, h - 1.1f, rng.RandfRange(CrtRoomBackZ + 1f, CrtRoomFrontZ - 1f)), new Vector3(-side, 0, 0), Vector3.Down,
				new Vector2(rng.RandfRange(0.4f, 0.9f), 2.2f), 0.5f, new Color(1, 1, 1, 0.85f));
		}
		for (int i = 0; i < 6; i++)
			BunkerKit.AddDecal(this, BunkerTextures.Puddle(), new Vector3(rng.RandfRange(-3f, 3f), 0f, rng.RandfRange(CrtRoomBackZ + 4f, CrtRoomFrontZ - 2f)),
				Vector3.Up, Vector3.Forward, new Vector2(rng.RandfRange(1f, 2f), rng.RandfRange(1f, 2.4f)), 0.3f, new Color(1, 1, 1, 0.85f), BunkerTextures.PuddleOrm());
		for (int i = 0; i < 6; i++)
			BunkerKit.AddDecal(this, BunkerTextures.Blotch(), new Vector3(rng.RandfRange(-5f, 5f), 0f, rng.RandfRange(CrtRoomBackZ + 1f, CrtRoomFrontZ - 1f)),
				Vector3.Up, Vector3.Forward, new Vector2(2f, 2f), 0.3f, new Color(1, 1, 1, 0.7f));
	}

	/// <summary>Steel shelving along the back and side walls, stacked unevenly with CRTs.</summary>
	private void BuildShelving()
	{
		var rng = new RandomNumberGenerator { Seed = 9101 };
		var frame = new MeshKit();
		var bodies = new MeshKit();
		var screens = new MeshKit();
		screens.Mat(_normalMat);
		var cables = new MeshKit();
		int screenCount = 0;

		void Unit(Transform3D xf)
		{
			const float uw = 1.9f, ud = 0.6f;
			float[] shelves = { 0.1f, 0.8f, 1.5f, 2.2f };
			frame.Mat(BunkerTextures.PaintedMetalMat);
			frame.Color = new Color(0.9f, 0.92f, 0.9f) * rng.RandfRange(0.8f, 1f);
			var oldXf = frame.Xf;
			frame.Xf = xf;
			foreach (float sx in new[] { -uw * 0.5f + 0.02f, uw * 0.5f - 0.02f })
				foreach (float sz in new[] { -ud * 0.5f + 0.02f, ud * 0.5f - 0.02f })
					frame.Box(new Vector3(sx, 1.2f, sz), new Vector3(0.04f, 2.4f, 0.04f));
			foreach (float y in shelves)
				frame.Box(new Vector3(0, y, 0), new Vector3(uw, 0.03f, ud));
			frame.Xf = oldXf;
			AddBox(xf * new Vector3(0, 1.2f, 0), new Vector3(uw, 2.4f, ud), xf.Basis);

			for (int s = 0; s < shelves.Length; s++)
			{
				float y = shelves[s] + 0.015f;
				float x = -uw * 0.5f + 0.05f;
				while (true)
				{
					int size = s == shelves.Length - 1 ? rng.RandiRange(0, 2) : rng.RandiRange(0, 1);
					var spec = BunkerKit.RandomCrt(rng, size);
					if (x + spec.W > uw * 0.5f - 0.03f) break;
					if (rng.Randf() < 0.08f) { x += spec.W * 0.7f; continue; }   // a gap where one's gone
					float cx = x + spec.W * 0.5f;
					var local = new Transform3D(Basis.FromEuler(new Vector3(0, rng.RandfRange(-0.09f, 0.09f), 0)),
						new Vector3(cx, y, ud * 0.5f - spec.D * 0.5f + rng.RandfRange(-0.05f, 0.03f)));
					PlaceCrt(xf * local, spec);
					// Stack another on top of the upper shelf now and then, a little askew.
					if (s == shelves.Length - 1 && rng.Randf() < 0.6f)
					{
						var top = BunkerKit.RandomCrt(rng, rng.RandiRange(0, 1));
						var up = new Transform3D(Basis.FromEuler(new Vector3(0, rng.RandfRange(-0.15f, 0.15f), 0)),
							new Vector3(cx + rng.RandfRange(-0.06f, 0.06f), y + spec.H, local.Origin.Z + rng.RandfRange(-0.05f, 0.02f)));
						PlaceCrt(xf * up, top);
					}
					x += spec.W + rng.RandfRange(0.01f, 0.06f);
				}
			}
			// A bundle of cables down the back of the unit and across the floor toward the console.
			cables.Mat(BunkerTextures.RubberMat);
			cables.Color = Colors.White;
			for (int c = 0; c < 4; c++)
			{
				Vector3 a = xf * new Vector3(rng.RandfRange(-0.8f, 0.8f), 2.25f, -ud * 0.5f + 0.05f);
				Vector3 b = xf * new Vector3(rng.RandfRange(-0.3f, 0.3f), 0.02f, ud * 0.5f + 0.1f);
				BunkerKit.Cable(cables, a, a.Lerp(b, 0.5f) + Vector3.Down * 0.6f, 0.2f, 0.012f, 4);
				BunkerKit.Cable(cables, a.Lerp(b, 0.5f) + Vector3.Down * 0.6f, b, 0.1f, 0.012f, 4);
			}
			Vector3 run0 = xf * new Vector3(0, 0.015f, ud * 0.5f + 0.1f);
			Vector3 run1 = new(rng.RandfRange(-0.5f, 0.5f), 0.015f, ConsoleZ + 0.2f);
			var pts = new System.Collections.Generic.List<Vector3>();
			for (int i = 0; i <= 8; i++)
			{
				float t = i / 8f;
				Vector3 p = run0.Lerp(run1, t);
				p += new Vector3(Mathf.Sin(t * 9f + run0.X) * 0.15f, 0, Mathf.Cos(t * 7f) * 0.1f) * Mathf.Sin(t * Mathf.Pi);
				pts.Add(p);
			}
			BunkerKit.Tube(cables, pts, 0.02f, 0.02f, 4);
		}

		void PlaceCrt(Transform3D xf, BunkerKit.CrtSpec spec)
		{
			var tint = new Color(rng.Randf(), (rng.RandiRange(0, 3) + 0.5f) / 4f, 0f, 1f);
			BunkerKit.Crt(bodies, screens, xf, spec, tint);
			screenCount++;
		}

		float backZ = CrtRoomBackZ + 0.35f;
		foreach (float x in new[] { -4.2f, -2.1f, 0f, 2.1f, 4.2f })
			Unit(new Transform3D(Basis.Identity, new Vector3(x, 0, backZ)));
		foreach (float z in new[] { -102.5f, -106.5f, -110.5f, -114.5f })
		{
			Unit(new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.Pi * 0.5f, 0)), new Vector3(-CrtRoomHalfWidth + 0.58f, 0, z)));
			Unit(new Transform3D(Basis.FromEuler(new Vector3(0, -Mathf.Pi * 0.5f, 0)), new Vector3(CrtRoomHalfWidth - 0.58f, 0, z)));
		}
		frame.CommitTo(this, "Shelving");
		bodies.CommitTo(this, "CrtBodies");
		screens.CommitTo(this, "CrtScreens", false);
		cables.CommitTo(this, "Cables");
		GD.Print($"[bunker] CRT room: {screenCount} screens");
	}

	private void BuildConsoleAndTarget()
	{
		var k = new MeshKit();
		k.Mat(BunkerTextures.PaintedMetalMat);
		k.Color = new Color(0.75f, 0.78f, 0.76f);
		// A steel console desk centred in front of the back wall.
		k.Box(new Vector3(0, ConsoleTop - 0.02f, ConsoleZ), new Vector3(1.6f, 0.04f, 0.8f));
		k.Box(new Vector3(0, ConsoleTop * 0.5f, ConsoleZ - 0.36f), new Vector3(1.56f, ConsoleTop - 0.04f, 0.03f));
		foreach (float x in new[] { -0.76f, 0.76f })
			k.Box(new Vector3(x, ConsoleTop * 0.5f, ConsoleZ), new Vector3(0.04f, ConsoleTop - 0.04f, 0.76f));
		k.Box(new Vector3(0.5f, ConsoleTop - 0.2f, ConsoleZ + 0.05f), new Vector3(0.45f, 0.3f, 0.65f));   // drawer block
		// Keyboard and a mug.
		k.Mat(BunkerTextures.PlasticMat);
		k.Color = new Color(0.55f, 0.52f, 0.45f);
		k.Box(new Vector3(-0.05f, ConsoleTop + 0.015f, ConsoleZ + 0.28f), new Vector3(0.46f, 0.03f, 0.16f), 1f, Basis.FromEuler(new Vector3(0, 0.08f, 0)));
		k.Color = new Color(0.25f, 0.3f, 0.35f);
		k.Cylinder(new Vector3(0.55f, ConsoleTop, ConsoleZ + 0.2f), new Vector3(0.55f, ConsoleTop + 0.1f, ConsoleZ + 0.2f), 0.04f, 0.04f, 8);
		k.CommitTo(this, "Console");
		AddBox(new Vector3(0, ConsoleTop * 0.5f, ConsoleZ), new Vector3(1.6f, ConsoleTop, 0.8f));

		// The marked set: on the console, facing the room.
		_target = new Node3D { Name = "CrtTarget", Position = new Vector3(0, ConsoleTop, ConsoleZ - 0.1f) };
		AddChild(_target);
		_target.AddToGroup("crt_target_marker");
		var body = new MeshKit();
		var screen = new MeshKit();
		screen.Mat(_targetMat);
		var spec = new BunkerKit.CrtSpec { W = 0.72f, H = 0.6f, D = 0.6f, KnobsRight = true, Body = new Color(0.44f, 0.4f, 0.32f) };
		_targetTubeLocal = BunkerKit.Crt(body, screen, Transform3D.Identity, spec, new Color(0.37f, 0f, 0f, 1f));
		body.CommitTo(_target, "Body");
		screen.CommitTo(_target, "Screen", false);

		_switch = new Interactable
		{
			Name = "Switch",
			Prompt = "Turn off the screen",
			MaxDistance = 3f,
			PickRadius = 0.45f,
			Position = _targetTubeLocal,
		};
		_target.AddChild(_switch);
		_switch.Interacted += _ =>
		{
			if (ScreensOff || ShowingStairs) return;
			_switch.Enabled = false;
			SwitchUsed?.Invoke();
		};

		var near = new Area3D { Name = "TargetReach", CollisionLayer = 0, CollisionMask = 2, Monitorable = false };
		near.Position = new Vector3(0, 0, 1.6f);
		near.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 2.2f } });
		_target.AddChild(near);
		near.BodyEntered += b => { if (b is PlayerController) PlayerAtTarget = true; };
		near.BodyExited += b => { if (b is PlayerController) PlayerAtTarget = false; };
	}

	private void BuildDressing()
	{
		var rng = new RandomNumberGenerator { Seed = 9102 };
		var k = new MeshKit();
		// A toppled office chair near the console.
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.4f, 0.4f, 0.38f);
		var chair = new Transform3D(Basis.FromEuler(new Vector3(0, 0.6f, Mathf.Pi * 0.5f)), new Vector3(-1.4f, 0.26f, ConsoleZ + 1.6f));
		var oldXf = k.Xf;
		k.Xf = chair;
		k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.42f, 0), 0.025f, 0.025f, 6);
		for (int i = 0; i < 5; i++)
		{
			float a = Mathf.Tau * i / 5f;
			k.Beam(Vector3.Zero, new Vector3(Mathf.Cos(a) * 0.3f, -0.04f, Mathf.Sin(a) * 0.3f), 0.04f, 0.03f);
		}
		k.Mat(BunkerTextures.PlasticMat);
		k.Color = new Color(0.12f, 0.12f, 0.13f);
		k.Box(new Vector3(0, 0.46f, 0), new Vector3(0.46f, 0.07f, 0.44f));
		k.Box(new Vector3(0, 0.78f, -0.21f), new Vector3(0.42f, 0.55f, 0.06f));
		k.Xf = oldXf;
		// Filing cabinets by the door, one drawer hanging open.
		k.Mat(BunkerTextures.PaintedMetalMat);
		for (int i = 0; i < 2; i++)
		{
			var c = new Vector3(-CrtRoomHalfWidth + 0.35f, 0.66f, CrtRoomFrontZ - 2f - i * 0.52f);
			k.Color = new Color(0.8f, 0.82f, 0.78f) * rng.RandfRange(0.8f, 1f);
			k.Box(c, new Vector3(0.6f, 1.32f, 0.5f));
			for (int d = 0; d < 4; d++)
			{
				float y = 0.18f + d * 0.32f;
				bool open = i == 1 && d == 2;
				k.Color = new Color(0.7f, 0.72f, 0.68f);
				k.Box(new Vector3(c.X + 0.3f + (open ? 0.2f : 0.005f), y, c.Z), new Vector3(open ? 0.4f : 0.01f, 0.26f, 0.44f));
			}
			AddBox(c, new Vector3(0.6f, 1.32f, 0.5f));
		}
		// Cardboard boxes of tapes stacked by the right wall.
		k.Mat(BunkerTextures.PlasticMat);
		for (int i = 0; i < 5; i++)
		{
			var size = new Vector3(rng.RandfRange(0.4f, 0.6f), rng.RandfRange(0.3f, 0.45f), rng.RandfRange(0.35f, 0.5f));
			float y = i < 3 ? size.Y * 0.5f : 0.45f + size.Y * 0.5f;
			var c = new Vector3(CrtRoomHalfWidth - 0.5f, y, CrtRoomFrontZ - 2.2f - (i % 3) * 0.6f);
			k.Color = new Color(0.46f, 0.36f, 0.24f) * rng.RandfRange(0.8f, 1.05f);
			k.Box(c, size, 1f, Basis.FromEuler(new Vector3(0, rng.RandfRange(-0.2f, 0.2f), 0)));
		}
		AddBox(new Vector3(CrtRoomHalfWidth - 0.5f, 0.45f, CrtRoomFrontZ - 2.8f), new Vector3(0.6f, 0.9f, 1.8f));
		// A fuse box by the door with conduit up into the ceiling.
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.55f, 0.56f, 0.5f);
		k.Box(new Vector3(DoorHalfWidth + 0.9f, 1.5f, CrtRoomFrontZ - BulkheadThickness - 0.08f), new Vector3(0.5f, 0.65f, 0.16f));
		k.Cylinder(new Vector3(DoorHalfWidth + 0.9f, 1.82f, CrtRoomFrontZ - BulkheadThickness - 0.06f), new Vector3(DoorHalfWidth + 0.9f, CrtRoomHeight, CrtRoomFrontZ - BulkheadThickness - 0.06f), 0.03f, 0.03f, 6, false);
		k.CommitTo(this, "Dressing");
	}

	private void BuildLighting()
	{
		// Practical light: caged pendants on cords. Warm and dim, but enough to read the room.
		var metal = new MeshKit();
		metal.Mat(ProcTextures.MetalMat);
		var cords = new MeshKit();
		cords.Mat(BunkerTextures.RubberMat);
		cords.Color = Colors.White;
		int n = 0;
		foreach (float z in new[] { -96.5f, -106.5f, -116.5f })
		{
			var xf = new Transform3D(Basis.FromEuler(new Vector3(0, 0.4f * n, 0)), new Vector3(0, CrtRoomHeight - 0.9f, z));
			cords.Cylinder(new Vector3(0, CrtRoomHeight, z), new Vector3(0, CrtRoomHeight - 0.9f, z), 0.008f, 0.008f, 4, false);
			BunkerKit.CagedLamp(metal, xf);
			BunkerKit.LampGlass(this, xf, BunkerTextures.NewLampGlass(new Color(1f, 0.82f, 0.55f), 1.8f), $"PendantGlass{n}");
			AddChild(new OmniLight3D
			{
				Name = $"Pendant{n}", LightColor = new Color(1f, 0.78f, 0.52f), LightEnergy = 1.5f, OmniRange = 10f,
				OmniAttenuation = 1.1f, Position = new Vector3(0, CrtRoomHeight - 1.3f, z),
			});
			n++;
		}
		metal.CommitTo(this, "PendantFixtures", false);
		cords.CommitTo(this, "PendantCords", false);

		// The screens' own glow, which dies with them.
		_glow = new[]
		{
			new OmniLight3D { Name = "GlowBack", Position = new Vector3(0, 1.7f, CrtRoomBackZ + 2.6f), OmniRange = 8f, LightEnergy = 1.3f },
			new OmniLight3D { Name = "GlowLeft", Position = new Vector3(-CrtRoomHalfWidth + 2.2f, 1.7f, -108.5f), OmniRange = 8f, LightEnergy = 1.1f },
			new OmniLight3D { Name = "GlowRight", Position = new Vector3(CrtRoomHalfWidth - 2.2f, 1.7f, -108.5f), OmniRange = 8f, LightEnergy = 1.1f },
		};
		_glowEnergy = new float[_glow.Length];
		for (int i = 0; i < _glow.Length; i++)
		{
			_glow[i].LightColor = GlowCold;
			_glow[i].LightSpecular = 0.3f;
			_glowEnergy[i] = _glow[i].LightEnergy;
			AddChild(_glow[i]);
		}
		_targetGlow = new OmniLight3D
		{
			Name = "GlowTarget", LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 0.9f, OmniRange = 3.2f,
			Position = _target.Position + _targetTubeLocal + new Vector3(0, 0, 0.7f),
		};
		AddChild(_targetGlow);
	}
}

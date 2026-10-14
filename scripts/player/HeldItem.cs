using Godot;

namespace ProjectDS.Player;

/// <summary>
/// Something held in the hand and seen (the lantern first; the compass and the lighter to follow, 2026-10-07): a node on
/// the camera that's raised into view when it's wanted and lowered out of it when not (eased, half a second each way),
/// bobbing with the stride and lagging a touch behind the view's turns, so it has weight. Each kind says where it's held
/// (<see cref="Rest"/>, <see cref="Lowered"/>), when it's wanted, and animates itself on top.
/// </summary>
public abstract partial class HeldItem : Node3D
{
	protected PlayerController Player { get; private set; }
	/// <summary>0 lowered out of view .. 1 up where it's held.</summary>
	public float Raise { get; private set; }
	/// <summary>Where it's held right now (a kind may move it: the lantern lifted to gather its beam).</summary>
	protected Vector3 Hold;
	protected abstract Vector3 Rest { get; }
	protected abstract Vector3 Lowered { get; }
	protected abstract bool Wanted { get; }
	protected abstract void Build();
	protected abstract void Animate(float dt);
	protected virtual void OnHidden() { }
	/// <summary>Seconds to raise it into view or lower it out (each way).</summary>
	protected virtual float RaiseSeconds => 0.5f;

	private float _stride, _amp, _run, _lagYaw, _lagPitch, _prevYaw, _prevPitch, _shakeT;
	private bool _built;

	/// <summary>The camera's viewfinder up to the eye (the hands are busy with it).</summary>
	protected bool CameraUp => Player?.GetNodeOrNull<CameraTool>("CameraTool") is { IsRaised: true };

	public static T Attach<T>(PlayerController player) where T : HeldItem, new()
	{
		var cam = player.CameraRig?.Camera;
		if (cam == null) return null;
		var item = new T { Name = typeof(T).Name };
		item.Player = player;
		cam.AddChild(item);
		KeepLightsOff(player.GetTree());
		return item;
	}

	private static SceneTree _watched;

	/// <summary>(2026-10-10) The world's lamps (omni and spot) kept off the held layer: right up against one (the pit's red
	/// lamps by the catwalk's rail) the lantern and the glove lit up solid red. The held things are lit by their own soft
	/// fills and the sky; every lamp put in the world from now on is kept off them too.</summary>
	private static void KeepLightsOff(SceneTree tree)
	{
		if (tree == null || _watched == tree) return;
		_watched = tree;
		static void Off(Node n) { if (n is OmniLight3D or SpotLight3D && n is Light3D l && l.LightCullMask != HeldLayer) l.LightCullMask &= ~HeldLayer; }
		static void Sweep(Node n) { Off(n); foreach (var c in n.GetChildren()) Sweep(c); }
		Sweep(tree.Root);
		tree.NodeAdded += Off;
	}

	public override void _Process(double delta)
	{
		if (Player == null) return;
		if (!_built)
		{
			_built = true;
			Build();
			// (2026-10-10c, the owner: "make the things the player is holding look way better") a faint cool rim from behind
			// and above on the held layer: the thing's edges and its metal read against the dark (it went to a black
			// silhouette, lit only by its own warm fill)
			AddChild(new OmniLight3D { Name = "HeldRim", Position = new Vector3(0.28f, 0.32f, 0.22f), LightColor = new Color(0.58f, 0.66f, 0.82f), LightEnergy = 0.45f, OmniRange = 1.1f, OmniAttenuation = 1.2f, ShadowEnabled = false, LightCullMask = HeldLayer, LightSpecular = 0.6f });
			Hold = Rest;
			Visible = false;
		}
		float dt = Mathf.Min((float)delta, 0.05f);
		bool want = Wanted;
		Raise = Mathf.MoveToward(Raise, want ? 1f : 0f, dt / RaiseSeconds);
		bool show = Raise > 0.01f;
		if (Visible != show) { Visible = show; if (!show) OnHidden(); }
		if (!show) return;
		Animate(dt);
		float r = Mathf.SmoothStep(0f, 1f, Raise);
		// the stride (2026-10-12, the owner: "the animation of the arm while sprinting ... looks spastic and erratic, it needs to
		// look smooth"): kept in step with the feet (PlayerFootsteps' phase, as the head's bob is), every curve a smooth one
		// (a dip that eases in and out of each footfall: the old |cos| bounced off the ground with a kink, twice a cycle,
		// faster the faster you went), the hand trailing the foot a touch; its size eased, never snapped, and kept small
		// at a run (the arm held in to the body, swinging gently fore and aft, not bouncing)
		float speed = Player.IsOnFloor() ? Player.GroundSpeed : 0f;
		bool running = Player.IsRunning && speed > 3f;
		_run = Mathf.Lerp(_run, running ? 1f : 0f, 1f - Mathf.Exp(-dt * 3f));
		float ampGoal = Mathf.Clamp(speed / 3.5f, 0f, 1f) * (1f - 0.5f * Player.CrouchAmount) * (1f - 0.35f * _run);
		_amp = Mathf.Lerp(_amp, ampGoal, 1f - Mathf.Exp(-dt * 4f));
		float dip, sway;
		if (Player.Footsteps is { } steps)
		{
			float ph = steps.StepPhase - 0.12f;
			dip = 0.5f + 0.5f * Mathf.Cos(ph * Mathf.Tau);
			sway = Mathf.Sin((steps.Steps + ph) * Mathf.Pi);
		}
		else
		{
			_stride += dt * Mathf.Lerp(1.7f, 2.6f, _run);
			dip = 0.5f + 0.5f * Mathf.Cos(_stride * Mathf.Tau);
			sway = Mathf.Sin(_stride * Mathf.Pi);
		}
		var bob = new Vector3(sway * 0.01f, -dip * 0.014f, sway * 0.012f * _run) * _amp
			+ new Vector3(0.025f, -0.03f, 0.04f) * _run;   // (held in closer and lower at a run)
		// a touch of lag behind the view's turns (it has weight), eased back
		var rig = Player.CameraRig;
		float yaw = rig?.Yaw ?? 0f, pitch = rig?.Camera?.Rotation.X ?? 0f;
		_lagYaw = Mathf.Lerp(_lagYaw + Mathf.AngleDifference(yaw, _prevYaw) * 0.6f, 0f, 1f - Mathf.Exp(-dt * 9f));
		_lagPitch = Mathf.Lerp(_lagPitch + (_prevPitch - pitch) * 0.6f, 0f, 1f - Mathf.Exp(-dt * 9f));
		_prevYaw = yaw; _prevPitch = pitch;
		_lagYaw = Mathf.Clamp(_lagYaw, -0.08f, 0.08f); _lagPitch = Mathf.Clamp(_lagPitch, -0.06f, 0.06f);
		// (2026-10-10) after a fright the hand shakes a little, settling over a few seconds (slow, small: never a jitter)
		_shakeT += dt;
		float tr = PlayerBreathing.Tremble;
		var shake = tr > 0.001f ? new Vector3(Mathf.Sin(_shakeT * 13.1f) + 0.6f * Mathf.Sin(_shakeT * 21.7f), Mathf.Sin(_shakeT * 15.3f + 1f) + 0.5f * Mathf.Sin(_shakeT * 24.1f), 0f) * 0.0025f * tr : Vector3.Zero;
		Position = Lowered.Lerp(Hold, r) + bob + shake + new Vector3(_lagYaw * 0.35f, _lagPitch * 0.3f, 0f);
		Rotation = new Vector3(_lagPitch * 0.5f + shake.Y * 3f, _lagYaw * 0.6f, shake.X * 3f);
	}

	/// <summary>The render layer the held things are on (out of the lantern's own light: see <see cref="SetLayer"/>).</summary>
	public const uint HeldLayer = 1u << 19;
	/// <summary>The jacket's sleeve, as the body's (a shade under its hips: the held things are lit close by their own fill).</summary>
	public static readonly Color JacketSleeve = new(0.085f, 0.1f, 0.07f);

	/// <summary>Puts everything drawn under <paramref name="n"/> on the held layer only.</summary>
	internal static void SetLayer(Node n)
	{
		if (n is VisualInstance3D v && n is not Light3D) v.Layers = HeldLayer;
		foreach (var c in n.GetChildren()) SetLayer(c);
	}

	/// <summary>(2026-10-11, the owner: "I can see pieces of the player arm not connected to each other ... we need to
	/// bridge the meshes") A first-person arm modelled in Blender as one fused skin (tools/Blender/fp_arms.py): glove,
	/// wrist, cuff and sleeve together, its fingers curled round the grip at its origin. Null if the model's missing.</summary>
	internal static Node3D BuildArm(Node3D parent, string model, Vector3 at, float scale = 1f)
	{
		string path = $"res://assets/models/player/{model}.glb";
		if (!ResourceLoader.Exists(path)) return null;
		var arm = GD.Load<PackedScene>(path).Instantiate<Node3D>();
		arm.Name = "Arm";
		arm.Position = at;
		arm.Scale = Vector3.One * scale;
		parent.AddChild(arm);
		// its own baked maps (skin, nails, the sleeve); the sleeve drawn both sides (its cuff is open), the skin a soft sheen
		foreach (var n in arm.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)n;
			mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			for (int si = 0; si < mi.Mesh.GetSurfaceCount(); si++)
				if (mi.Mesh.SurfaceGetMaterial(si) is StandardMaterial3D sm)
				{
					var m = (StandardMaterial3D)sm.Duplicate();
					bool cloth = sm.ResourceName.StartsWith("fp_sleeve");
					m.VertexColorUseAsAlbedo = true;   // (coloured by vertex: skin, knuckles, nails, the sleeve)
					m.AlbedoColor = Colors.White;
					m.AlbedoTexture = null;
					m.CullMode = cloth ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back;
					m.Roughness = cloth ? 0.95f : 0.55f;
					m.MetallicSpecular = cloth ? 0.15f : 0.42f;   // (the gloves: dark leather, a soft sheen along the knuckles)
					if (!cloth) { m.RimEnabled = true; m.Rim = 0.2f; m.RimTint = 0.3f; }
					mi.SetSurfaceOverrideMaterial(si, m);
				}
		}
		return arm;
	}

	/// <summary>The right hand's fist round a handle at <paramref name="at"/> (the grip), its forearm and sleeve back out
	/// of view (<see cref="BuildArm"/>; the old built-up fist only if the model's missing).</summary>
	protected static void BuildFist(Node3D parent, Vector3 at, Color glove, Color sleeve)
	{
		if (BuildArm(parent, "fp_arm_fist_R", at) != null) return;
		var k = new World.MeshKit();
		// (matte, no sheen: lit from beside, a glossy sleeve showed a pale streak and read as a pole)
		k.Mat(new StandardMaterial3D { ResourceName = "glove", AlbedoColor = glove, Roughness = 1f, MetallicSpecular = 0f });
		k.Color = Colors.White;
		k.Blob(at + new Vector3(0, 0.005f, 0), new Vector3(0.042f, 0.036f, 0.034f), 4101, 0.12f, true, 1f);
		for (int i = 0; i < 4; i++)
			k.Blob(at + new Vector3(-0.024f + i * 0.016f, 0.0f, -0.03f), new Vector3(0.011f, 0.014f, 0.012f), 4110 + i, 0.08f, true, 1f);
		k.Blob(at + new Vector3(0.03f, 0.012f, -0.018f), new Vector3(0.012f, 0.022f, 0.012f), 4120, 0.08f, true, 1f);
		// (2026-10-11) the jacket's own dark olive (PlayerBody's): the sleeve in the hand and the hips below it one coat
		k.Mat(new StandardMaterial3D { ResourceName = "sleeve", AlbedoColor = JacketSleeve, Roughness = 1f, MetallicSpecular = 0f });
		// (the wrist and the forearm dropping steeply out of the bottom of the view: a long one, straight back, read as a pole)
		k.Cylinder(at + new Vector3(-0.004f, 0.012f, 0.022f), at + new Vector3(-0.02f, -0.1f, 0.06f), 0.028f, 0.034f, 8, true);
		k.Cylinder(at + new Vector3(-0.02f, -0.1f, 0.06f), at + new Vector3(-0.04f, -0.36f, 0.1f), 0.034f, 0.04f, 8, true);
		var mi = k.CommitTo(parent, "Hand", false);
		mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
	}
}

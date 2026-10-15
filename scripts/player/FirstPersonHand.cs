using Godot;

namespace ProjectDS.Player;

/// <summary>Which hand.</summary>
public enum HandSide { Left, Right }

/// <summary>What the hand is doing (each a modelled pose, tools/Blender/fp_arms.py POSES).</summary>
public enum HandPose
{
	/// <summary>A fist round a horizontal handle running along X through the socket (the lantern's bail grip, 1.7 cm across).</summary>
	Handle,
	/// <summary>A fist round an upright stick running along Y through the socket (a flare, a tool's handle, 3.2 cm across),
	/// the thumb closed over the fingers.</summary>
	Stick,
	/// <summary>One end of a camera (or any box) held from the side: the fingers round its front edge, the thumb up its back;
	/// the grip (4.2 cm across) along Y through the socket. Use one of each side for a two-handed hold.</summary>
	Camera,
	/// <summary>Open and at ease, the fingers gently curled; the socket is the middle of the palm.</summary>
	Relaxed,
	/// <summary>(2026-10-14e) A hand cradling a thick upright neck (the newel cap's, 7.2 cm across) along Y through the
	/// socket: the fingers half round it, the thumb opposite.</summary>
	Wide,
	/// <summary>(2026-10-14e) A tight fist round a small upright thing (the lighter) along Y through the socket, 1.9 cm across,
	/// the thumb over the fingers (2026-10-14g: in the <see cref="Fist"/>'s frame, upright).</summary>
	Small,
	/// <summary>(2026-10-14g) The hammer grip, in the held things' one convention (<see cref="HeldModels"/>): a fist round a
	/// +Y grip, its knuckles (and the tool's working end) toward -Z, the back of the right hand out to +X, the thumb over
	/// on top; the forearm built turned back against the <see cref="HeldModels.FistLean"/> the tools are carried at, so
	/// they lean out ahead of the fist while the forearm comes in from the lower right. The axe, hammer, bar, knife,
	/// walkie and flare.</summary>
	Fist,
	/// <summary>(2026-10-14g) The open hand palm up, the fingers ahead and cupped a little: something broad carried on it (the
	/// newel cap, under its plinth). The socket is the middle of the palm; up out of the palm is +Y.</summary>
	Cradle,
	/// <summary>(2026-10-15) The key pinch: a flat small thing (a key's bow, a card's end, a bookmark) held between the pad of
	/// the thumb and the side of the first finger's middle bone, the other fingers curled loosely under it. The
	/// <see cref="Fist"/>'s frame (the thumb up +Y, the back of the right hand out +X, the knuckles ahead -Z); the socket is
	/// the pinch itself, the thing lying flat in XZ through it (made for one 3 mm thick).</summary>
	Pinch,
}

/// <summary>
/// (2026-10-13d, the owner: "make the players hand look more realistic and less robot", "less bendy at the wrist and more
/// strong") The first-person gloved hand, as one reusable piece: the hand, its glove's cuff and the jacket's sleeve, in one
/// of a few poses, for either side.
/// <list type="bullet">
/// <item><b>The socket is this node's origin</b>: put the held thing's grip there (as a child of this node, or at its
/// transform). Each pose's grip runs along <see cref="GripAxis"/> through it. The node itself is never mirrored, so
/// anything under it keeps its handedness.</item>
/// <item>The poses are modelled once, as the left hand (assets/models/player/fp_hand_L_&lt;pose&gt;.glb); the right
/// (fp_hand_R_&lt;pose&gt;.glb) is that mesh mirrored across X in the build, so the two always match (2026-10-14f: its own
/// file, so its normal map lights it the right way; without one, the left's mesh is mirrored here).</item>
/// <item>Its own life: set <see cref="Stride"/> (the walk's sway, -1..1, times how much you're moving) and
/// <see cref="Tension"/> (0..1: sprinting, scared) each frame and it shifts a touch on the grip and tightens on it.
/// <see cref="HeldItem"/> does this for the hands it builds.</item>
/// <item>Drawn without casting shadows (the body's own arms throw the shadow); put it on the held layer with the rest of
/// the held thing (<see cref="HeldItem"/>'s SetLayer).</item>
/// </list>
///     var hand = FirstPersonHand.Create(HandSide.Left, HandPose.Handle);
///     holder.AddChild(hand);            // the hand's socket at the holder's origin
///     hand.AddChild(lanternGripPoint);  // or place the item at hand.GlobalTransform
/// </summary>
public partial class FirstPersonHand : Node3D
{
	/// <summary>A gloved hand's size over a bare one's: the models are drawn at this.</summary>
	public const float GloveScale = 1.1f;

	public HandSide Side { get; private set; }
	public HandPose Pose { get; private set; }
	/// <summary>0..1: how hard it's gripping (a sprint, a fright): the fist closes a touch tighter and rolls over the grip.</summary>
	public float Tension { get; set; }
	/// <summary>-1..1: the walk's side-to-side sway (times how much you're walking): the hand shifts a little on the grip with it.</summary>
	public float Stride { get; set; }

	private Node3D _mesh;
	private float _tension;
	private float _scale = GloveScale;
	private bool _mirror;

	/// <summary>The way the pose's grip runs through the socket (this node's own frame; the same for either side).</summary>
	public Vector3 GripAxis => Pose switch
	{
		HandPose.Handle => Vector3.Right,
		HandPose.Stick or HandPose.Fist or HandPose.Camera or HandPose.Wide or HandPose.Small or HandPose.Pinch => Vector3.Up,
		_ => Vector3.Right,
	};

	/// <summary>The grip's radius the pose is made for (metres), for sizing what's put in it.</summary>
	public float GripRadius => Pose switch
	{
		HandPose.Handle => 0.0085f,
		HandPose.Stick or HandPose.Fist => 0.016f,
		HandPose.Camera => 0.021f,
		HandPose.Wide => 0.036f,
		HandPose.Small => 0.0095f,
		_ => 0f,
	};

	/// <summary>The pose's model for that side (2026-10-14f: the right is its own mirrored mesh, so its normal map's frame
	/// and its faces' winding are its own).</summary>
	public static string ModelPath(HandPose pose, HandSide side = HandSide.Left) =>
		$"res://assets/models/player/fp_hand_{(side == HandSide.Right ? "R" : "L")}_{pose.ToString().ToLowerInvariant()}.glb";

	/// <summary>A hand in <paramref name="pose"/> for <paramref name="side"/>, its socket at its origin (null if the model's
	/// missing). <paramref name="scale"/> is the glove's size (the models' own is right for a gloved hand).</summary>
	public static FirstPersonHand Create(HandSide side, HandPose pose, float scale = GloveScale)
	{
		string path = ModelPath(pose, side);
		bool mirror = false;
		if (!ResourceLoader.Exists(path)) { path = ModelPath(pose); mirror = side == HandSide.Right; }   // (no right-hand model: the left's, mirrored)
		if (!ResourceLoader.Exists(path)) return null;
		var hand = new FirstPersonHand { Name = $"Hand{side}{pose}", Side = side, Pose = pose, _scale = scale, _mirror = mirror };
		hand._mesh = GD.Load<PackedScene>(path).Instantiate<Node3D>();
		hand._mesh.Name = "Mesh";
		hand.AddChild(hand._mesh);
		Dress(hand._mesh);
		hand.Settle();
		return hand;
	}

	public override void _Process(double delta)
	{
		if (_mesh == null) return;
		float dt = Mathf.Min((float)delta, 0.05f);
		// (tightening comes on quick and lets go slowly; nothing jolts)
		float goal = Mathf.Clamp(Tension, 0f, 1f);
		_tension = Mathf.Lerp(_tension, goal, 1f - Mathf.Exp(-dt * (goal > _tension ? 6f : 1.5f)));
		Settle();
	}

	/// <summary>The mesh under the socket: rolled a hair over the grip with the stride, and closed a touch tighter (a firm
	/// hand: small movements only); mirrored only if the right hand had to borrow the left's model.</summary>
	private void Settle()
	{
		float side = Side == HandSide.Right ? -1f : 1f;
		float mirror = _mirror ? -1f : 1f;
		float roll = 0.012f * Mathf.Clamp(Stride, -1f, 1f) + 0.035f * _tension;
		var basis = new Basis(GripAxis, roll * side);
		float squeeze = 1f - 0.012f * _tension;
		_mesh.Transform = new Transform3D(basis * Basis.FromScale(new Vector3(mirror * _scale * squeeze, _scale, _scale * squeeze)), Vector3.Zero);
	}

	private static NoiseTexture2D _grain;
	private static Shader _shader;
	/// <summary>The glove leather's pebbled grain, as a seamless normal map (cellular noise, made once): the shader lays it
	/// over the model's baked folds and seams at two sizes (and the sleeve's canvas takes its slubs from it).</summary>
	private static NoiseTexture2D LeatherGrain => _grain ??= new NoiseTexture2D
	{
		Width = 256, Height = 256, Seamless = true, AsNormalMap = true, BumpStrength = 5f, GenerateMipmaps = true,
		Noise = new FastNoiseLite
		{
			NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.11f, Seed = 913,
			CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub, FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves = 2, FractalGain = 0.35f,
		},
	};
	private static Shader ArmShader => _shader ??= GD.Load<Shader>("res://assets/shaders/fp_arm.gdshader");

	/// <summary>(2026-10-14f) The modelled arm's materials, all one shader (assets/shaders/fp_arm.gdshader): the glove's
	/// worn brown leather in its own baked colour and normal maps (2026-10-14g: its grime, scuffs, scratches and the
	/// seams' thread in the colour map; its occlusion and wear in the vertices) under the shader's grain, its knuckles and
	/// fingertips polished smooth (2026-10-15: charcoal-black, a blacker grip palm, a low sheen and rim so it reads in the
	/// dark); the jacket's cotton duck sleeve, its hem down over the glove's gauntlet (the colour, a very dark olive-brown,
	/// the model's vertices'; the weave, seams, hem, fray and creases the shader's). (Models before 2026-10-15 also had a
	/// knitted cuff, "fp_cuff", still dressed as kind 1.) Nothing casts a shadow.</summary>
	public static void Dress(Node3D arm)
	{
		foreach (var n in arm.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)n;
			mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			for (int si = 0; si < mi.Mesh.GetSurfaceCount(); si++)
			{
				var src = mi.Mesh.SurfaceGetMaterial(si);
				string name = src?.ResourceName ?? "";
				int kind = name.StartsWith("fp_cuff") ? 1 : name.StartsWith("fp_sleeve") ? 2 : 0;
				var m = new ShaderMaterial { Shader = ArmShader, ResourceName = "fp_arm_" + kind };
				m.SetShaderParameter("kind", kind);
				m.SetShaderParameter("grain", LeatherGrain);
				if (kind == 0 && src is BaseMaterial3D bm)
				{
					if (bm.NormalTexture != null)
					{
						m.SetShaderParameter("baked_normal", bm.NormalTexture);
						m.SetShaderParameter("has_baked", true);
					}
					// (2026-10-14g) the glove's own colour map: its wear, grime, scuffs, scratches and the seams' thread
					if (bm.AlbedoTexture != null)
					{
						m.SetShaderParameter("baked_albedo", bm.AlbedoTexture);
						m.SetShaderParameter("has_albedo", true);
					}
				}
				mi.SetSurfaceOverrideMaterial(si, m);
			}
		}
	}
}

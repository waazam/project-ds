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
	/// the thumb over the fingers.</summary>
	Small,
}

/// <summary>
/// (2026-10-13d, the owner: "make the players hand look more realistic and less robot", "less bendy at the wrist and more
/// strong") The first-person gloved hand, as one reusable piece: the hand, its glove's cuff and the jacket's sleeve, in one
/// of a few poses, for either side.
/// <list type="bullet">
/// <item><b>The socket is this node's origin</b>: put the held thing's grip there (as a child of this node, or at its
/// transform). Each pose's grip runs along <see cref="GripAxis"/> through it. The node itself is never mirrored, so
/// anything under it keeps its handedness; only the hand's mesh (the child "Mesh") is.</item>
/// <item>The poses are modelled once, as the left hand (assets/models/player/fp_hand_L_&lt;pose&gt;.glb); the right is that mesh
/// mirrored across X, so the two always match.</item>
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

	/// <summary>The way the pose's grip runs through the socket (this node's own frame; the same for either side).</summary>
	public Vector3 GripAxis => Pose switch
	{
		HandPose.Handle => Vector3.Right,
		HandPose.Stick or HandPose.Camera or HandPose.Wide or HandPose.Small => Vector3.Up,
		_ => Vector3.Right,
	};

	/// <summary>The grip's radius the pose is made for (metres), for sizing what's put in it.</summary>
	public float GripRadius => Pose switch
	{
		HandPose.Handle => 0.0085f,
		HandPose.Stick => 0.016f,
		HandPose.Camera => 0.021f,
		HandPose.Wide => 0.036f,
		HandPose.Small => 0.0095f,
		_ => 0f,
	};

	public static string ModelPath(HandPose pose) => $"res://assets/models/player/fp_hand_L_{pose.ToString().ToLowerInvariant()}.glb";

	/// <summary>A hand in <paramref name="pose"/> for <paramref name="side"/>, its socket at its origin (null if the model's
	/// missing). <paramref name="scale"/> is the glove's size (the models' own is right for a gloved hand).</summary>
	public static FirstPersonHand Create(HandSide side, HandPose pose, float scale = GloveScale)
	{
		string path = ModelPath(pose);
		if (!ResourceLoader.Exists(path)) return null;
		var hand = new FirstPersonHand { Name = $"Hand{side}{pose}", Side = side, Pose = pose, _scale = scale };
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

	/// <summary>The mesh under the socket: mirrored for the right hand, rolled a hair over the grip with the stride, and
	/// closed a touch tighter (a firm hand: small movements only).</summary>
	private void Settle()
	{
		float mirror = Side == HandSide.Right ? -1f : 1f;
		float roll = 0.012f * Mathf.Clamp(Stride, -1f, 1f) + 0.035f * _tension;
		var basis = new Basis(GripAxis, roll * mirror);
		float squeeze = 1f - 0.012f * _tension;
		_mesh.Transform = new Transform3D(basis * Basis.FromScale(new Vector3(mirror * _scale * squeeze, _scale, _scale * squeeze)), Vector3.Zero);
	}

	private static NoiseTexture2D _grain, _grainRough;
	private static FastNoiseLite GrainNoise() => new()
	{
		NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.11f, Seed = 913,
		CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub, FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
		FractalOctaves = 2, FractalGain = 0.35f,
	};
	/// <summary>The glove leather's pebbled grain, about a millimetre a pebble (cellular noise as a normal map, seamless, made once).</summary>
	private static NoiseTexture2D LeatherGrain => _grain ??= new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, AsNormalMap = true, BumpStrength = 6f, Noise = GrainNoise() };
	/// <summary>The leather's sheen broken up: duller in the grain's hollows (0.72..1 of the material's roughness).</summary>
	private static NoiseTexture2D LeatherRough => _grainRough ??= new NoiseTexture2D
	{
		Width = 256, Height = 256, Seamless = true, Noise = GrainNoise(),
		ColorRamp = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(1f, 1f, 1f), new Color(0.72f, 0.72f, 0.72f) } },
	};

	/// <summary>The modelled arm's materials: coloured by vertex (the leather's wear, its creases and seams, the sleeve); the
	/// glove worn dark-brown leather, its grain a fine normal mapped from its own three sides (one fused skin: no UVs) and its
	/// sheen broken up with it; the sleeve matte and drawn both sides (its cuff is open). Nothing casts a shadow.</summary>
	public static void Dress(Node3D arm)
	{
		foreach (var n in arm.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)n;
			mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			for (int si = 0; si < mi.Mesh.GetSurfaceCount(); si++)
				if (mi.Mesh.SurfaceGetMaterial(si) is StandardMaterial3D sm)
				{
					var m = (StandardMaterial3D)sm.Duplicate();
					bool cloth = sm.ResourceName.StartsWith("fp_sleeve");
					m.VertexColorUseAsAlbedo = true;
					m.AlbedoColor = Colors.White;
					m.AlbedoTexture = null;
					m.CullMode = cloth ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back;
					// (2026-10-14e: worn work-glove leather, matte with a low sheen: a glossy one read as latex over bone)
					m.Roughness = cloth ? 0.95f : 0.74f;
					m.MetallicSpecular = cloth ? 0.15f : 0.3f;
					if (!cloth)
					{
						m.RimEnabled = true; m.Rim = 0.12f; m.RimTint = 0.4f;
						if (sm.NormalTexture != null)
						{
							// (the modelled hand's own baked map: its folds, seams and grain)
							m.NormalEnabled = true; m.NormalTexture = sm.NormalTexture; m.NormalScale = 1f;
							m.RoughnessTexture = null; m.Uv1Triplanar = false;
						}
						else
						{
							m.NormalEnabled = true; m.NormalTexture = LeatherGrain; m.NormalScale = 0.55f;
							m.RoughnessTexture = LeatherRough; m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
							m.Uv1Triplanar = true; m.Uv1Scale = Vector3.One * 38f; m.Uv1TriplanarSharpness = 2f;
						}
					}
					mi.SetSurfaceOverrideMaterial(si, m);
				}
		}
	}
}

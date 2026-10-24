using System.Threading;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Where R.H. hid half of the bunker's key (2026-10-15, the owner: "2 pieces of a key that need to be found by following
/// arrows on trees using the blacklight"): at the end of one of the <see cref="SurveyLot"/>'s arrow trails, a rusted tobacco
/// tin, in a hollow stump (<see cref="Place.Stump"/>, half 1, nailed in there) or tucked under the lintel stone of a cairn
/// (<see cref="Place.Cairn"/>, half 2). Under the blacklight the tin glints from a way off, so the last arrow's tree is
/// enough to find it; under the flame it's an old tin in the woods.
///
/// Opening it (one press) swings the lid up, shows the half lying in it, and takes it: the flag
/// (<see cref="StoryManager.Flag.KeyPiece"/>) is saved at once and a word comes up. The halves are joined at the bunker's
/// door (<see cref="Bunker"/>). Restore: taken already, the tin stands open and empty. The models are Blender's
/// (tools/Blender/rh_key.py → assets/models/items/rh_key.glb).
/// </summary>
public partial class KeyPieceCache : Node3D
{
	public enum Place { Stump, Cairn }
	/// <summary>How much bigger than its model the cairn is built.</summary>
	public const float CairnScale = 4f;

	/// <summary>Which half (1 or 2).</summary>
	[Export] public int Piece = 1;
	[Export] public Place Hide = Place.Stump;

	public bool Taken { get; private set; }
	/// <summary>Where the tin is (world), to aim at it.</summary>
	public Vector3 TinWorld => _tin?.GlobalPosition ?? GlobalPosition;
	/// <summary>The glint, 0..1 (for tests): the blacklight on the tin.</summary>
	public float Glint { get; private set; }
	public Interactable Use => _use;

	/// <summary>The key's shelf turn (the frame both halves and the joined key share, as they lie: flat, a little turned).</summary>
	public static readonly Transform3D KeyShelf = new(new Basis(Vector3.Up, 0.5f), new Vector3(0, 0.006f, 0));

	private Node3D _tin, _lid, _half;
	private MeshInstance3D _glint;
	private StandardMaterial3D _glintMat;
	private Interactable _use;
	private float _lidT;   // 0 shut .. 1 open
	private bool _opening;

	/// <summary>The lid's hinge, in the tin's frame (rh_key.py: along the back edge, on top).</summary>
	private static readonly Vector3 Hinge = new(0f, 0.028f, -0.037f);

	/// <summary>A mesh from assets/models/items/rh_key.glb under <paramref name="parent"/>, painted (null if missing).</summary>
	public static MeshInstance3D AddModel(Node3D parent, string node, Transform3D xf, string name = "Mesh")
		=> ItemModels.Add(parent, "rh_key", node, xf, name, n => n switch
		{
			"metal" => ProcTextures.MetalMat,
			"rust" => RustMat,
			_ => null,
		});

	private static StandardMaterial3D _rust;
	private static StandardMaterial3D RustMat => _rust ??= new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.19f, 0.1f), Roughness = 0.95f, Metallic = 0.15f };

	public override void _Ready()
	{
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null) GlobalPosition = GlobalPosition with { Y = terrain.HeightAt(GlobalPosition.X, GlobalPosition.Z) };
		Build();
		if (StoryManager.Instance is { } s && s.HasFlag(StoryManager.Flag.KeyPiece(Piece))) SetTaken();
	}

	private void Build()
	{
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		AddChild(body);
		Transform3D tinAt;
		if (Hide == Place.Stump)
		{
			// the hollow snag, its torn-low side toward the last arrow (this node's +Z); the tin on the rot inside, nailed
			// through its back into the wall
			if (AddModel(this, "HollowStump", Transform3D.Identity, "Stump") == null) FallbackStump();
			body.SetMeta("surface", "wood");
			// (low, so the look down into it isn't stopped: the pick sphere sits over its top)
			body.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.22f, 0), Shape = new CylinderShape3D { Radius = 0.4f, Height = 0.44f } });
			tinAt = new Transform3D(new Basis(Vector3.Up, 0.25f), new Vector3(0.02f, 0.53f, 0.03f));
		}
		else
		{
			// (2026-10-23, the owner: "make the stone cairn way bigger it is so small and easy to miss") a waist-high-plus pile,
			// CairnScale times the model; the tin stays its own size, in the gap at the front of its foot
			const float S = CairnScale;
			if (AddModel(this, "Cairn", new Transform3D(Basis.Identity.Scaled(Vector3.One * S), Vector3.Zero), "Cairn") is { } cairn)
				// paler stone than the woods' (it read as a dark lump in the storm's murk)
				cairn.MaterialOverlay = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.53f, 0.48f, 0.1f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.95f, BlendMode = BaseMaterial3D.BlendModeEnum.Add };
			else FallbackCairn(S);
			body.SetMeta("surface", "stone");
			// (behind the tin's gap, so nothing stands between it and a look from the front)
			body.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.33f * S, -0.12f * S), Shape = new BoxShape3D { Size = new Vector3(0.75f, 0.66f, 0.5f) * S } });
			tinAt = new Transform3D(new Basis(Vector3.Up, -0.12f), new Vector3(0f, 0.0f, 0.19f * S + 0.06f));
		}
		_tin = new Node3D { Name = "Tin", Transform = tinAt };
		AddChild(_tin);
		if (AddModel(_tin, "TinBox", Transform3D.Identity, "Box") == null)
			_tin.AddChild(new MeshInstance3D { Name = "Box", Mesh = new BoxMesh { Size = new Vector3(0.11f, 0.024f, 0.07f) }, Position = new Vector3(0, 0.012f, 0), MaterialOverride = ProcTextures.MetalMat });
		_lid = new Node3D { Name = "Lid", Position = Hinge };
		_tin.AddChild(_lid);
		if (AddModel(_lid, "TinLid", new Transform3D(Basis.Identity, -Hinge), "LidMesh") == null)
			_lid.AddChild(new MeshInstance3D { Name = "LidMesh", Mesh = new BoxMesh { Size = new Vector3(0.113f, 0.008f, 0.073f) }, Position = new Vector3(0, 0.004f, 0.037f), MaterialOverride = ProcTextures.MetalMat });
		// the half, in the tin (seen only as the lid comes up)
		_half = new Node3D { Name = "Half", Position = new Vector3(0, 0.03f, 0), Scale = Vector3.One * 0.92f, Visible = false };
		_tin.AddChild(_half);
		// (corner to corner, the half's middle on the tin's: the bow's half runs -0.082..0.027 along its X, the bit's 0.004..0.092)
		float mid = Piece == 1 ? -0.0275f : 0.048f;
		var turn = new Basis(Vector3.Up, 0.55f);
		var xf = new Transform3D(turn, turn * new Vector3(-mid, 0, 0));
		AddModel(_half, Piece == 1 ? "KeyHalfBow" : "KeyHalfBit", xf, "Mesh");

		// the glint: a little star of light on the tin where the beam finds it (the blacklight only)
		_glintMat = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			AlbedoTexture = Star(), AlbedoColor = new Color(0.75f, 0.85f, 1f, 0f), NoDepthTest = false,
		};
		_glint = new MeshInstance3D
		{
			Name = "Glint", Mesh = new QuadMesh { Size = new Vector2(0.22f, 0.22f) }, MaterialOverride = _glintMat,
			Position = new Vector3(0.03f, 0.05f, 0.02f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
		};
		_tin.AddChild(_glint);

		_use = new Interactable
		{
			// (2026-10-19: 0.17 m was a fiddly target down in the snag, found a second late even by the test's aim; about the glint's size now)
			Name = "Use", Prompt = "Open the tin", MaxDistance = 2.4f, PickRadius = 0.26f, PickOffset = new Vector3(0, 0.04f, 0),
		};
		_tin.AddChild(_use);
		_use.Interacted += OnUse;
	}

	private void FallbackStump()
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.6f, 0.55f, 0.5f);
		k.Cylinder(new Vector3(0, -0.1f, 0), new Vector3(0, 0.9f, 0), 0.4f, 0.34f, 10, false, 1f);
		k.CommitTo(this, "Stump");
	}

	private void FallbackCairn(float s)
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.RockMat);
		k.Color = new Color(0.7f, 0.7f, 0.66f);
		k.Box(new Vector3(0, 0.3f, -0.12f) * s, new Vector3(0.7f, 0.6f, 0.45f) * s, 1f, Basis.Identity);
		k.CommitTo(this, "Cairn");
	}

	private static Texture2D _star;
	/// <summary>A soft four-pointed star (the glint's sprite), drawn once.</summary>
	private static Texture2D Star()
	{
		if (_star != null) return _star;
		const int n = 64;
		var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
				float r = Mathf.Sqrt(u * u + v * v);
				float core = Mathf.Exp(-r * r * 30f);
				float rays = Mathf.Exp(-Mathf.Abs(u) * 40f) * Mathf.Max(0f, 1f - Mathf.Abs(v)) + Mathf.Exp(-Mathf.Abs(v) * 40f) * Mathf.Max(0f, 1f - Mathf.Abs(u));
				float a = Mathf.Clamp(core + rays * 0.8f, 0f, 1f);
				img.SetPixel(x, y, new Color(1, 1, 1, a));
			}
		img.GenerateMipmaps();
		return _star = ImageTexture.CreateFromImage(img);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		// the lid
		if (_opening && _lidT < 1f)
		{
			_lidT = Mathf.MoveToward(_lidT, 1f, dt * 2.6f);
			_lid.Rotation = new Vector3(-Mathf.DegToRad(105f) * Mathf.SmoothStep(0f, 1f, _lidT), 0, 0);
		}
		// the glint: the beam on the tin, a slow twinkle, gone once it's empty
		float target = 0f;
		if (!Taken && Player.Lantern.Uv.on)
		{
			Vector3 p = _glint.GlobalPosition;
			float uv = Player.Lantern.UvOn(p);
			if (uv > 0.05f) target = Mathf.Clamp(uv * 1.6f, 0f, 1f) * (0.7f + 0.3f * Mathf.Sin(Time.GetTicksMsec() * 0.0045f));
		}
		Glint = Mathf.MoveToward(Glint, target, dt * 4f);
		_glint.Visible = Glint > 0.01f;
		if (_glint.Visible) _glintMat.AlbedoColor = new Color(0.75f, 0.85f, 1f, Glint);
	}

	private void OnUse(PlayerController player)
	{
		if (Taken || _opening) return;
		_opening = true;
		_use.Enabled = false;
		_half.Visible = true;
		Audio.AudioDirector.OneShot(this, "item_take", 3, _tin.GlobalPosition, -12f, "Player", 2f);
		_ = TakeAfter(0.75f);
	}

	private async System.Threading.Tasks.Task TakeAfter(float seconds)
	{
		await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
		if (!IsInstanceValid(this)) return;
		var s = StoryManager.Instance;
		bool other = s != null && s.HasFlag(StoryManager.Flag.KeyPiece(3 - Piece));
		s?.SetFlag(StoryManager.Flag.KeyPiece(Piece));
		Taken = true;
		_half.Visible = false;
		Audio.AudioDirector.OneShot(this, "item_take", 3, _tin.GlobalPosition, -10f, "Player", 2f);
		GD.Print($"[story] key half {Piece} taken{(other ? " (both now)" : "")}");
		// the player says what it is, and whether it's whole (STORY.md Rev 5: a player line)
		_ = PlayerVoice.Say(this, other ? "key_piece_2" : "key_piece_1", 2.2f);
	}

	/// <summary>Restore: already taken (the tin open and empty).</summary>
	private void SetTaken()
	{
		Taken = true;
		_opening = true;
		_lidT = 1f;
		_lid.Rotation = new Vector3(-Mathf.DegToRad(105f), 0, 0);
		_use.Enabled = false;
		_half.Visible = false;
	}
}

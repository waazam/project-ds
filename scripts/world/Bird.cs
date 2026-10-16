using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

public enum BirdColor { Red, Blue, Purple, BlackOmen }

/// <summary>What a bird sits on (scenes store the number: append only).</summary>
public enum BirdPerch { Auto, Snag, Ground, Branch, Stump, Rock }

/// <summary>
/// Act 1's optional photo-minigame subject (STORY.md): a bird that resembles a
/// cardinal, in red, blue or purple, all muted toward the forest's palette, and
/// a fourth that is black with a glowing red eye. Photographing the black one
/// ends the minigame for good (CameraTool handles that).
///
/// The bird is ~250 triangles: lofted body, a separate head (crest, thick
/// conical beak, black face mask), folded wings whose tips cross over a long
/// tail, and legs with toes. It never hovers: if its anchor puts it off the
/// ground it sits on a dead snag with a side twig for a perch; otherwise it
/// stands on the ground. Idle is bird-like: quick jerky head turns and an
/// occasional turn (with a hop on the ground).
///
/// The node's origin is the bird's feet (CameraTool aims at GlobalPosition).
/// Capture() flies only the bird away; the perch stays.
/// </summary>
[Tool]
[GlobalClass]
public partial class Bird : Node3D
{
	[Export] public BirdColor Color = BirdColor.Red;
	/// <summary>Where it sits (2026-10-15, the owner: "all the colored birds shouldnt be standing on a post ... they should
	/// be in trees on the ground on post etc"). Auto: a snag if its anchor lifts it, else the ground.</summary>
	[Export] public BirdPerch Perch = BirdPerch.Auto;
	/// <summary>Height of the occasional hop when standing on the ground.</summary>
	[Export] public float BobHeight = 0.02f;
	/// <summary>Average seconds between body turns.</summary>
	[Export] public float BobSeconds = 1.6f;

	public bool Photographed { get; private set; }
	public bool IsOmen => Color == BirdColor.BlackOmen;
	public int TriangleCount { get; private set; }
	public bool Perched { get; private set; }

	private Node3D _model;
	private Node3D _head;
	private MeshInstance3D _folded;
	private Node3D _wingL, _wingR;
	private readonly RandomNumberGenerator _rng = new();
	private float _headYaw, _headYawTarget, _headTilt, _headTiltTarget;
	private float _bodyYaw, _bodyYawTarget;
	private float _headTimer, _bodyTimer, _hop = -1f;

	private static readonly Vector3 NeckPivot = new(0, 0.116f, 0.04f);
	/// <summary>About twice life size (a cardinal is ~22 cm): at 640x360 and photo distance a
	/// life-size bird is a few pixels, and the photo game needs it to read as a bird.</summary>
	private const float ModelScale = 2.1f;

	public override void _Ready()
	{
		AddToGroup("photo_birds");
		foreach (var n in new[] { "Model", "Perch" })
		{
			var old = GetNodeOrNull(n);
			if (old != null) { RemoveChild(old); old.QueueFree(); }
		}
		_rng.Seed = (ulong)(Seed() * 7919 + 13);
		if (!Engine.IsEditorHint()) BuildPerchOrGround();
		if (!Engine.IsEditorHint() && Systems.StoryManager.Instance is { } story)
		{
			// Restore: a bird already on the roll (or the whole flock, once the black one was shot) stays gone;
			// from Act 2 on the birds are gone and silent either way. The perch stays in both cases.
			bool shot = story.HasFlag(Systems.StoryManager.Flag.Photo("bird_" + FlagName)) || story.HasFlag(Systems.StoryManager.Flag.Photo("bird_black"));
			if (shot) { Photographed = true; RemoveFromGroup("photo_birds"); return; }
			if (story.Current >= Systems.Checkpoint.Act2StairsClimbed) { Gone = true; RemoveFromGroup("photo_birds"); return; }
			story.CheckpointReached += OnCheckpoint;
		}
		_model = new Node3D { Name = "Model", Scale = Vector3.One * ModelScale };
		AddChild(_model);
		if (!LoadModel()) Build();
		_headTimer = _rng.RandfRange(0.2f, 1f);
		_bodyTimer = _rng.RandfRange(1f, 4f);
	}

	/// <summary>The bird's name in the photo log ("photo_bird_" + this).</summary>
	public string FlagName => Color switch { BirdColor.Red => "red", BirdColor.Blue => "blue", BirdColor.Purple => "purple", _ => "black" };

	/// <summary>Hidden and silent from Act 2 on (a cardinal beside the burning cabin at night is wrong); the perch stays.</summary>
	public bool Gone { get; private set; }

	public override void _ExitTree()
	{
		if (!Engine.IsEditorHint() && Systems.StoryManager.Instance is { } story) story.CheckpointReached -= OnCheckpoint;
	}

	private void OnCheckpoint(Systems.Checkpoint cp)
	{
		if (cp < Systems.Checkpoint.Act2StairsClimbed || Gone) return;
		Gone = true;
		if (IsInGroup("photo_birds")) RemoveFromGroup("photo_birds");
		if (_model != null && IsInstanceValid(_model)) _model.QueueFree();
		_model = null;
	}

	private int Seed() => (int)Color * 191 + (int)(GlobalPosition.X * 13f) + (int)(GlobalPosition.Z * 7f);

	// ───────────────────────────── palette ─────────────────────────────

	private struct Palette { public Color Body, Belly, Wing, Tail, Mask, Beak, Legs; }

	/// <summary>MeshKit vertex colours are linear; the palette is authored in sRGB.</summary>
	private static Palette Linear(Palette p) => new()
	{
		Body = p.Body.SrgbToLinear(), Belly = p.Belly.SrgbToLinear(), Wing = p.Wing.SrgbToLinear(), Tail = p.Tail.SrgbToLinear(),
		Mask = p.Mask.SrgbToLinear(), Beak = p.Beak.SrgbToLinear(), Legs = p.Legs.SrgbToLinear(),
	};

	private Palette Colors3() => Color switch
	{
		// Muted, but each still unmistakably its colour through fog.
		BirdColor.Red => new Palette
		{
			Body = new Color(0.55f, 0.13f, 0.11f), Belly = new Color(0.6f, 0.2f, 0.16f), Wing = new Color(0.4f, 0.12f, 0.1f),
			Tail = new Color(0.38f, 0.11f, 0.1f), Mask = new Color(0.05f, 0.04f, 0.04f), Beak = new Color(0.74f, 0.4f, 0.22f), Legs = new Color(0.36f, 0.28f, 0.26f),
		},
		BirdColor.Blue => new Palette
		{
			Body = new Color(0.22f, 0.32f, 0.52f), Belly = new Color(0.32f, 0.4f, 0.54f), Wing = new Color(0.16f, 0.24f, 0.42f),
			Tail = new Color(0.16f, 0.23f, 0.4f), Mask = new Color(0.05f, 0.05f, 0.06f), Beak = new Color(0.7f, 0.42f, 0.26f), Legs = new Color(0.34f, 0.3f, 0.3f),
		},
		BirdColor.Purple => new Palette
		{
			Body = new Color(0.4f, 0.2f, 0.42f), Belly = new Color(0.47f, 0.28f, 0.47f), Wing = new Color(0.29f, 0.15f, 0.31f),
			Tail = new Color(0.28f, 0.14f, 0.3f), Mask = new Color(0.05f, 0.04f, 0.05f), Beak = new Color(0.72f, 0.42f, 0.26f), Legs = new Color(0.34f, 0.28f, 0.28f),
		},
		_ => new Palette
		{
			Body = new Color(0.045f, 0.045f, 0.05f), Belly = new Color(0.06f, 0.06f, 0.065f), Wing = new Color(0.03f, 0.03f, 0.035f),
			Tail = new Color(0.03f, 0.03f, 0.035f), Mask = new Color(0.02f, 0.02f, 0.02f), Beak = new Color(0.09f, 0.08f, 0.08f), Legs = new Color(0.08f, 0.08f, 0.08f),
		},
	};

	// ───────────────────────────── the model ─────────────────────────────

	/// <summary>(2026-10-15, the owner: "from now on everything should be made in blender") The songbird modelled, baked
	/// and rigged in Blender (tools/Blender/birds.py → assets/models/bird/bird.glb): one skinned body on a skeleton (body,
	/// neck, head, tail, the folded wings, the spread wings, the legs), the red feathers baked in, the other colours'
	/// albedo beside it, the eyes their own surface (the omen's glow). Its clips: idle, look, peck, hop, takeoff, flap,
	/// glide, lunge (the lunge plays at the omen's rush; the rest it does by hand here, as it always did: quick head
	/// turns, pecks, the bounding flight's beats and shut wings).</summary>
	private const string ModelPath = "res://assets/models/bird/bird.glb";
	/// <summary>For the before/after preview: false builds the old code-made bird.</summary>
	public static bool UseModel = true;
	/// <summary>For the preview: the wings opened at an angle (radians above level), or shut.</summary>
	public void PreviewWings(bool open, float up) => SetWings(open, up);
	private Skeleton3D _skel;
	private AnimationPlayer _anim;
	private int _bBody = -1, _bNeck = -1, _bHead = -1, _bTail = -1, _bWingL = -1, _bWingR = -1, _bFoldL = -1, _bFoldR = -1;
	private float _tailFlick;
	private bool _wingsOpen = true;

	private bool LoadModel()
	{
		if (!UseModel || !ResourceLoader.Exists(ModelPath)) return false;
		var scene = GD.Load<PackedScene>(ModelPath);
		if (scene?.Instantiate() is not Node3D inst) return false;
		inst.Name = "Bird";
		_model.AddChild(inst);
		_skel = FindOf<Skeleton3D>(inst);
		_anim = FindOf<AnimationPlayer>(inst);
		var mesh = FindOf<MeshInstance3D>(inst);
		if (_skel == null || mesh == null) { inst.QueueFree(); _skel = null; _anim = null; return false; }
		_bBody = _skel.FindBone("body"); _bNeck = _skel.FindBone("neck"); _bHead = _skel.FindBone("head"); _bTail = _skel.FindBone("tail");
		_bWingL = _skel.FindBone("wing_L"); _bWingR = _skel.FindBone("wing_R");
		_bFoldL = _skel.FindBone("wingfold_L"); _bFoldR = _skel.FindBone("wingfold_R");
		// its colour: the red is baked into the model; the others' albedo beside it
		if (Color != BirdColor.Red && mesh.Mesh != null && mesh.Mesh.GetSurfaceCount() > 0
			&& mesh.Mesh.SurfaceGetMaterial(0) is StandardMaterial3D fm)
		{
			string tex = $"res://assets/models/bird/bird_albedo_{(Color == BirdColor.Blue ? "blue" : Color == BirdColor.Purple ? "purple" : "black")}.png";
			if (ResourceLoader.Exists(tex))
			{
				var m = (StandardMaterial3D)fm.Duplicate();
				m.AlbedoTexture = GD.Load<Texture2D>(tex);
				mesh.SetSurfaceOverrideMaterial(0, m);
			}
		}
		// the omen's eyes: a glowing red
		if (IsOmen && mesh.Mesh != null && mesh.Mesh.GetSurfaceCount() > 1)
			mesh.SetSurfaceOverrideMaterial(1, new StandardMaterial3D
			{
				AlbedoColor = new Color(0.6f, 0.02f, 0.02f), EmissionEnabled = true, Emission = new Color(1f, 0.08f, 0.04f),
				EmissionEnergyMultiplier = 3.5f, Roughness = 0.1f,
			});
		mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
		TriangleCount = 0;
		if (mesh.Mesh is ArrayMesh am)
			for (int i = 0; i < am.GetSurfaceCount(); i++) TriangleCount += (am.SurfaceGetArrayIndexLen(i) > 0 ? am.SurfaceGetArrayIndexLen(i) : am.SurfaceGetArrayLen(i)) / 3;
		if (_anim != null)
		{
			_anim.Stop();
			_anim.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Idle;
		}
		SetWings(false, 0f);
		return true;
	}

	private static T FindOf<T>(Node n) where T : Node
	{
		if (n is T t) return t;
		foreach (var c in n.GetChildren())
			if (FindOf<T>(c) is { } f) return f;
		return null;
	}

	/// <summary>A bone turned by <paramref name="angle"/> about a model-space axis, from its rest (the axis carried into its
	/// parent's frame).</summary>
	private void PoseTurn(int bone, int parent, Vector3 modelAxis, float angle, Quaternion? extra = null)
	{
		if (_skel == null || bone < 0) return;
		var rest = _skel.GetBoneRest(bone).Basis.GetRotationQuaternion();
		var pBasis = parent >= 0 ? _skel.GetBoneGlobalRest(parent).Basis.Orthonormalized() : Basis.Identity;
		var axis = (pBasis.Inverse() * modelAxis).Normalized();
		var q = new Quaternion(axis, angle);
		if (extra is { } e) q = q * e;
		_skel.SetBonePoseRotation(bone, q * rest);
	}

	/// <summary>The head turned (yaw about up) and tipped (a peck: positive is down), whichever model it has.</summary>
	private void SetHead(float yaw, float tilt)
	{
		if (_skel != null && _bHead >= 0)
		{
			var pBasis = _bNeck >= 0 ? _skel.GetBoneGlobalRest(_bNeck).Basis.Orthonormalized() : Basis.Identity;
			var right = (pBasis.Inverse() * Vector3.Right).Normalized();
			PoseTurn(_bHead, _bNeck, Vector3.Up, yaw, new Quaternion(right, tilt));
			return;
		}
		if (_head != null) _head.Rotation = new Vector3(tilt, yaw, 0);
	}

	/// <summary>Wings: open and beating (<paramref name="up"/> radians above level), or shut along the body.</summary>
	private void SetWings(bool open, float up)
	{
		if (_skel != null)
		{
			if (_anim != null && _anim.IsPlaying()) return;   // (the lunge has them)
			if (open != _wingsOpen)
			{
				_wingsOpen = open;
				var tiny = Vector3.One * 0.001f;
				foreach (int b in new[] { _bWingL, _bWingR }) if (b >= 0) _skel.SetBonePoseScale(b, open ? Vector3.One : tiny);
				foreach (int b in new[] { _bFoldL, _bFoldR }) if (b >= 0) _skel.SetBonePoseScale(b, open ? tiny : Vector3.One);
			}
			if (open)
			{
				var fwd = new Vector3(0, 0, 1);
				PoseTurn(_bWingL, _bBody, fwd, up);
				PoseTurn(_bWingR, _bBody, fwd, -up);
			}
			return;
		}
		if (_folded == null) return;
		_folded.Visible = !open;
		_wingL.Visible = _wingR.Visible = open;
		if (open)
		{
			_wingL.Rotation = new Vector3(0, 0, up);
			_wingR.Rotation = new Vector3(0, 0, -up);
		}
	}

	// ───────────────────────────── build ─────────────────────────────

	private void Build()
	{
		var pal = Linear(Colors3());
		var mat = ItemTextures.FeatherMat;
		TriangleCount = 0;

		// Body: tail root to neck, belly toned lighter underneath.
		var k = new MeshKit();
		k.Mat(mat);
		k.Color = pal.Body;
		var rings = new List<ItemMeshes.Ring>
		{
			new(new Vector3(0, 0.074f, -0.056f), 0.013f, 0.011f),
			new(new Vector3(0, 0.071f, -0.03f), 0.028f, 0.026f),
			new(new Vector3(0, 0.073f, 0.0f), 0.034f, 0.033f),
			new(new Vector3(0, 0.086f, 0.025f), 0.032f, 0.03f),
			new(new Vector3(0, 0.104f, 0.037f), 0.024f, 0.022f),
			new(NeckPivot, 0.018f, 0.017f),
		};
		ItemMeshes.Loft(k, rings, 8, true, false, Vector3.Right, 30f, null,
			(r, a) => a < 0 ? pal.Tail : Mathf.Sin(a) > 0.35f ? pal.Belly : pal.Body);

		// Folded wings: tips reach past the rump over the tail (their own mesh: in flight they open, and between wingbeats
		// fold shut again).
		var fw = new MeshKit();
		fw.Mat(mat);
		fw.Color = pal.Wing;
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 sh = new(s * 0.031f, 0.101f, 0.03f), lo = new(s * 0.037f, 0.074f, 0.012f);
			Vector3 tip = new(s * 0.017f, 0.079f, -0.078f), up = new(s * 0.024f, 0.107f, -0.012f);
			Vector3 n = new Vector3(s, 0.35f, 0).Normalized();
			fw.Card(sh, lo, tip, up, n, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
		}
		_folded = fw.CommitTo(_model, "FoldedWings", false);
		TriangleCount += ItemMeshes.CountTriangles(_folded.Mesh);
		// Open wings, for flight: the arm out to the wrist, the long primaries past it swept back a little; each on its
		// shoulder's pivot, beating about the body's long axis. Hidden until it flies.
		foreach (float s in new[] { -1f, 1f })
		{
			var pivot = new Node3D { Name = s < 0 ? "WingR" : "WingL", Position = new Vector3(s * 0.028f, 0.1f, 0.02f), Visible = false };
			_model.AddChild(pivot);
			var w = new MeshKit();
			w.Mat(mat);
			w.Color = pal.Wing;
			Vector3 rl = new(0, 0, 0.012f), rt = new(0, 0, -0.032f);
			Vector3 wl = new(s * 0.07f, 0.004f, 0.008f), wt = new(s * 0.066f, 0f, -0.046f);
			Vector3 tip = new(s * 0.135f, 0.002f, -0.026f), pt = new(s * 0.112f, -0.002f, -0.064f);
			w.Card(rl, wl, wt, rt, Vector3.Up, new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, 1));
			w.Color = pal.Wing.Darkened(0.25f);   // (the flight feathers a shade darker)
			w.Card(wl, tip, pt, wt, Vector3.Up, new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0.5f, 1));
			var wm = w.CommitTo(pivot, "WingMesh", false);
			TriangleCount += ItemMeshes.CountTriangles(wm.Mesh);
			if (s < 0) _wingR = pivot; else _wingL = pivot;
		}
		// Long tail, a shallow V.
		k.Color = pal.Tail;
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 r0 = new(0, 0.076f, -0.05f), r1 = new(s * 0.009f, 0.074f, -0.05f);
			Vector3 t0 = new(0, 0.055f, -0.135f), t1 = new(s * 0.019f, 0.059f, -0.13f);
			k.Card(r0, r1, t1, t0, new Vector3(s * 0.2f, 1f, 0).Normalized(), Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down);
		}
		// Legs and toes.
		k.Color = pal.Legs;
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 hip = new(s * 0.011f, 0.056f, 0.0f), foot = new(s * 0.012f, 0.003f, 0.004f);
			k.Cylinder(hip, foot, 0.0028f, 0.0022f, 3, false);
			k.Cylinder(foot, new Vector3(s * 0.02f, 0.0015f, 0.022f), 0.0018f, 0.001f, 3, false);
			k.Cylinder(foot, new Vector3(s * 0.006f, 0.0015f, 0.024f), 0.0018f, 0.001f, 3, false);
			k.Cylinder(foot, new Vector3(s * 0.012f, 0.0015f, -0.016f), 0.0018f, 0.001f, 3, false);
		}
		var body = k.CommitTo(_model, "BirdMesh", false);
		TriangleCount += ItemMeshes.CountTriangles(body.Mesh);

		// Head on its own pivot so it can turn in quick jerks.
		_head = new Node3D { Name = "Head", Position = NeckPivot };
		_model.AddChild(_head);
		var h = new MeshKit();
		h.Mat(mat);
		h.Color = pal.Body;
		var hr = new List<ItemMeshes.Ring>
		{
			new(new Vector3(0, -0.004f, -0.002f), 0.018f, 0.018f),
			new(new Vector3(0, 0.011f, 0.006f), 0.021f, 0.023f),
			new(new Vector3(0, 0.025f, 0.004f), 0.019f, 0.02f),
			new(new Vector3(0, 0.035f, -0.004f), 0.012f, 0.013f),
		};
		// Black face mask around the beak and throat (front, lower rings).
		ItemMeshes.Loft(h, hr, 7, false, true, Vector3.Right, 30f, null,
			(r, a) => r <= 1 && a >= 0 && Mathf.Sin(a) > 0.55f ? pal.Mask : pal.Body, 1f, 0.6f);
		// Crest: the cardinal's peak, swept back.
		h.Cylinder(new Vector3(0, 0.03f, 0.002f), new Vector3(0, 0.06f, -0.02f), 0.009f, 0f, 4, true);
		// Thick conical beak.
		h.Color = pal.Beak;
		h.Cylinder(new Vector3(0, 0.014f, 0.02f), new Vector3(0, 0.01f, 0.039f), 0.0078f, 0f, 5, true);
		// Eyes (ordinary birds): small dark beads.
		if (!IsOmen)
		{
			h.Color = new Color(0.02f, 0.02f, 0.02f);
			h.Box(new Vector3(0.0175f, 0.02f, 0.012f), new Vector3(0.004f, 0.005f, 0.005f));
			h.Box(new Vector3(-0.0175f, 0.02f, 0.012f), new Vector3(0.004f, 0.005f, 0.005f));
		}
		var head = h.CommitTo(_head, "HeadMesh", false);
		TriangleCount += ItemMeshes.CountTriangles(head.Mesh);

		if (IsOmen)
		{
			// The story's red eye glow.
			var eyeMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.6f, 0.02f, 0.02f),
				EmissionEnabled = true,
				Emission = new Color(1f, 0.08f, 0.04f),
				EmissionEnergyMultiplier = 3.5f,
			};
			var eyeMesh = new SphereMesh { Radius = 0.0045f, Height = 0.009f, RadialSegments = 6, Rings = 3, Material = eyeMat };
			foreach (float s in new[] { -1f, 1f })
				_head.AddChild(new MeshInstance3D
				{
					Name = s > 0 ? "EyeL" : "EyeR",
					Mesh = eyeMesh,
					Position = new Vector3(s * 0.0175f, 0.02f, 0.012f),
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				});
		}
	}

	/// <summary>
	/// Off the ground: grow a dead snag with a side twig right under the feet.
	/// On (or near) the ground: stand on it.
	/// </summary>
	private void BuildPerchOrGround()
	{
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain == null) return;
		Vector3 p = GlobalPosition;
		float ground = terrain.HeightAt(p.X, p.Z);
		float elev = p.Y - ground;
		switch (Perch)
		{
			case BirdPerch.Ground: elev = 0f; break;
			case BirdPerch.Stump: BuildStump(p, ground); return;
			case BirdPerch.Rock: BuildRock(p, ground); return;
			case BirdPerch.Branch: BuildBranch(p, ground, Mathf.Max(elev, 1.5f)); return;
			case BirdPerch.Snag: elev = Mathf.Max(elev, 0.5f); GlobalPosition = new Vector3(p.X, ground + elev, p.Z); break;
		}
		if (elev < 0.15f)
		{
			// on the ground: pecking about (see _Process)
			GlobalPosition = new Vector3(p.X, ground + 0.005f, p.Z);
			return;
		}
		Perched = true;
		var perch = new Node3D { Name = "Perch" };
		AddChild(perch);
		// A broken-off dead snag: bark sides, a jagged pale top the bird stands on, one
		// dead side branch. Reads as a natural perch and gives the bird a light backdrop.
		var k = new MeshKit();
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.62f, 0.58f, 0.54f);
		float gy = -elev - 0.12f;
		const int sides = 7;
		ItemMeshes.Loft(k, new[]
		{
			new ItemMeshes.Ring(new Vector3(0.01f, gy, -0.01f), 0.17f, 0.16f),
			new ItemMeshes.Ring(new Vector3(0.005f, -elev + 0.1f, -0.005f), 0.14f, 0.135f),
			new ItemMeshes.Ring(new Vector3(0f, -0.02f, 0f), 0.12f, 0.115f),
		}, sides, false, false, Vector3.Right, 2f);
		// Jagged broken top: a low fan whose rim rises and falls, highest at the back.
		var rim = new Vector3[sides];
		for (int i = 0; i < sides; i++)
		{
			float a = Mathf.Tau * i / sides;
			float jag = _rng.RandfRange(-0.012f, 0.02f) + (Mathf.Sin(a) < -0.5f ? 0.025f : 0f);
			rim[i] = new Vector3(Mathf.Cos(a) * 0.12f, -0.02f + jag, Mathf.Sin(a) * 0.115f);
		}
		k.Color = new Color(0.88f, 0.8f, 0.66f);
		k.Mat(ProcTextures.EndGrainMat);
		Vector3 top = new(0, -0.004f, 0);
		for (int i = 0; i < sides; i++)
		{
			Vector3 a = rim[i], b = rim[(i + 1) % sides];
			Vector3 n = (a - top).Cross(b - top).Normalized();
			if (n.Y < 0) n = -n;
			k.Tri(top, a, b, n, new Vector2(0.5f, 0.5f), new Vector2(a.X * 4f + 0.5f, a.Z * 4f + 0.5f), new Vector2(b.X * 4f + 0.5f, b.Z * 4f + 0.5f));
			// bark lip between the side's top ring and the jagged rim
			float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
			Vector3 s0 = new(Mathf.Cos(a0) * 0.12f, -0.02f, Mathf.Sin(a0) * 0.115f), s1 = new(Mathf.Cos(a1) * 0.12f, -0.02f, Mathf.Sin(a1) * 0.115f);
			Vector3 on = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0, Mathf.Sin((a0 + a1) * 0.5f));
			if (a.Y > s0.Y + 0.002f || b.Y > s1.Y + 0.002f)
				k.Quad(s0, s1, b, a, on);
		}
		// One dead side branch.
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.55f, 0.5f, 0.46f);
		k.Cylinder(new Vector3(0.1f, -0.2f, 0.02f), new Vector3(0.3f, -0.05f, 0.08f), 0.022f, 0.008f, 5, true, 3f);
		k.CommitTo(perch, "PerchMesh");
	}

	/// <summary>A cut stump, knee high: rough bark sides flaring into the ground, a pale ringed top the bird stands on.</summary>
	private void BuildStump(Vector3 p, float ground)
	{
		float h = _rng.RandfRange(0.38f, 0.55f), r = _rng.RandfRange(0.2f, 0.27f);
		GlobalPosition = new Vector3(p.X, ground + h, p.Z);
		Perched = true;
		var perch = new Node3D { Name = "Perch" };
		AddChild(perch);
		var k = new MeshKit();
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.58f, 0.52f, 0.46f);
		const int sides = 9;
		ItemMeshes.Loft(k, new[]
		{
			new ItemMeshes.Ring(new Vector3(0, -h - 0.15f, 0), r * 1.45f, r * 1.4f),
			new ItemMeshes.Ring(new Vector3(0.01f, -h + 0.06f, 0), r * 1.2f, r * 1.15f),
			new ItemMeshes.Ring(new Vector3(0.005f, -h * 0.45f, -0.005f), r * 1.02f, r),
			new ItemMeshes.Ring(new Vector3(0, -0.01f, 0), r, r * 0.97f),
		}, sides, false, false, Vector3.Right, 2f);
		// the saw-cut top, a shade proud of the bark so it reads as end grain
		k.Mat(ProcTextures.EndGrainMat);
		k.Color = new Color(0.86f, 0.78f, 0.64f);
		k.Cylinder(new Vector3(0, -0.012f, 0), new Vector3(0, -0.002f, 0), r * 0.97f, r * 0.95f, sides, true, 1f);
		k.CommitTo(perch, "PerchMesh");
		Solid(perch, new CylinderShape3D { Radius = r * 1.05f, Height = h }, new Vector3(0, -h * 0.5f, 0));
	}

	/// <summary>A mossy rock, its top just under the bird's feet.</summary>
	private void BuildRock(Vector3 p, float ground)
	{
		var radii = new Vector3(_rng.RandfRange(0.42f, 0.55f), _rng.RandfRange(0.28f, 0.36f), _rng.RandfRange(0.36f, 0.48f));
		GlobalPosition = new Vector3(p.X, ground + radii.Y * 0.82f, p.Z);
		Perched = true;
		var perch = new Node3D { Name = "Perch" };
		AddChild(perch);
		var k = new MeshKit();
		k.Color = new Color(0.86f, 0.86f, 0.82f);
		// its top flattened a little where the bird stands (the blob's jitter kept small so the feet meet it)
		k.Mat(ProcTextures.RockMat).Blob(new Vector3(0, -radii.Y * 0.98f, 0), radii, Seed() + 5, 0.06f, true, 0.8f);
		k.CommitTo(perch, "PerchMesh");
		Solid(perch, new SphereShape3D { Radius = Mathf.Min(radii.X, radii.Z) * 0.9f }, new Vector3(0, -radii.Y, 0));
	}

	/// <summary>A young tree beside the way, the bird out on its low limb: a leaning trunk, a limb reaching out to it, a few
	/// twigs past the bird and a light crown of leaves above, so it sits in a tree and is still easy to frame.</summary>
	private void BuildBranch(Vector3 p, float ground, float height)
	{
		GlobalPosition = new Vector3(p.X, ground + height, p.Z);
		Perched = true;
		var perch = new Node3D { Name = "Perch" };
		AddChild(perch);
		var k = new MeshKit();
		// the trunk stands a metre off to one side (local -X), leaning a hair
		float side = _rng.Randf() < 0.5f ? -1f : 1f;
		var foot = new Vector3(-0.95f * side, -height - 0.2f, _rng.RandfRange(-0.15f, 0.15f));
		var top = foot + new Vector3(_rng.RandfRange(-0.15f, 0.15f), height + _rng.RandfRange(1.4f, 2.0f), _rng.RandfRange(-0.15f, 0.15f));
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.6f, 0.55f, 0.5f);
		ItemMeshes.Loft(k, new[]
		{
			new ItemMeshes.Ring(foot, 0.16f, 0.15f),
			new ItemMeshes.Ring(foot.Lerp(top, 0.15f), 0.11f, 0.105f),
			new ItemMeshes.Ring(foot.Lerp(top, 0.6f), 0.085f, 0.08f),
			new ItemMeshes.Ring(top, 0.03f, 0.03f),
		}, 8, false, false, Vector3.Right, 2f);
		// the limb: from the trunk a little above the bird, sagging out under its feet, on past it
		Vector3 Along(float t) => foot + (top - foot) * t;
		float tAt = (height + 0.2f + 0.18f) / (top.Y - foot.Y);
		var root = Along(tAt);
		var under = new Vector3(0, -0.025f, 0);
		var tip = new Vector3(0.45f * side, 0.06f, _rng.RandfRange(-0.1f, 0.1f));
		k.Cylinder(root, under, 0.045f, 0.028f, 6, false, 2f);
		k.Cylinder(under, tip, 0.028f, 0.008f, 5, true, 2f);
		// twigs off the limb and the trunk
		for (int i = 0; i < 5; i++)
		{
			float t = _rng.RandfRange(0.2f, 0.9f);
			var a = i < 3 ? under.Lerp(tip, t) : Along(_rng.RandfRange(0.55f, 0.9f));
			var b = a + new Vector3(_rng.RandfRange(-0.25f, 0.25f), _rng.RandfRange(0.1f, 0.3f), _rng.RandfRange(-0.25f, 0.25f));
			k.Cylinder(a, b, 0.012f, 0.004f, 4, true, 3f);
		}
		// a light crown, well above the bird (it never hides it): (2026-10-15, the playtest: its crown was four solid
		// leaf-textured balls, a camouflage blob against the sky) now twigs up off the top with loose clusters of single
		// leaves at their ends, the way the woods' saplings grow theirs, sky between them
		var tips = new System.Collections.Generic.List<(Vector3 at, Vector3 dir)>();
		for (int b = 0; b < 6; b++)
		{
			float a = _rng.RandfRange(0, Mathf.Tau);
			var from = Along(_rng.RandfRange(0.72f, 0.98f));
			var dir = new Vector3(Mathf.Cos(a), _rng.RandfRange(0.35f, 0.9f), Mathf.Sin(a)).Normalized();
			var to = from + dir * _rng.RandfRange(0.4f, 0.8f);
			k.Mat(ProcTextures.BarkMat);
			k.Color = new Color(0.55f, 0.52f, 0.47f);
			k.Cylinder(from, to, 0.014f, 0.004f, 4, true, 3f);
			tips.Add((from.Lerp(to, 0.6f), dir));
			tips.Add((to, dir));
		}
		tips.Add((top, Vector3.Up));
		tips.Add((tip, new Vector3(side, 0.3f, 0).Normalized()));   // a few on the limb's end
		k.Mat(BunkerParts.BunkerTextures.IvyMat);
		foreach (var (at, dir) in tips)
			for (int i = 0; i < 8; i++)
			{
				var lp = at + new Vector3(_rng.RandfRange(-0.25f, 0.25f), _rng.RandfRange(-0.15f, 0.2f), _rng.RandfRange(-0.25f, 0.25f));
				var nrm = (Vector3.Up * 0.6f + new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-0.2f, 0.6f), _rng.RandfRange(-1f, 1f))).Normalized();
				var t = (dir + new Vector3(_rng.RandfRange(-0.6f, 0.6f), -0.4f, _rng.RandfRange(-0.6f, 0.6f))).Normalized();
				t = (t - nrm * t.Dot(nrm)).Normalized();
				if (!t.IsFinite() || t.LengthSquared() < 0.5f) continue;
				var sd = nrm.Cross(t).Normalized();
				float sz = _rng.RandfRange(0.09f, 0.15f), hs = sz * 0.5f;
				float l = _rng.RandfRange(0.85f, 1.15f), yel = _rng.Randf() < 0.25f ? _rng.RandfRange(0.4f, 0.9f) : 0f;
				k.Color = new Color(l, l, l * 0.9f).Lerp(new Color(l * 2.2f, l * 1.6f, l * 0.4f), yel);
				int cell = _rng.Randf() < 0.6f ? 3 : _rng.Randf() < 0.5f ? 1 : 0;
				float u0 = (cell % 2) * 0.5f, v0 = (cell / 2) * 0.5f;
				k.Quad(lp - sd * hs, lp + sd * hs, lp + t * sz + sd * hs, lp + t * sz - sd * hs, nrm,
					new Vector2(u0, v0 + 0.5f), new Vector2(u0 + 0.5f, v0 + 0.5f), new Vector2(u0 + 0.5f, v0), new Vector2(u0, v0));
			}
		k.CommitTo(perch, "PerchMesh");
		var mid = foot.Lerp(top, 0.5f);
		Solid(perch, new CylinderShape3D { Radius = 0.13f, Height = top.Y - foot.Y }, mid);
	}

	/// <summary>The perch is solid to walk into (on the world layer), so the player stands beside it, not in it.</summary>
	private static void Solid(Node3D perch, Shape3D shape, Vector3 at)
	{
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		perch.AddChild(body);
		body.AddChild(new CollisionShape3D { Shape = shape, Position = at });
	}

	// ───────────────────────────── idle ─────────────────────────────

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || _model == null || !IsInstanceValid(_model)) return;
		float dt = (float)delta;
		if (Flying) { Fly(dt); return; }
		if (Photographed) return;

		_headTimer -= dt;
		if (_headTimer <= 0f)
		{
			_headTimer = _rng.RandfRange(0.35f, 1.8f);
			_headYawTarget = Mathf.DegToRad(_rng.RandfRange(-65f, 65f));
			_headTiltTarget = Mathf.DegToRad(_rng.RandfRange(-12f, 18f));
			// on the ground it feeds: now and then a quick peck down at the litter
			if (!Perched && _rng.Randf() < 0.4f)
			{
				_headTimer = _rng.RandfRange(0.18f, 0.32f);
				_headYawTarget = Mathf.DegToRad(_rng.RandfRange(-15f, 15f));
				_headTiltTarget = Mathf.DegToRad(_rng.RandfRange(55f, 70f));
			}
		}
		_bodyTimer -= dt;
		if (_bodyTimer <= 0f)
		{
			_bodyTimer = _rng.RandfRange(BobSeconds * 1.8f, BobSeconds * 4.5f);
			_bodyYawTarget = Mathf.Clamp(_bodyYaw + Mathf.DegToRad(_rng.RandfRange(-35f, 35f)), -1.2f, 1.2f);
			if (!Perched) _hop = 0f;
		}
		Sing(dt);

		// Birds snap, they don't drift.
		float kHead = 1f - Mathf.Exp(-28f * dt), kBody = 1f - Mathf.Exp(-14f * dt);
		_headYaw = Mathf.Lerp(_headYaw, _headYawTarget, kHead);
		_headTilt = Mathf.Lerp(_headTilt, _headTiltTarget, kHead);
		_bodyYaw = Mathf.Lerp(_bodyYaw, _bodyYawTarget, kBody);
		SetHead(_headYaw, _headTilt);
		// (the modelled bird) its tail flicks now and then as it looks about
		if (_skel != null && _bTail >= 0)
		{
			_tailFlick = Mathf.MoveToward(_tailFlick, 0f, dt * 4f);
			if (_rng.Randf() < dt * 0.5f) _tailFlick = 1f;
			PoseTurn(_bTail, _bBody, new Vector3(1, 0, 0), 0.25f * Mathf.Sin(_tailFlick * Mathf.Pi));
		}

		float y = 0f;
		if (_hop >= 0f)
		{
			_hop += dt / 0.16f;
			y = Mathf.Sin(Mathf.Clamp(_hop, 0f, 1f) * Mathf.Pi) * BobHeight;
			if (_hop >= 1f) _hop = -1f;
		}
		_model.Position = new Vector3(0, y, 0);
		_model.Rotation = new Vector3(0, _bodyYaw, 0);
	}

	// ───────────────────────────── voice ─────────────────────────────

	/// <summary>Within earshot of the walker, an occasional quiet call from right where the bird sits,
	/// so it can be found by ear and the walk has something to stop for. The omen croaks instead.
	/// Quiet once the stairs have been found: the minigame is over by then.</summary>
	private const float SingRange = 35f;
	private float _singTimer = -1f;
	private Node3D _listener;

	/// <summary>A songbird call, 1-8 but never 7 (the owner, 2026-10-15).</summary>
	private static int BirdCall(RandomNumberGenerator rng) { int n = rng.RandiRange(1, 7); return n >= 7 ? 8 : n; }

	private void Sing(float dt)
	{
		if (_singTimer < 0f) _singTimer = _rng.RandfRange(1f, 5f);
		_singTimer -= dt;
		if (_singTimer > 0f) return;
		_singTimer = IsOmen ? _rng.RandfRange(9f, 16f) : _rng.RandfRange(4.5f, 10f);

		if (Systems.StoryManager.Instance is { StairsClimbed: true }) return;
		if (_listener == null || !IsInstanceValid(_listener)) _listener = GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (_listener == null || _listener.GlobalPosition.DistanceTo(GlobalPosition) > SingRange) return;

		string path = IsOmen ? $"res://assets/audio/sfx/raven_0{_rng.RandiRange(1, 2)}.wav" : $"res://assets/audio/sfx/bird_0{BirdCall(_rng)}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = "Birds",
			UnitSize = 4f, MaxDistance = SingRange + 5f,
			VolumeDb = IsOmen ? 4f : -6f + _rng.RandfRange(-2f, 1f),
			PitchScale = _rng.RandfRange(0.94f, 1.08f),
			Position = new Vector3(0, 0.3f, 0),
		};
		AddChild(voice);
		voice.Finished += voice.QueueFree;
		voice.Play();
		if (!Perched) _hop = 0f;   // a little hop as it calls
	}

	/// <summary>Startles up and away, then gone, rather than just vanishing in place. The perch stays.
	/// The omen instead lunges straight at whoever took the shot, for a jumpscare.</summary>
	public void Capture()
	{
		if (Photographed) return;
		Photographed = true;
		RemoveFromGroup("photo_birds");
		if (_model == null || !IsInstanceValid(_model)) return;
		var model = _model;
		var tween = CreateTween();
		if (IsOmen && GetTree().GetFirstNodeInGroup("player") is Node3D player)
		{
			Vector3 face = player.GlobalPosition + Vector3.Up * 1.6f;
			Vector3 dir = (face - GlobalPosition).Normalized();
			Vector3 through = ToLocal(face + dir * 1.5f);   // fly past the player, not just up to them
			// (the modelled bird) wings flung open, head thrust out, at the face
			if (_anim != null && _anim.HasAnimation("lunge")) _anim.Play("lunge", -1, 2.4f);
			tween.TweenProperty(model, "position", through, 0.2f)
				.SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.In);
		}
		else
		{
			tween.Kill();
			TakeOff();
			return;
		}
		tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(model)) model.QueueFree(); }));
	}

	// ───────────────────────────── flight ─────────────────────────────

	/// <summary>In the air (after its picture), until it's away behind the trees; then it's gone.</summary>
	public bool Flying { get; private set; }
	/// <summary>For tests: how far from the eye it was when it went, and whether it was out of sight then.</summary>
	public float GoneAtDistance { get; private set; } = -1f;
	public bool GoneBehind { get; private set; }

	private Vector3 _vel, _away;
	private float _flyT, _flapPhase, _burst, _weave, _checkT, _cruiseY;
	private bool _gliding;
	private ForestTerrain _terrain;

	/// <summary>
	/// Off, the way small birds go (the owner: "better flight animations ... disappear by flying away further behind the
	/// trees out of view, right now they just disappear in the air"): a spring up and away from whoever is there with a
	/// flurry of wingbeats; then out through the trees in bounding flight (a burst of beats, rising; a moment with the wings
	/// shut, dropping), weaving a little, climbing to treetop height; gone only once it is far off and behind a trunk (or
	/// out of view, or lost in the fog), never in plain sight close by.
	/// </summary>
	private void TakeOff()
	{
		Flying = true;
		_terrain = GroundSnap.FindTerrain(this);
		var from = _model.GlobalPosition;
		var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		var away = player != null ? (from - player.GlobalPosition) with { Y = 0 } : Vector3.Zero;
		if (away.LengthSquared() < 0.01f) away = new Vector3(_rng.RandfRange(-1f, 1f), 0, _rng.RandfRange(-1f, 1f));
		_away = away.Normalized().Rotated(Vector3.Up, _rng.RandfRange(-0.7f, 0.7f));
		_vel = _away * 2.2f + Vector3.Up * 3.2f;
		_weave = _rng.RandfRange(0f, 6.28f);
		float ground = _terrain?.HeightAt(from.X, from.Z) ?? from.Y;
		_cruiseY = ground + _rng.RandfRange(7f, 11f);
		_burst = 0.9f;   // (the first burst long: it has to get up)
		_model.TopLevel = true;   // (it keeps where it is, and leaves its perch)
		_model.GlobalPosition = from;
		SetHead(0f, 0f);
		SetWings(true, 0.6f);
		string fl = $"res://assets/audio/sfx/wing_flutter_0{_rng.RandiRange(1, 3)}.wav";
		if (ResourceLoader.Exists(fl))
		{
			var a = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(fl), Bus = "Birds", UnitSize = 3f, MaxDistance = 40f, VolumeDb = -4f, PitchScale = _rng.RandfRange(0.92f, 1.1f) };
			AddChild(a);
			a.GlobalPosition = from;
			a.Finished += a.QueueFree;
			a.Play();
		}
	}

	private void Fly(float dt)
	{
		_flyT += dt;
		var pos = _model.GlobalPosition;
		// bounding: a burst of beats (rising), then the wings shut a moment (falling); flapping hard all the way at first
		_burst -= dt;
		if (_burst <= 0f)
		{
			_gliding = !_gliding && _flyT > 1.2f;
			_burst = _gliding ? _rng.RandfRange(0.14f, 0.26f) : _rng.RandfRange(0.3f, 0.5f);
		}
		float speed = Mathf.Lerp(3.5f, 9f, Mathf.Clamp(_flyT / 1.4f, 0f, 1f));
		// weaving a little as it goes; climbing to its height, then bounding along it
		_weave += dt * 1.3f;
		var dir = _away.Rotated(Vector3.Up, Mathf.Sin(_weave) * 0.35f);
		float climb = _flyT < 1f ? 3.4f : Mathf.Clamp((_cruiseY - pos.Y) * 0.8f, -1.5f, 2.5f) + (_gliding ? -1.6f : 1.0f);
		var want = dir * speed + Vector3.Up * climb;
		_vel = _vel.Lerp(want, 1f - Mathf.Exp(-3.5f * dt));
		pos += _vel * dt;
		if (_terrain != null) pos.Y = Mathf.Max(pos.Y, _terrain.HeightAt(pos.X, pos.Z) + 1.2f);
		// facing along its way (the beak is +Z), its nose following the climb
		var fwd = _vel.LengthSquared() > 0.01f ? _vel.Normalized() : _away;
		var basis = Basis.LookingAt(-fwd, Vector3.Up).Scaled(Vector3.One * ModelScale);
		_model.GlobalTransform = new Transform3D(basis, pos);
		// the wings: beating fast (faster getting up), or shut tight along the body
		if (!_gliding) _flapPhase += dt * Mathf.Tau * (_flyT < 1f ? 17f : 13f);
		SetWings(!_gliding, 0.25f + 0.95f * Mathf.Sin(_flapPhase));   // (up past level, and down below it)
		// gone: only far off and out of sight behind something (or out of view, or lost in the fog, or long since away)
		_checkT -= dt;
		if (_checkT > 0f) return;
		_checkT = 0.15f;
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;
		float d = cam.GlobalPosition.DistanceTo(pos);
		bool behind = false;
		if (d > 22f)
		{
			var q = PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, pos, 1);
			behind = GetWorld3D().DirectSpaceState.IntersectRay(q).Count > 0;
		}
		bool unseen = !cam.IsPositionInFrustum(pos);
		if ((d > 22f && (behind || unseen)) || d > 55f || _flyT > 16f)
		{
			GoneAtDistance = d;
			GoneBehind = behind || unseen;
			Flying = false;
			_model.QueueFree();
			_model = null;
		}
	}
}

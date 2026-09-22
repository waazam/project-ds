using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Cheap, procedurally built set dressing for the park: trailhead sign, info
/// kiosk, trash can, vault toilet, parking bumpers, trail markers, and the
/// small wrong things along the trail. One node per prop, placed by hand in
/// the level (usually with a GroundSnap or TrailAnchor child).
/// Local frame: the prop's "front" faces +Z.
/// </summary>
[Tool]
[GlobalClass]
public partial class ParkProp : Node3D
{
	public enum PropKind
	{
		TrailheadSign, InfoBoard, TrashCan, VaultToilet, ParkingBumper, TrailMarker,
		Backpack, Boot, WalkingStick, CutLog, Boulder, TornMap, Tent, Mushrooms,
		// append only: scenes store the kind as a number
		Wildflowers, HoledStone, TrailRegister, Car, CameraItem,
	}

	[Export] public PropKind Kind = PropKind.TrailMarker;
	/// <summary>Text for props that carry some (trail marker number).</summary>
	[Export] public string Label = "1";
	/// <summary>If &gt; 0, keeps procedural trees/rocks/foliage out of this radius.</summary>
	[Export] public float ClearRadius = 0f;
	[Export] public bool ClearFoliage = true;
	[Export] public int Seed = 1;
	/// <summary>TrailheadSign only: trail distance of the directional signpost it places (&lt; 0 = none).</summary>
	[Export] public float DirectionSignDistance = 9f;

	private MeshKit _k;
	private StaticBody3D _body;
	private Node3D _gen;

	public override void _Ready() => Build();

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_gen = new Node3D { Name = "Generated" };
		AddChild(_gen);
		_k = new MeshKit();
		_body = null;

		switch (Kind)
		{
			case PropKind.TrailheadSign: TrailheadSign(); break;
			case PropKind.InfoBoard: InfoBoard(); break;
			case PropKind.TrashCan: TrashCan(); break;
			case PropKind.VaultToilet: VaultToilet(); break;
			case PropKind.ParkingBumper: Bumper(); break;
			case PropKind.TrailMarker: TrailMarker(); break;
			case PropKind.Backpack: Backpack(); break;
			case PropKind.Boot: Boot(); break;
			case PropKind.WalkingStick: WalkingStick(); break;
			case PropKind.CutLog: CutLog(); break;
			case PropKind.Boulder: Boulder(); break;
			case PropKind.TornMap: TornMap(); break;
			case PropKind.Tent: Tent(); break;
			case PropKind.Mushrooms: Mushrooms(); break;
			case PropKind.Wildflowers: Wildflowers(); break;
			case PropKind.HoledStone: HoledStone(); break;
			case PropKind.TrailRegister: TrailRegister(); break;
			case PropKind.Car: Car(); break;
			case PropKind.CameraItem: CameraItem(); break;
		}
		if (!_k.IsEmpty) _k.CommitTo(_gen, "Mesh");
		if (ClearRadius > 0f && !Engine.IsEditorHint())
			_gen.AddChild(new ClearZone { Name = "ClearZone", Radius = ClearRadius, ClearFoliage = ClearFoliage });
	}

	// ---------------------------------------------------------------- helpers

	private void Col(Vector3 center, Vector3 size, Basis? rot = null)
	{
		if (Engine.IsEditorHint()) return;
		if (_body == null)
		{
			_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
			_gen.AddChild(_body);
		}
		_body.AddChild(new CollisionShape3D
		{
			Transform = new Transform3D(rot ?? Basis.Identity, center),
			Shape = new BoxShape3D { Size = size },
		});
	}

	private Label3D Text(string text, Vector3 pos, int fontSize, float pixel, Color color, float width = 0f)
	{
		var l = new Label3D
		{
			Text = text, Position = pos, FontSize = fontSize, PixelSize = pixel, Modulate = color,
			Shaded = true, AlphaCut = Label3D.AlphaCutMode.Discard, OutlineSize = 0,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			HorizontalAlignment = HorizontalAlignment.Center, DoubleSided = false,
		};
		if (width > 0f) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.Width = width; }
		_gen.AddChild(l);
		return l;
	}

	private static StandardMaterial3D Tint(string key, Color c, float rough = 0.95f)
		=> ProcTextures.Flat(key, c, rough);

	// ---------------------------------------------------------------- props

	private void TrailheadSign()
	{
		// Park entrance sign: three routed planks between two heavy square posts.
		var post = PropTextures.PostMat;
		var plank = PropTextures.SignPlankMat;
		const float postW = 0.2f, postH = 2.35f, bw = 2.2f, pt = 0.06f;
		_k.Color = new Color(0.9f, 0.88f, 0.85f);
		foreach (float x in new[] { -1.02f, 1.02f })
		{
			_k.Mat(post);
			SignKit.Post(_k, new Vector3(x, -0.4f, 0), postH + 0.4f, postW, 1.5f);
			Col(new Vector3(x, postH * 0.5f, 0), new Vector3(postW + 0.02f, postH, postW + 0.02f));
		}
		// planks, top to bottom
		float[] ph = { 0.36f, 0.3f, 0.3f };
		float y = 2.1f, z = postW * 0.5f + pt * 0.5f + 0.002f;
		var centers = new float[3];
		for (int i = 0; i < 3; i++)
		{
			float cy = y - ph[i] * 0.5f;
			centers[i] = cy;
			float shade = 0.86f + 0.1f * Mathf.Abs(Mathf.Sin(i * 7.1f + Seed));
			_k.Color = new Color(shade, shade * 0.98f, shade * 0.95f);
			_k.Mat(plank).Box(new Vector3(0, cy, z), new Vector3(bw, ph[i] - 0.014f, pt), 1.1f);
			y -= ph[i];
		}
		Col(new Vector3(0, 2.1f - 0.48f, z), new Vector3(bw, 0.96f, pt + 0.02f));
		// cap board keeping the rain off
		_k.Color = new Color(0.8f, 0.78f, 0.75f);
		_k.Mat(post).Box(new Vector3(0, 2.14f, z - 0.01f), new Vector3(bw + 0.36f, 0.07f, 0.2f), 1.5f);
		_k.Color = Colors.White;

		float face = z + pt * 0.5f;
		var fb = Basis.Identity;
		SignKit.Text(_gen, "OVERLOOK", new Vector3(0, centers[0] - 0.005f, face), fb, 0.23f);
		SignKit.Text(_gen, "PARK", new Vector3(0, centers[1] + 0.02f, face), fb, 0.17f);
		SignKit.Text(_gen, "Blackfern Trail   2.1 mi", new Vector3(0, centers[2] + 0.055f, face), fb, 0.12f);
		SignKit.Text(_gen, "Clearwater Loop   CLOSED", new Vector3(0, centers[2] - 0.065f, face), fb, 0.1f, SignKit.Carve * 0.8f);

		// The directional post where the trail leaves the lot. Placed by trail
		// distance so it follows edits to the trail curve.
		if (!Engine.IsEditorHint() && DirectionSignDistance >= 0f)
		{
			var dirSign = new SignPost
			{
				Name = "DirectionSign",
				Style = SignPost.SignStyle.Directional,
				Boards = new[] { "Blackfern Trail >", "Overlook >", "Ranger Station <" },
				LeanDegrees = new Vector2(1.5f, -1.2f),
				Seed = Seed + 5,
			};
			dirSign.AddChild(new TrailAnchor { Distance = DirectionSignDistance, Offset = -3.3f, YawDegrees = 28f, HeightOffset = -0.03f });
			_gen.AddChild(dirSign);
		}
	}

	private void InfoBoard()
	{
		var post = PropTextures.PostMat;
		var plank = PropTextures.SignPlankMat;
		var roof = Tint("kiosk_roof", new Color(0.10f, 0.09f, 0.08f));
		_k.Color = new Color(0.9f, 0.88f, 0.85f);
		foreach (float x in new[] { -0.84f, 0.84f })
		{
			_k.Mat(post).Box(new Vector3(x, 1.05f, 0), new Vector3(0.16f, 2.9f, 0.16f), 1.5f);
			Col(new Vector3(x, 1.25f, 0), new Vector3(0.17f, 2.5f, 0.17f));
		}
		// backboard: horizontal planks
		for (int i = 0; i < 5; i++)
		{
			float shade = 0.82f + 0.14f * Mathf.Abs(Mathf.Sin(i * 3.7f + 1f));
			_k.Color = new Color(shade, shade * 0.98f, shade * 0.95f);
			_k.Mat(plank).Box(new Vector3(0, 1.06f + i * 0.22f, 0f), new Vector3(1.52f, 0.21f, 0.04f), 1.1f);
		}
		Col(new Vector3(0, 1.5f, 0f), new Vector3(1.5f, 1.1f, 0.05f));
		// frame
		_k.Color = new Color(0.8f, 0.78f, 0.75f);
		_k.Mat(post);
		_k.Box(new Vector3(0, 2.07f, 0.035f), new Vector3(1.6f, 0.07f, 0.05f), 1.5f);
		_k.Box(new Vector3(0, 0.93f, 0.035f), new Vector3(1.6f, 0.07f, 0.05f), 1.5f);
		// routed header board under the roof
		_k.Color = new Color(0.92f, 0.9f, 0.87f);
		_k.Mat(plank).Box(new Vector3(0, 2.25f, 0.06f), new Vector3(1.6f, 0.24f, 0.05f), 1.1f);
		_k.Color = Colors.White;
		SignKit.Text(_gen, "BLACKFERN TRAIL", new Vector3(0, 2.245f, 0.085f), Basis.Identity, 0.15f);
		// gable roof
		float peak = 2.8f, eave = 2.48f;
		var tilt = new Vector3(0, peak - eave, 0.55f);
		float ang = Mathf.Atan2(tilt.Y, tilt.Z);
		_k.Mat(roof);
		_k.Box(new Vector3(0, (peak + eave) * 0.5f, 0.28f), new Vector3(2.0f, 0.05f, tilt.Length() + 0.05f), 1f, Basis.FromEuler(new Vector3(ang, 0, 0)));
		_k.Box(new Vector3(0, (peak + eave) * 0.5f, -0.28f), new Vector3(2.0f, 0.05f, tilt.Length() + 0.05f), 1f, Basis.FromEuler(new Vector3(-ang, 0, 0)));
		_k.Color = new Color(0.8f, 0.78f, 0.75f);
		_k.Mat(post).Box(new Vector3(0, 2.44f, 0), new Vector3(1.84f, 0.1f, 0.12f), 1.5f);
		_k.Color = Colors.White;
		// map + notices, pinned to the planks
		float fz = 0.021f;
		var paper = ProcTextures.PaperMat;
		_k.Mat(ProcTextures.MapMat).Quad(new Vector3(-0.68f, 1.04f, fz), new Vector3(-0.02f, 1.04f, fz),
			new Vector3(-0.02f, 1.96f, fz), new Vector3(-0.68f, 1.96f, fz), Vector3.Back);
		_k.Color = new Color(0.82f, 0.8f, 0.72f);
		_k.Mat(paper).Quad(new Vector3(0.1f, 1.62f, fz), new Vector3(0.6f, 1.62f, fz),
			new Vector3(0.6f, 1.95f, fz), new Vector3(0.1f, 1.95f, fz), Vector3.Back);
		_k.Color = Colors.White;
		var ink = new Color(0.12f, 0.12f, 0.12f);
		Text("TRAIL CLOSES\nAT DUSK", new Vector3(0.35f, 1.79f, fz + 0.002f), 18, 0.0032f, ink);
		Text("BLACKFERN TRAIL", new Vector3(-0.35f, 1.9f, fz + 0.002f), 14, 0.0032f, ink);
		if (Engine.IsEditorHint()) return;

		// The readable: the park's bird card.
		PaperKit.Pinned(_gen, new Vector3(0.38f, 1.3f, fz + 0.002f), Vector3.Back, new Vector2(0.32f, 0.24f),
			PaperKit.Look.Card, "", BirdCard, Systems.Readable.NoteStyle.Printed, 1.5f, "Read", Seed + 41);
	}

	private const string BirdCard =
		"BIRDS OF BLACKFERN TRAIL\n\nNorthern Cardinal\nIndigo Bunting\nPurple Finch";

	private const string RegisterText =
		"DATE   PARTY   NAME   DESTINATION   RETURN\n\n" +
		"10/2   2   Hendry   Blackfern + overlook   4:30\n" +
		"10/5   4   Ostrowski family   overlook   3 pm\n" +
		"10/11  1   D. Paulk   Blackfern Trail\n" +
		"10/12  2   M. & T.   overlook   by lunch\n" +
		"10/18  3   Farris   Blackfern, back to lot   5\n" +
		"10/24  1  R.H.  Blackfern, the old steps  back by dark\n" +
		"10/25  1  R.H.  the steps";

	private const string BrochureText =
		"Overlook Park\n" +
		"Open year-round. Stay on marked trails.";


	private void TrashCan()
	{
		var metal = ProcTextures.MetalMat;
		_k.Mat(metal).Box(new Vector3(0, 0.5f, 0), new Vector3(0.75f, 1.0f, 0.6f), 1.5f);
		_k.Box(new Vector3(0, 1.03f, 0.0f), new Vector3(0.8f, 0.06f, 0.66f), 1.5f);
		_k.Color = new Color(0.4f, 0.4f, 0.4f);
		_k.Box(new Vector3(0, 0.95f, 0.32f), new Vector3(0.4f, 0.04f, 0.05f));
		_k.Color = Colors.White;
		Col(new Vector3(0, 0.53f, 0), new Vector3(0.8f, 1.06f, 0.66f));
		Text("PACK IT IN\nPACK IT OUT", new Vector3(0, 0.62f, 0.302f), 16, 0.003f, new Color(0.75f, 0.72f, 0.6f));
		if (Engine.IsEditorHint()) return;
		// a park brochure dropped beside it, face up in the leaves
		var at = new Vector3(0.62f, 0f, 0.42f);
		at.Y = GroundY(at) + 0.012f;
		PaperKit.Flat(_gen, at, 28f, new Vector2(0.14f, 0.24f), PaperKit.Look.Card, "", BrochureText,
			Systems.Readable.NoteStyle.Printed, "Read", Seed + 43);
	}

	/// <summary>Local height of the ground under a local point (0 without a terrain).</summary>
	private float GroundY(Vector3 local)
	{
		var terrain = Engine.IsEditorHint() || !IsInsideTree() ? null : GroundSnap.FindTerrain(this);
		if (terrain == null) return 0f;
		Vector3 w = GlobalTransform * new Vector3(local.X, 0, local.Z);
		return (GlobalTransform.AffineInverse() * new Vector3(w.X, terrain.HeightAt(w.X, w.Z), w.Z)).Y;
	}

	private void VaultToilet()
	{
		var wall = ProcTextures.ConcreteMat;
		var roof = Tint("toilet_roof", new Color(0.14f, 0.12f, 0.10f));
		var door = ProcTextures.MetalMat;
		_k.Color = new Color(0.78f, 0.70f, 0.58f);
		_k.Mat(wall).Box(new Vector3(0, 1.25f, 0), new Vector3(2.3f, 2.5f, 2.4f), 1f);
		_k.Color = Colors.White;
		_k.Mat(roof).Box(new Vector3(0, 2.62f, 0), new Vector3(2.8f, 0.14f, 2.9f), 1f, Basis.FromEuler(new Vector3(0.12f, 0, 0)));
		_k.Color = new Color(0.55f, 0.42f, 0.30f);
		_k.Mat(door).Box(new Vector3(0.25f, 1.02f, 1.22f), new Vector3(0.92f, 2.0f, 0.05f), 1f);
		_k.Color = new Color(0.5f, 0.5f, 0.5f);
		_k.Box(new Vector3(-0.1f, 1.0f, 1.26f), new Vector3(0.04f, 0.14f, 0.04f));
		_k.Color = Colors.White;
		_k.Mat(Tint("vent_pipe", new Color(0.1f, 0.1f, 0.1f))).Cylinder(new Vector3(-0.8f, 2.3f, -0.8f), new Vector3(-0.8f, 3.9f, -0.8f), 0.1f, 0.1f, 6);
		Col(new Vector3(0, 1.3f, 0), new Vector3(2.3f, 2.6f, 2.4f));
		// a taped paper on the door
		_k.Color = new Color(0.9f, 0.88f, 0.8f);
		_k.Mat(ProcTextures.PaperMat).Quad(new Vector3(0.05f, 1.42f, 1.252f), new Vector3(0.45f, 1.43f, 1.252f),
			new Vector3(0.45f, 1.72f, 1.252f), new Vector3(0.05f, 1.71f, 1.252f), Vector3.Back);
		_k.Color = Colors.White;
		Text("CLOSED\nFOR THE SEASON", new Vector3(0.25f, 1.57f, 1.255f), 16, 0.0026f, new Color(0.1f, 0.1f, 0.1f));
		Text("RESTROOM", new Vector3(0f, 2.28f, 1.21f), 20, 0.004f, new Color(0.62f, 0.56f, 0.42f));
	}

	private void Bumper()
	{
		_k.Color = new Color(0.9f, 0.9f, 0.88f);
		_k.Mat(ProcTextures.ConcreteMat).Box(new Vector3(0, 0.05f, 0), new Vector3(1.8f, 0.15f, 0.2f), 2f);
	}

	private void TrailMarker()
	{
		// Thick square post: a faded paint blaze and the number routed into the front and back faces.
		const float w = 0.15f, h = 1.15f;
		_k.Color = new Color(0.9f, 0.88f, 0.85f);
		_k.Mat(PropTextures.PostMat);
		SignKit.Post(_k, new Vector3(0, -0.3f, 0), h + 0.3f, w, 1.8f);
		_k.Color = Colors.White;
		// weathered paint blaze, mostly gone
		_k.Mat(Tint("blaze_faded", new Color(0.11f, 0.14f, 0.15f))).Box(new Vector3(0, h - 0.1f, 0), new Vector3(w + 0.004f, 0.06f, w + 0.004f));
		foreach (int side in new[] { 1, -1 })
		{
			var b = side > 0 ? Basis.Identity : Basis.FromEuler(new Vector3(0, Mathf.Pi, 0));
			float fz = side * w * 0.5f;
			SignKit.Text(_gen, Label, new Vector3(0, 0.8f, fz), b, 0.16f);
			// small routed arrow under the number, pointing up
			_k.Mat(PropTextures.RoutedMat);
			SignKit.RoutedArrow(_k, new Vector3(0, 0.6f, fz + side * 0.002f), b * Basis.FromEuler(new Vector3(0, 0, Mathf.Pi / 2f)), 0.1f, 0.05f, 1);
		}
		Col(new Vector3(0, h * 0.5f, 0), new Vector3(w + 0.02f, h, w + 0.02f));
	}

	private void Backpack()
	{
		var nylon = Tint("pack_red", new Color(0.42f, 0.13f, 0.09f));
		var dark = Tint("pack_dark", new Color(0.12f, 0.11f, 0.10f));
		var pad = Tint("pack_pad", new Color(0.30f, 0.32f, 0.20f));
		// lying on its back, top toward -Z, slightly twisted
		_k.Xf = new Transform3D(Basis.FromEuler(new Vector3(0, 0.4f, 0.12f)), Vector3.Zero);
		_k.Mat(nylon).Box(new Vector3(0, 0.11f, 0), new Vector3(0.34f, 0.22f, 0.5f));
		_k.Box(new Vector3(0, 0.2f, 0.06f), new Vector3(0.26f, 0.1f, 0.24f));          // front pocket
		_k.Box(new Vector3(0, 0.14f, -0.27f), new Vector3(0.36f, 0.2f, 0.1f));         // lid flap
		_k.Mat(dark);
		_k.Box(new Vector3(-0.1f, 0.235f, -0.02f), new Vector3(0.04f, 0.012f, 0.5f));  // compression straps
		_k.Box(new Vector3(0.1f, 0.235f, -0.02f), new Vector3(0.04f, 0.012f, 0.5f));
		_k.Box(new Vector3(0.24f, 0.03f, 0.05f), new Vector3(0.06f, 0.02f, 0.4f), 1f, Basis.FromEuler(new Vector3(0, 0.3f, 0)));  // loose shoulder strap
		_k.Mat(pad).Cylinder(new Vector3(-0.22f, 0.08f, 0.3f), new Vector3(0.22f, 0.08f, 0.3f), 0.075f, 0.075f, 7);
		_k.Xf = Transform3D.Identity;
	}

	private void Boot()
	{
		var leather = Tint("boot_leather", new Color(0.24f, 0.15f, 0.09f));
		var sole = Tint("boot_sole", new Color(0.07f, 0.07f, 0.07f));
		// build upright (toe toward -Z) then lay it on its side
		_k.Xf = new Transform3D(Basis.FromEuler(new Vector3(0, 0.9f, 1.35f)), new Vector3(0, 0.05f, 0));
		_k.Mat(sole).Box(new Vector3(0, 0.017f, 0), new Vector3(0.105f, 0.035f, 0.29f));
		_k.Mat(leather).Box(new Vector3(0, 0.07f, -0.02f), new Vector3(0.1f, 0.075f, 0.26f));
		_k.Box(new Vector3(0, 0.1f, -0.1f), new Vector3(0.09f, 0.03f, 0.1f), 1f, Basis.FromEuler(new Vector3(-0.35f, 0, 0)));
		_k.Cylinder(new Vector3(0, 0.08f, 0.07f), new Vector3(0, 0.2f, 0.08f), 0.055f, 0.05f, 7);
		_k.Mat(Tint("boot_lace", new Color(0.45f, 0.40f, 0.30f))).Box(new Vector3(0, 0.11f, -0.02f), new Vector3(0.03f, 0.01f, 0.14f));
		_k.Xf = Transform3D.Identity;
	}

	private void WalkingStick()
	{
		var wood = Tint("stick_wood", new Color(0.50f, 0.40f, 0.28f));
		var pale = Tint("stick_break", new Color(0.72f, 0.64f, 0.50f));
		Vector3 a0 = new(-0.7f, 0.018f, 0.05f), a1 = new(0.02f, 0.02f, -0.02f);
		Vector3 b0 = new(0.1f, 0.02f, 0.06f), b1 = new(0.62f, 0.018f, 0.32f);
		_k.Mat(wood).Cylinder(a0, a1, 0.016f, 0.016f, 6);
		_k.Cylinder(b0, b1, 0.016f, 0.015f, 6);
		// splintered ends
		_k.Mat(pale).Cylinder(a1, a1 + (a1 - a0).Normalized() * 0.07f, 0.014f, 0.002f, 5, false);
		_k.Cylinder(b0, b0 - (b1 - b0).Normalized() * 0.06f, 0.014f, 0.002f, 5, false);
		// rubber tip + wrist strap
		_k.Mat(Tint("stick_tip", new Color(0.06f, 0.06f, 0.06f))).Cylinder(b1, b1 + (b1 - b0).Normalized() * 0.05f, 0.018f, 0.018f, 6);
		_k.Mat(Tint("stick_strap", new Color(0.18f, 0.2f, 0.26f))).Box(new Vector3(-0.66f, 0.01f, 0.13f), new Vector3(0.03f, 0.006f, 0.16f));
	}

	private void CutLog()
	{
		var bark = ProcTextures.BarkMat;
		var end = ProcTextures.EndGrainMat;
		float r = 0.36f, y = 0.27f, gap = 1.3f;
		float xl = -7f, xr = 5.2f;
		_k.Color = new Color(0.85f, 0.8f, 0.75f);
		_k.Mat(bark).Cylinder(new Vector3(xl, y + 0.05f, 0.3f), new Vector3(-gap, y, 0), r * 1.08f, r, 8, false, 1f);
		_k.Cylinder(new Vector3(gap, y, 0), new Vector3(xr, y - 0.03f, -0.25f), r, r * 0.85f, 8, false, 1f);
		_k.Color = Colors.White;
		// saw cuts, slightly lighter end grain
		Vector3 dl = (new Vector3(-gap, y, 0) - new Vector3(xl, y + 0.05f, 0.3f)).Normalized();
		Vector3 dr = (new Vector3(xr, y - 0.03f, -0.25f) - new Vector3(gap, y, 0)).Normalized();
		_k.Mat(end).Cylinder(new Vector3(-gap, y, 0) - dl * 0.01f, new Vector3(-gap, y, 0), r - 0.005f, r - 0.005f, 8, true, 1.35f);
		_k.Cylinder(new Vector3(gap, y, 0), new Vector3(gap, y, 0) + dr * 0.01f, r - 0.005f, r - 0.005f, 8, true, 1.35f);
		// root plate at the far left, broken top at the far right
		_k.Color = new Color(0.55f, 0.45f, 0.35f);
		_k.Mat(ProcTextures.RockMat).Blob(new Vector3(xl - 0.3f, 0.55f, 0.35f), new Vector3(0.35f, 0.95f, 1.0f), Seed + 3, 0.3f);
		_k.Color = new Color(0.85f, 0.8f, 0.75f);
		_k.Mat(bark).Cylinder(new Vector3(xr, y - 0.03f, -0.25f), new Vector3(xr + 0.5f, y - 0.05f, -0.3f), r * 0.85f, 0.05f, 8, false);
		_k.Color = Colors.White;
		// stub branches
		_k.Mat(bark).Cylinder(new Vector3(-3.5f, y + 0.2f, 0.12f), new Vector3(-3.3f, y + 0.9f, 0.5f), 0.06f, 0.02f, 5, false);
		_k.Cylinder(new Vector3(3.2f, y + 0.1f, -0.12f), new Vector3(3.6f, y + 0.25f, -0.9f), 0.05f, 0.015f, 5, false);
		Col(new Vector3((xl - gap) * 0.5f, y, 0.15f), new Vector3(-gap - xl, 2 * r, 2 * r), Basis.FromEuler(new Vector3(0, Mathf.Atan2(0.3f, -gap - xl), 0)));
		Col(new Vector3((xr + gap) * 0.5f, y, -0.12f), new Vector3(xr - gap, 2 * r, 2 * r), Basis.FromEuler(new Vector3(0, Mathf.Atan2(0.25f, xr - gap), 0)));
	}

	private void Boulder()
	{
		_k.Color = new Color(0.9f, 0.9f, 0.88f);
		_k.Mat(ProcTextures.RockMat).Blob(Vector3.Zero, new Vector3(1.05f, 0.8f, 0.9f), Seed + 11, 0.2f, true, 0.8f);
		if (!Engine.IsEditorHint())
		{
			_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
			_gen.AddChild(_body);
			_body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.85f } });
		}
	}

	/// <summary>A clump of small tan and rust caps at the foot of a boulder: the shot list's one close-up. No collision.</summary>
	private void Mushrooms()
	{
		var stem = Tint("mush_stem", new Color(0.74f, 0.68f, 0.56f));
		var capTan = Tint("mush_cap_tan", new Color(0.66f, 0.5f, 0.3f));
		var capRust = Tint("mush_cap_rust", new Color(0.55f, 0.27f, 0.14f));
		var capBruised = Tint("mush_cap_bruised", new Color(0.24f, 0.17f, 0.13f));
		var gill = Tint("mush_gill", new Color(0.5f, 0.42f, 0.3f));
		float R(int i, float lo, float hi)
		{
			unchecked
			{
				uint h = (uint)(Seed * 7919 + i * 104729 + 31);
				h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
				return lo + (hi - lo) * ((h & 0xFFFF) / 65535f);
			}
		}
		const int n = 7;
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.Tau * i / n + R(i * 5, -0.3f, 0.3f);
			float d = i == 0 ? 0f : R(i * 5 + 1, 0.07f, 0.2f);
			var foot = new Vector3(Mathf.Cos(a) * d, -0.01f, Mathf.Sin(a) * d);
			float h = R(i * 5 + 2, 0.05f, 0.12f) * (i == 0 ? 1.25f : 1f);
			float cr = R(i * 5 + 3, 0.035f, 0.07f) * (i == 0 ? 1.3f : 1f);
			// stems lean a little, every one its own way
			var top = foot + new Vector3(R(i * 5 + 4, -0.2f, 0.2f) * h, h, R(i * 7 + 9, -0.2f, 0.2f) * h);
			_k.Color = Colors.White;
			_k.Mat(stem).Cylinder(foot, top, cr * 0.3f, cr * 0.22f, 5, false);
			// a small gill disc under the cap, then the cap: a squashed blob, one of them bruised dark
			_k.Mat(gill).Cylinder(top - new Vector3(0, 0.004f, 0), top + new Vector3(0, 0.004f, 0), cr * 0.85f, cr * 0.85f, 7, true);
			_k.Mat(i == 3 ? capBruised : i % 2 == 0 ? capRust : capTan);
			_k.Blob(top + new Vector3(0, cr * 0.28f, 0), new Vector3(cr, cr * 0.5f, cr * 0.95f), Seed * 13 + i, 0.08f, true, 1f);
		}
	}

	private float R(int i, float lo, float hi)
	{
		unchecked
		{
			uint h = (uint)(Seed * 7919 + i * 104729 + 31);
			h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
			return lo + (hi - lo) * ((h & 0xFFFF) / 65535f);
		}
	}

	private static StandardMaterial3D _petalMat;
	/// <summary>Flat, double-sided, vertex-coloured: petals and leaves are single quads seen from both sides.</summary>
	private static StandardMaterial3D PetalMat => _petalMat ??= new StandardMaterial3D
	{
		VertexColorUseAsAlbedo = true, Roughness = 0.9f, MetallicSpecular = 0.2f,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};

	/// <summary>
	/// A loose patch of small wildflowers, purple and white, about 3 x 2 m: thin stems, a few
	/// narrow leaves at the foot, and each head a little five-petal star (a fan of triangles)
	/// tipped toward the light. Every stem stands on the ground under it. No collision.
	/// </summary>
	private void Wildflowers()
	{
		var stem = Tint("flower_stem", new Color(0.27f, 0.36f, 0.16f));
		Color purple = new Color(0.62f, 0.38f, 0.86f).SrgbToLinear() * 1.6f, purpleDeep = new Color(0.5f, 0.28f, 0.74f).SrgbToLinear() * 1.6f, white = new Color(0.96f, 0.95f, 0.9f);
		Color leaf = new(0.25f, 0.38f, 0.14f), eye = new(0.95f, 0.78f, 0.2f);
		const int n = 150;
		for (int i = 0; i < n; i++)
		{
			// three loose clumps, thinning at the edges
			int clump = i % 3;
			Vector2 cc = clump switch { 0 => new Vector2(-0.8f, 0.1f), 1 => new Vector2(0.5f, -0.35f), _ => new Vector2(0.9f, 0.55f) };
			float a = R(i * 7, 0f, Mathf.Tau), d = Mathf.Sqrt(R(i * 7 + 1, 0f, 1f)) * (clump == 0 ? 1.0f : 0.8f);
			var foot = new Vector3(cc.X + Mathf.Cos(a) * d, 0, cc.Y + Mathf.Sin(a) * d * 0.8f);
			foot.Y = GroundY(foot) - 0.02f;
			float h = R(i * 7 + 2, 0.18f, 0.45f);
			var top = foot + new Vector3(R(i * 7 + 3, -0.06f, 0.06f), h, R(i * 7 + 4, -0.06f, 0.06f));
			_k.Color = Colors.White;
			_k.Mat(stem).Cylinder(foot, top, 0.005f, 0.0035f, 3, false, 4f);
			// a narrow leaf or two at the foot
			_k.Mat(PetalMat);
			_k.Color = leaf * R(i * 7 + 5, 0.8f, 1.15f);
			for (int l = 0; l < 2; l++)
			{
				float la = R(i * 11 + l, 0f, Mathf.Tau);
				var dir = new Vector3(Mathf.Cos(la), 0.9f, Mathf.Sin(la)).Normalized();
				var side = new Vector3(-Mathf.Sin(la), 0, Mathf.Cos(la)) * 0.012f;
				var tip = foot + dir * R(i * 11 + l + 5, 0.08f, 0.14f);
				_k.Tri(foot - side, foot + side, tip, Vector3.Up, Vector2.Zero, Vector2.Right, Vector2.Down);
			}
			// the head: five petals round a tiny eye, tipped a little off the vertical
			bool isWhite = R(i * 7 + 6, 0f, 1f) < 0.38f;
			Color pc = isWhite ? white : (R(i * 7 + 8, 0f, 1f) < 0.4f ? purpleDeep : purple);
			float pr = R(i * 7 + 9, 0.045f, 0.068f) * (isWhite ? 0.85f : 1f);
			var up = new Vector3(R(i * 7 + 10, -0.5f, 0.5f), 1f, R(i * 7 + 11, -0.5f, 0.5f)).Normalized();
			var ax = up.Cross(Vector3.Forward).Normalized();
			var az = ax.Cross(up).Normalized();
			float rot = R(i * 7 + 12, 0f, Mathf.Tau);
			_k.Color = pc * R(i * 7 + 13, 0.9f, 1.08f);
			for (int p = 0; p < 5; p++)
			{
				float p0 = rot + Mathf.Tau * p / 5f;
				Vector3 Dir(float ang) => ax * Mathf.Cos(ang) + az * Mathf.Sin(ang);
				var outer = top + Dir(p0) * pr + up * 0.006f;
				var l0 = top + Dir(p0 - 0.45f) * pr * 0.45f;
				var l1 = top + Dir(p0 + 0.45f) * pr * 0.45f;
				_k.Tri(top, l0, outer, up, new Vector2(0.5f, 0.5f), Vector2.Zero, Vector2.Right);
				_k.Tri(top, outer, l1, up, new Vector2(0.5f, 0.5f), Vector2.Right, Vector2.Down);
			}
			_k.Color = eye;
			var e0 = top + up * 0.004f;
			_k.Tri(e0 + ax * 0.008f, e0 - ax * 0.005f + az * 0.007f, e0 - ax * 0.005f - az * 0.007f, up, Vector2.Zero, Vector2.Right, Vector2.Down);
		}
		_k.Color = Colors.White;
	}

	/// <summary>
	/// A tall, thin standing stone, leaning a little, with a hole worn clean through it near the
	/// top. Built as a ring of quads between the hole's rim and the stone's outline (star-shaped
	/// about the hole), front and back faces bulged and roughened, closed by walls round the
	/// outline and through the hole. Runs 0.4 m into the ground. Front faces +Z. Solid.
	/// </summary>
	private void HoledStone()
	{
		const float height = 2.25f, sink = 0.4f, holeY = 1.55f;
		const int seg = 28, rings = 4;
		// outline: a tall rounded slab, wider at the foot, the top rounded and a little lopsided
		float HalfW(float y) => Mathf.Lerp(0.42f, 0.27f, Mathf.Clamp((y + sink) / (height + sink), 0f, 1f));
		Vector2 Outline(float ang)
		{
			// march out from the hole centre along ang until we leave the slab
			var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
			float lo = 0f, hi = 4f;
			for (int it = 0; it < 22; it++)
			{
				float m = (lo + hi) * 0.5f;
				var p = new Vector2(0, holeY) + d * m;
				bool inside = p.Y > -sink && Mathf.Abs(p.X) < HalfW(p.Y);
				float topY = height - 0.28f;
				if (p.Y > topY)
				{
					// rounded, off-centre crown
					var q = new Vector2((p.X - 0.05f) / (HalfW(topY) * 1.05f), (p.Y - topY) / (0.3f + 0.04f * p.X));
					inside &= q.LengthSquared() < 1f;
				}
				if (inside) lo = m; else hi = m;
			}
			return new Vector2(0, holeY) + d * lo;
		}
		Vector2 Hole(float ang) => new Vector2(0, holeY) + new Vector2(Mathf.Cos(ang) * 0.17f, Mathf.Sin(ang) * 0.22f)
			* (1f + 0.12f * Mathf.Sin(ang * 3f + 1.3f));
		// thickness: thin at the edges, bulging in the middle and toward the foot
		float Thick(Vector2 p, float rim)
		{
			float t = Mathf.Lerp(0.16f, 0.11f, Mathf.Clamp(p.Y / height, 0f, 1f));
			return t * (0.55f + 0.45f * Mathf.Sin(rim * Mathf.Pi)) + 0.012f * Mathf.Sin(p.X * 23f + p.Y * 17f + Seed);
		}
		var grid = new Vector3[seg + 1, rings + 1, 2];
		for (int i = 0; i <= seg; i++)
		{
			float ang = Mathf.Tau * i / seg;
			Vector2 o = Outline(ang), hl = Hole(ang);
			for (int r = 0; r <= rings; r++)
			{
				float u = r / (float)rings;   // 0 at the hole, 1 at the outline
				var p = hl.Lerp(o, u);
				// no bulge right at the hole's rim or the outline: the edges are worn round
				float th = Thick(p, Mathf.Clamp(u, 0.12f, 0.88f)) * (r == 0 || r == rings ? 0.72f : 1f);
				float jx = R(i * 13 + r, -0.012f, 0.012f), jy = R(i * 17 + r, -0.012f, 0.012f);
				grid[i, r, 0] = new Vector3(p.X + jx, p.Y + jy, th);
				grid[i, r, 1] = new Vector3(p.X + jx, p.Y + jy, -th * 0.9f);
			}
		}
		for (int r = 0; r <= rings; r++) { grid[seg, r, 0] = grid[0, r, 0]; grid[seg, r, 1] = grid[0, r, 1]; }

		// lean: tipped back a little and to one side
		var lean = Basis.FromEuler(new Vector3(Mathf.DegToRad(-4f), 0, Mathf.DegToRad(8.5f)));
		_k.Xf = new Transform3D(lean, Vector3.Zero);
		_k.Mat(ProcTextures.RockMat);
		Vector2 UV(Vector3 v) => new Vector2(v.X * 1.4f, v.Y * 1.4f);
		for (int i = 0; i < seg; i++)
			for (int r = 0; r < rings; r++)
				for (int f = 0; f < 2; f++)
				{
					Vector3 a = grid[i, r, f], b = grid[i + 1, r, f], c = grid[i + 1, r + 1, f], d = grid[i, r + 1, f];
					var nrm = (b - a).Cross(d - a).Normalized();
					if ((f == 0 && nrm.Z < 0) || (f == 1 && nrm.Z > 0)) nrm = -nrm;
					float y = (a.Y + c.Y) * 0.5f;
					// lichen-pale weathered face, darker and damp toward the ground
					float g = 2.1f + 0.4f * Mathf.Clamp(y / height, 0f, 1f) + R(i * 31 + r * 7 + f, -0.12f, 0.12f);
					_k.Color = new Color(g, g * 0.99f, g * 0.93f);
					_k.Quad(a, b, c, d, nrm, UV(a), UV(b), UV(c), UV(d));
				}
		// walls: round the outline (outward) and through the hole (inward)
		for (int i = 0; i < seg; i++)
			foreach (int r in new[] { 0, rings })
			{
				Vector3 a = grid[i, r, 0], b = grid[i + 1, r, 0], c = grid[i + 1, r, 1], d = grid[i, r, 1];
				var mid = (a + b) * 0.5f;
				var outward = new Vector3(mid.X, mid.Y - holeY, 0).Normalized() * (r == 0 ? -1f : 1f);
				_k.Color = r == 0 ? new Color(1.3f, 1.27f, 1.2f) : new Color(1.95f, 1.92f, 1.82f);
				_k.Quad(a, b, c, d, outward, new Vector2(0, a.Z * 3f), new Vector2(0.2f, b.Z * 3f), new Vector2(0.2f, c.Z * 3f), new Vector2(0, d.Z * 3f));
			}
		_k.Xf = Transform3D.Identity;
		_k.Color = Colors.White;
		// a few small stones round its foot
		for (int i = 0; i < 4; i++)
		{
			float a = R(i * 3 + 200, 0f, Mathf.Tau);
			var p = new Vector3(Mathf.Cos(a) * R(i * 3 + 201, 0.5f, 0.9f), 0, Mathf.Sin(a) * R(i * 3 + 202, 0.35f, 0.7f));
			p.Y = GroundY(p);
			float rr = R(i * 3 + 203, 0.08f, 0.16f);
			_k.Color = new Color(0.8f, 0.8f, 0.77f);
			_k.Mat(ProcTextures.RockMat).Blob(p, new Vector3(rr * 1.3f, rr * 0.6f, rr), Seed * 5 + i, 0.2f, true, 1f);
		}
		_k.Color = Colors.White;
		Col(lean * new Vector3(0, (height - sink) * 0.5f, 0), new Vector3(0.7f, height + sink, 0.32f), lean);
	}

	/// <summary>
	/// The trail register: a weathered box on a post, sloped like a lectern, its lid propped open
	/// against a hook, the register lying open inside (readable). Front (the reader's side) is +Z.
	/// </summary>
	private void TrailRegister()
	{
		var post = PropTextures.PostMat;
		var plank = PropTextures.SignPlankMat;
		_k.Color = new Color(0.9f, 0.88f, 0.85f);
		_k.Mat(post);
		SignKit.Post(_k, new Vector3(0, -0.35f, 0), 1.33f, 0.11f, 1.8f, 0.02f);
		Col(new Vector3(0, 0.5f, 0), new Vector3(0.13f, 1.0f, 0.13f));
		// the box, tipped toward the reader
		var tilt = Basis.FromEuler(new Vector3(Mathf.DegToRad(18f), 0, 0));
		var c = new Vector3(0, 1.08f, 0.02f);
		const float bw = 0.42f, bd = 0.3f, bh = 0.1f;
		_k.Color = new Color(0.86f, 0.84f, 0.8f);
		_k.Mat(plank);
		_k.Box(c + tilt * new Vector3(0, -bh * 0.5f + 0.01f, 0), new Vector3(bw, 0.02f, bd), 1.2f, tilt);                 // floor
		_k.Box(c + tilt * new Vector3(0, 0, bd * 0.5f), new Vector3(bw, bh, 0.02f), 1.2f, tilt);                          // front
		_k.Box(c + tilt * new Vector3(0, 0, -bd * 0.5f), new Vector3(bw, bh, 0.02f), 1.2f, tilt);                         // back
		_k.Box(c + tilt * new Vector3(-bw * 0.5f, 0, 0), new Vector3(0.02f, bh, bd), 1.2f, tilt);
		_k.Box(c + tilt * new Vector3(bw * 0.5f, 0, 0), new Vector3(0.02f, bh, bd), 1.2f, tilt);
		// lid, hinged at the back edge and propped past upright
		var hinge = c + tilt * new Vector3(0, bh * 0.5f, -bd * 0.5f);
		var lidB = Basis.FromEuler(new Vector3(Mathf.DegToRad(-100f), 0, 0));
		_k.Color = new Color(0.8f, 0.78f, 0.74f);
		_k.Box(hinge + lidB * new Vector3(0, 0.012f, bd * 0.5f), new Vector3(bw + 0.03f, 0.022f, bd + 0.02f), 1.2f, lidB);
		Col(c, new Vector3(bw, bh + 0.05f, bd), tilt);
		_k.Color = Colors.White;
		SignKit.Text(_gen, "TRAIL REGISTER", c + tilt * new Vector3(0, -0.005f, bd * 0.5f + 0.0115f), tilt, 0.045f);
		if (Engine.IsEditorHint()) return;
		// the register lying open in the box (face +Y of the tipped box)
		var sheetB = tilt * Basis.FromEuler(new Vector3(-Mathf.Pi / 2f, 0, 0));
		PaperKit.Sheet(_gen, c + tilt * new Vector3(0, -bh * 0.5f + 0.03f, 0), sheetB, new Vector2(0.36f, 0.25f),
			PaperKit.Look.Ledger, "Trail register", RegisterText, Systems.Readable.NoteStyle.Handwritten, "Read the register", Seed + 42);
		// a stub of pencil on a string
		_k.Mat(Tint("pencil", new Color(0.55f, 0.45f, 0.15f)));
		_k.Cylinder(c + tilt * new Vector3(0.12f, -bh * 0.5f + 0.04f, 0.09f), c + tilt * new Vector3(0.02f, -bh * 0.5f + 0.04f, 0.12f), 0.005f, 0.005f, 5, true);
	}

	private static readonly Dictionary<string, StandardMaterial3D> _carMats = new();
	private static StandardMaterial3D CarMat(string key, Color c, float rough, float spec, bool vertexColor = true)
	{
		if (_carMats.TryGetValue(key, out var m)) return m;
		m = new StandardMaterial3D
		{
			AlbedoColor = c, Roughness = rough, MetallicSpecular = spec, VertexColorUseAsAlbedo = vertexColor,
			AlbedoTexture = PropTextures.Fur(), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		_carMats[key] = m;
		return m;
	}

	/// <summary>
	/// The player's own car, backed into a bay: a plain, slightly old mid-90s estate in faded dark
	/// green. Boxy lower body, a glass house with body-colour pillars, black trim and bumpers, four
	/// wheels, and the rear hatch lifted open over a carpeted load floor with a duffel and a water
	/// bottle in it (the camera lying there is OpeningAtCar's TrunkCamera). Local frame: the hatch
	/// end is +Z (the prop's front), the nose -Z; origin on the ground at the car's middle. Solid.
	/// </summary>
	private void Car()
	{
		var paint = CarMat("car_paint", new Color(0.2f, 0.29f, 0.24f).SrgbToLinear() * 1.9f, 0.5f, 0.45f);
		var glass = CarMat("car_glass", new Color(0.07f, 0.09f, 0.1f), 0.12f, 0.8f, false);
		var trim = CarMat("car_trim", new Color(0.07f, 0.07f, 0.075f), 0.8f, 0.25f, false);
		var tyre = CarMat("car_tyre", new Color(0.035f, 0.035f, 0.035f), 0.95f, 0.1f, false);
		var hub = CarMat("car_hub", new Color(0.45f, 0.46f, 0.47f), 0.45f, 0.6f, false);
		var inner = CarMat("car_inner", new Color(0.13f, 0.13f, 0.14f), 0.95f, 0.15f, false);
		var lamp = CarMat("car_lamp", new Color(0.55f, 0.08f, 0.06f), 0.3f, 0.6f, false);
		var head = CarMat("car_head", new Color(0.75f, 0.74f, 0.66f), 0.25f, 0.7f, false);
		const float L = 4.6f, W = 1.74f, zN = -L * 0.5f, zR = L * 0.5f;
		const float sill = 0.3f, belt = 0.88f, roof = 1.43f;
		float zWs = -0.95f, zRoofF = -0.12f, zRoofR = zR - 0.12f;
		Color body = Colors.White;

		// lower body: solid up to the rear seat; behind it the load space is open above its floor
		float floorY = sill + 0.3f, zSeat = zR - 1.15f;
		_k.Color = body;
		_k.Mat(paint).Box(new Vector3(0, (sill + belt) * 0.5f, (zN + zSeat) * 0.5f + 0.05f), new Vector3(W, belt - sill, zSeat - zN - 0.1f), 1f);
		_k.Box(new Vector3(0, (sill + floorY) * 0.5f, (zSeat + zR) * 0.5f), new Vector3(W, floorY - sill, zR - zSeat), 1f);
		foreach (float side in new[] { -1f, 1f })
			_k.Box(new Vector3(side * (W * 0.5f - 0.04f), (floorY + belt) * 0.5f, (zSeat + zR) * 0.5f), new Vector3(0.08f, belt - floorY, zR - zSeat), 1f);
		// bonnet: a shallow slope from the windscreen down to the nose
		Vector3 bl = new(-W * 0.5f + 0.03f, belt, zWs), br = new(W * 0.5f - 0.03f, belt, zWs);
		Vector3 nl = new(-W * 0.5f + 0.06f, belt - 0.06f, zN + 0.02f), nr = new(W * 0.5f - 0.06f, belt - 0.06f, zN + 0.02f);
		_k.Quad(nl, nr, br, bl, Vector3.Up);
		// greenhouse: glass all round with body-colour pillars and roof
		float hw0 = W * 0.5f - 0.05f, hw1 = W * 0.5f - 0.13f;
		Vector3 G(float side, float y, float z) => new(side * (y > belt + 0.01f ? hw1 : hw0), y, z);
		_k.Mat(glass);
		foreach (float side in new[] { -1f, 1f })
		{
			var n = new Vector3(side, 0.25f, 0).Normalized();
			_k.Quad(G(side, belt, zWs), G(side, belt, zRoofR + 0.1f), G(side, roof, zRoofR), G(side, roof, zRoofF), n);
		}
		var wsN = new Vector3(0, zRoofF - zWs, -(roof - belt)).Normalized();
		_k.Quad(G(-1, belt, zWs), G(1, belt, zWs), G(1, roof, zRoofF), G(-1, roof, zRoofF), wsN);
		_k.Mat(paint);
		_k.Box(new Vector3(0, roof + 0.02f, (zRoofF + zRoofR) * 0.5f), new Vector3(hw1 * 2f + 0.04f, 0.05f, zRoofR - zRoofF + 0.04f), 1f);
		foreach (float side in new[] { -1f, 1f })
		{
			// A, B, C and D pillars, a hair outside the glass
			float x = side * (hw1 + 0.035f);
			foreach (float z in new[] { 0.55f, 1.55f })
				_k.Box(new Vector3(side * ((hw0 + hw1) * 0.5f + 0.02f), (belt + roof) * 0.5f, z), new Vector3(0.06f, roof - belt, 0.09f), 1f);
			_k.Box(new Vector3(side * ((hw0 + hw1) * 0.5f + 0.02f), (belt + roof) * 0.5f, zRoofR - 0.02f), new Vector3(0.06f, roof - belt, 0.16f), 1f);
			var a0 = G(side, belt, zWs) + new Vector3(side * 0.02f, 0, 0); var a1 = G(side, roof, zRoofF) + new Vector3(side * 0.02f, 0, 0);
			_k.Beam(a0, a1, 0.07f, 0.07f, 1f);
			// roof rails, door seams, handles, mirror
			_k.Mat(trim);
			_k.Box(new Vector3(side * (hw1 - 0.1f), roof + 0.07f, (zRoofF + zRoofR) * 0.5f + 0.2f), new Vector3(0.04f, 0.04f, zRoofR - zRoofF - 0.4f), 1f);
			foreach (float z in new[] { 0.52f, 1.52f })
				_k.Box(new Vector3(side * (W * 0.5f + 0.002f), (sill + belt) * 0.5f + 0.05f, z), new Vector3(0.006f, belt - sill - 0.08f, 0.012f), 1f);
			foreach (float z in new[] { 0.3f, 1.3f })
				_k.Box(new Vector3(side * (W * 0.5f + 0.012f), belt - 0.1f, z), new Vector3(0.02f, 0.03f, 0.12f), 1f);
			_k.Box(new Vector3(side * (W * 0.5f + 0.1f), belt + 0.12f, zWs + 0.2f), new Vector3(0.16f, 0.1f, 0.06f), 1f);
			// rubbing strip along the side
			_k.Box(new Vector3(side * (W * 0.5f + 0.008f), sill + 0.2f, 0.05f), new Vector3(0.02f, 0.05f, L - 0.6f), 1f);
			_k.Mat(paint);
		}
		// bumpers, grille, lights
		_k.Mat(trim);
		_k.Box(new Vector3(0, sill + 0.08f, zN - 0.04f), new Vector3(W + 0.04f, 0.18f, 0.14f), 1f);
		_k.Box(new Vector3(0, sill + 0.08f, zR + 0.04f), new Vector3(W + 0.04f, 0.18f, 0.14f), 1f);
		_k.Box(new Vector3(0, belt - 0.2f, zN - 0.005f), new Vector3(0.7f, 0.16f, 0.02f), 1f);
		_k.Mat(head);
		foreach (float side in new[] { -1f, 1f })
			_k.Box(new Vector3(side * 0.62f, belt - 0.2f, zN - 0.008f), new Vector3(0.32f, 0.14f, 0.02f), 1f);
		_k.Mat(lamp);
		foreach (float side in new[] { -1f, 1f })
			_k.Box(new Vector3(side * (W * 0.5f - 0.1f), belt - 0.12f, zR + 0.005f), new Vector3(0.18f, 0.3f, 0.02f), 1f);
		// plate on the rear bumper, blank
		_k.Mat(Tint("car_plate", new Color(0.78f, 0.77f, 0.7f), 0.6f));
		_k.Box(new Vector3(0, sill + 0.08f, zR + 0.115f), new Vector3(0.52f, 0.12f, 0.01f), 1f);

		// the load space: needle-felt floor, the back of the rear seat, trim inside the pillars
		_k.Mat(CarMat("car_carpet", new Color(0.2f, 0.2f, 0.21f), 1f, 0.05f));
		_k.Color = Colors.White;
		_k.Quad(new Vector3(-hw0 + 0.06f, floorY, zSeat), new Vector3(hw0 - 0.06f, floorY, zSeat), new Vector3(hw0 - 0.06f, floorY, zR - 0.02f), new Vector3(-hw0 + 0.06f, floorY, zR - 0.02f), Vector3.Up,
			new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1.5f), new Vector2(0, 1.5f));
		_k.Mat(inner);
		_k.Box(new Vector3(0, floorY + 0.3f, zSeat - 0.05f), new Vector3(hw0 * 2f - 0.12f, 0.6f, 0.1f), 2f);
		foreach (float side in new[] { -1f, 1f })
			_k.Box(new Vector3(side * (hw0 - 0.04f), (floorY + roof) * 0.5f, (zSeat + zR) * 0.5f), new Vector3(0.04f, roof - floorY, zR - zSeat), 1f);
		_k.Box(new Vector3(0, roof - 0.02f, (zSeat + zR) * 0.5f), new Vector3(hw0 * 2f, 0.03f, zR - zSeat), 1f);
		// what's in it: a duffel and a water bottle
		_k.Mat(Tint("car_duffel", new Color(0.2f, 0.24f, 0.32f), 0.9f));
		_k.Cylinder(new Vector3(-0.55f, floorY + 0.17f, zSeat + 0.25f), new Vector3(0.05f, floorY + 0.16f, zSeat + 0.3f), 0.17f, 0.16f, 8, true, 2f);
		_k.Mat(trim);
		_k.Box(new Vector3(-0.25f, floorY + 0.33f, zSeat + 0.28f), new Vector3(0.5f, 0.02f, 0.04f), 1f);
		_k.Mat(Tint("car_bottle", new Color(0.3f, 0.45f, 0.5f), 0.4f));
		_k.Cylinder(new Vector3(0.55f, floorY + 0.045f, zSeat + 0.45f), new Vector3(0.55f, floorY + 0.045f, zSeat + 0.72f), 0.042f, 0.042f, 8, true, 3f);

		// the hatch: hinged at the roof's rear edge and lifted, glass above, body below
		var hinge = new Vector3(0, roof, zR - 0.04f);
		var open = Basis.FromEuler(new Vector3(Mathf.DegToRad(-128f), 0, 0));   // hangs down (-Y) when shut; swung back and up
		Vector3 H(float x, float d, float t) => hinge + open * new Vector3(x, -d, t);
		float hh = roof - sill - 0.25f;   // hatch height when shut
		_k.Mat(glass);
		_k.Quad(H(-hw1, 0.06f, 0.03f), H(hw1, 0.06f, 0.03f), H(hw1, hh * 0.55f, 0.03f), H(-hw1, hh * 0.55f, 0.03f), open * Vector3.Back);
		_k.Mat(paint);
		_k.Color = body;
		_k.Box(hinge + open * new Vector3(0, -hh * 0.78f, 0.0f), new Vector3(hw0 * 2f, hh * 0.44f, 0.06f), 1f, open);
		_k.Box(hinge + open * new Vector3(0, -0.03f, 0.0f), new Vector3(hw0 * 2f, 0.06f, 0.06f), 1f, open);
		foreach (float side in new[] { -1f, 1f })
			_k.Box(hinge + open * new Vector3(side * (hw1 + 0.02f), -hh * 0.3f, 0.0f), new Vector3(0.07f, hh * 0.6f, 0.06f), 1f, open);
		_k.Mat(inner);
		_k.Box(hinge + open * new Vector3(0, -hh * 0.78f, -0.035f), new Vector3(hw0 * 2f - 0.1f, hh * 0.4f, 0.01f), 1f, open);
		// gas struts
		_k.Mat(hub);
		foreach (float side in new[] { -1f, 1f })
			_k.Cylinder(new Vector3(side * (hw0 - 0.1f), belt - 0.05f, zR - 0.1f), hinge + open * new Vector3(side * (hw0 - 0.1f), -hh * 0.45f, -0.05f), 0.012f, 0.012f, 5, false, 1f);

		// wheels
		foreach (float side in new[] { -1f, 1f })
			foreach (float z in new[] { -1.42f, 1.38f })
			{
				Vector3 c = new(side * (W * 0.5f - 0.1f), 0.31f, z);
				_k.Mat(tyre).Cylinder(c - new Vector3(0.1f, 0, 0), c + new Vector3(0.1f, 0, 0), 0.31f, 0.31f, 10, true, 1f);
				_k.Mat(hub).Cylinder(c + new Vector3(side * 0.1f, 0, 0), c + new Vector3(side * 0.106f, 0, 0), 0.19f, 0.19f, 10, true, 1f);
				// the arch: a dark band over the wheel
				_k.Mat(trim).Box(c + new Vector3(side * 0.02f, 0.3f, 0), new Vector3(0.2f, 0.06f, 0.78f), 1f);
			}
		_k.Color = Colors.White;

		Col(new Vector3(0, (0.15f + belt) * 0.5f, 0), new Vector3(W, belt - 0.15f, L));
		Col(new Vector3(0, (belt + roof) * 0.5f, (zWs + zSeat) * 0.5f), new Vector3(W - 0.1f, roof - belt, zSeat - zWs));
	}

	/// <summary>The film camera on its own (the Act 1 camera, as ItemMeshes builds it), for the one lying in the car.</summary>
	private void CameraItem() => ItemMeshes.Build(Player.ToolKind.Camera, _gen);

	private void TornMap()
	{
		// A trail map, dropped and rained on: one corner curled up, one torn away.
		var map = ProcTextures.MapMat;
		_k.Xf = new Transform3D(Basis.FromEuler(new Vector3(0, 0.5f, 0)), Vector3.Zero);
		_k.Mat(map);
		Vector3 crease0 = new(-0.21f, 0.01f, -0.09f), crease1 = new(0.21f, 0.01f, -0.09f);
		Vector3 far0 = new(-0.19f, 0.03f, 0.29f), far1 = new(0.21f, 0.02f, 0.27f);
		Vector3 curl0 = new(-0.16f, 0.07f, -0.28f), curl1 = new(0.21f, 0f, -0.29f);   // the curled/torn near edge
		_k.Color = new Color(0.9f, 0.88f, 0.8f);
		_k.Quad(curl0, curl1, crease1, crease0, Vector3.Up, new Vector2(0.08f, 0.92f), new Vector2(0.9f, 0.94f), new Vector2(0.92f, 0.5f), new Vector2(0.06f, 0.52f));
		_k.Color = new Color(0.78f, 0.76f, 0.64f);
		_k.Quad(crease0, crease1, far1, far0, Vector3.Up, new Vector2(0.06f, 0.5f), new Vector2(0.92f, 0.5f), new Vector2(0.88f, 0.06f), new Vector2(0.1f, 0.08f));
		_k.Color = Colors.White;
		_k.Xf = Transform3D.Identity;
	}

	/// <summary>
	/// A forgotten two-person ridge tent: olive-grey canvas that sags between its poles (a
	/// subdivided cloth sheet pushed in by a sine sag, deepest mid-panel), the rear pole leaning
	/// so the back half droops, one front door flap rolled back onto a dark interior, a ground
	/// sheet, guy lines out to stakes. The eaves and stakes follow the terrain under them.
	/// Front (door) is the -X end.
	/// </summary>
	private void Tent()
	{
		var canvas = BuildingTextures.CanvasMat;
		var pole = Tint("tent_pole", new Color(0.35f, 0.33f, 0.30f));
		var rope = Tint("tent_rope", new Color(0.52f, 0.5f, 0.42f));
		var stake = Tint("tent_stake", new Color(0.3f, 0.28f, 0.25f));
		var terrain = Engine.IsEditorHint() ? null : GroundSnap.FindTerrain(this);
		float Gnd(float x, float z)
		{
			if (terrain == null || !IsInsideTree()) return 0f;
			Vector3 w = GlobalTransform * new Vector3(x, 0, z);
			return terrain.HeightAt(w.X, w.Z) - GlobalPosition.Y;
		}

		const float L = 2.0f, hw = 0.72f, ridgeH = 1.0f;
		const int nu = 8, nv = 4;
		float x0 = -L * 0.5f, x1 = L * 0.5f;
		// ridge height along the tent: the rear pole has slipped, so the back sags down
		float Ridge(float u) => ridgeH - 0.32f * Mathf.SmoothStep(0.55f, 1f, u) - 0.05f * Mathf.Sin(Mathf.Pi * u);
		Vector3 Cloth(int side, float u, float v)
		{
			float x = Mathf.Lerp(x0, x1, u);
			float z = side * hw * v * (1f - 0.08f * Mathf.SmoothStep(0.6f, 1f, u));
			float eave = Gnd(x, side * hw) + 0.03f;
			float y = Mathf.Lerp(Ridge(u), eave, v);
			// sag: inward along the panel normal, strongest mid-panel, worse on the loose rear half
			float sag = (0.07f + 0.08f * u) * Mathf.Sin(Mathf.Pi * u) * Mathf.Sin(Mathf.Pi * Mathf.Clamp(v * 1.05f, 0, 1));
			Vector3 nOut = new Vector3(0, hw, side * (Ridge(u) - eave)).Normalized();
			return new Vector3(x, y, z) - nOut * sag;
		}
		_k.Mat(canvas);
		foreach (int side in new[] { -1, 1 })
		{
			var pts = new Vector3[nu + 1, nv + 1];
			for (int i = 0; i <= nu; i++)
				for (int j = 0; j <= nv; j++)
					pts[i, j] = Cloth(side, i / (float)nu, j / (float)nv);
			for (int i = 0; i < nu; i++)
				for (int j = 0; j < nv; j++)
				{
					Vector3 a = pts[i, j], b = pts[i + 1, j], c = pts[i + 1, j + 1], d = pts[i, j + 1];
					Vector3 n = (b - a).Cross(d - a).Normalized();
					if (n.Y < 0) n = -n;
					// weathering: darker, damper toward the ground and on the shaded side
					float shade = (side > 0 ? 1f : 0.8f) * (1f - 0.25f * (j + 0.5f) / nv);
					_k.Color = new Color(shade, shade, shade * 0.97f);
					float u0 = i / (float)nu * L, u1 = (i + 1) / (float)nu * L, v0 = j / (float)nv * 0.9f, v1 = (j + 1) / (float)nv * 0.9f;
					_k.Quad(a, b, c, d, n, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
				}
		}
		// rear end panel (closed), following the drooped ridge
		_k.Color = new Color(0.72f, 0.72f, 0.7f);
		Vector3 rTop = Cloth(1, 1f, 0f), rL = Cloth(-1, 1f, 1f), rR = Cloth(1, 1f, 1f);
		_k.Tri(rL, rR, rTop + new Vector3(0.05f, 0, 0), Vector3.Right, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0));
		// front: the left door flap hangs closed, the right one is rolled back (dark inside shows)
		Vector3 fTop = Cloth(1, 0f, 0f), fL = Cloth(-1, 0f, 1f), fR = Cloth(1, 0f, 1f);
		Vector3 fMid = new(x0 - 0.02f, Gnd(x0, 0) + 0.03f, -0.05f);
		_k.Color = new Color(0.78f, 0.78f, 0.76f);
		_k.Tri(fL, fMid, fTop, Vector3.Left, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0));
		_k.Tri(fL, fMid + new Vector3(0.12f, 0.35f, 0), fTop, Vector3.Left, new Vector2(0, 1), new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0));
		_k.Color = new Color(0.7f, 0.7f, 0.68f);
		_k.Cylinder(fTop + new Vector3(-0.01f, -0.05f, 0.05f), fR + new Vector3(-0.01f, 0.08f, -0.08f), 0.045f, 0.06f, 5, true, 1f);
		// ground sheet sticking out at the door, and the dark floor inside: a small grid laid on the
		// ground under every corner (on a side slope a single flat quad left its downhill edge in the air)
		_k.Color = new Color(0.35f, 0.36f, 0.3f);
		const int sx = 4, sz = 2;
		for (int i = 0; i < sx; i++)
			for (int j = 0; j < sz; j++)
			{
				float xa = Mathf.Lerp(x0 - 0.18f, x1 - 0.05f, i / (float)sx), xb = Mathf.Lerp(x0 - 0.18f, x1 - 0.05f, (i + 1) / (float)sx);
				float za = Mathf.Lerp(-hw * 0.8f, hw * 0.8f, j / (float)sz), zb = Mathf.Lerp(-hw * 0.8f, hw * 0.8f, (j + 1) / (float)sz);
				Vector3 S(float x, float z) => new(x, Gnd(x, z) + 0.015f, z);
				_k.Quad(S(xa, za), S(xb, za), S(xb, zb), S(xa, zb), Vector3.Up);
			}

		// poles: the front one upright, the rear one leaning out
		_k.Color = Colors.White;
		_k.Mat(pole);
		_k.Cylinder(new Vector3(x0, Gnd(x0, 0), 0), new Vector3(x0, Ridge(0) + 0.06f, 0), 0.016f, 0.014f, 5, true);
		_k.Cylinder(new Vector3(x1 - 0.1f, Gnd(x1, 0), 0.02f), new Vector3(x1 + 0.05f, Ridge(1f) + 0.04f, 0.1f), 0.016f, 0.014f, 5, true);

		// guy lines to stakes: fore and aft off the ridge, and one off each eave corner (one has let go)
		void Guy(Vector3 from, Vector3 stakeXZ, bool slack)
		{
			var s = new Vector3(stakeXZ.X, Gnd(stakeXZ.X, stakeXZ.Z), stakeXZ.Z);
			_k.Mat(rope);
			_k.Color = Colors.White;
			if (slack)
			{
				Vector3 mid = (from + s) * 0.5f; mid.Y = Mathf.Max(s.Y, (from.Y + s.Y) * 0.5f - 0.35f);
				_k.Cylinder(from, mid, 0.005f, 0.005f, 3, false);
				_k.Cylinder(mid, s + new Vector3(0, 0.02f, 0), 0.005f, 0.005f, 3, false);
			}
			else _k.Cylinder(from, s + new Vector3(0, 0.08f, 0), 0.005f, 0.005f, 3, false);
			_k.Mat(stake);
			_k.Box(s + new Vector3(0, 0.05f, 0), new Vector3(0.025f, 0.12f, 0.025f), 3f, Basis.FromEuler(new Vector3(0, 0, 0.3f)));
		}
		Guy(new Vector3(x0, Ridge(0) + 0.04f, 0), new Vector3(x0 - 0.95f, 0, 0.05f), false);
		Guy(new Vector3(x1 + 0.05f, Ridge(1f) + 0.02f, 0.1f), new Vector3(x1 + 0.9f, 0, 0.2f), true);
		Guy(Cloth(-1, 0.02f, 0.95f), new Vector3(x0 - 0.35f, 0, -hw - 0.55f), false);
		Guy(Cloth(1, 0.02f, 0.95f), new Vector3(x0 - 0.35f, 0, hw + 0.55f), false);
		Guy(Cloth(-1, 0.98f, 0.95f), new Vector3(x1 + 0.35f, 0, -hw - 0.55f), false);
		Guy(Cloth(1, 0.98f, 0.95f), new Vector3(x1 + 0.4f, 0, hw + 0.5f), true);
		_k.Color = Colors.White;
	}
}

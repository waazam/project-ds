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
		Backpack, Boot, WalkingStick, CutLog, Boulder,
	}

	[Export] public PropKind Kind = PropKind.TrailMarker;
	/// <summary>Text for props that carry some (trail marker number).</summary>
	[Export] public string Label = "1";
	/// <summary>If &gt; 0, keeps procedural trees/rocks/foliage out of this radius.</summary>
	[Export] public float ClearRadius = 0f;
	[Export] public bool ClearFoliage = true;
	[Export] public int Seed = 1;

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
		var wood = ProcTextures.SignWoodMat;
		var post = ProcTextures.WoodMat;
		_k.Color = new Color(0.8f, 0.75f, 0.7f);
		foreach (float x in new[] { -0.9f, 0.9f })
		{
			_k.Mat(post).Box(new Vector3(x, 1.15f, 0), new Vector3(0.11f, 2.3f, 0.11f), 2f);
			Col(new Vector3(x, 1.15f, 0), new Vector3(0.12f, 2.3f, 0.12f));
		}
		_k.Color = Colors.White;
		_k.Mat(wood).Box(new Vector3(0, 1.72f, 0.075f), new Vector3(2.1f, 0.95f, 0.05f), 1.2f);
		_k.Mat(post).Box(new Vector3(0, 2.22f, 0.07f), new Vector3(2.25f, 0.05f, 0.16f), 2f);
		Col(new Vector3(0, 1.72f, 0.075f), new Vector3(2.1f, 0.95f, 0.06f));
		var cream = new Color(0.80f, 0.72f, 0.50f);
		Text("HARROW CREEK", new Vector3(0, 1.98f, 0.102f), 40, 0.0052f, cream);
		Text("NATIONAL PARK", new Vector3(0, 1.79f, 0.102f), 26, 0.0052f, cream);
		_k.Mat(Tint("routed", new Color(0.62f, 0.55f, 0.38f))).Box(new Vector3(0, 1.66f, 0.1f), new Vector3(1.5f, 0.012f, 0.005f));
		Text("BLACKFERN TRAIL   2.1 mi", new Vector3(0, 1.49f, 0.102f), 24, 0.0048f, cream);
		Text("CLEARWATER LOOP   CLOSED", new Vector3(0, 1.36f, 0.102f), 18, 0.0048f, cream * 0.85f);
	}

	private void InfoBoard()
	{
		var post = ProcTextures.WoodMat;
		var wood = ProcTextures.SignWoodMat;
		var roof = Tint("kiosk_roof", new Color(0.13f, 0.11f, 0.09f));
		_k.Color = new Color(0.85f, 0.8f, 0.75f);
		foreach (float x in new[] { -0.8f, 0.8f })
		{
			_k.Mat(post).Box(new Vector3(x, 1.25f, 0), new Vector3(0.12f, 2.5f, 0.12f), 2f);
			Col(new Vector3(x, 1.25f, 0), new Vector3(0.13f, 2.5f, 0.13f));
		}
		_k.Color = Colors.White;
		_k.Mat(wood).Box(new Vector3(0, 1.5f, 0.02f), new Vector3(1.5f, 1.1f, 0.04f), 1.3f);
		Col(new Vector3(0, 1.5f, 0.02f), new Vector3(1.5f, 1.1f, 0.05f));
		// frame
		_k.Mat(post);
		_k.Box(new Vector3(0, 2.07f, 0.05f), new Vector3(1.6f, 0.06f, 0.05f), 2f);
		_k.Box(new Vector3(0, 0.93f, 0.05f), new Vector3(1.6f, 0.06f, 0.05f), 2f);
		// gable roof
		float peak = 2.72f, eave = 2.42f;
		var tilt = new Vector3(0, peak - eave, 0.55f);
		float ang = Mathf.Atan2(tilt.Y, tilt.Z);
		_k.Mat(roof);
		_k.Box(new Vector3(0, (peak + eave) * 0.5f, 0.28f), new Vector3(2.0f, 0.05f, tilt.Length() + 0.05f), 1f, Basis.FromEuler(new Vector3(ang, 0, 0)));
		_k.Box(new Vector3(0, (peak + eave) * 0.5f, -0.28f), new Vector3(2.0f, 0.05f, tilt.Length() + 0.05f), 1f, Basis.FromEuler(new Vector3(-ang, 0, 0)));
		_k.Mat(post).Box(new Vector3(0, 2.4f, 0), new Vector3(1.75f, 0.1f, 0.1f), 2f);
		// map + notices
		var paper = ProcTextures.PaperMat;
		_k.Mat(ProcTextures.MapMat).Quad(new Vector3(-0.68f, 1.02f, 0.045f), new Vector3(-0.02f, 1.02f, 0.045f),
			new Vector3(-0.02f, 1.98f, 0.045f), new Vector3(-0.68f, 1.98f, 0.045f), Vector3.Back);
		_k.Color = new Color(0.95f, 0.93f, 0.85f);
		_k.Mat(paper).Quad(new Vector3(0.1f, 1.62f, 0.045f), new Vector3(0.6f, 1.62f, 0.045f),
			new Vector3(0.6f, 1.95f, 0.045f), new Vector3(0.1f, 1.95f, 0.045f), Vector3.Back);
		_k.Color = new Color(0.8f, 0.78f, 0.62f);
		_k.Quad(new Vector3(0.14f, 1.12f, 0.045f), new Vector3(0.62f, 1.1f, 0.045f),
			new Vector3(0.63f, 1.5f, 0.045f), new Vector3(0.15f, 1.52f, 0.045f), Vector3.Back);
		_k.Color = Colors.White;
		var ink = new Color(0.12f, 0.12f, 0.12f);
		Text("TRAIL CLOSES\nAT DUSK", new Vector3(0.35f, 1.79f, 0.047f), 18, 0.0032f, ink);
		Text("PLEASE STAY ON\nMARKED TRAILS", new Vector3(0.385f, 1.31f, 0.047f), 16, 0.0028f, ink * 1.5f);
		Text("BLACKFERN TRAIL", new Vector3(-0.35f, 1.9f, 0.047f), 14, 0.0032f, ink);
	}

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
		var post = ProcTextures.WoodMat;
		_k.Color = new Color(0.75f, 0.68f, 0.6f);
		_k.Mat(post).Box(new Vector3(0, 0.6f, 0), new Vector3(0.1f, 1.2f, 0.1f), 2f);
		// pyramid-ish cap
		_k.Cylinder(new Vector3(0, 1.2f, 0), new Vector3(0, 1.27f, 0), 0.075f, 0.0f, 4, true, 2f, Mathf.Pi / 4f);
		_k.Color = Colors.White;
		// faded blaze + plate
		_k.Mat(Tint("blaze", new Color(0.20f, 0.30f, 0.38f))).Box(new Vector3(0, 1.08f, 0), new Vector3(0.104f, 0.05f, 0.104f));
		_k.Mat(Tint("plate", new Color(0.68f, 0.66f, 0.60f), 0.6f)).Box(new Vector3(0, 0.9f, 0.052f), new Vector3(0.075f, 0.1f, 0.006f));
		_k.Box(new Vector3(0, 0.9f, -0.052f), new Vector3(0.075f, 0.1f, 0.006f));
		var ink = new Color(0.1f, 0.1f, 0.1f);
		Text(Label, new Vector3(0, 0.9f, 0.056f), 48, 0.0016f, ink);
		var back = Text(Label, new Vector3(0, 0.9f, -0.056f), 48, 0.0016f, ink);
		back.RotationDegrees = new Vector3(0, 180, 0);
		Col(new Vector3(0, 0.6f, 0), new Vector3(0.12f, 1.2f, 0.12f));
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
}

using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.ChurchParts;

namespace ProjectDS.World;

/// <summary>The church's furnishing: the chancel and its altar under the gilded canopy, the pews, the font,
/// the four standing candles, the doors (the great door with its four niches), the vestry, the lights.</summary>
public partial class Church
{
	public static readonly Vector3 FontLocal = new(-11.5f, 0f, 4.8f);
	/// <summary>The four candles round the nave (the owner: lighting all four opens a wing's door).</summary>
	public static readonly Vector3[] CandleLocal = { new(-5.2f, 0, 3.2f), new(5.2f, 0, 3.2f), new(-5.2f, 0, 48.8f), new(5.2f, 0, 48.8f) };
	/// <summary>The great door's four niches (x along the door, at hand height): the chalice, the bell, the crown, the dove.</summary>
	public static readonly string[] NicheNames = { "chalice", "bell", "crown", "dove" };
	public static readonly float[] NicheX = { -1.85f, -0.75f, 0.75f, 1.85f };
	public const float NicheY = 1.55f;
	public const float VestryX0 = -TransHalf - WallT, VestryX1 = VestryX0 - 8f, VestryZ0 = 55.5f, VestryZ1 = 65.5f, VestryH = 4.6f;

	private readonly List<(Node3D flame, OmniLight3D light, PickupInteractable use)> _candles = new();
	private Node3D _fontLid, _vestryDoor, _chaliceInDoor, _greatLeft, _greatRight;
	private StaticBody3D _greatBody;
	private static StandardMaterial3D _nicheBack;
	private static StandardMaterial3D NicheBackMat => _nicheBack ??= new StandardMaterial3D { AlbedoColor = new Color(0.04f, 0.035f, 0.03f), Roughness = 1f };
	private PickupInteractable _fontUse;
	private readonly List<PickupInteractable> _niches = new();

	// ------------------------------------------------------------------ the chancel

	private void BuildChancel()
	{
		var k = new MeshKit();
		var red = ChurchTextures.RedMarbleMat;
		var marble = ChurchTextures.CobbleMat;
		float w = NaveHalf - 0.45f;
		// five steps up from the crossing: each its own block, side by side (stacked blocks shared their side
		// faces and fought over them - the owner saw the steps clipping)
		for (int i = 0; i < 5; i++)
		{
			float z0 = CrossEnd + i * 0.4f, stepTop = (i + 1) * 0.18f;
			k.Mat(red);
			k.Color = Colors.White;
			BuildKit.Box(k, new Vector3(0, stepTop * 0.5f, z0 + 0.2f), new Vector3(w * 2f, stepTop, 0.4f), 1f, BuildKit.Face.NY);
		}
		var basis = new Basis(Vector3.Right, -Mathf.Atan2(0.9f, 2f));
		Collide(_marbleBody, new Vector3(0, 0.45f - 0.2f, CrossEnd + 1f), new Vector3(w * 2f, 0.4f, 2.3f), basis);
		// the chancel's floor, and the apse's
		k.Mat(marble);
		k.Quad(new Vector3(-w, ChancelY, CrossEnd + 2f), new Vector3(w, ChancelY, CrossEnd + 2f), new Vector3(w, ChancelY, ChancelEnd), new Vector3(-w, ChancelY, ChancelEnd), Vector3.Up,
			new Vector2(-w, CrossEnd + 2f), new Vector2(w, CrossEnd + 2f), new Vector2(w, ChancelEnd), new Vector2(-w, ChancelEnd));
		for (int i = 0; i < 14; i++)
		{
			float a0 = -Mathf.Pi * 0.5f + Mathf.Pi * i / 14f, a1 = -Mathf.Pi * 0.5f + Mathf.Pi * (i + 1) / 14f;
			Vector3 c = new(0, ChancelY, ChancelEnd), p0 = c + new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * (w + 0.1f), p1 = c + new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * (w + 0.1f);
			k.Tri(c, p0, p1, Vector3.Up, new Vector2(c.X, c.Z), new Vector2(p0.X, p0.Z), new Vector2(p1.X, p1.Z));
		}
		Collide(_marbleBody, new Vector3(0, ChancelY - 0.5f, (CrossEnd + 2f + ChancelEnd) * 0.5f), new Vector3(w * 2f, 1f, ChancelEnd - CrossEnd - 2f));
		_marbleBody.AddChild(new CollisionShape3D { Position = new Vector3(0, ChancelY - 0.5f, ChancelEnd), Shape = new CylinderShape3D { Radius = w, Height = 1f } });
		// the altar: on two steps, a block of white marble with a gilded front, a white cloth, a tall gold
		// cross and two candlesticks on it
		float az = ChancelEnd - 0.4f;
		k.Mat(red);
		BuildKit.Box(k, new Vector3(0, ChancelY + 0.09f, az), new Vector3(5.2f, 0.18f, 3.6f), 1f, BuildKit.Face.NY);
		BuildKit.Box(k, new Vector3(0, ChancelY + 0.27f, az), new Vector3(4.4f, 0.18f, 2.8f), 1f, BuildKit.Face.NY);
		Collide(_marbleBody, new Vector3(0, ChancelY + 0.18f, az), new Vector3(5.2f, 0.36f, 3.6f));
		float ay = ChancelY + 0.36f;
		k.Mat(ChurchTextures.MarbleMat);   // the altar itself is marble
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(0, ay + 0.5f, az), new Vector3(3.2f, 1f, 1.2f), 1f, BuildKit.Face.NY);
		BuildKit.Box(k, new Vector3(0, ay + 1.03f, az), new Vector3(3.4f, 0.06f, 1.35f), 1f);
		Collide(_marbleBody, new Vector3(0, ay + 0.53f, az), new Vector3(3.4f, 1.06f, 1.35f));
		k.Mat(ChurchTextures.GoldMat);
		BuildKit.Box(k, new Vector3(0, ay + 0.5f, az - 0.61f), new Vector3(2.8f, 0.7f, 0.03f), 1f);
		for (int i = 0; i < 5; i++)
			k.Cylinder(new Vector3(-1.2f + i * 0.6f, ay + 0.2f, az - 0.63f), new Vector3(-1.2f + i * 0.6f, ay + 0.85f, az - 0.63f), 0.04f, 0.04f, 6, false);
		var cloth = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.88f, 0.84f), Roughness = 0.9f };
		k.Mat(cloth);
		BuildKit.Box(k, new Vector3(0, ay + 1.065f, az), new Vector3(3.5f, 0.01f, 1.45f), 1f);
		BuildKit.Box(k, new Vector3(0, ay + 0.85f, az - 0.73f), new Vector3(3.5f, 0.42f, 0.01f), 1f);
		k.Mat(ChurchTextures.GoldMat);
		Vector3 top = new(0, ay + 1.07f, az + 0.2f);
		k.Cylinder(top, top + Vector3.Up * 0.12f, 0.16f, 0.1f, 8, true);
		k.Cylinder(top + Vector3.Up * 0.12f, top + Vector3.Up * 1.35f, 0.035f, 0.035f, 8, true);
		k.Box(top + Vector3.Up * 1.05f, new Vector3(0.62f, 0.07f, 0.07f));
		foreach (float x in new[] { -1.2f, 1.2f })
		{
			Vector3 b = new(x, ay + 1.07f, az + 0.1f);
			k.Cylinder(b, b + Vector3.Up * 0.08f, 0.1f, 0.06f, 8, true);
			k.Cylinder(b + Vector3.Up * 0.08f, b + Vector3.Up * 0.55f, 0.025f, 0.025f, 6, false);
			k.Cylinder(b + Vector3.Up * 0.55f, b + Vector3.Up * 0.6f, 0.06f, 0.06f, 8, true);
		}
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.9f, 0.82f), Roughness = 0.6f });
		foreach (float x in new[] { -1.2f, 1.2f }) k.Cylinder(new Vector3(x, ay + 1.67f, az + 0.1f), new Vector3(x, ay + 1.92f, az + 0.1f), 0.03f, 0.03f, 6, true);
		// the canopy over it: four gilded columns, pointed arches on every side under steep gables, a
		// pinnacle at each corner and a spire in the middle
		k.Mat(ChurchTextures.GoldMat);
		float cx = 2.3f, cz = 2f, ch = 5.2f;
		foreach (float x in new[] { -cx, cx })
			foreach (float z in new[] { -cz, cz })
			{
				Vector3 b = new(x, ChancelY + 0.36f, az + z);
				k.Cylinder(b, b + Vector3.Up * 0.3f, 0.26f, 0.22f, 8, true);
				k.Cylinder(b + Vector3.Up * 0.3f, b + Vector3.Up * ch, 0.13f, 0.13f, 10, false);
				k.Cylinder(b + Vector3.Up * ch, b + Vector3.Up * (ch + 0.25f), 0.2f, 0.26f, 8, true);
				_stone.AddChild(new CollisionShape3D { Position = b + Vector3.Up * (ch * 0.5f), Shape = new CylinderShape3D { Radius = 0.26f, Height = ch } });
				// its pinnacle
				Vector3 p = b + Vector3.Up * (ch + 0.25f);
				k.Box(p + Vector3.Up * 0.5f, new Vector3(0.36f, 1f, 0.36f));
				k.Cylinder(p + Vector3.Up * 1f, p + Vector3.Up * 2.6f, 0.2f, 0.01f, 4, false, 1f, Mathf.Pi * 0.25f);
			}
		float cy = ChancelY + 0.36f + ch + 0.25f;
		// the canopy's cornice, and on each face a pointed arch between the columns under a gable
		foreach (var (a, b) in new[] { (new Vector3(-cx, cy, az - cz), new Vector3(cx, cy, az - cz)), (new Vector3(-cx, cy, az + cz), new Vector3(cx, cy, az + cz)), (new Vector3(-cx, cy, az - cz), new Vector3(-cx, cy, az + cz)), (new Vector3(cx, cy, az - cz), new Vector3(cx, cy, az + cz)) })
		{
			k.Beam(a, b, 0.3f, 0.35f);
			Vector3 mid = (a + b) * 0.5f, dir = (b - a).Normalized();
			float half = (b - a).Length() * 0.5f;
			var prev = a - Vector3.Up * 1.4f;
			for (int i = 1; i <= 10; i++)
			{
				float u = -half + 2f * half * i / 10f;
				var q = mid + dir * u + Vector3.Up * (Pointed(u, half, half * 1.6f) - 1.4f - Pointed(0, half, half * 1.6f) + 1.2f);
				k.Beam(prev, q, 0.12f, 0.16f);
				prev = q;
			}
			// the gable: two slopes up to a finial
			var apex = mid + Vector3.Up * 2.2f;
			k.Beam(a + Vector3.Up * 0.15f, apex, 0.16f, 0.2f);
			k.Beam(b + Vector3.Up * 0.15f, apex, 0.16f, 0.2f);
			k.Cylinder(apex, apex + Vector3.Up * 0.5f, 0.08f, 0.01f, 6, false);
		}
		k.Box(new Vector3(0, cy + 0.2f, az), new Vector3(cx * 2f, 0.1f, cz * 2f));
		k.Cylinder(new Vector3(0, cy, az), new Vector3(0, cy + 6.5f, az), 0.75f, 0.02f, 8, false, 1f, Mathf.Pi / 8f);
		// choir stalls either side of the chancel
		k.Mat(ChurchTextures.OakMat);
		k.Color = Colors.White;
		foreach (float s in new[] { -1f, 1f })
		{
			for (int row = 0; row < 2; row++)
			{
				float x = s * (w - 0.5f - row * 1.05f), y = ChancelY + row * 0.25f;
				BuildKit.Box(k, new Vector3(x, y + 0.13f, (CrossEnd + 2.8f + ChancelEnd - 3.2f) * 0.5f), new Vector3(0.9f, 0.26f, ChancelEnd - CrossEnd - 6f), 1f, BuildKit.Face.NY);
				BuildKit.Box(k, new Vector3(x, y + 0.45f, (CrossEnd + 2.8f + ChancelEnd - 3.2f) * 0.5f), new Vector3(0.5f, 0.06f, ChancelEnd - CrossEnd - 6f), 1f);
				BuildKit.Box(k, new Vector3(x + s * 0.28f, y + 0.9f, (CrossEnd + 2.8f + ChancelEnd - 3.2f) * 0.5f), new Vector3(0.06f, 0.9f, ChancelEnd - CrossEnd - 6f), 1f);
				Collide(_wood, new Vector3(x, y + 0.5f, (CrossEnd + 2.8f + ChancelEnd - 3.2f) * 0.5f), new Vector3(0.9f, 1f, ChancelEnd - CrossEnd - 6f));
			}
		}
		k.CommitTo(this, "Chancel", true);
		// the sanctuary lamp: a red glass lamp on a long chain, burning
		var lamp = new Vector3(-3.2f, 6f, CrossEnd + 5f);
		var chain = new MeshKit();
		chain.Mat(ChurchTextures.GoldMat);
		chain.Cylinder(lamp + Vector3.Up * 0.3f, new Vector3(lamp.X, NaveVaultY(lamp.X) - 0.2f, lamp.Z), 0.012f, 0.012f, 4, false);
		chain.Cylinder(lamp + Vector3.Up * 0.3f, lamp + Vector3.Up * 0.1f, 0.02f, 0.12f, 8, true);
		chain.Cylinder(lamp + Vector3.Down * 0.25f, lamp + Vector3.Down * 0.1f, 0.03f, 0.1f, 8, true);
		chain.CommitTo(this, "SanctuaryLamp", false);
		AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.11f, Height = 0.22f }, Position = lamp, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.08f, 0.06f, 0.85f), EmissionEnabled = true, Emission = new Color(0.9f, 0.1f, 0.06f), EmissionEnergyMultiplier = 2.2f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		AddChild(new OmniLight3D { Position = lamp, LightColor = new Color(1f, 0.25f, 0.15f), LightEnergy = 0.8f, OmniRange = 7f, ShadowEnabled = false });
	}

	// ------------------------------------------------------------------ the pews

	private void BuildPews()
	{
		const float len = 5.3f;
		var k = new MeshKit();
		k.Mat(ChurchTextures.OakMat);
		k.Color = Colors.White;
		// one pew, facing the altar (+Z): a seat, a raked back, a shelf behind for the books, carved ends
		BuildKit.Box(k, new Vector3(0, 0.45f, 0), new Vector3(len, 0.05f, 0.44f), 1f);
		k.Box(new Vector3(0, 0.74f, -0.24f), new Vector3(len, 0.55f, 0.045f), 1f, new Basis(Vector3.Right, -0.14f));
		BuildKit.Box(k, new Vector3(0, 1.0f, -0.32f), new Vector3(len, 0.035f, 0.16f), 1f);
		BuildKit.Box(k, new Vector3(0, 0.14f, 0.38f), new Vector3(len - 0.2f, 0.07f, 0.16f), 1f);      // the kneeler
		foreach (float x in new[] { -len * 0.5f, len * 0.5f })
		{
			BuildKit.Box(k, new Vector3(x, 0.5f, -0.02f), new Vector3(0.07f, 1.0f, 0.66f), 1f);
			k.Cylinder(new Vector3(x - 0.035f, 1.0f, -0.02f), new Vector3(x + 0.035f, 1.0f, -0.02f), 0.33f, 0.33f, 12, true);
			k.Cylinder(new Vector3(x, 1.28f, -0.02f), new Vector3(x, 1.34f, -0.02f), 0.035f, 0.045f, 8, false);   // a turned neck
			k.Blob(new Vector3(x, 1.39f, -0.02f), new Vector3(0.06f, 0.055f, 0.06f), 3, 0.08f);                     // and a round poppyhead
		}
		var mesh = k.Commit();
		var xf = new List<Transform3D>();
		for (float z = 7.2f; z < 29.8f; z += 1.05f)
			foreach (float s in new[] { -1f, 1f })
			{
				var at = new Vector3(s * (1.35f + len * 0.5f), 0, z);
				xf.Add(new Transform3D(Basis.Identity, at));
				Collide(_wood, at + new Vector3(0, 0.55f, -0.04f), new Vector3(len + 0.1f, 1.1f, 0.7f));
			}
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xf.Count };
		for (int i = 0; i < xf.Count; i++) mm.SetInstanceTransform(i, xf[i]);
		AddChild(new MultiMeshInstance3D { Name = "Pews", Multimesh = mm });
	}

	// ------------------------------------------------------------------ the font

	private void BuildFont()
	{
		var at = FontLocal;
		var k = new MeshKit();
		k.Mat(ChurchTextures.AshlarMat);
		k.Color = Colors.White;
		k.Cylinder(at, at + Vector3.Up * 0.18f, 0.95f, 0.9f, 8, true, 1f, Mathf.Pi / 8f);
		k.Cylinder(at + Vector3.Up * 0.18f, at + Vector3.Up * 0.3f, 0.6f, 0.45f, 8, true, 1f, Mathf.Pi / 8f);
		k.Cylinder(at + Vector3.Up * 0.3f, at + Vector3.Up * 0.75f, 0.32f, 0.32f, 8, false, 1f, Mathf.Pi / 8f);
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * i / 8f + Mathf.Pi / 8f;
			Vector3 o = new(Mathf.Cos(a) * 0.36f, 0, Mathf.Sin(a) * 0.36f);
			k.Cylinder(at + o + Vector3.Up * 0.3f, at + o + Vector3.Up * 0.78f, 0.05f, 0.05f, 6, false);
		}
		k.Cylinder(at + Vector3.Up * 0.75f, at + Vector3.Up * 0.95f, 0.4f, 0.72f, 8, false, 1f, Mathf.Pi / 8f);
		k.Cylinder(at + Vector3.Up * 0.95f, at + Vector3.Up * 1.18f, 0.72f, 0.72f, 8, false, 1f, Mathf.Pi / 8f);
		// the rim, and the bowl's inside down to the water
		k.Cylinder(at + Vector3.Up * 1.18f, at + Vector3.Up * 1.22f, 0.74f, 0.74f, 8, false, 1f, Mathf.Pi / 8f);
		k.Color = Colors.White * 0.7f;
		k.Cylinder(at + Vector3.Up * 1.22f, at + Vector3.Up * 0.98f, 0.6f, 0.52f, 8, false, 1f, Mathf.Pi / 8f);
		for (int i = 0; i < 8; i++)
		{
			float a0 = Mathf.Tau * i / 8f + Mathf.Pi / 8f, a1 = Mathf.Tau * (i + 1) / 8f + Mathf.Pi / 8f;
			Vector3 A(float a, float r, float y) => at + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
			k.Color = Colors.White;
			k.Quad(A(a0, 0.6f, 1.22f), A(a1, 0.6f, 1.22f), A(a1, 0.74f, 1.22f), A(a0, 0.74f, 1.22f), Vector3.Up);
		}
		k.CommitTo(this, "Font", true);
		// (solid only up to below the water, so nothing in the bowl is hidden from the hand reaching in)
		_stone.AddChild(new CollisionShape3D { Position = at + Vector3.Up * 0.5f, Shape = new CylinderShape3D { Radius = 0.75f, Height = 1f } });
		// the water in it, cold and black, skinned with ice at the edges
		AddChild(new MeshInstance3D
		{
			Name = "FontWater", Mesh = new CylinderMesh { TopRadius = 0.53f, BottomRadius = 0.53f, Height = 0.02f, RadialSegments = 8 }, Position = at + Vector3.Up * 1.02f,
			Rotation = new Vector3(0, Mathf.Pi / 8f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.04f, 0.05f, 0.06f), Roughness = 0.05f, MetallicSpecular = 1f },
		});
		// the lid: an octagon of oak bound with iron, a ring to lift it by, a chain through the ring and
		// a padlock on the chain
		_fontLid = new Node3D { Name = "FontLid", Position = at + Vector3.Up * 1.22f };
		AddChild(_fontLid);
		var l = new MeshKit();
		l.Mat(ChurchTextures.OakMat);
		l.Color = Colors.White * 0.9f;
		l.Cylinder(Vector3.Zero, Vector3.Up * 0.09f, 0.78f, 0.74f, 8, true, 1f, Mathf.Pi / 8f);
		l.Cylinder(Vector3.Up * 0.09f, Vector3.Up * 0.3f, 0.5f, 0.06f, 8, true, 1f, Mathf.Pi / 8f);
		l.Mat(ChurchTextures.IronMat);
		l.Color = Colors.White;
		for (int i = 0; i < 4; i++)
		{
			float a = Mathf.Tau * i / 4f;
			l.Beam(new Vector3(Mathf.Cos(a) * 0.1f, 0.26f, Mathf.Sin(a) * 0.1f), new Vector3(Mathf.Cos(a) * 0.77f, 0.08f, Mathf.Sin(a) * 0.77f), 0.07f, 0.015f);
		}
		for (int s = 0; s < 14; s++)
		{
			float a0 = Mathf.Tau * s / 14f, a1 = Mathf.Tau * (s + 1) / 14f;
			l.Beam(new Vector3(Mathf.Cos(a0) * 0.08f, 0.38f, Mathf.Sin(a0) * 0.08f * 0.3f), new Vector3(Mathf.Cos(a1) * 0.08f, 0.38f, Mathf.Sin(a1) * 0.08f * 0.3f), 0.015f, 0.015f);
		}
		// the chain down the side to a staple in the stone, and the padlock
		for (int i = 0; i < 6; i++) l.Box(new Vector3(0.12f + i * 0.1f, 0.3f - i * 0.07f, 0), new Vector3(0.06f, 0.02f, 0.04f));
		l.Box(new Vector3(0.74f, -0.14f, 0), new Vector3(0.1f, 0.12f, 0.05f));
		l.Cylinder(new Vector3(0.74f, -0.06f, -0.03f), new Vector3(0.74f, -0.06f, 0.03f), 0.035f, 0.035f, 8, false);
		l.CommitTo(_fontLid, "Lid", true);
		_fontUse = new PickupInteractable
		{
			Name = "FontUse", PickRadius = 0.8f, MaxDistance = 2.6f, Position = at + Vector3.Up * 1.3f,
			PromptFor = p => FontOpen ? "" : p?.Inventory is { } inv && inv.HasTool(ToolKind.FontKey) ? "Unlock the font" : "The font's lid is chained and locked.",
			CanUse = p => !FontOpen && p?.Inventory is { } inv && inv.HasTool(ToolKind.FontKey),
		};
		_fontUse.Interacted += OnFont;
		AddChild(_fontUse);
	}

	// ------------------------------------------------------------------ the candles

	private void BuildCandles()
	{
		var brass = ItemTextures.BrassMat;
		var wax = new StandardMaterial3D { AlbedoColor = new Color(0.94f, 0.91f, 0.82f), Roughness = 0.55f };
		for (int i = 0; i < CandleLocal.Length; i++)
		{
			var at = CandleLocal[i];
			var k = new MeshKit();
			k.Mat(brass);
			k.Color = new Color(0.8f, 0.62f, 0.34f);
			// three clawed feet, a stem with knops, a wide drip pan, a pricket
			for (int f = 0; f < 3; f++)
			{
				float a = Mathf.Tau * f / 3f;
				Vector3 d = new(Mathf.Cos(a), 0, Mathf.Sin(a));
				k.Beam(at + Vector3.Up * 0.25f, at + d * 0.34f + Vector3.Up * 0.02f, 0.06f, 0.05f);
				k.Blob(at + d * 0.36f + Vector3.Up * 0.04f, new Vector3(0.05f, 0.04f, 0.05f), i * 3 + f, 0.1f);
			}
			k.Cylinder(at + Vector3.Up * 0.2f, at + Vector3.Up * 0.34f, 0.1f, 0.05f, 10, true);
			k.Cylinder(at + Vector3.Up * 0.34f, at + Vector3.Up * 1.62f, 0.032f, 0.026f, 10, false);
			foreach (float y in new[] { 0.62f, 1.05f, 1.42f }) k.Cylinder(at + Vector3.Up * (y - 0.05f), at + Vector3.Up * (y + 0.05f), 0.06f, 0.06f, 10, true);
			k.Cylinder(at + Vector3.Up * 1.62f, at + Vector3.Up * 1.68f, 0.03f, 0.16f, 12, true);
			k.Cylinder(at + Vector3.Up * 1.68f, at + Vector3.Up * 1.72f, 0.17f, 0.17f, 12, true);
			k.Mat(wax);
			k.Color = Colors.White;
			k.Cylinder(at + Vector3.Up * 1.72f, at + Vector3.Up * 2.1f, 0.065f, 0.062f, 12, true);
			k.Blob(at + Vector3.Up * 1.73f + new Vector3(0.06f, 0, 0.02f), new Vector3(0.03f, 0.02f, 0.03f), i, 0.2f);   // a run of wax
			k.CommitTo(this, $"Candle{i + 1}", true);
			_stone.AddChild(new CollisionShape3D { Position = at + Vector3.Up * 1f, Shape = new CylinderShape3D { Radius = 0.3f, Height = 2f } });
			// the flame (unlit until the lighter touches it)
			var flame = new Node3D { Name = $"Flame{i + 1}", Position = at + Vector3.Up * 2.16f, Visible = false };
			AddChild(flame);
			var fm = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.62f, 0.25f), EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
			flame.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.022f, Height = 0.09f, RadialSegments = 8, Rings = 4 }, MaterialOverride = fm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
			var light = new OmniLight3D { Name = $"CandleLight{i + 1}", Position = at + Vector3.Up * 2.25f, LightColor = new Color(1f, 0.7f, 0.4f), LightEnergy = 0f, OmniRange = 11f, OmniAttenuation = 1.2f, ShadowEnabled = i < 2, Visible = false };
			AddChild(light);
			int idx = i;
			var use = new PickupInteractable
			{
				Name = $"CandleUse{i + 1}", PickRadius = 0.35f, MaxDistance = 2.6f, Position = at + Vector3.Up * 2f,
				PromptFor = p => CandleLit(idx) ? "" : p?.Inventory is { } inv && inv.HasTool(ToolKind.Lighter) ? "Light the candle" : "A tall candle, cold.",
				CanUse = p => !CandleLit(idx) && p?.Inventory is { } inv && inv.HasTool(ToolKind.Lighter),
			};
			use.Interacted += p => OnCandle(idx, p);
			AddChild(use);
			_candles.Add((flame, light, use));
		}
	}

	// ------------------------------------------------------------------ the doors

	private void BuildDoors()
	{
		// ---- the great door: two tall oak leaves under the pointed arch, banded with iron, great rings,
		// and four niches cut into them for the four pieces. Each leaf hangs on its own hinge (at the jamb,
		// x = +-2.6) so the door can swing in at Act 21's end.
		float z = -0.75f;
		_greatBody = new StaticBody3D { Name = "GreatDoorBody", CollisionLayer = 1, CollisionMask = 0 };
		_greatBody.SetMeta("surface", "wood");
		AddChild(_greatBody);
		foreach (float s in new[] { -1f, 1f })
		{
			var hinge = new Node3D { Name = s < 0 ? "GreatDoorLeft" : "GreatDoorRight", Position = new Vector3(s * 2.6f, 0, z) };
			AddChild(hinge);
			if (s < 0) _greatLeft = hinge; else _greatRight = hinge;
			var k = new MeshKit();
			// the leaf in the hinge's space: its x runs from the jamb (0) toward the middle (-s * 2.6)
			float hx = s * 2.6f;
			k.Mat(ChurchTextures.OakMat);
			k.Color = Colors.White * 0.85f;
			for (float u = 0f; u < 2.59f; u += 0.2f)
			{
				float ua = s * u, ub = s * Mathf.Min(u + 0.2f, 2.6f);
				float ta = 5f + Pointed(ua, 2.6f, 2.6f * 2f * 0.6f), tb = 5f + Pointed(ub, 2.6f, 2.6f * 2f * 0.6f);
				float x0 = Mathf.Min(ua, ub) - hx, x1 = Mathf.Max(ua, ub) - hx, t0 = ua < ub ? ta : tb, t1 = ua < ub ? tb : ta;
				// a board, front and back, following the arch at its top
				k.Quad(new Vector3(x0, 0, 0.08f), new Vector3(x1, 0, 0.08f), new Vector3(x1, t1, 0.08f), new Vector3(x0, t0, 0.08f), Vector3.Back,
					new Vector2(x0 + hx, t0), new Vector2(x1 + hx, t1), new Vector2(x1 + hx, 0), new Vector2(x0 + hx, 0));
				k.Quad(new Vector3(x1, 0, -0.08f), new Vector3(x0, 0, -0.08f), new Vector3(x0, t0, -0.08f), new Vector3(x1, t1, -0.08f), Vector3.Forward);
			}
			// iron bands with scrolled ends, a great ring and its boss
			k.Mat(ChurchTextures.IronMat);
			k.Color = Colors.White;
			foreach (float y in new[] { 0.6f, 2.6f, 4.6f })
			{
				BuildKit.Box(k, new Vector3(s * 1.3f - hx, y, 0.1f), new Vector3(2.5f, 0.1f, 0.03f), 1f);
				k.Cylinder(new Vector3(s * 0.2f - hx, y, 0.1f), new Vector3(s * 0.2f - hx, y, 0.14f), 0.07f, 0.07f, 8, true);
			}
			k.Cylinder(new Vector3(s * 0.45f - hx, 3.2f, 0.1f), new Vector3(s * 0.45f - hx, 3.2f, 0.16f), 0.12f, 0.1f, 10, true);
			for (int i = 0; i < 16; i++)
			{
				float a0 = Mathf.Tau * i / 16f, a1 = Mathf.Tau * (i + 1) / 16f;
				k.Beam(new Vector3(s * 0.45f - hx + Mathf.Cos(a0) * 0.2f, 3.0f + Mathf.Sin(a0) * 0.2f, 0.17f), new Vector3(s * 0.45f - hx + Mathf.Cos(a1) * 0.2f, 3.0f + Mathf.Sin(a1) * 0.2f, 0.17f), 0.03f, 0.03f);
			}
			// the niches on this leaf: a carved stone frame round a dark hollow, and on its back, the shape of what goes in it
			for (int i = 0; i < 4; i++)
			{
				if (Mathf.Sign(NicheX[i]) != s) continue;
				var c = new Vector3(NicheX[i] - hx, NicheY, 0.09f);
				k.Mat(ChurchTextures.AshlarMat);
				k.Color = Colors.White;
				BuildKit.Box(k, c + new Vector3(0, 0.32f, 0.04f), new Vector3(0.56f, 0.08f, 0.1f), 1f);
				BuildKit.Box(k, c + new Vector3(0, -0.32f, 0.04f), new Vector3(0.56f, 0.08f, 0.1f), 1f);
				BuildKit.Box(k, c + new Vector3(-0.24f, 0, 0.04f), new Vector3(0.08f, 0.56f, 0.1f), 1f);
				BuildKit.Box(k, c + new Vector3(0.24f, 0, 0.04f), new Vector3(0.08f, 0.56f, 0.1f), 1f);
				k.Mat(NicheBackMat);
				k.Quad(c + new Vector3(-0.2f, -0.28f, 0.005f), c + new Vector3(0.2f, -0.28f, 0.005f), c + new Vector3(0.2f, 0.28f, 0.005f), c + new Vector3(-0.2f, 0.28f, 0.005f), Vector3.Back);
				k.Mat(ChurchTextures.GoldMat);
				NicheIcon(k, i, c + new Vector3(0, 0, 0.02f));
			}
			k.CommitTo(hinge, "Leaf", true);
			_greatBody.AddChild(new CollisionShape3D { Name = s < 0 ? "LeftShape" : "RightShape", Position = new Vector3(s * 1.3f, 3.5f, z), Shape = new BoxShape3D { Size = new Vector3(2.6f, 7f, 0.3f) } });
		}
		var doorUse = new PickupInteractable { Name = "GreatDoorUse", PickRadius = 1.4f, MaxDistance = 3.2f, Position = new Vector3(0, 2.6f, z + 0.2f), PromptFor = _ => DoorOpen ? "" : "Locked fast. Four empty niches in it.", CanUse = _ => false };
		doorUse.Interacted += _ => { };
		AddChild(doorUse);
		for (int i = 0; i < 4; i++)
		{
			int idx = i;
			var n = new PickupInteractable
			{
				Name = $"Niche{i}", PickRadius = 0.28f, MaxDistance = 2.6f, Position = new Vector3(NicheX[i], NicheY, z + 0.3f),
				PromptFor = p => idx == 0 && ChalicePlaced ? "" : idx == 0 && p?.Inventory is { } inv && inv.HasTool(ToolKind.Chalice) ? "Set the chalice in its place" : $"An empty niche, the shape of a {NicheNames[idx]} worked in gold at its back.",
				CanUse = p => idx == 0 && !ChalicePlaced && p?.Inventory is { } inv && inv.HasTool(ToolKind.Chalice),
			};
			n.Interacted += p => OnNiche(idx, p);
			AddChild(n);
			_niches.Add(n);
		}
		// ---- the vestry's door (left transept) and the chapel's (right), the tower's (west front)
		_vestryDoor = WingDoor(new Vector3(-TransHalf - 0.6f, 0, (NaveEnd + CrossEnd) * 0.5f), Vector3.Right, 2.4f, 2.7f, "VestryDoor", out var vestryBlock);
		_vestryBlock = vestryBlock;
		var vestryUse = new PickupInteractable { Name = "VestryDoorUse", PickRadius = 0.9f, MaxDistance = 2.8f, Position = new Vector3(-TransHalf + 0.1f, 1.4f, (NaveEnd + CrossEnd) * 0.5f), PromptFor = _ => VestryOpen ? "" : "Locked.", CanUse = _ => false };
		vestryUse.Interacted += _ => { };
		AddChild(vestryUse);
		WingDoor(new Vector3(TransHalf + 0.6f, 0, (NaveEnd + CrossEnd) * 0.5f), Vector3.Left, 2.4f, 2.7f, "ChapelDoor", out _);
		var chapelUse = new PickupInteractable { Name = "ChapelDoorUse", PickRadius = 0.9f, MaxDistance = 2.8f, Position = new Vector3(TransHalf - 0.1f, 1.4f, (NaveEnd + CrossEnd) * 0.5f), PromptFor = _ => "Locked. Cold air through the keyhole, and a smell of old wax.", CanUse = _ => false };
		chapelUse.Interacted += _ => { };
		AddChild(chapelUse);
		WingDoor(new Vector3(11.5f, 0, -0.75f), Vector3.Back, 1.2f, 2.4f, "TowerDoor", out _);
		var towerUse = new PickupInteractable { Name = "TowerDoorUse", PickRadius = 0.6f, MaxDistance = 2.6f, Position = new Vector3(11.5f, 1.3f, -0.1f), PromptFor = _ => "A small door, locked. Behind it, stone steps going up.", CanUse = _ => false };
		towerUse.Interacted += _ => { };
		AddChild(towerUse);
	}

	private StaticBody3D _vestryBlock;

	/// <summary>A single oak door leaf in a pointed doorway <paramref name="width"/> wide, its straight sides
	/// <paramref name="spring"/> high; hinged on its left edge (as seen from <paramref name="facing"/>), with a
	/// body that blocks the way while it's shut. Returns the hinge node (rotate it to open).</summary>
	private Node3D WingDoor(Vector3 at, Vector3 facing, float width, float spring, string name, out StaticBody3D block)
	{
		var basis = Basis.LookingAt(-facing, Vector3.Up);   // local +Z faces the way the door faces
		var hinge = new Node3D { Name = name, Transform = new Transform3D(basis, at + basis * new Vector3(-width * 0.5f, 0, 0)) };
		AddChild(hinge);
		var k = new MeshKit();
		k.Mat(ChurchTextures.OakMat);
		k.Color = Colors.White * 0.8f;
		float half = width * 0.5f;
		for (float u = 0; u < width - 0.001f; u += 0.15f)
		{
			float ub = Mathf.Min(u + 0.15f, width);
			float ta = spring + Pointed(u - half, half, width * 0.7f), tb = spring + Pointed(ub - half, half, width * 0.7f);
			k.Quad(new Vector3(u, 0, 0.06f), new Vector3(ub, 0, 0.06f), new Vector3(ub, tb, 0.06f), new Vector3(u, ta, 0.06f), Vector3.Back, new Vector2(u, ta), new Vector2(ub, tb), new Vector2(ub, 0), new Vector2(u, 0));
			k.Quad(new Vector3(ub, 0, -0.06f), new Vector3(u, 0, -0.06f), new Vector3(u, ta, -0.06f), new Vector3(ub, tb, -0.06f), Vector3.Forward);
		}
		k.Mat(ChurchTextures.IronMat);
		k.Color = Colors.White;
		foreach (float y in new[] { 0.4f, spring * 0.5f, spring - 0.2f })
			foreach (float zz in new[] { 0.075f, -0.075f })
				BuildKit.Box(k, new Vector3(width * 0.4f, y, zz), new Vector3(width * 0.75f, 0.07f, 0.02f), 1f);
		k.Cylinder(new Vector3(width - 0.25f, 1.1f, 0.07f), new Vector3(width - 0.25f, 1.1f, 0.12f), 0.06f, 0.06f, 8, true);
		k.CommitTo(hinge, "Leaf", true);
		block = new StaticBody3D { Name = "Block", CollisionLayer = 1, CollisionMask = 0 };
		block.SetMeta("surface", "wood");
		block.AddChild(new CollisionShape3D { Position = new Vector3(half, (spring + half) * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(width, spring + half, 0.14f) } });
		hinge.AddChild(block);
		return hinge;
	}

	/// <summary>The shape of what belongs in a niche, in thin gold lines on its back.</summary>
	private static void NicheIcon(MeshKit k, int i, Vector3 c)
	{
		void L(float x0, float y0, float x1, float y1) => k.Beam(c + new Vector3(x0, y0, 0), c + new Vector3(x1, y1, 0), 0.012f, 0.012f, 1f, Vector3.Back);
		switch (i)
		{
			case 0:   // the chalice
				L(-0.09f, 0.16f, 0.09f, 0.16f); L(-0.09f, 0.16f, -0.05f, 0.04f); L(0.09f, 0.16f, 0.05f, 0.04f); L(-0.05f, 0.04f, 0.05f, 0.04f);
				L(0f, 0.04f, 0f, -0.14f); L(-0.08f, -0.17f, 0.08f, -0.17f); L(-0.08f, -0.17f, 0f, -0.14f); L(0.08f, -0.17f, 0f, -0.14f);
				break;
			case 1:   // the bell
				L(-0.03f, 0.17f, 0.03f, 0.17f); L(-0.03f, 0.17f, -0.07f, 0.02f); L(0.03f, 0.17f, 0.07f, 0.02f); L(-0.07f, 0.02f, -0.12f, -0.12f);
				L(0.07f, 0.02f, 0.12f, -0.12f); L(-0.12f, -0.12f, 0.12f, -0.12f); L(0f, -0.12f, 0f, -0.17f);
				break;
			case 2:   // the crown
				L(-0.12f, -0.1f, 0.12f, -0.1f); L(-0.12f, -0.1f, -0.13f, 0.1f); L(0.12f, -0.1f, 0.13f, 0.1f); L(-0.13f, 0.1f, -0.06f, 0f);
				L(-0.06f, 0f, 0f, 0.14f); L(0f, 0.14f, 0.06f, 0f); L(0.06f, 0f, 0.13f, 0.1f);
				break;
			default:  // the dove, wings spread
				L(-0.14f, 0.08f, -0.03f, 0f); L(0.14f, 0.08f, 0.03f, 0f); L(-0.03f, 0f, 0.03f, 0f); L(0f, 0f, 0f, -0.12f);
				L(0f, -0.12f, -0.05f, -0.16f); L(0f, -0.12f, 0.05f, -0.16f); L(-0.03f, 0f, 0f, 0.07f); L(0.03f, 0f, 0f, 0.07f);
				break;
		}
	}

	// ------------------------------------------------------------------ the vestry

	private void BuildVestry()
	{
		var k = new MeshKit();
		var ash = ChurchTextures.AshlarMat;
		float x0 = VestryX0, x1 = VestryX1, z0 = VestryZ0, z1 = VestryZ1, h = VestryH;
		// its floor of oak boards, and a plain coffered ceiling
		k.Mat(ChurchTextures.OakMat);
		k.Color = Colors.White * 0.9f;
		k.Quad(new Vector3(x1, 0, z0), new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), Vector3.Up, new Vector2(x1, z0), new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1));
		Collide(_wood, new Vector3((x0 + x1) * 0.5f, -0.3f, (z0 + z1) * 0.5f), new Vector3(x0 - x1, 0.6f, z1 - z0));
		k.Quad(new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, h, z1), new Vector3(x0, h, z1), Vector3.Down, new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1));
		for (float x = x1 + 1f; x < x0; x += 1.6f) BuildKit.Box(k, new Vector3(x, h - 0.12f, (z0 + z1) * 0.5f), new Vector3(0.2f, 0.24f, z1 - z0), 1f);
		// its walls: the far wall with a window of clear glass (the snow outside), the two ends
		Wall(k, ash, new Vector3(x1, 0, 0), Vector3.Back, Vector3.Right, 0.9f, z0, z1, 0f, _ => h,
			new List<Hole> { Hole.Lancet((z0 + z1) * 0.5f - 0.9f, (z0 + z1) * 0.5f + 0.9f, 1.3f, 3.4f, ChurchTextures.QuarryMat, 0.8f) });
		Wall(k, ash, new Vector3(0, 0, z0), Vector3.Left, Vector3.Back, 0.9f, -x0, -x1 + 0.9f, 0f, _ => h, null);
		Wall(k, ash, new Vector3(0, 0, z1), Vector3.Left, Vector3.Forward, 0.9f, -x0, -x1 + 0.9f, 0f, _ => h, null);
		Collide(_stone, new Vector3(x1 - 0.45f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.9f, h, z1 - z0 + 1.8f));
		Collide(_stone, new Vector3((x0 + x1) * 0.5f, h * 0.5f, z0 - 0.45f), new Vector3(x0 - x1, h, 0.9f));
		Collide(_stone, new Vector3((x0 + x1) * 0.5f, h * 0.5f, z1 + 0.45f), new Vector3(x0 - x1, h, 0.9f));
		// the wardrobe for the vestments, a long table, a hook board
		k.Mat(ChurchTextures.OakMat);
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(x1 + 3.5f, 1.3f, z1 - 0.4f), new Vector3(2.4f, 2.6f, 0.7f), 1f, BuildKit.Face.NY);
		Collide(_wood, new Vector3(x1 + 3.5f, 1.3f, z1 - 0.4f), new Vector3(2.4f, 2.6f, 0.7f));
		BuildKit.Box(k, new Vector3(x1 + 1.3f, 0.78f, (z0 + z1) * 0.5f), new Vector3(0.9f, 0.06f, 3.2f), 1f);
		foreach (float dz in new[] { -1.4f, 1.4f }) foreach (float dx in new[] { -0.35f, 0.35f })
				BuildKit.Box(k, new Vector3(x1 + 1.3f + dx, 0.38f, (z0 + z1) * 0.5f + dz), new Vector3(0.07f, 0.76f, 0.07f), 1f);
		Collide(_wood, new Vector3(x1 + 1.3f, 0.4f, (z0 + z1) * 0.5f), new Vector3(0.9f, 0.8f, 3.2f));
		BuildKit.Box(k, new Vector3(x1 + 5.2f, 1.6f, z0 + 0.06f), new Vector3(1.6f, 0.3f, 0.06f), 1f);
		k.Mat(ChurchTextures.IronMat);
		for (int i = 0; i < 4; i++) k.Cylinder(new Vector3(x1 + 4.6f + i * 0.4f, 1.62f, z0 + 0.09f), new Vector3(x1 + 4.6f + i * 0.4f, 1.6f, z0 + 0.2f), 0.012f, 0.012f, 5, false);
		k.CommitTo(this, "Vestry", true);
		// a white alb hanging on a peg, a candle burning low on the table
		var alb = new MeshKit();
		alb.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.84f, 0.8f), Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
		alb.Cylinder(new Vector3(x1 + 6.6f, 0.4f, z0 + 0.35f), new Vector3(x1 + 6.6f, 1.9f, z0 + 0.35f), 0.38f, 0.14f, 10, false);
		alb.CommitTo(this, "Alb", true);
		// the key to the font, on the hook board
		var key = new Pickup { Name = "FontKey", Kind = ToolKind.FontKey, UseSpot = false, SnapToSurface = false, TakenId = "church_font_key", Position = new Vector3(x1 + 5.4f, 1.42f, z0 + 0.14f), Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0) };
		AddChild(key);
		FontKeyPickup = key;
		// the sexton's note on the table
		PaperKit.Flat(this, new Vector3(x1 + 1.3f, 0.815f, (z0 + z1) * 0.5f + 0.6f), 80f, new Vector2(0.16f, 0.22f), PaperKit.Look.Lined, "A note on the vestry table",
			"The snow came early and it has not stopped.\n\nI have locked the door and put the four away, one to each corner of the house, as the old priest asked. "
			+ "The cup I left in the font, under the lid, and the key to it on my hooks. The bell is where bells go. The crown sleeps with the dead. "
			+ "The dove I gave to the Lady.\n\nWhoever comes up from below: light the four candles first. The house listens to fire.",
			Readable.NoteStyle.Handwritten);
		var candle = new Vector3(x1 + 1.3f, 0.81f, (z0 + z1) * 0.5f - 1f);
		var c = new MeshKit();
		c.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.9f, 0.8f), Roughness = 0.6f });
		c.Cylinder(candle, candle + Vector3.Up * 0.12f, 0.03f, 0.03f, 8, true);
		c.CommitTo(this, "VestryCandle", false);
		AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.012f, Height = 0.04f }, Position = candle + Vector3.Up * 0.15f, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.62f, 0.25f), EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
		AddChild(new OmniLight3D { Position = candle + Vector3.Up * 0.3f, LightColor = new Color(1f, 0.7f, 0.4f), LightEnergy = 1.2f, OmniRange = 7f, ShadowEnabled = true });
		// the snow's grey light through its window
		AddChild(new OmniLight3D { Position = new Vector3(x1 + 1.5f, 2.6f, (z0 + z1) * 0.5f), LightColor = new Color(0.7f, 0.78f, 0.9f), LightEnergy = 0.6f, OmniRange = 7f, ShadowEnabled = false });
	}

	public Pickup FontKeyPickup { get; private set; }

	// ------------------------------------------------------------------ the lights

	private void BuildLights()
	{
		// daylight off the snow, in through the glass: cold, high and soft
		// (faint: the church is lit by its candles; the owner found the first, bright version glaring)
		foreach (var (at, e, r) in new[] { (new Vector3(0, 22f, 14f), 0.42f, 36f), (new Vector3(0, 22f, 38f), 0.42f, 36f), (new Vector3(0, 20f, 60.5f), 0.42f, 32f) })
			AddChild(new OmniLight3D { Position = at, LightColor = new Color(0.5f, 0.56f, 0.72f), LightEnergy = e, OmniRange = r, OmniAttenuation = 0.9f, ShadowEnabled = false });
		foreach (float s in new[] { -1f, 1f })
			for (int b = 0; b < NaveBays; b += 2)
				AddChild(new OmniLight3D { Position = new Vector3(s * 13.4f, 5f, (b + 0.5f) * BayLen), LightColor = new Color(0.6f, 0.68f, 0.82f), LightEnergy = 0.22f, OmniRange = 8f, OmniAttenuation = 1.2f, ShadowEnabled = false });
		// the stained glass throwing its colour down across the floor
		var colours = new[] { new Color(0.3f, 0.45f, 1f), new Color(1f, 0.3f, 0.25f), new Color(1f, 0.8f, 0.35f) };
		int n = 0;
		foreach (float s in new[] { -1f, 1f })
			foreach (float z in new[] { 11.25f, 26.25f, 41.25f })
			{
				var spot = new SpotLight3D
				{
					LightColor = colours[n % 3], LightEnergy = 2.2f, SpotRange = 34f, SpotAngle = 14f, SpotAttenuation = 0.8f, ShadowEnabled = false,
				};
				AddChild(spot);
				spot.Position = new Vector3(s * 7.2f, 21f, z);
				spot.LookAt(ToGlobal(new Vector3(-s * 2.5f, 0, z + 4f)), Vector3.Up);
				n++;
			}
		// chandeliers down the nave: iron hoops of dead candles on long chains
		var ch = new MeshKit();
		ch.Mat(ChurchTextures.IronMat);
		ch.Color = Colors.White;
		foreach (float s in new[] { -1f, 1f })
			foreach (float z in new[] { 11.25f, 26.25f, 41.25f })
			{
				Vector3 c = new(s * 4.2f, 9f, z);
				ch.Cylinder(c + Vector3.Up * 0.5f, new Vector3(c.X, NaveVaultY(c.X) - 0.2f, c.Z), 0.02f, 0.02f, 4, false);
				for (int r = 0; r < 2; r++)
				{
					float rr = r == 0 ? 1.1f : 0.7f, yy = r == 0 ? 0f : 0.6f;
					for (int i = 0; i < 16; i++)
					{
						float a0 = Mathf.Tau * i / 16f, a1 = Mathf.Tau * (i + 1) / 16f;
						ch.Beam(c + new Vector3(Mathf.Cos(a0) * rr, yy, Mathf.Sin(a0) * rr), c + new Vector3(Mathf.Cos(a1) * rr, yy, Mathf.Sin(a1) * rr), 0.05f, 0.05f);
					}
					for (int i = 0; i < 8; i++)
					{
						float a = Mathf.Tau * i / 8f;
						Vector3 p = c + new Vector3(Mathf.Cos(a) * rr, yy, Mathf.Sin(a) * rr);
						ch.Cylinder(p, p + Vector3.Up * 0.12f, 0.025f, 0.025f, 5, true);
						if (r == 0) ch.Beam(p + Vector3.Up * 0.02f, c + Vector3.Up * 0.5f, 0.02f, 0.02f);
					}
				}
			}
		ch.CommitTo(this, "Chandeliers", false);
	}

	private void BuildSound()
	{
		AudioDirector.Zone(this, new Vector3(0, 18f, (0 + ChancelEnd) * 0.5f), new Vector3(TransHalf * 2f, 36f, ChancelEnd + ApseR), AudioDirector.Space.Cavern, "NaveVerb");
		AudioDirector.Zone(this, new Vector3((VestryX0 + VestryX1) * 0.5f, VestryH * 0.5f, (VestryZ0 + VestryZ1) * 0.5f), new Vector3(8f, VestryH, VestryZ1 - VestryZ0), AudioDirector.Space.Room, "VestryVerb");
		AudioDirector.Haunt(this, new[] { new Vector3(-10f, 16f, 10f), new Vector3(10f, 22f, 40f), new Vector3(0f, 30f, 60f), new Vector3(-18f, 10f, 60f) },
			new[] { "haunt_boards", "haunt_moan", "haunt_chain" }, new Vector2(30f, 56f), -14f, 50f, () => StoryManager.Instance is { } s && s.Current >= Checkpoint.Act21ChurchReached);
		foreach (var (name, at, db) in new[] { ("winter_wind_loop", new Vector3(-16f, 8f, 26f), -16f), ("winter_wind_loop", new Vector3(16f, 8f, 26f), -18f), ("church_tone_loop", new Vector3(0, 12f, 40f), -20f) })
		{
			string path = $"res://assets/audio/ambient/{name}.wav";
			if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
			if (!ResourceLoader.Exists(path)) continue;
			var stream = GD.Load<AudioStream>(path);
			if (stream is AudioStreamWav w) { w = (AudioStreamWav)w.Duplicate(); w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopEnd = Mathf.RoundToInt(w.GetLength() * w.MixRate); stream = w; }
			AddChild(new AudioStreamPlayer3D { Stream = stream, Bus = "Events", VolumeDb = db, UnitSize = 14f, MaxDistance = 90f, Position = at, Autoplay = true });
		}
	}
}

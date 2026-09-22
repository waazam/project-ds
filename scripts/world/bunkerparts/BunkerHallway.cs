using System.Collections.Generic;
using Godot;
using static ProjectDS.World.BunkerParts.BunkerLayout;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Act 8's hallway: a long barrel-vaulted concrete tunnel (Z 0 .. -90) with a
/// rib every 2.5 m, conduits and sagging cable runs, wet floor strips and
/// joint lines, and a caged white lamp every 5 m. The lamps follow the story's
/// light sequence: deeper in they start to flicker, then commit to deep red,
/// and every lamp behind the player (back to the entrance) goes red with them.
/// The far end is swallowed by fog so the tunnel seems to go on forever.
/// </summary>
public partial class BunkerHallway : Node3D
{
	private class Lamp
	{
		public StandardMaterial3D Mat;
		public OmniLight3D Light;
		public float Z;
		public bool Red;
	}

	private static readonly Color White = new(0.85f, 0.85f, 0.9f);
	private static readonly Color Red = new(0.9f, 0.05f, 0.03f);
	private static readonly Color RedDim = new(0.7f, 0.04f, 0.02f);

	private readonly List<Lamp> _lamps = new();
	private float _maxDepth;

	/// <summary>The lamps have committed to red for good.</summary>
	public bool RedTriggered { get; private set; }

	public override void _Ready()
	{
		BuildShell();
		BuildRibsAndServices();
		BuildLamps();
		BuildStains();
		BuildCollision();
	}

	// ------------------------------------------------------------------ light sequence (story: unchanged)

	/// <summary>Only the lamps within this many metres of the player cast light (the glass glows regardless).</summary>
	public const float LampLightRange = 22f;

	/// <summary>Called every frame with the player's position local to the interior (null when absent).</summary>
	public void Animate(Vector3? playerLocal, double clock)
	{
		if (playerLocal is { } local && local.Z <= 0.01f && local.Z >= -HallLength - 1f)
			_maxDepth = Mathf.Max(_maxDepth, -local.Z);
		float frac = Mathf.Clamp(_maxDepth / HallLength, 0f, 1f);
		if (frac >= RedTriggerFrac) RedTriggered = true;

		foreach (var l in _lamps)
		{
			float depthHere = -l.Z;
			// Eighteen omnis overlap three deep along the tunnel; only the few near the player need to be lit
			// (a preview with no player leaves them all on).
			if (playerLocal is { } pl) l.Light.Visible = Mathf.Abs(pl.Z - l.Z) < LampLightRange;
			bool aheadFlicker = !RedTriggered && frac >= RedFlickerFrac && depthHere <= _maxDepth + 15f && depthHere >= _maxDepth - 2f;
			bool permRed = l.Red || (RedTriggered && depthHere <= _maxDepth + 0.5f);
			if (permRed) l.Red = true;

			Color target = l.Red ? Red : White;
			float energy = 1.6f;
			if (aheadFlicker)
			{
				// A lazy, dying-fluorescent pulse, not a strobe. Rate barely varies per lamp so they don't
				// all flicker in unison, but stays slow throughout.
				float flickRate = 1.1f + (depthHere % 7f) * 0.08f;
				float flick = Mathf.Sin((float)clock * flickRate) > (1f - (frac - RedFlickerFrac) / (RedTriggerFrac - RedFlickerFrac)) ? 0.1f : 1.6f;
				energy = flick;
				target = flick < 0.5f ? RedDim : target;
			}
			l.Mat.Emission = target;
			l.Mat.AlbedoColor = target;
			l.Mat.EmissionEnergyMultiplier = energy;
			l.Light.LightColor = target;
			l.Light.LightEnergy = energy * 1.3f;
		}
	}

	/// <summary>Previews: as if the player had already walked <paramref name="depth"/> metres in.</summary>
	public void ForceDepth(float depth)
	{
		_maxDepth = Mathf.Max(_maxDepth, depth);
		Animate(null, 0);
	}

	/// <summary>Restore: the lights committed to red on an earlier run; every lamp is red at once, no sequence.</summary>
	public void SetRedInstant() => ForceDepth(HallLength);

	/// <summary>For the preview's restore check: how many lamps are red, out of how many.</summary>
	public (int red, int total) LampTally()
	{
		int red = 0;
		foreach (var l in _lamps) if (l.Red) red++;
		return (red, _lamps.Count);
	}

	/// <summary>Previews only: back to the untouched white state, as at level load.</summary>
	public void ResetLightsForPreview()
	{
		_maxDepth = 0f;
		RedTriggered = false;
		foreach (var l in _lamps) { l.Red = false; l.Light.Visible = true; }
		Animate(null, 0);
	}

	// ------------------------------------------------------------------ building

	private void BuildShell()
	{
		var rng = new RandomNumberGenerator { Seed = 8101 };
		var k = new MeshKit();
		var prof = HallProfile();
		int bays = Mathf.CeilToInt(HallLength / RibSpacing);

		// Walls and vault: the profile swept in 2.5 m bays with smooth, inward normals.
		k.Mat(BunkerTextures.HallWallMat);
		for (int b = 0; b < bays; b++)
		{
			float z0 = -b * RibSpacing, z1 = Mathf.Max(-(b + 1) * RibSpacing, -HallLength);
			float shade = rng.RandfRange(0.9f, 1.04f);
			for (int j = 0; j < prof.Length - 1; j++)
			{
				Vector2 a = prof[j], c = prof[j + 1];
				bool kick = Mathf.IsEqualApprox(a.X, c.X);
				Vector3 na = kick ? new Vector3(-Mathf.Sign(a.X), 0, 0) : ArchNormal(a);
				Vector3 nc = kick ? new Vector3(-Mathf.Sign(c.X), 0, 0) : ArchNormal(c);
				// Water-darkened low courses near the far end, where the damp comes through.
				float damp = Mathf.Clamp((-z0 - 60f) / 30f, 0f, 1f);
				k.Color = new Color(shade, shade, shade * 0.98f) * (1f - 0.2f * damp * (a.Y < 1f ? 1f : 0.3f));
				Vector3 A0 = new(a.X, a.Y, z0), A1 = new(a.X, a.Y, z1), C0 = new(c.X, c.Y, z0), C1 = new(c.X, c.Y, z1);
				k.Tri(A0, C0, C1, na, nc, nc, Vector2.Zero, Vector2.Zero, Vector2.Zero);
				k.Tri(A0, C1, A1, na, nc, na, Vector2.Zero, Vector2.Zero, Vector2.Zero);
			}
		}

		// Floor: a 0.5 m grid so wetness (vertex alpha) can pool in soft strips: along the central
		// drain line in some bays, in blotches by the walls, and more of it toward the damp far end.
		// The clean industrial floor gives way to dirt and moss over the last stretch before the door.
		const float cell = 0.5f;
		int nx = Mathf.RoundToInt(HallHalfWidth * 2f / cell), nz = Mathf.RoundToInt(HallLength / cell);
		var wetBay = new float[bays + 1];
		for (int b = 0; b <= bays; b++) wetBay[b] = rng.Randf() < 0.45f ? rng.RandfRange(0.4f, 1f) : 0f;
		float Wet(float x, float z)
		{
			float depth = -z;
			float drain = Mathf.Max(0f, 1f - Mathf.Abs(x) / 0.45f) * wetBay[Mathf.Clamp((int)(depth / RibSpacing), 0, bays)];
			float edge = Mathf.Max(0f, (Mathf.Abs(x) - 1.5f) / 0.7f) * Mathf.Clamp(Mathf.Sin(depth * 0.37f + x) * 1.6f - 0.6f, 0f, 1f);
			float far = Mathf.Clamp((depth - 70f) / 20f, 0f, 1f) * 0.5f;
			return Mathf.Clamp(Mathf.Max(drain, edge) + far * Mathf.Max(0f, Mathf.Sin(depth * 1.3f + x * 2.1f)), 0f, 0.95f);
		}
		Color FloorCol(float x, float z)
		{
			float g = 0.95f + 0.1f * Mathf.Sin(z * 0.9f + x * 1.7f) * Mathf.Sin(z * 0.23f);
			var c = new Color(g, g, g * 0.98f);
			float over = Mathf.Clamp((-z - 78f) / 12f, 0f, 1f);   // overgrowth creeping in before the vine door
			c = c.Lerp(new Color(0.42f, 0.44f, 0.3f), over * 0.8f);
			c.A = 1f - Wet(x, z);
			return c;
		}
		var floorMesh = BunkerKit.Grid(nx, nz, (i, j) =>
		{
			float x = -HallHalfWidth + i * cell, z = -j * cell;
			return (new Vector3(x, 0, z), Vector3.Up, FloorCol(x, z));
		}, BunkerTextures.HallFloorMat);
		AddChild(new MeshInstance3D { Name = "HallwayFloor", Mesh = floorMesh });

		// Entrance end wall (Z 0). A solid slab rather than a fan: the vault's shell masks everything
		// outside its own outline, so the cap can never show a gap (the old fan skipped the bottom band).
		k.Mat(BunkerTextures.HallWallMat);
		k.Color = new Color(0.88f, 0.88f, 0.86f);
		k.Box(new Vector3(0, HallHeight * 0.5f, 0.15f), new Vector3(HallHalfWidth * 2f + 0.4f, HallHeight + 0.4f, 0.3f));
		k.CommitTo(this, "HallwayShell");
	}

	private static Vector3 ArchNormal(Vector2 p)
	{
		var d = new Vector2(p.X, p.Y - HallKickHeight).Normalized();
		return new Vector3(-d.X, -d.Y, 0);
	}

	private void BuildRibsAndServices()
	{
		var rng = new RandomNumberGenerator { Seed = 8102 };
		var k = new MeshKit();
		var prof = HallProfile();
		const float inset = 0.1f, half = 0.14f;

		// Ribs: a band following the profile, standing 0.1 m proud of the shell.
		k.Mat(BunkerTextures.HallWallMat);
		for (float z = -1.25f; z > -HallLength; z -= RibSpacing)
		{
			k.Color = new Color(0.97f, 0.97f, 0.95f) * rng.RandfRange(0.92f, 1.02f);
			for (int j = 0; j < prof.Length - 1; j++)
			{
				Vector2 a = prof[j], c = prof[j + 1];
				bool kick = Mathf.IsEqualApprox(a.X, c.X);
				Vector3 na = kick ? new Vector3(-Mathf.Sign(a.X), 0, 0) : ArchNormal(a);
				Vector3 nc = kick ? new Vector3(-Mathf.Sign(c.X), 0, 0) : ArchNormal(c);
				Vector3 A = new(a.X, a.Y, 0), C = new(c.X, c.Y, 0);
				Vector3 Ai = A + na * inset, Ci = C + nc * inset;
				Vector3 zf = new(0, 0, z + half), zb = new(0, 0, z - half);
				// inner face
				k.Tri(Ai + zf, Ci + zf, Ci + zb, na, nc, nc, Vector2.Zero, Vector2.Zero, Vector2.Zero);
				k.Tri(Ai + zf, Ci + zb, Ai + zb, na, nc, na, Vector2.Zero, Vector2.Zero, Vector2.Zero);
				// front and back faces
				k.Quad(A + zf, C + zf, Ci + zf, Ai + zf, Vector3.Back);
				k.Quad(A + zb, C + zb, Ci + zb, Ai + zb, Vector3.Forward);
			}
			// Floor joint across the tunnel under each rib.
			k.Color = new Color(0.35f, 0.35f, 0.34f);
			k.Box(new Vector3(0, 0.002f, z), new Vector3(HallHalfWidth * 2f - 0.2f, 0.004f, 0.035f));
		}
		k.CommitTo(this, "Ribs");

		// Services: two conduits low on the left wall, a cable tray high on the right, and cables
		// sagging between hooks on the ribs on the left.
		var s = new MeshKit();
		s.Mat(ProcTextures.MetalMat);
		s.Color = new Color(0.62f, 0.64f, 0.6f);
		float xl = -HallCollisionHalfWidth + 0.02f;
		BunkerKit.Tube(s, new[] { new Vector3(xl, 1.02f, -0.1f), new Vector3(xl, 1.02f, -HallLength + 0.1f) }, 0.035f, 0.035f, 6);
		BunkerKit.Tube(s, new[] { new Vector3(xl + 0.02f, 0.9f, -0.1f), new Vector3(xl + 0.02f, 0.9f, -HallLength + 0.1f) }, 0.024f, 0.024f, 6);
		// Cable tray on the right, under the spring of the vault.
		Vector2 tray = new(1.72f, 2.2f);
		s.Color = new Color(0.5f, 0.52f, 0.5f);
		s.Box(new Vector3(tray.X, tray.Y, -HallLength * 0.5f), new Vector3(0.3f, 0.015f, HallLength - 0.2f));
		s.Box(new Vector3(tray.X - 0.15f, tray.Y + 0.04f, -HallLength * 0.5f), new Vector3(0.012f, 0.08f, HallLength - 0.2f));
		for (float z = -1.25f; z > -HallLength; z -= RibSpacing)
		{
			s.Color = new Color(0.45f, 0.46f, 0.44f);
			s.Box(new Vector3(xl - 0.05f, 0.96f, z), new Vector3(0.1f, 0.2f, 0.05f));             // conduit saddle
			s.Beam(new Vector3(tray.X, tray.Y - 0.01f, z), new Vector3(2.02f, tray.Y + 0.3f, z), 0.03f, 0.03f);   // tray bracket
		}
		s.Mat(BunkerTextures.RubberMat);
		s.Color = Colors.White;
		for (int c = 0; c < 3; c++)
			s.Box(new Vector3(tray.X - 0.08f + c * 0.07f, tray.Y + 0.025f, -HallLength * 0.5f), new Vector3(0.05f, 0.035f, HallLength - 0.3f));
		float hookY = 2.35f, hookX = -1.72f;
		for (float z = -1.25f; z > -HallLength + RibSpacing; z -= RibSpacing)
		{
			float z2 = z - RibSpacing;
			BunkerKit.Cable(s, new Vector3(hookX, hookY, z), new Vector3(hookX, hookY, z2), 0.14f + rng.RandfRange(-0.03f, 0.05f), 0.016f);
			BunkerKit.Cable(s, new Vector3(hookX + 0.05f, hookY - 0.04f, z), new Vector3(hookX + 0.05f, hookY - 0.04f, z2), 0.22f + rng.RandfRange(-0.04f, 0.04f), 0.012f);
		}
		s.CommitTo(this, "Services");
	}

	private void BuildLamps()
	{
		var metal = new MeshKit();
		metal.Mat(ProcTextures.MetalMat);
		int count = Mathf.FloorToInt(HallLength / LightSpacing);
		for (int i = 0; i < count; i++)
		{
			float z = -LightSpacing * (i + 0.5f);
			var xf = new Transform3D(Basis.FromEuler(new Vector3(0, 0.3f * i, 0)), new Vector3(0, HallHeight - 0.02f, z));
			BunkerKit.CagedLamp(metal, xf);
			var mat = BunkerTextures.NewLampGlass(White, 1.6f);
			BunkerKit.LampGlass(this, xf, mat, $"LampGlass{i}");
			var light = new OmniLight3D
			{
				Name = $"Lamp{i}",
				LightColor = White, LightEnergy = 2.08f, OmniRange = 6.5f, OmniAttenuation = 1.2f,
				Position = new Vector3(0, HallHeight - 0.5f, z),
				LightSpecular = 0.6f,
			};
			AddChild(light);
			_lamps.Add(new Lamp { Mat = mat, Light = light, Z = z });
		}
		metal.CommitTo(this, "LampFixtures", false);
	}

	private void BuildStains()
	{
		var rng = new RandomNumberGenerator { Seed = 8103 };
		var streak = BunkerTextures.Streak();
		var rust = BunkerTextures.RustRun();
		var puddle = BunkerTextures.Puddle();
		// Water streaks weeping from the vault down the walls, and rust under the conduit saddles.
		for (int i = 0; i < 22; i++)
		{
			float z = -rng.RandfRange(3f, HallLength - 2f);
			float side = rng.Randf() < 0.5f ? -1f : 1f;
			float y = rng.RandfRange(1.6f, 2.3f);
			float x = side * Mathf.Sqrt(Mathf.Max(0.01f, HallHalfWidth * HallHalfWidth - (y - HallKickHeight) * (y - HallKickHeight)));
			BunkerKit.AddDecal(this, streak, new Vector3(x, y - 0.8f, z), new Vector3(-side, 0, 0), Vector3.Down,
				new Vector2(rng.RandfRange(0.5f, 1.1f), rng.RandfRange(1.8f, 2.6f)), 0.6f, new Color(1, 1, 1, rng.RandfRange(0.55f, 0.9f)));
		}
		for (int i = 0; i < 8; i++)
		{
			float z = -1.25f - RibSpacing * rng.RandiRange(1, 34);
			BunkerKit.AddDecal(this, rust, new Vector3(-HallHalfWidth, 0.55f, z), Vector3.Right, Vector3.Down,
				new Vector2(0.25f, 0.8f), 0.5f, new Color(1, 1, 1, 0.8f));
		}
		// Standing water catches the lamps.
		for (int i = 0; i < 9; i++)
		{
			float z = -rng.RandfRange(6f, HallLength - 3f);
			BunkerKit.AddDecal(this, puddle, new Vector3(rng.RandfRange(-1.2f, 1.2f), 0f, z), Vector3.Up, Vector3.Forward,
				new Vector2(rng.RandfRange(0.8f, 1.6f), rng.RandfRange(1.2f, 2.4f)), 0.3f, new Color(1, 1, 1, 0.9f), BunkerTextures.PuddleOrm());
		}
	}

	private void BuildCollision()
	{
		var body = new StaticBody3D { Name = "HallwayBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		AddChild(body);
		float w = HallCollisionHalfWidth;
		// A simple rectangular tunnel comfortably inside the visual vault (the curved upper
		// facets are well above head height and never need their own collision).
		body.AddChild(new CollisionShape3D { Position = new Vector3(-w - 0.1f, HallCollisionRoof * 0.5f, -HallLength * 0.5f), Shape = new BoxShape3D { Size = new Vector3(0.2f, HallCollisionRoof, HallLength) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(w + 0.1f, HallCollisionRoof * 0.5f, -HallLength * 0.5f), Shape = new BoxShape3D { Size = new Vector3(0.2f, HallCollisionRoof, HallLength) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.05f, -HallLength * 0.5f), Shape = new BoxShape3D { Size = new Vector3(HallHalfWidth * 2f, 0.1f, HallLength) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, HallCollisionRoof + 0.05f, -HallLength * 0.5f), Shape = new BoxShape3D { Size = new Vector3(HallHalfWidth * 2f, 0.1f, HallLength) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, HallCollisionRoof * 0.5f, 0.1f), Shape = new BoxShape3D { Size = new Vector3(HallHalfWidth * 2f, HallCollisionRoof, 0.2f) } });
	}
}

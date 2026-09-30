using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.World;
using ProjectDS.World.LakeParts;

namespace ProjectDS.Entities;

/// <summary>
/// The wendigo (Act 22 on; the owner's "best monster yet", after their references and the lore). The winter
/// woods' apex predator: in Act 22 it only stalks. It never touches the player; it is glimpsed, for a moment,
/// when they turn round, and the instant they look anywhere near it, it is gone, up into the trees faster than
/// anything should move, the boughs dumping their snow behind it (<see cref="Leap"/>). It speaks in voices it has
/// copied from people, and they come out wrong. Its steps are almost nothing.
///
/// The look, ours (Algonquian accounts of it, and the owner's pictures): a giant and yet starved: ash-grey skin
/// drawn tight over the bones, every rib and knuckle of the spine showing, the belly sunk to the backbone;
/// limbs far too long, the arms hanging past the knees to black frostbitten hands with long fingers; legs that
/// bend backward like a deer's; a mantle of matted black hair off the shoulders, frosted at the tips. Its head
/// is an elk's skull, bleached and cracked, with a man's jaw hung under it full of long uneven teeth and torn
/// lips; deep sockets with a pinprick of cold light in each; a rack of antlers, one snapped off short, hung with
/// strips of dead velvet. Through a gap in its ribs, where a heart should be, a shard of blue ice glows faintly
/// (the lore's heart of ice: the one light it carries). Its head hangs to one side, and twitches.
///
/// The node's origin is on the ground between its feet; it faces -Z. About 3.6 m tall standing hunched.
/// </summary>
public partial class Wendigo : Node3D
{
	public const float Height = 3.6f;
	public int Leaps { get; private set; }
	public bool Leaping => _leapT >= 0f;
	/// <summary>Where its chest is (for the view tests).</summary>
	public Vector3 ChestWorld => ToGlobal(new Vector3(0, 2.5f, -0.15f));

	private Node3D _body, _head, _armL, _armR;
	private GpuParticles3D _breath;
	private readonly RandomNumberGenerator _rng = new() { Seed = 666 };
	private double _time, _twitchAt = 1.2, _breathAt = 2.0;
	private Vector3 _headRest = new(0.12f, 0f, 0.42f), _headGoal;
	private float _leapT = -1f, _leapDur;
	private Vector3 _leapFrom, _leapTo;
	private System.Action _landed;

	private StandardMaterial3D _limb, _bone, _black, _hair, _frost, _eye, _heart, _lip, _socket;

	public override void _Ready()
	{
		// one skin for all of it, coloured per vertex (ash grey, bruised, frostbitten black-blue at the ends, bone at the joints)
		_limb = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = Colors.White, Roughness = 0.82f, AlbedoTexture = ProcTextures.Grime(), RimEnabled = true, Rim = 0.2f, RimTint = 0.4f };
		// the skull and the antlers: bone with a faint pallor of their own, so the head and the rack show in the murk
		_bone = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = Colors.White, Roughness = 0.7f, AlbedoTexture = ProcTextures.Grime(),
			EmissionEnabled = true, Emission = new Color(0.55f, 0.58f, 0.62f), EmissionEnergyMultiplier = 0.18f };
		_black = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.07f), Roughness = 0.45f, MetallicSpecular = 0.6f };
		_hair = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = Colors.White, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoTexture = PropTextures.FurMat.AlbedoTexture };
		_frost = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.66f, 0.72f), Roughness = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		_eye = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.9f, 1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color(0.7f, 0.85f, 1f), EmissionEnergyMultiplier = 0.9f };
		_heart = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.65f, 0.9f), EmissionEnabled = true, Emission = new Color(0.35f, 0.62f, 1f), EmissionEnergyMultiplier = 0.45f, Roughness = 0.05f, MetallicSpecular = 1f };
		_lip = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.07f, 0.06f), Roughness = 0.35f, MetallicSpecular = 0.6f };
		_socket = new StandardMaterial3D { AlbedoColor = new Color(0.015f, 0.012f, 0.012f), Roughness = 1f };
		_body = new Node3D { Name = "Body" };
		AddChild(_body);
		BuildLegs();
		BuildTorso();
		BuildArms();
		BuildHead();
		BuildBreath();
		_headGoal = _headRest;
		PhotoSubject.Attach(_head, "wendigo", new Vector3(0, 0.05f, -0.2f), 3f, 60f, 10f, false);
	}

	// ------------------------------------------------------------------ the model

	// its colours (vertex colours over one grimy skin material: ash grey, darker and bluer to the extremities)
	internal static readonly Color Ash = new(0.47f, 0.46f, 0.45f), AshDark = new(0.33f, 0.33f, 0.34f), Frostbite = new(0.1f, 0.11f, 0.14f),
		Bruise = new(0.28f, 0.26f, 0.29f), BoneCol = new(0.74f, 0.71f, 0.64f), Sore = new(0.3f, 0.12f, 0.1f);

	/// <summary>A lofted limb or trunk: rings along a path, each an ellipse (rx across, rz front-to-back) with its own
	/// colour, the frame carried smoothly along so it never twists. Knobbed joints, wasted muscle, bone under skin.</summary>
	internal static void Loft(MeshKit k, List<(Vector3 c, float rx, float rz, Color col)> rings, int sides = 9)
	{
		int n = rings.Count;
		var bx = new Vector3[n]; var bz = new Vector3[n];
		Vector3 refDir = Vector3.Forward;
		for (int i = 0; i < n; i++)
		{
			var t = (rings[Mathf.Min(i + 1, n - 1)].c - rings[Mathf.Max(i - 1, 0)].c).Normalized();
			var r = Mathf.Abs(t.Dot(refDir)) > 0.95f ? Vector3.Right : refDir;
			bx[i] = t.Cross(r).Normalized();
			bz[i] = bx[i].Cross(t).Normalized();
			// keep the frame's handedness facing forward (-Z) so rz is front-to-back
			if (bz[i].Z > 0) { bx[i] = -bx[i]; bz[i] = -bz[i]; }
		}
		for (int i = 0; i < n - 1; i++)
			for (int s = 0; s < sides; s++)
			{
				float a0 = Mathf.Tau * s / sides, a1 = Mathf.Tau * (s + 1) / sides;
				Vector3 P(int ri, float a) => rings[ri].c + bx[ri] * Mathf.Cos(a) * rings[ri].rx + bz[ri] * Mathf.Sin(a) * rings[ri].rz;
				Vector3 N(int ri, float a) => (bx[ri] * Mathf.Cos(a) / Mathf.Max(rings[ri].rx, 0.001f) + bz[ri] * Mathf.Sin(a) / Mathf.Max(rings[ri].rz, 0.001f)).Normalized();
				Vector3 p00 = P(i, a0), p01 = P(i, a1), p10 = P(i + 1, a0), p11 = P(i + 1, a1);
				Vector2 u00 = new(s / (float)sides, i * 0.25f), u01 = new((s + 1) / (float)sides, i * 0.25f), u10 = new(s / (float)sides, (i + 1) * 0.25f), u11 = new((s + 1) / (float)sides, (i + 1) * 0.25f);
				k.TriC(p00, p10, p11, N(i, a0), N(i + 1, a0), N(i + 1, a1), u00, u10, u11, rings[i].col, rings[i + 1].col, rings[i + 1].col);
				k.TriC(p00, p11, p01, N(i, a0), N(i + 1, a1), N(i, a1), u00, u11, u01, rings[i].col, rings[i + 1].col, rings[i].col);
			}
	}

	private void BuildLegs()
	{
		var k = new MeshKit();
		k.Mat(_limb);
		foreach (float s in new[] { -1f, 1f })
		{
			// a starved man's thigh, a knee like a knot, then a deer's backward-bent shank to a black, long-toed foot
			Vector3 hip = new(s * 0.19f, 1.8f, 0.06f), knee = new(s * 0.24f, 1.14f, -0.26f), hock = new(s * 0.23f, 0.56f, 0.22f), foot = new(s * 0.22f, 0.07f, 0.0f);
			Loft(k, new()
			{
				(hip + Vector3.Up * 0.06f, 0.11f, 0.12f, Ash), (hip.Lerp(knee, 0.3f), 0.085f, 0.09f, Ash), (hip.Lerp(knee, 0.75f), 0.058f, 0.062f, AshDark),
				(knee, 0.072f, 0.07f, AshDark), (knee.Lerp(hock, 0.3f), 0.05f, 0.058f, AshDark), (knee.Lerp(hock, 0.8f), 0.036f, 0.04f, Bruise),
				(hock, 0.05f, 0.055f, Bruise), (hock.Lerp(foot, 0.5f), 0.034f, 0.036f, Frostbite), (foot, 0.04f, 0.045f, Frostbite),
			}, 8);
			for (int t = -1; t <= 1; t++)
			{
				Vector3 a = foot + new Vector3(t * 0.05f, -0.02f, -0.02f), b = a + new Vector3(t * 0.04f, -0.03f, -0.16f), c = b + new Vector3(t * 0.02f, -0.02f, -0.13f);
				Loft(k, new() { (a, 0.024f, 0.022f, Frostbite), (b, 0.018f, 0.017f, Frostbite), (c, 0.004f, 0.004f, Frostbite) }, 5);
			}
		}
		k.CommitTo(_body, "Legs", false);
	}

	private void BuildTorso()
	{
		var k = new MeshKit();
		k.Mat(_limb);
		// the trunk, hunched: the pelvis, the waist sunk to the backbone, the ribcage, the high bony shoulders
		Loft(k, new()
		{
			(new Vector3(0, 1.72f, 0.07f), 0.16f, 0.11f, AshDark), (new Vector3(0, 1.84f, 0.07f), 0.21f, 0.13f, Ash),
			(new Vector3(0, 2.0f, 0.07f), 0.13f, 0.09f, AshDark), (new Vector3(0, 2.18f, 0.04f), 0.115f, 0.08f, Bruise),
			(new Vector3(0, 2.36f, -0.02f), 0.2f, 0.15f, Ash), (new Vector3(0, 2.56f, -0.08f), 0.25f, 0.19f, Ash),
			(new Vector3(0, 2.76f, -0.15f), 0.26f, 0.18f, Ash), (new Vector3(0, 2.92f, -0.22f), 0.21f, 0.14f, AshDark),
			(new Vector3(0, 3.0f, -0.27f), 0.11f, 0.09f, AshDark),
		}, 12);
		// the neck, long and thin, thrust forward and down, its cords standing out
		Loft(k, new()
		{
			(new Vector3(0, 2.96f, -0.26f), 0.085f, 0.08f, AshDark), (new Vector3(0, 3.09f, -0.38f), 0.062f, 0.066f, Ash),
			(new Vector3(0, 3.16f, -0.5f), 0.058f, 0.06f, Ash), (new Vector3(0, 3.19f, -0.6f), 0.07f, 0.065f, AshDark),
		}, 8);
		// shoulders: knobs of bone, sloping, and the collarbones across the front
		k.Color = Ash;
		foreach (float s in new[] { -1f, 1f })
		{
			k.Blob(new Vector3(s * 0.3f, 2.9f, -0.2f), new Vector3(0.1f, 0.075f, 0.1f), 74 + (int)s, 0.12f, false, 1f);
			Loft(k, new() { (new Vector3(s * 0.04f, 2.9f, -0.33f), 0.02f, 0.02f, BoneCol), (new Vector3(s * 0.2f, 2.93f, -0.3f), 0.022f, 0.02f, BoneCol), (new Vector3(s * 0.3f, 2.92f, -0.24f), 0.018f, 0.018f, BoneCol) }, 5);
			// hip bones jutting
			k.Color = BoneCol;
			k.Blob(new Vector3(s * 0.19f, 1.92f, -0.02f), new Vector3(0.05f, 0.04f, 0.06f), 71 + (int)s, 0.2f, false, 1f);
			k.Color = Ash;
		}
		// ribs: hoops standing out of the skin round the front and sides, a gap torn over the heart
		for (int r = 0; r < 8; r++)
		{
			float y = 2.82f - r * 0.07f;
			float z0 = Mathf.Lerp(-0.15f, -0.05f, (2.82f - y) / 0.5f);
			float w = Mathf.Lerp(0.26f, 0.23f, Mathf.Abs(r - 3) / 4f) + 0.012f, d = Mathf.Lerp(0.19f, 0.15f, Mathf.Abs(r - 3) / 4f) + 0.012f;
			var ring = new List<Vector3>();
			for (int j = 0; j <= 14; j++)
			{
				float a = Mathf.Lerp(-2.4f, 2.4f, j / 14f);
				bool gap = r >= 2 && r <= 4 && a > -0.75f && a < 0.15f;
				var p = new Vector3(Mathf.Sin(a) * w, y - Mathf.Abs(a) * 0.035f, z0 - Mathf.Cos(a) * d);
				if (gap) { if (ring.Count > 1) Loft(k, Ribs(ring), 4); ring.Clear(); continue; }
				ring.Add(p);
			}
			if (ring.Count > 1) Loft(k, Ribs(ring), 4);
		}
		// the spine: a row of knuckles down its back
		k.Color = BoneCol;
		for (int v = 0; v < 15; v++)
		{
			float t = v / 14f;
			var p = new Vector3(0, Mathf.Lerp(2.98f, 1.86f, t), Mathf.Lerp(-0.1f, 0.19f, t) + Mathf.Sin(t * Mathf.Pi) * 0.1f + 0.02f);
			k.Blob(p, new Vector3(0.032f, 0.026f, 0.04f), 80 + v, 0.2f, false, 1f);
		}
		k.CommitTo(_body, "Torso", false);
		// the heart: a shard of ice behind the torn ribs, glowing faintly blue
		var h = new MeshKit();
		h.Mat(_heart);
		h.Color = Colors.White;
		h.Blob(new Vector3(-0.06f, 2.63f, -0.2f), new Vector3(0.03f, 0.05f, 0.03f), 90, 0.4f, true, 1f);
		h.CommitTo(_body, "Heart", false);
		_body.AddChild(new OmniLight3D { Name = "HeartGlow", Position = new Vector3(-0.06f, 2.63f, -0.36f), LightColor = new Color(0.45f, 0.7f, 1f), LightEnergy = 0.1f, OmniRange = 0.55f, ShadowEnabled = false });
		BuildMantle();
	}

	private static List<(Vector3, float, float, Color)> Ribs(List<Vector3> pts)
	{
		var l = new List<(Vector3, float, float, Color)>();
		foreach (var p in pts) l.Add((p, 0.013f, 0.013f, BoneCol));
		return l;
	}

	/// <summary>The mantle: a shaggy hump of matted black hair over the shoulders and down the back, hanging in
	/// clumped, ragged locks that curl a little, frost whitening their ends.</summary>
	private void BuildMantle()
	{
		var k = new MeshKit();
		k.Mat(_hair);
		// the hump: fur over the shoulders and the upper back
		k.Color = new Color(0.1f, 0.09f, 0.085f);
		k.Blob(new Vector3(0, 2.92f, -0.08f), new Vector3(0.36f, 0.16f, 0.26f), 140, 0.3f, false, 1f);
		k.Blob(new Vector3(0, 2.72f, 0.06f), new Vector3(0.3f, 0.24f, 0.16f), 141, 0.3f, false, 1f);
		// the locks
		for (int i = 0; i < 90; i++)
		{
			float a = _rng.RandfRange(-2.6f, 2.6f);   // round the shoulders, 0 = the back
			float rr = _rng.RandfRange(0.24f, 0.37f);
			var root = new Vector3(Mathf.Sin(a) * rr, _rng.RandfRange(2.78f, 3.02f), -0.1f + Mathf.Cos(a) * rr * 0.8f);
			bool back = Mathf.Abs(a) < 1.3f;
			float len = back ? _rng.RandfRange(0.45f, 1.05f) : _rng.RandfRange(0.25f, 0.55f);
			var outward = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a) * 0.8f).Normalized();
			float w = _rng.RandfRange(0.035f, 0.07f);
			var side = outward.Cross(Vector3.Up).Normalized();
			// three segments, curling out and in again, narrowing to a point
			Vector3 p0 = root, p1 = root + Vector3.Down * len * 0.35f + outward * 0.05f, p2 = root + Vector3.Down * len * 0.7f + outward * 0.07f + side * _rng.RandfRange(-0.04f, 0.04f), p3 = root + Vector3.Down * len + outward * 0.04f + side * _rng.RandfRange(-0.06f, 0.06f);
			Color dark = new(0.08f, 0.075f, 0.07f), mid = new(0.13f, 0.12f, 0.115f), tip = new(0.55f, 0.58f, 0.64f);
			Lock(k, p0, p1, w, w * 0.85f, side, outward, dark, dark);
			Lock(k, p1, p2, w * 0.85f, w * 0.5f, side, outward, dark, mid);
			Lock(k, p2, p3, w * 0.5f, 0.003f, side, outward, mid, tip);
		}
		k.CommitTo(_body, "Mantle", false);
	}

	private static void Lock(MeshKit k, Vector3 a, Vector3 b, float wa, float wb, Vector3 side, Vector3 outward, Color ca, Color cb)
	{
		Vector3 a0 = a - side * wa, a1 = a + side * wa, b0 = b - side * wb, b1 = b + side * wb;
		k.TriC(a0, b0, b1, outward, outward, outward, Vector2.Zero, Vector2.Down, Vector2.One, ca, cb, cb);
		k.TriC(a0, b1, a1, outward, outward, outward, Vector2.Zero, Vector2.One, Vector2.Right, ca, cb, ca);
		k.TriC(a0, b1, b0, -outward, -outward, -outward, Vector2.Zero, Vector2.One, Vector2.Down, ca, cb, cb);
		k.TriC(a0, a1, b1, -outward, -outward, -outward, Vector2.Zero, Vector2.Right, Vector2.One, ca, ca, cb);
	}

	private void BuildArms()
	{
		foreach (float s in new[] { -1f, 1f })
		{
			var pivot = new Node3D { Name = s < 0 ? "ArmL" : "ArmR", Position = new Vector3(s * 0.33f, 2.9f, -0.2f) };
			_body.AddChild(pivot);
			if (s < 0) _armL = pivot; else _armR = pivot;
			var k = new MeshKit();
			k.Mat(_limb);
			// far too long: the hands hang past the knees
			Vector3 elbow = new(s * 0.1f, -0.8f, -0.12f), wrist = new(s * 0.07f, -1.66f, -0.24f), palm = wrist + new Vector3(0, -0.12f, -0.02f);
			Loft(k, new()
			{
				(new Vector3(0, 0.02f, 0), 0.075f, 0.08f, Ash), (elbow * 0.35f, 0.06f, 0.062f, Ash), (elbow * 0.8f, 0.042f, 0.045f, AshDark),
				(elbow, 0.055f, 0.052f, AshDark), (elbow.Lerp(wrist, 0.3f), 0.045f, 0.04f, AshDark), (elbow.Lerp(wrist, 0.85f), 0.03f, 0.026f, Bruise),
				(wrist, 0.036f, 0.028f, Frostbite), (palm, 0.045f, 0.022f, Frostbite),
			}, 8);
			// four fingers, each far too long, three joints, curled, clawed black
			for (int f = 0; f < 4; f++)
			{
				float spread = (f - 1.5f) * 0.03f;
				Vector3 a = palm + new Vector3(spread * s, -0.02f, 0), b = a + new Vector3(spread * 0.5f * s, -0.17f, -0.03f), c = b + new Vector3(spread * 0.2f * s, -0.14f, -0.07f), d = c + new Vector3(0, -0.07f, -0.08f);
				Loft(k, new() { (a, 0.015f, 0.014f, Frostbite), (b, 0.012f, 0.012f, Frostbite), (c, 0.009f, 0.009f, Frostbite), (d, 0.002f, 0.002f, new Color(0.03f, 0.03f, 0.04f)) }, 5);
			}
			// the thumb
			Loft(k, new() { (palm + new Vector3(-s * 0.035f, 0.03f, -0.02f), 0.013f, 0.013f, Frostbite), (palm + new Vector3(-s * 0.06f, -0.12f, -0.07f), 0.004f, 0.004f, Frostbite) }, 5);
			k.CommitTo(pivot, "Arm", false);
		}
	}

	private void BuildHead()
	{
		_head = new Node3D { Name = "Head", Position = new Vector3(0, 3.2f, -0.6f) };
		_body.AddChild(_head);
		var k = new MeshKit();
		k.Mat(_bone);
		// the skull: a bleached elk's, long and narrow, the snout angled down; the sockets on its sides
		Loft(k, new()
		{
			(new Vector3(0, 0.1f, 0.08f), 0.07f, 0.07f, BoneCol), (new Vector3(0, 0.1f, -0.02f), 0.115f, 0.1f, BoneCol),
			(new Vector3(0, 0.06f, -0.14f), 0.1f, 0.085f, BoneCol), (new Vector3(0, 0.0f, -0.28f), 0.065f, 0.06f, BoneCol),
			(new Vector3(0, -0.07f, -0.44f), 0.05f, 0.045f, BoneCol), (new Vector3(0, -0.13f, -0.58f), 0.035f, 0.03f, new Color(0.62f, 0.58f, 0.52f)),
		}, 10);
		// the brow ridges over the sockets
		k.Color = BoneCol;
		foreach (float s in new[] { -1f, 1f })
			k.Blob(new Vector3(s * 0.085f, 0.12f, -0.11f), new Vector3(0.045f, 0.025f, 0.05f), 111 + (int)s, 0.15f, false, 1f);
		// the sockets: long, slanted, black, deep, and far back in each a pinprick of cold light
		k.Mat(_socket);
		k.Color = Colors.White;
		foreach (float s in new[] { -1f, 1f })
			k.Blob(new Vector3(s * 0.095f, 0.075f, -0.12f), new Vector3(0.028f, 0.034f, 0.05f), 113 + (int)s, 0.1f, false, 1f);
		k.Blob(new Vector3(0, -0.1f, -0.55f), new Vector3(0.02f, 0.016f, 0.03f), 115, 0.1f, false, 1f);   // the nose holes
		k.Mat(_eye);
		foreach (float s in new[] { -1f, 1f })
			k.Blob(new Vector3(s * 0.11f, 0.075f, -0.125f), Vector3.One * 0.007f, 116 + (int)s, 0f, false, 1f);
		// the jaw: a man's, hung loose and too low under the snout, torn lips in strips, long uneven teeth
		k.Mat(_limb);
		foreach (float s in new[] { -1f, 1f })
			Loft(k, new() { (new Vector3(s * 0.075f, -0.03f, -0.1f), 0.022f, 0.028f, AshDark), (new Vector3(s * 0.05f, -0.2f, -0.28f), 0.018f, 0.024f, Ash), (new Vector3(s * 0.02f, -0.27f, -0.42f), 0.016f, 0.02f, Ash) }, 6);
		k.Mat(_lip);
		k.Color = Colors.White;
		for (int i = 0; i < 10; i++)
		{
			float u = i / 9f, s = i % 2 == 0 ? -1f : 1f;
			var top = new Vector3(s * Mathf.Lerp(0.07f, 0.03f, u), Mathf.Lerp(-0.06f, -0.2f, u), Mathf.Lerp(-0.14f, -0.45f, u));
			Loft(k, new() { (top, 0.01f, 0.006f, Sore), (top + new Vector3(0, -_rng.RandfRange(0.03f, 0.09f), 0), 0.002f, 0.002f, Sore) }, 4);
		}
		k.Mat(_bone);
		for (int t = 0; t < 18; t++)
		{
			float u = t / 17f, s = t % 2 == 0 ? -1f : 1f;
			var root = new Vector3(s * Mathf.Lerp(0.062f, 0.024f, u), Mathf.Lerp(-0.05f, -0.16f, u), Mathf.Lerp(-0.18f, -0.52f, u));
			float len = 0.045f + (t * 37 % 7) * 0.013f;
			Loft(k, new() { (root, 0.007f, 0.007f, new Color(0.7f, 0.66f, 0.5f)), (root + new Vector3(0, -len, -0.01f), 0.001f, 0.001f, new Color(0.62f, 0.58f, 0.44f)) }, 4);
		}
		// the antlers: a great spreading rack, one side snapped short, strips of dead velvet hanging off it
		k.Mat(_bone);
		Rack(k, 1f, 1.25f, false);
		Rack(k, -1f, 1.2f, true);
		k.CommitTo(_head, "Skull", false);
		_head.Rotation = _headRest;
	}

	/// <summary>One antler: a main beam sweeping back, out and up from the skull, with tines off its front edge; a
	/// snapped one ends in a jagged stump after its second tine.</summary>
	private void Rack(MeshKit k, float s, float len, bool broken)
	{
		var rng = new RandomNumberGenerator { Seed = s > 0 ? 118UL : 119UL };
		var beam = new List<(Vector3 c, float rx, float rz, Color col)>();
		int segs = broken ? 4 : 9;
		Vector3 prev = new(s * 0.07f, 0.17f, -0.02f);
		for (int i = 0; i <= segs; i++)
		{
			float t = i / 9f;
			var p = new Vector3(s * (0.07f + Mathf.Sin(t * 1.6f) * len * 0.62f), 0.17f + t * len * 0.72f + Mathf.Sin(t * 3f) * 0.05f, -0.02f + Mathf.Sin(t * 2.2f) * len * 0.28f - t * t * 0.1f);
			float r = Mathf.Lerp(0.034f, 0.01f, t);
			beam.Add((p, r, r, BoneCol.Lerp(new Color(0.55f, 0.5f, 0.44f), t * 0.6f)));
			// tines off the front and top of the beam
			if (i >= 1 && i < segs && i % 2 == 1)
			{
				var dir = new Vector3(s * rng.RandfRange(-0.1f, 0.35f), rng.RandfRange(0.55f, 0.9f), rng.RandfRange(-0.6f, -0.25f)).Normalized();
				float tl = rng.RandfRange(0.22f, 0.38f) * len * (1f - t * 0.4f);
				var mid = p + dir * tl * 0.55f + new Vector3(0, 0.02f, 0);
				Loft(k, new() { (p, r * 0.8f, r * 0.8f, BoneCol), (mid, r * 0.5f, r * 0.5f, BoneCol), (p + dir * tl, 0.002f, 0.002f, new Color(0.6f, 0.56f, 0.48f)) }, 5);
				// a strip of dead velvet hanging off the tine
				if (rng.Randf() < 0.6f)
				{
					k.Mat(_hair);
					var at = mid;
					Loft(k, new() { (at, 0.01f, 0.004f, new Color(0.16f, 0.12f, 0.1f)), (at + new Vector3(rng.RandfRange(-0.02f, 0.02f), -rng.RandfRange(0.12f, 0.3f), 0), 0.004f, 0.002f, new Color(0.22f, 0.17f, 0.14f)) }, 4);
					k.Mat(_bone);
				}
			}
			prev = p;
		}
		if (broken)
		{
			// a jagged, splintered end
			var (c, r, _, _) = beam[^1];
			beam.Add((c + new Vector3(s * 0.01f, 0.03f, 0.01f), r * 0.6f, r * 1.1f, new Color(0.8f, 0.76f, 0.66f)));
		}
		Loft(k, beam, 7);
	}

	private void BuildBreath()
	{
		_breath = new GpuParticles3D
		{
			Name = "Breath", Amount = 18, Lifetime = 1.6f, OneShot = true, Emitting = false, Explosiveness = 0.7f, Position = new Vector3(0, -0.14f, -0.58f),
			ProcessMaterial = new ParticleProcessMaterial
			{
				Direction = new Vector3(0, -0.2f, -1f), Spread = 20f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.5f, Gravity = new Vector3(0, 0.08f, 0),
				ScaleMin = 0.6f, ScaleMax = 1.4f, Color = new Color(0.7f, 0.74f, 0.8f, 0.1f),
			},
			DrawPass1 = new QuadMesh
			{
				Size = new Vector2(0.09f, 0.09f),
				Material = new StandardMaterial3D { AlbedoTexture = LakeFx.SoftDot(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles },
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_head.AddChild(_breath);
	}

	/// <summary>Just the head (the dining hall's platter, Act 23): everything else hidden, the skull at the node's origin.
	/// It still twitches.</summary>
	public void ShowOnlyHead()
	{
		foreach (var c in _body.GetChildren()) if (c is Node3D n && n != _head) n.Visible = false;
		_head.Position = Vector3.Zero;
		_headRest = new Vector3(0.1f, 0f, 0.25f);
		_head.Rotation = _headRest;
		_headGoal = _headRest;
		Visible = true;
	}

	/// <summary>The skull's mouth (for the blood).</summary>
	public Vector3 MouthWorld => _head.ToGlobal(new Vector3(0, -0.2f, -0.42f));

	// ------------------------------------------------------------------ life

	/// <summary>Stand at <paramref name="ground"/> facing <paramref name="look"/>, visible.</summary>
	public void StandAt(Vector3 ground, Vector3 look)
	{
		_leapT = -1f;
		GlobalPosition = ground;
		var d = (look - ground) with { Y = 0 };
		if (d.LengthSquared() > 0.01f) GlobalBasis = Basis.LookingAt(d, Vector3.Up);
		_body.Position = Vector3.Zero;
		_body.Scale = Vector3.One;
		_armL.Rotation = _armR.Rotation = Vector3.Zero;
		Visible = true;
	}

	/// <summary>Up into the trees: a crouch of a few hundredths of a second, then gone upward along an arc to
	/// <paramref name="perch"/> in <paramref name="seconds"/>, stretched with the speed, arms thrown up.
	/// <paramref name="landed"/> runs when it's there (the snow falls, it vanishes).</summary>
	public void Leap(Vector3 perch, float seconds, System.Action landed)
	{
		Leaps++;
		_leapFrom = GlobalPosition;
		_leapTo = perch;
		_leapDur = seconds;
		_leapT = 0f;
		_landed = landed;
		AudioDirector.OneShot(this, "wendigo_leap", 3, GlobalPosition + Vector3.Up * 2f, -3f, "Events", 7f, 0.05f);
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;
		float dt = (float)delta;
		_time += delta;
		if (_leapT >= 0f)
		{
			_leapT += dt;
			const float crouch = 0.05f;
			if (_leapT < crouch)
			{
				float c = _leapT / crouch;
				_body.Position = Vector3.Down * 0.35f * c;
				_body.Scale = new Vector3(1f, 1f - 0.12f * c, 1f);
				return;
			}
			float u = Mathf.Clamp((_leapT - crouch) / _leapDur, 0f, 1f);
			float e = 1f - Mathf.Pow(1f - u, 2.2f);
			GlobalPosition = _leapFrom.Lerp(_leapTo, e) + Vector3.Up * Mathf.Sin(u * Mathf.Pi) * 1.2f;
			_body.Position = Vector3.Zero;
			_body.Scale = new Vector3(0.9f, 1.35f, 0.9f).Lerp(Vector3.One, u * u);
			_armL.Rotation = new Vector3(Mathf.Pi * 0.9f, 0, 0.3f);
			_armR.Rotation = new Vector3(Mathf.Pi * 0.9f, 0, -0.3f);
			if (u >= 1f)
			{
				_leapT = -1f;
				Visible = false;
				var cb = _landed; _landed = null;
				cb?.Invoke();
			}
			return;
		}
		// standing: breathing, the head hanging to one side and now and then snapping to a new angle
		_body.Scale = new Vector3(1f, 1f + Mathf.Sin((float)_time * 1.4f) * 0.008f, 1f);
		if (_time >= _twitchAt)
		{
			_twitchAt = _time + _rng.RandfRange(0.6f, 2.4f);
			_headGoal = _headRest + new Vector3(_rng.RandfRange(-0.2f, 0.25f), _rng.RandfRange(-0.35f, 0.35f), _rng.RandfRange(-0.5f, 0.3f));
		}
		_head.Rotation = _head.Rotation.Lerp(_headGoal, 1f - Mathf.Exp(-dt * 30f));
		if (_time >= _breathAt)
		{
			_breathAt = _time + _rng.RandfRange(2.2f, 3.6f);
			_breath.Restart();
		}
		// the arms hang, swaying a hair
		float sway = Mathf.Sin((float)_time * 0.9f) * 0.03f;
		_armL.Rotation = new Vector3(sway, 0, 0.05f);
		_armR.Rotation = new Vector3(-sway, 0, -0.05f);
	}
}

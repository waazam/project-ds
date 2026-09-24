using Godot;

namespace ProjectDS.World.LakeParts;

/// <summary>
/// The rowboat: a small clinker-built open boat (overlapping planks, a pointed bow, a flat transom),
/// its paint gone to a chalky green outside and bare grey wood inside, "STATION 7" still just legible
/// on the bow — it came from the rescue station across the water. Three thwarts, ribs, and two oars that
/// really row.
///
/// Boat-local space: origin at the waterline amidships, bow toward -Z. The player sits on the middle
/// thwart facing the bow (forward-facing "push" rowing reads best in first person) at
/// <see cref="SeatLocal"/>, with the two oars in their locks just ahead of them.
///
/// <see cref="Stroke"/> plays one oar's catch, drive, release and recovery; the owner hears about the
/// blade entering and leaving the water through <see cref="BladeIn"/> / <see cref="BladeOut"/> (for
/// splashes and sound), with the blade tip's world position.
/// </summary>
public partial class Rowboat : Node3D
{
	public const float Length = 3.9f, HalfLength = Length * 0.5f, Beam = 1.4f, Depth = 0.56f, Draft = 0.2f;
	public const float SeatZ = 0.28f;
	/// <summary>Where the rower's body origin goes (the eye is <see cref="SitEyeHeight"/> above it).</summary>
	public static readonly Vector3 SeatLocal = new(0, 0.15f, SeatZ + 0.05f);
	/// <summary>Eye height above <see cref="SeatLocal"/> while sitting (the camera rig's EyeHeight is eased to this).</summary>
	public const float SitEyeHeight = 0.92f;
	private const float OarlockZ = SeatZ - 0.42f;
	private const float Inboard = 0.55f, Outboard = 1.9f;

	/// <summary>The oar that just dipped (-1 left, +1 right) and its blade's world position.</summary>
	[Signal] public delegate void BladeInEventHandler(int side, Vector3 bladeWorld);
	[Signal] public delegate void BladeOutEventHandler(int side, Vector3 bladeWorld);

	public Node3D Hull { get; private set; }
	private readonly Oar[] _oars = new Oar[2];

	private sealed class Oar
	{
		public int Side;
		public Node3D Pivot;
		/// <summary>0..1 through the current stroke; &gt;= 1 = at rest, ready.</summary>
		public float T = 1f;
		public float Duration = 0.62f;
		public bool Wet;
		/// <summary>Extra reach/force this stroke (a hard pull in the storm sweeps further).</summary>
		public float Power = 1f;
		/// <summary>Eased pose, so a stroke cut short never snaps.</summary>
		public float Yaw, Pitch;
	}

	public override void _Ready()
	{
		Hull = new Node3D { Name = "Hull" };
		AddChild(Hull);
		BuildHull();
		for (int i = 0; i < 2; i++) _oars[i] = BuildOar(i == 0 ? -1 : 1);
	}

	// ------------------------------------------------------------------ hull

	/// <summary>Half-width of the hull at the gunwale, t = -1 (bow) .. +1 (stern).</summary>
	private static float GunwaleHalf(float t) => Beam * 0.5f * (t < 0f ? Mathf.Sqrt(Mathf.Max(0f, 1f - t * t * 0.98f)) : 1f - 0.32f * t * t);
	private static float SheerY(float t) => Depth - Draft + (t < 0f ? 0.2f : 0.08f) * t * t;
	private static float KeelY(float t) => -Draft + (t < 0f ? 0.16f : 0.07f) * t * t;

	/// <summary>A point on the hull skin: t along the length, s from keel (0) to gunwale (1), on one side.</summary>
	private static Vector3 Skin(float t, float s, int side)
	{
		float gw = GunwaleHalf(t), bw = gw * 0.55f;
		float y0 = KeelY(t), y1 = SheerY(t);
		// the bilge: round out between the flat-ish bottom and the flaring topsides
		float x = Mathf.Lerp(bw, gw, Mathf.Pow(s, 0.7f)) + Mathf.Sin(s * Mathf.Pi) * 0.05f * gw / (Beam * 0.5f);
		float y = Mathf.Lerp(y0, y1, s);
		return new Vector3(side * x, y, t * HalfLength);
	}

	private void BuildHull()
	{
		var k = new MeshKit();
		var paint = ProcTextures.Std("boat_paint", ProcTextures.WeatheredWood(), new Color(0.46f, 0.56f, 0.5f), 0.85f, vertexColor: true);
		var wood = PropTextures.DeckMat;
		const int ts = 16, strakes = 5;
		for (int side = -1; side <= 1; side += 2)
			for (int s = 0; s < strakes; s++)
			{
				float s0 = s / (float)strakes, s1 = (s + 1) / (float)strakes;
				for (int i = 0; i < ts; i++)
				{
					float t0 = -1f + 2f * i / ts, t1 = -1f + 2f * (i + 1) / ts;
					Vector3 a = Skin(t0, s0, side), b = Skin(t1, s0, side), c = Skin(t1, s1, side), d = Skin(t0, s1, side);
					// clinker: each strake's lower edge laps a hair outside the one below it
					Vector3 lap = new(side * 0.012f, 0, 0);
					Vector3 outN = (b - a).Cross(d - a).Normalized() * (side > 0 ? -1f : 1f);
					if (outN.X * side < 0f) outN = -outN;
					float sh = 0.82f + 0.12f * (s % 2) - 0.1f * s0;
					k.Mat(paint);
					k.Color = new Color(sh, sh, sh);
					k.Quad(a + lap, b + lap, c, d, outN);
					k.Mat(wood);
					float wsh = 0.5f + 0.06f * (s % 2);
					k.Color = new Color(wsh, wsh * 0.95f, wsh * 0.86f);
					Vector3 inset = new(-side * 0.035f, 0, 0);
					k.Quad(a + inset, b + inset, c + inset, d + inset, -outN);
				}
			}
		// the bottom boards, inside and out
		for (int i = 0; i < ts; i++)
		{
			float t0 = -1f + 2f * i / ts, t1 = -1f + 2f * (i + 1) / ts;
			Vector3 al = Skin(t0, 0, -1), ar = Skin(t0, 0, 1), bl = Skin(t1, 0, -1), br = Skin(t1, 0, 1);
			k.Mat(paint);
			k.Color = new Color(0.5f, 0.5f, 0.5f);
			k.Quad(al, ar, br, bl, Vector3.Down);
			k.Mat(wood);
			k.Color = new Color(0.44f, 0.41f, 0.36f);
			Vector3 up = Vector3.Up * 0.035f;
			k.Quad(al + up, ar + up, br + up, bl + up, Vector3.Up);
		}
		// transom (stern board) and a stem post at the bow
		k.Mat(paint);
		k.Color = new Color(0.8f, 0.8f, 0.8f);
		for (int s = 0; s < strakes; s++)
		{
			float s0 = s / (float)strakes, s1 = (s + 1) / (float)strakes;
			k.Quad(Skin(1f, s0, -1), Skin(1f, s0, 1), Skin(1f, s1, 1), Skin(1f, s1, -1), Vector3.Back);
		}
		k.Mat(wood);
		k.Color = new Color(0.48f, 0.45f, 0.4f);
		for (int s = 0; s < strakes; s++)
		{
			float s0 = s / (float)strakes, s1 = (s + 1) / (float)strakes;
			Vector3 f = Vector3.Forward * 0.035f;
			k.Quad(Skin(1f, s0, -1) + f, Skin(1f, s0, 1) + f, Skin(1f, s1, 1) + f, Skin(1f, s1, -1) + f, Vector3.Forward);
		}
		// the narrow bow closes on a stem post
		k.Mat(paint);
		k.Color = new Color(0.8f, 0.8f, 0.8f);
		for (int s = 0; s < strakes; s++)
		{
			float s0 = s / (float)strakes, s1 = (s + 1) / (float)strakes;
			k.Quad(Skin(-1f, s0, -1), Skin(-1f, s0, 1), Skin(-1f, s1, 1), Skin(-1f, s1, -1), Vector3.Forward);
		}
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.4f, 0.36f, 0.3f);
		k.Cylinder(Skin(-1f, 0f, 1) with { X = 0 } + new Vector3(0, -0.02f, -0.02f), Skin(-1f, 1f, 1) with { X = 0 } + new Vector3(0, 0.08f, -0.05f), 0.06f, 0.05f, 5, true);
		// gunwale rails
		for (int side = -1; side <= 1; side += 2)
			for (int i = 0; i < ts; i++)
			{
				float t0 = -1f + 2f * i / ts, t1 = -1f + 2f * (i + 1) / ts;
				k.Cylinder(Skin(t0, 1f, side) + Vector3.Up * 0.015f, Skin(t1, 1f, side) + Vector3.Up * 0.015f, 0.032f, 0.032f, 5, false);
			}
		// ribs
		k.Color = new Color(0.44f, 0.4f, 0.34f);
		for (float t = -0.65f; t < 0.9f; t += 0.26f)
			for (int side = -1; side <= 1; side += 2)
			{
				Vector3 prev = Skin(t, 0f, side) + new Vector3(-side * 0.05f, 0.05f, 0);
				for (int s = 1; s <= 4; s++)
				{
					Vector3 p = Skin(t, s / 4f, side) + new Vector3(-side * 0.05f, 0, 0);
					k.Cylinder(prev, p, 0.022f, 0.022f, 4, false);
					prev = p;
				}
			}
		// thwarts: bow, the rower's, stern
		k.Mat(wood);
		foreach (var (t, hy) in new[] { (-0.55f, 0.62f), (SeatZ / HalfLength, 0.58f), (0.7f, 0.66f) })
		{
			float gw = GunwaleHalf(t) - 0.05f;
			float y = Mathf.Lerp(KeelY(t), SheerY(t), hy);
			k.Color = new Color(0.55f, 0.5f, 0.42f);
			BuildKit.Box(k, new Vector3(0, y, t * HalfLength), new Vector3(gw * 2f, 0.045f, 0.24f), 1.4f);
		}
		// oarlocks: a pad on the gunwale and a bronze horn
		foreach (int side in new[] { -1, 1 })
		{
			Vector3 lk = OarlockLocal(side);
			k.Mat(wood);
			k.Color = new Color(0.4f, 0.36f, 0.3f);
			BuildKit.Box(k, lk + new Vector3(0, -0.03f, 0), new Vector3(0.1f, 0.05f, 0.3f));
			k.Mat(ProcTextures.MetalMat);
			k.Color = new Color(0.55f, 0.42f, 0.25f);
			k.Cylinder(lk + new Vector3(0, -0.02f, -0.05f), lk + new Vector3(0, 0.07f, -0.05f), 0.012f, 0.012f, 4, false);
			k.Cylinder(lk + new Vector3(0, -0.02f, 0.05f), lk + new Vector3(0, 0.07f, 0.05f), 0.012f, 0.012f, 4, false);
		}
		k.Color = Colors.White;
		k.CommitTo(Hull, "HullMesh");

		// "STATION 7", painted on both sides of the bow, faded almost to nothing
		foreach (int side in new[] { -1, 1 })
		{
			Vector3 p = Skin(-0.62f, 0.72f, side);
			Vector3 n = new Vector3(side, 0.05f, -0.35f).Normalized();
			var b = Basis.LookingAt(-n, Vector3.Up);
			var label = new Label3D
			{
				Text = "STATION 7", FontSize = 48, PixelSize = 0.0024f, Font = SignKit.RoutedFont,
				Modulate = new Color(0.8f, 0.78f, 0.7f, 0.8f), OutlineSize = 0, Shaded = true,
				AlphaCut = Label3D.AlphaCutMode.Discard, DoubleSided = false,
				Transform = new Transform3D(b, p + n * 0.012f),
			};
			Hull.AddChild(label);
		}

		// something to stand on / bump against while it's moored
		var body = new StaticBody3D { Name = "BoatBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		Hull.AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.05f, 0), Shape = new BoxShape3D { Size = new Vector3(Beam, 0.25f, Length * 0.9f) } });
	}

	public static Vector3 OarlockLocal(int side)
	{
		float t = OarlockZ / HalfLength;
		return Skin(t, 1f, side) + new Vector3(0, 0.07f, 0);
	}

	// ------------------------------------------------------------------ oars

	private Oar BuildOar(int side)
	{
		var pivot = new Node3D { Name = side < 0 ? "OarLeft" : "OarRight", Position = OarlockLocal(side) };
		Hull.AddChild(pivot);
		var k = new MeshKit();
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.62f, 0.55f, 0.44f);
		Vector3 inb = new(-side * Inboard, 0, 0), outb = new(side * Outboard, 0, 0);
		// grip, loom, shaft
		k.Cylinder(inb, inb + new Vector3(side * 0.16f, 0, 0), 0.022f, 0.022f, 6, true);
		k.Cylinder(inb + new Vector3(side * 0.16f, 0, 0), Vector3.Zero, 0.03f, 0.032f, 6, false);
		k.Cylinder(Vector3.Zero, outb * 0.62f, 0.032f, 0.027f, 6, false);
		// leather collar at the lock
		k.Color = new Color(0.3f, 0.22f, 0.16f);
		k.Cylinder(new Vector3(-side * 0.1f, 0, 0), new Vector3(side * 0.1f, 0, 0), 0.037f, 0.037f, 6, true);
		// the blade: a flattened, widening paddle, painted end
		k.Color = new Color(0.58f, 0.52f, 0.42f);
		Vector3 b0 = outb * 0.62f, b1 = outb;
		float w0 = 0.04f, w1 = 0.085f;
		k.Quad(b0 + new Vector3(0, 0.006f, -w0), b1 + new Vector3(0, 0.006f, -w1), b1 + new Vector3(0, 0.006f, w1), b0 + new Vector3(0, 0.006f, w0), Vector3.Up);
		k.Quad(b0 + new Vector3(0, -0.006f, w0), b1 + new Vector3(0, -0.006f, w1), b1 + new Vector3(0, -0.006f, -w1), b0 + new Vector3(0, -0.006f, -w0), Vector3.Down);
		k.Mat(ProcTextures.Flat("oar_tip", new Color(0.52f, 0.14f, 0.1f), 0.8f, 0.2f));
		k.Color = Colors.White;
		Vector3 t0 = outb * 0.93f;
		k.Quad(t0 + new Vector3(0, 0.007f, -w1 * 0.95f), b1 + new Vector3(0, 0.007f, -w1), b1 + new Vector3(0, 0.007f, w1), t0 + new Vector3(0, 0.007f, w1 * 0.95f), Vector3.Up);
		k.CommitTo(pivot, "OarMesh", true);
		var oar = new Oar { Side = side, Pivot = pivot };
		Pose(oar, RestYaw, RestPitch);
		oar.Yaw = RestYaw; oar.Pitch = RestPitch;
		return oar;
	}

	// Pose angles, radians. Yaw: + swings the blade toward the bow. Pitch: + dips the blade down.
	private const float RestYaw = 0.35f, RestPitch = -0.06f;
	private const float CatchYaw = 0.6f, FinishYaw = -0.45f, InPitch = 0.31f, OutPitch = -0.1f;

	private void Pose(Oar o, float yaw, float pitch)
	{
		int s = o.Side;
		// yaw about vertical (toward the bow = -Z), then dip about the boat's length axis
		var yawB = new Basis(Vector3.Up, s * yaw);
		var pitchB = new Basis(Vector3.Back, -s * pitch);
		o.Pivot.Basis = yawB * pitchB;
	}

	/// <summary>World position of an oar's blade tip right now.</summary>
	public Vector3 BladeTipWorld(int side)
	{
		var o = _oars[side < 0 ? 0 : 1];
		return o.Pivot.GlobalTransform * new Vector3(side * Outboard * 0.85f, 0, 0);
	}

	/// <summary>Starts a stroke on one oar. <paramref name="power"/> 1 = an ordinary pull; the storm's
	/// frantic strokes come in shorter and harder.</summary>
	public void Stroke(int side, float power = 1f, float duration = 0.62f)
	{
		var o = _oars[side < 0 ? 0 : 1];
		o.T = 0f;
		o.Power = power;
		o.Duration = Mathf.Max(0.25f, duration);
		o.Wet = false;
	}

	/// <summary>True while that oar's blade is in the water.</summary>
	public bool BladeWet(int side) => _oars[side < 0 ? 0 : 1].Wet;

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		foreach (var o in _oars) Animate(o, dt);
	}

	private void Animate(Oar o, float dt)
	{
		float yaw, pitch;
		if (o.T < 1f)
		{
			o.T = Mathf.Min(1f, o.T + dt / o.Duration);
			float t = o.T;
			float reachY = CatchYaw * Mathf.Lerp(1f, 1.15f, Mathf.Clamp(o.Power - 1f, 0f, 1f));
			// catch 0-0.12 (blade drops in, forward) / drive 0.12-0.62 (sweep back, in) / release 0.62-0.72 (lift, feather) / recovery (swing forward in the air)
			if (t < 0.12f) { float u = t / 0.12f; yaw = Mathf.Lerp(RestYaw, reachY, u); pitch = Mathf.Lerp(RestPitch, InPitch, Mathf.SmoothStep(0, 1, u)); }
			else if (t < 0.62f) { float u = Mathf.SmoothStep(0, 1, (t - 0.12f) / 0.5f); yaw = Mathf.Lerp(reachY, FinishYaw, u); pitch = InPitch; }
			else if (t < 0.72f) { float u = (t - 0.62f) / 0.1f; yaw = FinishYaw; pitch = Mathf.Lerp(InPitch, OutPitch, Mathf.SmoothStep(0, 1, u)); }
			else { float u = Mathf.SmoothStep(0, 1, (t - 0.72f) / 0.28f); yaw = Mathf.Lerp(FinishYaw, RestYaw, u); pitch = Mathf.Lerp(OutPitch, RestPitch, u); }

			bool wet = t >= 0.1f && t < 0.66f;
			if (wet && !o.Wet) EmitSignal(SignalName.BladeIn, o.Side, BladeTipWorld(o.Side));
			if (!wet && o.Wet) EmitSignal(SignalName.BladeOut, o.Side, BladeTipWorld(o.Side));
			o.Wet = wet;
		}
		else if (_stowed)
		{
			// shipped: swung in along the gunwale, blade toward the stern and clear of the water
			yaw = -1.32f; pitch = -0.1f;
		}
		else
		{
			// idle: the oars trail at rest, nodding a little with the water
			float bob = Mathf.Sin((float)Time.GetTicksMsec() * 0.0017f + o.Side) * 0.025f;
			yaw = RestYaw; pitch = RestPitch + bob;
		}
		// ease toward the pose (a restarted stroke blends instead of jumping)
		float k = 1f - Mathf.Exp(-dt * 30f);
		o.Yaw = Mathf.Lerp(o.Yaw, yaw, k);
		o.Pitch = Mathf.Lerp(o.Pitch, pitch, k);
		Pose(o, o.Yaw, o.Pitch);
	}

	/// <summary>Stows the oars flat along the gunwales (moored, and once landed).</summary>
	public void Stow(bool stowed)
	{
		foreach (var o in _oars) { o.T = 1f; o.Wet = false; }
		_stowed = stowed;
	}
	private bool _stowed;
	public bool Stowed => _stowed;
}

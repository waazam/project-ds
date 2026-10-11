using System.Collections.Generic;
using Godot;

namespace ProjectDS.Player;

/// <summary>
/// The player's own body (2026-10-11, the owner's list: "A first-person body: look down and see your legs and boots, your
/// shadow has arms"; and "it needs to look really really good"). The hiker modelled in tools/Blender/player_body.py
/// (assets/models/player/player_body.glb), posed here every frame; there are no baked clips, so the legs always fit the
/// ground and the pace:
/// <list type="bullet">
/// <item><b>the legs</b>: a stride in step with the footsteps' own (a foot comes down as its step sounds), each foot
/// carried along the way they're going (forward, back, sideways) and set down on the ground under it (stairs, slopes),
/// knees bent by two-bone IK; crouched, the hips sink and the knees fold; seated (the rowboat), the legs out in front;</item>
/// <item><b>standing and turning</b>: the body lags the view and, past 50 degrees, turns after it with a small
/// shuffle of the feet;</item>
/// <item><b>the hips and the spine</b>: the hips' dip on each footfall and their turn with the leading leg, the chest
/// turned back against them, leaning into a run, breathing at rest;</item>
/// <item><b>the arms</b>, in the shadow only (the hands in view are the held things' own): swinging against the legs,
/// bent at a run; the left one out in front while the lantern's held up;</item>
/// <item><b>the head</b> (in the shadow) follows the look.</item>
/// </list>
/// Seen: the jacket's front, its straps and zip, the jeans and the boots. In the shadow only: the arms, the hands, the
/// head and the pack. The body sits back from the eye as the view tips down (the head bows forward over it), so it's
/// the legs and the boots that are seen, not the inside of the collar. Hidden whenever the view isn't the player's own
/// (a cutscene's camera).
/// </summary>
public partial class PlayerBody : Node3D
{
	public const string ModelPath = "res://assets/models/player/player_body.glb";
	/// <summary>The body's render layer: the player's own lights (the lantern, a flare in the hand) don't cast it. Right
	/// beside them, the arm's shadow fell along the view as a black blade; the world's lights still do.</summary>
	public const uint BodyLayer = 1u << 17;
	private double _lightSweep;
	private bool _inView = true;
	private static readonly string[] Seen = { "Torso", "Legs", "Boots" };

	private PlayerController _p;
	private PlayerFootsteps _steps;
	private Skeleton3D _sk;
	private readonly List<GeometryInstance3D> _seen = new(), _shadow = new();
	private int _root, _hips, _spine, _chest, _neck, _head;
	private readonly int[] _thigh = new int[2], _shin = new int[2], _foot = new int[2], _toe = new int[2];
	private readonly int[] _clav = new int[2], _upper = new int[2], _fore = new int[2], _hand = new int[2];
	private Transform3D[] _restG, _restL, _cur;
	private Vector3[] _axis;   // each bone's direction (toward its child), in its own rest space
	private int[] _order;
	private float _l1, _l2;     // thigh and shin
	private float _bodyYaw, _turnPhase, _breathT, _cycle, _lookDown, _sitW, _lanternW, _runW;
	private bool _turning, _yawSet;
	private Vector3 _lastPos;
	/// <summary>For tests: whether it's drawn (seen and in the shadow), and where its feet are (world).</summary>
	public bool Shown { get; private set; }
	public Vector3 FootWorld(int side) => _sk == null ? GlobalPosition : _sk.ToGlobal(_cur[_foot[side]].Origin);
	public static PlayerBody Instance { get; private set; }

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		TopLevel = true;
		_p = GetParent<PlayerController>();
		_steps = _p.GetNodeOrNull<PlayerFootsteps>("Footsteps") ?? FindSteps(_p);
		if (!ResourceLoader.Exists(ModelPath)) { SetProcess(false); return; }
		var model = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
		model.Name = "Model";
		AddChild(model);
		foreach (var n in model.FindChildren("*", "Skeleton3D", true, false)) { _sk = (Skeleton3D)n; break; }
		if (_sk == null) { SetProcess(false); return; }
		foreach (var n in model.FindChildren("*", "GeometryInstance3D", true, false))
		{
			var g = (GeometryInstance3D)n;
			bool seen = System.Array.Exists(Seen, s => g.Name.ToString().StartsWith(s));
			(seen ? _seen : _shadow).Add(g);
			g.CastShadow = seen ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
			g.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
			g.ExtraCullMargin = 1f;   // (posed far from its rest: never culled away while in view)
			g.Layers = BodyLayer;
			// cloth and leather: no sheen (with the glTF's default specular the jacket and the jeans went a pale grey
			// under the lantern)
			if (g is MeshInstance3D mi && mi.Mesh != null)
				for (int si = 0; si < mi.Mesh.GetSurfaceCount(); si++)
					if (mi.Mesh.SurfaceGetMaterial(si) is StandardMaterial3D sm)
					{
						var m = (StandardMaterial3D)sm.Duplicate();
						bool leather = g.Name.ToString().StartsWith("Boots");
						m.MetallicSpecular = leather ? 0.35f : 0.12f;
						m.Roughness = leather ? 0.7f : 1f;
						// (the jacket's hips, seen from just above: one even olive, the bake's colours patchy over its cut)
						if (g.Name.ToString().StartsWith("Torso")) { m.AlbedoTexture = null; m.AlbedoColor = new Color(0.12f, 0.14f, 0.1f); }
						mi.SetSurfaceOverrideMaterial(si, m);
					}
		}
		Bones();
		_lastPos = _p.GlobalPosition;
		GD.Print($"[body] the player's body: {_sk.GetBoneCount()} bones, seen {_seen.Count}, in the shadow {_shadow.Count}");
	}

	/// <summary>Every light the player carries (under the player, the camera included) leaves the body out of its shadows.</summary>
	private static void KeepOwnLightsOff(Node n)
	{
		if (n is Light3D l && (l.ShadowCasterMask & BodyLayer) != 0) l.ShadowCasterMask &= ~BodyLayer;
		foreach (var c in n.GetChildren()) KeepOwnLightsOff(c);
	}

	private static PlayerFootsteps FindSteps(Node n)
	{
		foreach (var c in n.GetChildren()) if (c is PlayerFootsteps f) return f;
		return null;
	}

	private void Bones()
	{
		int B(string n) => _sk.FindBone(n);
		_root = B("root"); _hips = B("hips"); _spine = B("spine"); _chest = B("chest"); _neck = B("neck"); _head = B("head");
		string[] sx = { "R", "L" };
		for (int i = 0; i < 2; i++)
		{
			_thigh[i] = B($"thigh_{sx[i]}"); _shin[i] = B($"shin_{sx[i]}"); _foot[i] = B($"foot_{sx[i]}"); _toe[i] = B($"toe_{sx[i]}");
			_clav[i] = B($"clav_{sx[i]}"); _upper[i] = B($"upper_{sx[i]}"); _fore[i] = B($"fore_{sx[i]}"); _hand[i] = B($"hand_{sx[i]}");
		}
		int n = _sk.GetBoneCount();
		_restG = new Transform3D[n]; _restL = new Transform3D[n]; _cur = new Transform3D[n]; _axis = new Vector3[n];
		for (int i = 0; i < n; i++)
		{
			_restG[i] = _sk.GetBoneGlobalRest(i);
			_restL[i] = _sk.GetBoneRest(i);
		}
		// each bone's direction: toward its (first) child; the ends along their own Y
		for (int i = 0; i < n; i++)
		{
			var kids = _sk.GetBoneChildren(i);
			Vector3 dir = _restG[i].Basis.Y;
			foreach (int k in kids)
			{
				var d = _restG[k].Origin - _restG[i].Origin;
				if (d.LengthSquared() > 1e-6f) { dir = d; break; }
			}
			_axis[i] = (_restG[i].Basis.Inverse() * dir).Normalized();
		}
		// parents before children
		var order = new List<int>();
		void Visit(int b) { order.Add(b); foreach (int k in _sk.GetBoneChildren(b)) Visit(k); }
		foreach (int r in _sk.GetParentlessBones()) Visit(r);
		_order = order.ToArray();
		_l1 = (_restG[_shin[0]].Origin - _restG[_thigh[0]].Origin).Length();
		_l2 = (_restG[_foot[0]].Origin - _restG[_shin[0]].Origin).Length();
	}

	// ------------------------------------------------------------------ each frame

	public override void _Process(double delta)
	{
		if (_sk == null || _p == null) return;
		float dt = Mathf.Min((float)delta, 0.05f);
		var rig = _p.CameraRig;
		var cam = rig?.Camera;
		bool ours = cam != null && cam.IsCurrent() && rig.IsFirstPerson && _p.Visible && _p.IsInsideTree();
		if (Shown != ours)
		{
			Shown = ours;
			Visible = ours;
		}
		if (!ours) { _lastPos = _p.GlobalPosition; _yawSet = false; return; }
		// the setting: their legs seen, or the body only in the shadow
		bool inView = Systems.GameSettings.Instance?.BodyInView ?? true;
		if (inView != _inView)
		{
			_inView = inView;
			foreach (var g in _seen) g.CastShadow = inView ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
		}
		_lightSweep -= delta;
		if (_lightSweep <= 0) { _lightSweep = 1.0; KeepOwnLightsOff(_p); }

		// where it faces: after the view, lagging; standing, only once the look has gone 50 degrees round
		float viewYaw = rig.Yaw;
		var flatV = new Vector3(_p.Velocity.X, 0, _p.Velocity.Z);
		float speed = flatV.Length();
		if (!_yawSet) { _bodyYaw = viewYaw; _yawSet = true; }
		float diff = Mathf.AngleDifference(_bodyYaw, viewYaw);
		if (speed > 0.4f) { _bodyYaw = Mathf.LerpAngle(_bodyYaw, viewYaw, 1f - Mathf.Exp(-dt * 9f)); _turning = false; }
		else
		{
			if (Mathf.Abs(diff) > Mathf.DegToRad(50f)) _turning = true;
			if (_turning)
			{
				float step = Mathf.Sign(diff) * Mathf.Min(Mathf.Abs(diff), dt * 4.5f);
				_bodyYaw += step;
				_turnPhase += Mathf.Abs(step) * 0.9f;   // (the feet shuffle round with it)
				if (Mathf.Abs(diff) < 0.06f) _turning = false;
			}
		}
		var basis = new Basis(Vector3.Up, _bodyYaw);
		var fwd = -basis.Z;
		// back from the eye: the eye is ahead of the spine, and further ahead as the head bows to look down
		_lookDown = Mathf.Clamp(-rig.Pitch / 1.1f, 0f, 1f);
		_sitW = Mathf.MoveToward(_sitW, rig.EyeHeight < 1.3f && _p.CrouchAmount < 0.2f ? 1f : 0f, dt * 3f);
		// (walking, less: the feet stepping out ahead stay in view; crouched, more: the knees go under the eye, not up
		// in front of it)
		float moving = Mathf.SmoothStep(0.3f, 1.5f, new Vector2(_p.Velocity.X, _p.Velocity.Z).Length());
		float back = (0.09f + 0.16f * _lookDown * (1f - 0.6f * moving) + 0.28f * _p.CrouchAmount) * (1f - _sitW);
		GlobalTransform = new Transform3D(basis, _p.GlobalPosition - fwd * back);

		// the stride: in step with the footsteps (a foot down on each footfall); standing, the feet still
		float moveW = Mathf.SmoothStep(0.15f, 1.0f, speed);
		_runW = Mathf.MoveToward(_runW, _p.IsRunning && speed > 3f ? 1f : 0f, dt * 3f);
		if (_steps != null && speed > 0.3f)
			_cycle = ((_steps.Steps & 1) + _steps.StepPhase) * 0.5f;
		else if (_turning)
			_cycle = _turnPhase;
		_breathT += dt;
		_lanternW = Mathf.MoveToward(_lanternW, cam.GetNodeOrNull<HeldLantern>("HeldLantern") is { Raise: > 0.5f } ? 1f : 0f, dt * 3f);
		Pose(basis, flatV, speed, moveW, dt);
		_lastPos = _p.GlobalPosition;
	}

	private void Pose(Basis basis, Vector3 flatV, float speed, float moveW, float dt)
	{
		int n = _cur.Length;
		var have = new bool[n];
		// the way they're going, in the body's own space (it faces -Z)
		var local = basis.Inverse() * flatV;
		var dir = speed > 0.1f ? new Vector3(local.X, 0, local.Z).Normalized() : new Vector3(0, 0, -1);
		float crouch = _p.CrouchAmount;
		float cyc = _cycle * Mathf.Tau;
		float shuffle = _turning && speed < 0.4f ? 1f : 0f;

		// the hips: down on each footfall, down for a crouch, down to sit; turned with the leading leg
		var hipsRest = _restG[_hips];
		float bob = -0.022f * moveW * (0.5f + 0.5f * Mathf.Cos(cyc * 2f)) * (1f + _runW);
		float drop = crouch * (_p.CrouchEyeDrop * 0.85f) + _sitW * 0.5f;
		// on a slope or a stair going down, the hips go down to let the lower foot reach
		float reachDrop = 0f;
		float[] ground = new float[2];
		for (int s = 0; s < 2; s++)
		{
			var a = _restG[_foot[s]].Origin;
			ground[s] = GroundAt(a) ?? 0f;
			reachDrop = Mathf.Min(reachDrop, ground[s]);
		}
		// each foot's place along the stride (and its lift), first: the hips have to come down for the longer reach
		var along = new float[2];
		var lifts = new float[2];
		var toes = new float[2];
		float duty = Mathf.Lerp(0.6f, 0.38f, _runW);
		float amp = Mathf.Clamp(speed * 0.12f, 0f, Mathf.Lerp(0.3f, 0.4f, _runW)) * (1f - 0.35f * crouch) * moveW + 0.07f * shuffle;
		float reach = 0f;
		for (int s = 0; s < 2; s++)
		{
			float ph = Mathf.PosMod(_cycle - s * 0.5f, 1f);
			if (ph < duty) along[s] = amp * (1f - 2f * ph / duty);
			else
			{
				float u = (ph - duty) / (1f - duty);
				along[s] = -amp + 2f * amp * Mathf.SmoothStep(0f, 1f, u);
				lifts[s] = Mathf.Sin(u * Mathf.Pi) * (Mathf.Lerp(0.1f, 0.2f, _runW) * moveW + 0.04f * shuffle) * (1f - 0.4f * crouch);
				toes[s] = Mathf.Sin(u * Mathf.Pi) * 0.25f * moveW;
			}
			float legLen = _l1 + _l2 - 0.01f;
			float a2 = Mathf.Min(along[s] * along[s], legLen * legLen * 0.8f);
			if (lifts[s] < 0.02f) reach = Mathf.Max(reach, legLen - Mathf.Sqrt(legLen * legLen - a2));
		}
		// (a little give in the knees standing: a straight leg locks and reads stiff)
		float give = 0.02f + reach * 0.95f;
		var hipPos = hipsRest.Origin + new Vector3(0, bob - drop - give * (1f - _sitW) + reachDrop * 0.85f * (1f - _sitW), 0) + dir * (0.03f * _runW);
		float hipTwist = 0.11f * moveW * Mathf.Sin(cyc) * (local.Z <= 0 ? 1f : -1f);
		float hipRoll = 0.04f * moveW * Mathf.Sin(cyc);
		float lean = 0.18f * _runW + 0.55f * crouch * (1f - _sitW) + 0.05f * moveW;
		var hipsB = new Basis(Vector3.Up, hipTwist) * new Basis(new Vector3(0, 0, 1), hipRoll) * new Basis(Vector3.Right, -0.08f * _sitW) * hipsRest.Basis;
		Set(_hips, new Transform3D(hipsB, hipPos), have);
		// the spine and the chest: turned back against the hips, leaning in, breathing
		float breath = 0.012f * Mathf.Sin(_breathT * 1.6f) * (1f - moveW * 0.5f);
		Rotate(_spine, new Basis(Vector3.Up, -hipTwist * 0.6f) * new Basis(Vector3.Right, -lean * 0.5f), have);
		Rotate(_chest, new Basis(Vector3.Up, -hipTwist * 0.7f) * new Basis(Vector3.Right, -lean * 0.5f - breath), have);
		// the head follows the look (the shadow's)
		var rig = _p.CameraRig;
		float yawOff = Mathf.Clamp(Mathf.AngleDifference(_bodyYaw, rig.Yaw), -1.2f, 1.2f);
		Rotate(_neck, new Basis(Vector3.Up, yawOff * 0.35f) * new Basis(Vector3.Right, rig.Pitch * 0.35f), have);
		Rotate(_head, new Basis(Vector3.Up, yawOff * 0.65f) * new Basis(Vector3.Right, rig.Pitch * 0.55f), have);

		// the legs
		for (int s = 0; s < 2; s++)
		{
			float side = s == 0 ? 1f : -1f;
			var rest = _restG[_foot[s]].Origin;
			Vector3 target;
			float lift = 0f, toeUp = 0f;
			if (_sitW > 0.5f)
			{
				// seated: the feet out in front, flat on the boards
				target = new Vector3(rest.X * 1.3f, hipPos.Y - 0.42f, -0.45f);
			}
			else
			{
				lift = lifts[s];
				toeUp = toes[s];
				// (shuffling round: the feet step about the body's turn)
				var flatDir = shuffle > 0f && moveW < 0.3f ? new Vector3(side, 0, 0) : dir;
				target = rest + flatDir * along[s] + new Vector3(side * 0.03f * crouch, 0, 0);
				var g = GroundAt(target);
				target.Y = rest.Y + (g ?? 0f) + lift;
			}
			LegIK(s, target, toeUp, basis, have);
		}

		// the arms (the shadow's): swinging against the legs, bent at a run; the left out in front with the lantern up
		for (int s = 0; s < 2; s++)
		{
			float side = s == 0 ? 1f : -1f;
			// (a forward swing is positive; the right arm back as the right foot comes down, at the cycle's start)
			float swing = (s == 0 ? -1f : 1f) * Mathf.Cos(cyc) * Mathf.Lerp(0.3f, 0.65f, _runW) * moveW;
			float bend = 0.15f + 0.25f * Mathf.Max(0f, swing) + 1.1f * _runW + 0.3f * crouch;
			float raise = s == 1 ? _lanternW : 0f;
			var restUp = _restG[_upper[s]].Basis * _axis[_upper[s]];
			AimAt(_upper[s], Forward(restUp, swing * (1f - raise) + raise * 0.75f) + new Vector3(-side * 0.2f * raise, 0, 0), have);
			AimAt(_fore[s], Forward(restUp, swing * (1f - raise) + bend * (1f - raise) + raise * 1.45f), have);
		}

		// every bone not set: carried by its parent
		foreach (int b in _order)
		{
			if (!have[b]) Carry(b, have);
			var parent = _sk.GetBoneParent(b);
			var localT = parent < 0 ? _cur[b] : _cur[parent].AffineInverse() * _cur[b];
			_sk.SetBonePosePosition(b, localT.Origin);
			_sk.SetBonePoseRotation(b, localT.Basis.Orthonormalized().GetRotationQuaternion());
		}
	}

	// ------------------------------------------------------------------ the solver

	private Transform3D Inherited(int b)
	{
		int p = _sk.GetBoneParent(b);
		return p < 0 ? _restL[b] : _cur[p] * _restL[b];
	}

	private void Carry(int b, bool[] have)
	{
		int p = _sk.GetBoneParent(b);
		if (p >= 0 && !have[p]) Carry(p, have);
		_cur[b] = Inherited(b);
		have[b] = true;
	}

	private void Set(int b, Transform3D t, bool[] have)
	{
		int p = _sk.GetBoneParent(b);
		if (p >= 0 && !have[p]) Carry(p, have);
		_cur[b] = t;
		have[b] = true;
	}

	/// <summary>The bone as its parent carries it, then turned by <paramref name="r"/> (in the body's space).</summary>
	private void Rotate(int b, Basis r, bool[] have)
	{
		if (b < 0) return;
		int p = _sk.GetBoneParent(b);
		if (p >= 0 && !have[p]) Carry(p, have);
		var t = Inherited(b);
		Set(b, new Transform3D(r * t.Basis, t.Origin), have);
	}

	/// <summary>The bone as its parent carries it, swung to point along <paramref name="dir"/> (the body's space).</summary>
	private void AimAt(int b, Vector3 dir, bool[] have)
	{
		if (b < 0) return;
		int p = _sk.GetBoneParent(b);
		if (p >= 0 && !have[p]) Carry(p, have);
		var t = Inherited(b);
		var now = (t.Basis * _axis[b]).Normalized();
		var want = dir.Normalized();
		var q = Arc(now, want);
		Set(b, new Transform3D(new Basis(q) * t.Basis, t.Origin), have);
	}

	/// <summary><paramref name="v"/> swung forward (toward -Z) by <paramref name="a"/> radians about the body's sideways axis.</summary>
	private static Vector3 Forward(Vector3 v, float a) => new Basis(Vector3.Right, a) * v;

	private static Quaternion Arc(Vector3 from, Vector3 to)
	{
		float d = from.Dot(to);
		if (d > 0.99999f) return Quaternion.Identity;
		if (d < -0.9999f) return new Quaternion(from.Cross(Vector3.Right).LengthSquared() > 0.01f ? from.Cross(Vector3.Right).Normalized() : from.Cross(Vector3.Up).Normalized(), Mathf.Pi);
		return new Quaternion(from, to);
	}

	/// <summary>The leg to put its ankle at <paramref name="ankle"/> (the body's space): the knee bent forward (two-bone
	/// IK), the foot along the body, flat (its toes up a little mid-swing).</summary>
	private void LegIK(int s, Vector3 ankle, float toeUp, Basis basis, bool[] have)
	{
		Carry(_thigh[s], have);
		var hip = _cur[_thigh[s]].Origin;
		var d = ankle - hip;
		float dist = Mathf.Clamp(d.Length(), 0.05f, _l1 + _l2 - 0.002f);
		var dirv = d.Normalized();
		float cosA = (_l1 * _l1 + dist * dist - _l2 * _l2) / (2f * _l1 * dist);
		float ang = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
		var pole = new Vector3(s == 0 ? 0.12f : -0.12f, 0, -1f);   // (knees forward, a touch out)
		var sideV = dirv.Cross(pole);
		if (sideV.LengthSquared() < 1e-6f) sideV = Vector3.Right;
		var bend = sideV.Normalized().Cross(dirv).Normalized();
		if (bend.Dot(pole) < 0) bend = -bend;
		var knee = hip + dirv * (_l1 * Mathf.Cos(ang)) + bend * (_l1 * Mathf.Sin(ang));
		var end = hip + dirv * dist;
		AimAt(_thigh[s], knee - hip, have);
		AimAt(_shin[s], end - knee, have);
		// the foot: its rest direction (forward and down to the ball), kept level, toes up mid-swing
		var restFoot = _restG[_foot[s]].Basis * _axis[_foot[s]];
		var footDir = new Basis(Vector3.Right, toeUp).Orthonormalized() * restFoot;
		AimAt(_foot[s], footDir, have);
	}

	/// <summary>The ground's height under a point of the body's space, relative to the body's feet (null: none near).</summary>
	private float? GroundAt(Vector3 localPoint)
	{
		var w = ToGlobal(new Vector3(localPoint.X, 0, localPoint.Z));
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return null;
		var q = PhysicsRayQueryParameters3D.Create(w + Vector3.Up * 0.55f, w + Vector3.Down * 0.7f, 1u);
		q.Exclude = new Godot.Collections.Array<Rid> { _p.GetRid() };
		var hit = space.IntersectRay(q);
		if (hit.Count == 0) return null;
		float h = ((Vector3)hit["position"]).Y - _p.GlobalPosition.Y;
		return Mathf.Clamp(h, -0.45f, 0.45f);
	}
}

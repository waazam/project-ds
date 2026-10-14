using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// The animation overhaul's shared toolkit (2026-10-12, the owner: "weight/inertia, foot-planting IK, transitions,
/// secondary motion, root motion, contact points"). Small pieces every creature's own animation can lay over its clips:
/// <list type="bullet">
/// <item><see cref="Spring1"/> and <see cref="Spring3"/>: critically damped springs (a value that eases into its target,
/// carries its momentum through a change, and settles, never snaps or overshoots);</item>
/// <item><see cref="TwoBone"/>: a two-bone limb solved to put its end on a point, the knee or elbow keeping the way it
/// already bends;</item>
/// <item><see cref="BodyDynamics"/>: weight on a skeleton driven by clips: the spine leaning into the body's
/// acceleration and its turns, the arms swinging behind a start and on past a stop, and the feet planted on the ground
/// under them (the hips lowered to the lower foot, each leg bent to its own ground).</item>
/// </list>
/// </summary>
public static class AnimKit
{
	/// <summary>A critically damped spring on one value: <paramref name="omega"/> is how quickly it follows (about 4/omega
	/// seconds to settle).</summary>
	public struct Spring1
	{
		public float X, V;
		public void Step(float target, float omega, float dt)
		{
			float a = (target - X) * omega * omega - V * 2f * omega;
			V += a * dt;
			X += V * dt;
		}
	}

	public struct Spring3
	{
		public Vector3 X, V;
		public void Step(Vector3 target, float omega, float dt)
		{
			Vector3 a = (target - X) * omega * omega - V * 2f * omega;
			V += a * dt;
			X += V * dt;
		}
	}

	/// <summary>Solves the chain <paramref name="upper"/> → <paramref name="lower"/> → <paramref name="end"/> so the end's
	/// joint sits on <paramref name="targetSkel"/> (skeleton space), by <paramref name="weight"/>. The middle joint stays
	/// on the side it bends toward now (so a knee never pops backward). The end bone keeps its own orientation.</summary>
	public static void TwoBone(Skeleton3D sk, int upper, int lower, int end, Vector3 targetSkel, float weight = 1f)
	{
		if (upper < 0 || lower < 0 || end < 0 || weight <= 0.001f) return;
		Vector3 a = sk.GetBoneGlobalPose(upper).Origin, b = sk.GetBoneGlobalPose(lower).Origin, c = sk.GetBoneGlobalPose(end).Origin;
		Basis endBasis = sk.GetBoneGlobalPose(end).Basis;
		Vector3 t = c.Lerp(targetSkel, Mathf.Clamp(weight, 0f, 1f));
		float l1 = a.DistanceTo(b), l2 = b.DistanceTo(c);
		if (l1 < 1e-4f || l2 < 1e-4f) return;
		Vector3 at = t - a;
		float d = Mathf.Clamp(at.Length(), Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
		Vector3 u = at.Normalized();
		Vector3 bend = (b - a) - u * (b - a).Dot(u);
		if (bend.LengthSquared() < 1e-8f) bend = (c - a).Cross(u).Cross(u) * -1f;
		if (bend.LengthSquared() < 1e-8f) return;
		Vector3 v = bend.Normalized();
		float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
		Vector3 b2 = a + u * (l1 * cosA) + v * (l1 * Mathf.Sqrt(1f - cosA * cosA));
		BonePose.AimFrom(sk, upper, b - a, b2 - a);
		Vector3 bNow = sk.GetBoneGlobalPose(lower).Origin, cNow = sk.GetBoneGlobalPose(end).Origin;
		BonePose.AimFrom(sk, lower, cNow - bNow, (a + u * d) - bNow);
		// the end (a foot, a hand) as it was: its parent's turn taken back out
		int p = sk.GetBoneParent(end);
		if (p >= 0)
		{
			Basis pg = sk.GetBoneGlobalPose(p).Basis.Orthonormalized();
			sk.SetBonePoseRotation(end, (pg.Inverse() * endBasis.Orthonormalized()).GetRotationQuaternion().Normalized());
		}
	}

	/// <summary>A mesh built straight up +Y in its rest pose (a limb, a tail, a tentacle) skinned to a chain of
	/// <paramref name="bones"/> bones <paramref name="seg"/> long (bone i's joint at y = i * seg): each vertex blended between
	/// the two bones whose middles it lies between, so the chain bends it as one smooth thing (no seams, no rigid pieces).
	/// Every surface and its material kept.</summary>
	public static ArrayMesh SkinAlongY(ArrayMesh src, float seg, int bones)
	{
		var dst = new ArrayMesh();
		for (int s = 0; s < src.GetSurfaceCount(); s++)
		{
			var arr = src.SurfaceGetArrays(s);
			var v = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
			var bi = new int[v.Length * 4];
			var bw = new float[v.Length * 4];
			for (int i = 0; i < v.Length; i++)
			{
				float u = v[i].Y / seg - 0.5f;
				int i0 = Mathf.FloorToInt(u);
				float f = u - i0;
				int a = Mathf.Clamp(i0, 0, bones - 1), b = Mathf.Clamp(i0 + 1, 0, bones - 1);
				if (a == b) { bi[i * 4] = a; bw[i * 4] = 1f; continue; }
				// eased across the joint (smoothstep): the bend spread round it, the middle of each piece its own
				float e = f * f * (3f - 2f * f);
				bi[i * 4] = a; bw[i * 4] = 1f - e;
				bi[i * 4 + 1] = b; bw[i * 4 + 1] = e;
			}
			arr[(int)Mesh.ArrayType.Bones] = bi;
			arr[(int)Mesh.ArrayType.Weights] = bw;
			dst.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
			dst.SurfaceSetMaterial(s, src.SurfaceGetMaterial(s));
		}
		return dst;
	}

	/// <summary>A chain of <paramref name="bones"/> bones up +Y, <paramref name="seg"/> apart, under <paramref name="parent"/>,
	/// and <paramref name="mesh"/> (skinned to it: <see cref="SkinAlongY"/>) on it.</summary>
	public static Skeleton3D ChainSkeleton(Node3D parent, ArrayMesh mesh, float seg, int bones, string name = "Skel")
	{
		var sk = new Skeleton3D { Name = name };
		parent.AddChild(sk);
		for (int i = 0; i < bones; i++)
		{
			sk.AddBone($"B{i}");
			if (i > 0) sk.SetBoneParent(i, i - 1);
			sk.SetBoneRest(i, new Transform3D(Basis.Identity, i == 0 ? Vector3.Zero : new Vector3(0, seg, 0)));
		}
		sk.ResetBonePoses();
		var mi = new MeshInstance3D { Name = "Flesh", Mesh = mesh, ExtraCullMargin = seg * bones };
		sk.AddChild(mi);
		mi.Skeleton = mi.GetPathTo(sk);
		mi.Skin = sk.CreateSkinFromRestTransforms();
		return sk;
	}

	/// <summary>
	/// Weight on a clip-driven skeleton, laid over the clip each frame (after it's been applied):
	/// <list type="bullet">
	/// <item>the spine leans into the body's acceleration (forward as it sets off, back as it stops) and rolls into its
	/// turns; the head counters a little of it, so it stays on what it's watching;</item>
	/// <item>the upper arms hang back as it sets off and swing on past a stop (a pendulum on a spring);</item>
	/// <item>the feet planted on the ground: each foot's ground found under it, the hips lowered to the lower one, each leg
	/// bent so its foot lands on its own ground (stairs, slopes, the uneven snow).</item>
	/// </list>
	/// </summary>
	public sealed class BodyDynamics
	{
		public float LeanPerAccel = 0.045f, MaxLean = 0.32f, TurnRoll = 0.08f, ArmSwing = 0.06f, MaxArm = 0.5f, MaxStep = 0.6f;
		public bool PlantFeet = true;
		/// <summary>0 .. 1: how much of it is on (a cutscene's pose, a leap: off).</summary>
		public float Weight = 1f;
		/// <summary>For tests: how far (m) the hips were lowered to the ground last frame, and the lean (radians).</summary>
		public float HipDrop { get; private set; }
		public float Lean => _pitch.X;

		private readonly Skeleton3D _sk;
		private readonly int _hips;
		private readonly int[] _spine, _arms;
		private readonly (int thigh, int shin, int foot, int toe)[] _legs;
		private readonly int _head;
		private Vector3 _lastPos, _lastVel;
		private float _lastYaw;
		private bool _set;
		private Spring1 _pitch, _roll, _drop;
		private Spring3 _arm;
		private readonly Spring1[] _legOff;
		private readonly Godot.Collections.Array<Rid> _exclude = new();
		private PhysicsRayQueryParameters3D _plantQ;
		// every bone it turns: what its pose was before (this frame's clip) and after; a bone the clip doesn't key keeps last
		// frame's result, so it's put back first (else the lean would pile up frame on frame)
		private readonly int[] _touched;
		private readonly Quaternion[] _before, _after;
		private Vector3 _skBase;

		public BodyDynamics(Skeleton3D sk, string hips, string[] spine, string head, string[] upperArms, (string thigh, string shin, string foot, string toe)[] legs, CollisionObject3D exclude = null)
		{
			_sk = sk;
			int B(string n) => n == null ? -1 : sk.FindBone(n);
			_hips = B(hips);
			_spine = System.Array.ConvertAll(spine ?? System.Array.Empty<string>(), B);
			_head = B(head);
			_arms = System.Array.ConvertAll(upperArms ?? System.Array.Empty<string>(), B);
			_legs = System.Array.ConvertAll(legs ?? System.Array.Empty<(string, string, string, string)>(), l => (B(l.thigh), B(l.shin), B(l.foot), B(l.toe)));
			_legOff = new Spring1[_legs.Length];
			if (exclude != null) _exclude.Add(exclude.GetRid());
			var t = new System.Collections.Generic.List<int>();
			void Add(int b) { if (b >= 0 && !t.Contains(b)) t.Add(b); }
			foreach (int b in _spine) Add(b);
			Add(_head);
			foreach (int b in _arms) Add(b);
			foreach (var l in _legs) { Add(l.thigh); Add(l.shin); Add(l.foot); }
			_touched = t.ToArray();
			_before = new Quaternion[_touched.Length];
			_after = new Quaternion[_touched.Length];
			for (int i = 0; i < _touched.Length; i++) _after[i] = new Quaternion(0, 0, 0, 0);
			_skBase = sk.Position;
		}

		/// <summary>Lays the weight over this frame's pose. <paramref name="body"/> is what moves through the world (its
		/// velocity, its facing); <paramref name="grounded"/> false lets the feet go (a leap).</summary>
		public void Apply(Node3D body, float dt, bool grounded = true)
		{
			if (_sk == null || !GodotObject.IsInstanceValid(_sk) || dt <= 0f) return;
			for (int i = 0; i < _touched.Length; i++)
			{
				var now = _sk.GetBonePoseRotation(_touched[i]);
				if (now.IsEqualApprox(_after[i])) _sk.SetBonePoseRotation(_touched[i], _before[i]);   // (unkeyed: last frame's, put back)
				_before[i] = _sk.GetBonePoseRotation(_touched[i]);
			}
			ApplyInner(body, dt, grounded);
			for (int i = 0; i < _touched.Length; i++) _after[i] = _sk.GetBonePoseRotation(_touched[i]);
		}

		private void ApplyInner(Node3D body, float dt, bool grounded)
		{
			Vector3 pos = body.GlobalPosition;
			float yaw = body.GlobalRotation.Y;
			if (!_set || pos.DistanceTo(_lastPos) > 6f) { _set = true; _lastPos = pos; _lastVel = Vector3.Zero; _lastYaw = yaw; }
			Vector3 vel = (pos - _lastPos) / dt;
			Vector3 acc = ((vel - _lastVel) / dt).LimitLength(40f);
			float turn = Mathf.AngleDifference(_lastYaw, yaw) / dt;
			_lastPos = pos; _lastVel = vel; _lastYaw = yaw;
			// the body's own frame: forward is -Z
			Basis bb = body.GlobalBasis.Orthonormalized();
			Vector3 fwd = -bb.Z, right = bb.X;
			float accF = acc.Dot(fwd), accR = acc.Dot(right);
			float speed = new Vector2(vel.X, vel.Z).Length();
			float w = Mathf.Clamp(Weight, 0f, 1f);
			_pitch.Step(Mathf.Clamp(accF * LeanPerAccel, -MaxLean, MaxLean) * w, 7f, dt);
			_roll.Step(Mathf.Clamp(-accR * LeanPerAccel * 0.6f - turn * speed * TurnRoll * 0.1f, -MaxLean, MaxLean) * w, 6f, dt);
			// the arms: a pendulum hanging behind the acceleration
			_arm.Step(new Vector3(Mathf.Clamp(-accF * ArmSwing, -MaxArm, MaxArm), 0, Mathf.Clamp(-accR * ArmSwing, -MaxArm, MaxArm)) * w, 5f, dt);

			Basis skInv = _sk.GlobalBasis.Orthonormalized().Inverse();
			Vector3 rightSk = (skInv * right).Normalized(), fwdSk = (skInv * fwd).Normalized();
			// the spine: the lean shared out down its bones; the head taking back half, so its gaze holds
			if (_spine.Length > 0 && (Mathf.Abs(_pitch.X) > 1e-4f || Mathf.Abs(_roll.X) > 1e-4f))
			{
				float per = 1f / _spine.Length;
				foreach (int s in _spine)
				{
					BonePose.Turn(_sk, s, rightSk, -_pitch.X * per);
					BonePose.Turn(_sk, s, fwdSk, _roll.X * per);
				}
				if (_head >= 0)
				{
					BonePose.Turn(_sk, _head, rightSk, _pitch.X * 0.5f);
					BonePose.Turn(_sk, _head, fwdSk, -_roll.X * 0.5f);
				}
			}
			foreach (int a in _arms)
			{
				if (a < 0) continue;
				BonePose.Turn(_sk, a, rightSk, _arm.X.X);
				BonePose.Turn(_sk, a, fwdSk, -_arm.X.Z);
			}
			if (PlantFeet && _legs.Length > 0 && _hips >= 0) Plant(body, grounded, w, dt);
		}

		private void Plant(Node3D body, bool grounded, float w, float dt)
		{
			var space = _sk.GetWorld3D()?.DirectSpaceState;
			if (space == null) return;
			float floorY = body.GlobalPosition.Y;
			float scale = _sk.GlobalBasis.Scale.Y;
			float reach = MaxStep * Mathf.Max(scale, 0.2f);
			var want = new float[_legs.Length];
			float lowest = 0f;
			for (int i = 0; i < _legs.Length; i++)
			{
				var (thigh, shin, foot, toe) = _legs[i];
				if (thigh < 0 || shin < 0 || foot < 0) continue;
				int contact = toe >= 0 ? toe : foot;
				Vector3 c = _sk.GlobalTransform * _sk.GetBoneGlobalPose(contact).Origin;
				// (one query kept, the ends moved: a new one per leg per frame was garbage for the collector; the lag pass)
				_plantQ ??= PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Down, 1u, _exclude);
				_plantQ.From = c + Vector3.Up * reach;
				_plantQ.To = c + Vector3.Down * reach;
				using var hit = space.IntersectRay(_plantQ);
				// the ground under the foot against the ground the body stands on: only the difference (a lifted foot in the
				// clip keeps its lift)
				float d = hit.Count > 0 && grounded ? Mathf.Clamp(((Vector3)hit["position"]).Y - floorY, -reach, reach) : 0f;
				_legOff[i].Step(d * w, 12f, dt);
				want[i] = _legOff[i].X;
				lowest = Mathf.Min(lowest, want[i]);
			}
			_drop.Step(lowest, 10f, dt);
			HipDrop = -_drop.X;
			Basis skInv = _sk.GlobalBasis.Inverse();
			Vector3 upSk = skInv * Vector3.Up;   // (world metres: the skeleton's scale undone)
			// (the whole skeleton lowered, not the hips bone: a channel the clip may not key)
			Node3D skParent = _sk.GetParent<Node3D>();
			_sk.Position = _skBase + (skParent != null ? skParent.GlobalBasis.Inverse() * (Vector3.Up * _drop.X) : Vector3.Up * _drop.X);
			for (int i = 0; i < _legs.Length; i++)
			{
				var (thigh, shin, foot, _) = _legs[i];
				if (thigh < 0 || shin < 0 || foot < 0) continue;
				float lift = want[i] - _drop.X;   // (the hips came down by drop: this leg's foot only needs the rest)
				if (Mathf.Abs(lift) < 1e-4f) continue;
				// the ankle where it was before the hips came down, raised by the ground under it
				Vector3 ankle = _sk.GetBoneGlobalPose(foot).Origin;
				TwoBone(_sk, thigh, shin, foot, ankle + upSk * lift, 1f);
			}
		}

		/// <summary>A skeleton-space offset in the frame of <paramref name="bone"/>'s parent (for a bone's pose position).</summary>
		private Vector3 ParentSpace(int bone, Vector3 skelOffset)
		{
			int p = _sk.GetBoneParent(bone);
			return p < 0 ? skelOffset : _sk.GetBoneGlobalPose(p).Basis.Inverse() * skelOffset;
		}
	}
}

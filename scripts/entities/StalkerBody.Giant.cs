using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// The face pass and the last staircase's giant (the owner, 2026-10-03: "a jaw that is barely hanging on from one side";
/// the giant's "movements more horrific and unnatural feeling ... grab the player with a janky haste"):
/// - the jaw: torn loose on its left (the model's weights: a clean tear there, sinews across it) and hanging from its
///   right hinge; every stalker, always. Alive (the owner: "the jaw needs some animation work to move it around so it
///   feels more real and scary"): it lags and swings on springs as the head turns, twists sideways on its one hinge,
///   lolls, is kicked by spasms, and chatters in short bursts;
/// - its breath: the chest and shoulders heaving, ragged, catching;
/// - the giant's wrongness, set by its owner each frame: the spine bent double (<see cref="SpineBow"/>), a shoulder
///   hitched (<see cref="ChestRoll"/>), the neck drawn out (<see cref="NeckStretch"/>), the head over on its side
///   (<see cref="HeadRoll"/>) and jolted (<see cref="HeadJolt"/>);
/// - stop-motion: <see cref="Hold"/> freezes the whole figure where it is until <see cref="Step"/> lets one frame
///   through (the grab's juddering haste: pose, hold, pose);
/// - the grab: one arm reaching for a point, its elbow bent the wrong way (<see cref="Grab"/>).
/// </summary>
public partial class StalkerBody
{
	/// <summary>How far the torn jaw hangs (radians about its held hinge).</summary>
	[Export] public float JawHang = 0.42f;

	public float SpineBow { get; set; }
	public float ChestRoll { get; set; }
	public float HeadRoll { get; set; }
	public float NeckStretch { get; set; }
	public Vector3 HeadJolt { get; set; }
	/// <summary>Frozen where it stands (stop-motion), but for the frames <see cref="Step"/> lets through.</summary>
	public bool Hold { get; set; }
	public void Step() => _step = true;
	private bool _step;

	private Vector3 _jawPivotHead, _jawAxisHead;   // the held hinge and the hang's axis, in the head bone's space
	private Vector3 _jawSideHead, _jawUpHead;      // across the face and up the skull, in the head bone's space
	private float _jawDrop, _jawDropV, _jawTwist, _jawTwistV, _chatterIn = 3f, _chatterLeft;
	private Quaternion _lastHeadQ = Quaternion.Identity;
	private bool _jawLive;
	private readonly RandomNumberGenerator _jawRng = new() { Seed = 77 };
	private float _jawSign = 1f;
	private bool _jawReady;

	/// <summary>Where a bone is now (world).</summary>
	public Vector3 BoneWorld(string name)
	{
		int b = _skel != null ? Bone(name) : -1;
		return b >= 0 ? _skel.GlobalTransform * _skel.GetBoneGlobalPose(b).Origin : GlobalPosition + Vector3.Up * Size * 2f;
	}

	/// <summary>Shoulder to wrist at rest (world metres).</summary>
	public float ArmLength(string side)
	{
		if (_skel == null) return Size;
		int u = Bone("upper_" + side), f = Bone("fore_" + side), h = Bone("hand_" + side);
		if (u < 0 || f < 0 || h < 0) return Size;
		Vector3 U = _skel.GlobalTransform * _skel.GetBoneGlobalRest(u).Origin, F = _skel.GlobalTransform * _skel.GetBoneGlobalRest(f).Origin,
			H = _skel.GlobalTransform * _skel.GetBoneGlobalRest(h).Origin;
		return U.DistanceTo(F) + F.DistanceTo(H);
	}

	/// <summary>One hand reaching for <paramref name="target"/> (world), its fingers out along <paramref name="fingers"/>,
	/// the elbow the wrong way; snapped there (no ease: the owner's stop-motion carries the motion).</summary>
	public void Grab(Vector3 target, Vector3 fingers, string side = "R")
	{
		if (_skel == null) return;
		int hand = Bone("hand_" + side);
		var g = _grips.Find(x => x.Hand == hand);
		if (g == null)
		{
			g = MakeGrip(side, target, fingers.Normalized(), true);
			if (g == null) return;
			_grips.Add(g);
		}
		g.Target = target;
		g.Wrap = fingers.Normalized();
		g.On = true;
		g.W = 1f;
		g.Wrong = true;
	}

	/// <summary>The held hinge, found once from the rest skeleton: the jaw's right corner (its right hand's side).</summary>
	private void FindJawHinge()
	{
		_jawReady = false;
		int hr = Bone("hand_R"), hl = Bone("hand_L");
		if (_bJaw < 0 || _bHead < 0 || hr < 0 || hl < 0) return;
		Transform3D jaw = _skel.GetBoneGlobalRest(_bJaw), head = _skel.GetBoneGlobalRest(_bHead);
		Vector3 side = (_skel.GetBoneGlobalRest(hr).Origin - _skel.GetBoneGlobalRest(hl).Origin).Normalized();
		Vector3 along = jaw.Basis.Y.Normalized();
		Vector3 up = Vector3.Up;
		// across its face to the corner, and forward down the jaw to it (the model's units: design metres)
		Vector3 pivot = jaw.Origin + side * 0.054f + along * 0.09f;
		// the hang's axis: along the jaw, so the far (torn) side swings down
		Vector3 axis = along;
		_jawSign = axis.Cross(-side).Dot(up) > 0f ? -1f : 1f;
		var inv = head.AffineInverse();
		_jawPivotHead = inv * pivot;
		_jawAxisHead = (inv.Basis * axis).Normalized();
		_jawSideHead = (inv.Basis * side).Normalized();
		_jawUpHead = (inv.Basis * along.Cross(side)).Normalized();
		_jawRng.Seed = (ulong)(Seed + 77);
		_jawReady = true;
	}

	/// <summary>Before the look: the spine bowed, the shoulder hitched, the neck drawn out.</summary>
	private void GiantPre()
	{
		Vector3 side = _skel.GlobalBasis.Inverse() * GlobalBasis.X;
		Vector3 fwd = _skel.GlobalBasis.Inverse() * GlobalBasis.Z;
		int spine = Bone("spine");
		if (Mathf.Abs(SpineBow) > 1e-4f)
		{
			if (spine >= 0) TurnBone(spine, side, SpineBow * 0.45f);
			if (_bChest >= 0) TurnBone(_bChest, side, SpineBow * 0.55f);
		}
		if (Mathf.Abs(ChestRoll) > 1e-4f && _bChest >= 0) TurnBone(_bChest, fwd, ChestRoll);
		if (NeckStretch > 1e-4f)
		{
			if (_bNeck2 >= 0) _skel.SetBonePosePosition(_bNeck2, _skel.GetBonePosePosition(_bNeck2) * (1f + 0.7f * NeckStretch));
			_skel.SetBonePosePosition(_bHead, _skel.GetBonePosePosition(_bHead) * (1f + 1.8f * NeckStretch));
		}
	}

	/// <summary>After the look: the head over on its side and jolted; then the jaw, hung from its hinge and alive.</summary>
	private void GiantPost(float dt)
	{
		if (Mathf.Abs(HeadRoll) > 1e-4f)
		{
			Vector3 face = _skel.GetBoneGlobalPose(_bHead).Basis.Y.Normalized();
			TurnBone(_bHead, face, HeadRoll);
		}
		if (HeadJolt.LengthSquared() > 1e-8f)
			_skel.SetBonePoseRotation(_bHead, _skel.GetBonePoseRotation(_bHead) * Quaternion.FromEuler(HeadJolt));
		if (!_jawReady || JawHang <= 1e-4f) return;
		Transform3D head = _skel.GetBoneGlobalPose(_bHead);
		Vector3 side = (head.Basis * _jawSideHead).Normalized(), up = (head.Basis * _jawUpHead).Normalized();
		// the head's turning, felt by the loose jaw: it lags, overshoots and swings back
		var q = head.Basis.Orthonormalized().GetRotationQuaternion();
		Vector3 spin = Vector3.Zero;
		if (_jawLive && dt > 1e-4f)
		{
			var dq = (q * _lastHeadQ.Inverse()).Normalized();
			float ang = Mathf.Wrap(dq.GetAngle(), -Mathf.Pi, Mathf.Pi);
			if (Mathf.Abs(ang) > 1e-5f && Mathf.Abs(ang) < 1.5f) spin = dq.GetAxis().Normalized() * ang / dt;
		}
		_lastHeadQ = q;
		_jawLive = true;
		if (!spin.IsFinite()) spin = Vector3.Zero;
		spin = spin.LimitLength(8f);
		_jawDropV += (-38f * _jawDrop - 4.5f * _jawDropV + spin.Dot(side) * 5f) * dt;
		_jawTwistV += (-28f * _jawTwist - 3.5f * _jawTwistV - spin.Dot(up) * 4f) * dt;
		// spasms: now and then something kicks it
		if (_jawRng.Randf() < dt * 0.4f) _jawDropV += _jawRng.RandfRange(-2.5f, 4f);
		if (_jawRng.Randf() < dt * 0.3f) _jawTwistV += _jawRng.RandfRange(-2.5f, 2.5f);
		_jawDrop = Mathf.Clamp(_jawDrop + _jawDropV * dt, -0.25f, 0.45f);
		_jawTwist = Mathf.Clamp(_jawTwist + _jawTwistV * dt, -0.3f, 0.3f);
		// chattering: short bursts of the teeth knocking, every few seconds
		_chatterIn -= dt;
		if (_chatterIn <= 0f) { _chatterLeft = _jawRng.RandfRange(0.35f, 1.1f); _chatterIn = _jawRng.RandfRange(4f, 11f); }
		float chatter = 0f;
		if (_chatterLeft > 0f)
		{
			_chatterLeft -= dt;
			chatter = 0.09f * Mathf.Max(0f, Mathf.Sin(_t * 52f));
		}
		if (chatter > 1e-4f) TurnBone(_bJaw, side, -chatter);
		// hung from the held hinge: the torn side down, lolling; twisted sideways about it
		float hang = JawHang + _jawDrop + 0.05f * Mathf.Sin(_t * 1.3f) + 0.03f * Mathf.Sin(_t * 3.1f + 1f);
		Vector3 pivot = head * _jawPivotHead;
		Vector3 axis = (head.Basis * _jawAxisHead).Normalized();
		var turn = new Transform3D(new Basis(axis, hang * _jawSign) * new Basis(up, _jawTwist), Vector3.Zero);
		var to = new Transform3D(Basis.Identity, pivot);
		_skel.SetBoneGlobalPose(_bJaw, to * turn * to.AffineInverse() * _skel.GetBoneGlobalPose(_bJaw));
	}

	/// <summary>Its breath: the chest heaving and the shoulders lifting with it, slow and ragged, catching halfway now
	/// and then (a rattle in it), never quite even.</summary>
	private void Breathe()
	{
		if (_bChest < 0) return;
		float ph = _t * 0.9f + Seed * 0.1f;
		float b = Mathf.Sin(ph) + 0.35f * Mathf.Sin(ph * 2.3f + 1f);
		// the catch: the in-breath stalls for a moment, every third breath or so
		if (Mathf.Sin(ph * 0.31f) > 0.6f) b = Mathf.Min(b, 0.2f + 0.1f * Mathf.Sin(_t * 23f));
		Vector3 side = _skel.GlobalBasis.Inverse() * GlobalBasis.X;
		TurnBone(_bChest, side, -0.035f * b);
		int cr = Bone("clav_R"), cl = Bone("clav_L");
		foreach (int c in new[] { cr, cl })
			if (c >= 0)
			{
				Vector3 fwd = _skel.GetBoneGlobalPose(c).Basis.Z.Normalized();
				TurnBone(c, fwd, 0.03f * b * (c == cr ? 1f : -1f));
			}
	}
}

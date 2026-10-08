using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// The horror pass (the owner, 2026-10-02: "make things as horrific as possible for the stalker"), on top of the clips:
/// - its head follows you: the eyes (and the eyeshine) stay on you as you move;
/// - unwatched, its head leans over a little toward its shoulder and the needle teeth bare a little (its neck drawing out
///   round the trunk is gone: the owner, 2026-10-07, found it looked bad);
/// - watched, it doesn't move, except its mouth: the slit opens, slowly, the longer you look, and the head goes on
///   tipping;
/// - a hand on the bark drums its long fingers on it, one after another, and stops dead when you look;
/// - (Owl) its body turned away from you and its head right round on its twisted neck, looking at you over its back.
/// All of it slow: nothing snaps across the view (the motion is the monster's, never the camera's).
/// </summary>
public partial class StalkerBody
{
	/// <summary>Being looked at right now (its owner says so, every frame).</summary>
	public bool Watched { get; set; }
	/// <summary>A world point its head follows (the camera; null: the clips' and LookTarget's own).</summary>
	public Vector3? TrackTarget { get; set; }
	/// <summary>How far it has crept, unwatched, on this peek (0..1); how far its mouth has opened (radians). Tests.</summary>
	public float Creep => _creep;
	public float Gape => _gape;

	private float _creep, _gape, _watchedFor, _tip;
	private Vector3 _trackDir;
	private bool _trackStarted;

	private void BeginPeek()
	{
		_creep = 0f;
		_gape = 0f;
		_watchedFor = 0f;
		_tip = 0f;
		_trackStarted = false;
	}

	/// <summary>A direction turned toward another by a share of the way (Godot's Slerp throws on near-parallel pairs).</summary>
	private static Vector3 Turn(Vector3 from, Vector3 to, float w)
	{
		if (!from.IsFinite() || from.LengthSquared() < 1e-8f) return to;
		if (!to.IsFinite() || to.LengthSquared() < 1e-8f) return from;
		from = from.Normalized(); to = to.Normalized();
		Vector3 axis = from.Cross(to);
		float s = axis.Length();
		if (s < 1e-4f) return (from + (to - from) * w).Normalized() is { } v && v.IsFinite() && v.LengthSquared() > 0.5f ? v : from;
		return from.Rotated((axis / s).Normalized(), Mathf.Atan2(s, from.Dot(to)) * w).Normalized();
	}

	private bool Peeking => _hold != null && (_hold.StartsWith("peek") || _hold == "cling");

	private void Horror(float dt)
	{
		if (_bHead < 0) return;
		Breathe();
		GiantPre();
		Vector3 up = (_skel.GlobalBasis.Inverse() * Vector3.Up).Normalized();
		// the owl: the head right round on the twisted neck (a third of the turn at each bone)
		if (Kind == PeekKind.Owl && Peeking)
		{
			int neck = Bone("neck");
			if (neck >= 0) TurnBone(neck, up, Mathf.Pi / 3f);
			if (_bNeck2 >= 0) TurnBone(_bNeck2, up, Mathf.Pi / 3f);
			TurnBone(_bHead, up, Mathf.Pi / 3f);
		}
		// creeping, unwatched; the mouth, watched
		if (Peeking)
		{
			if (Watched) _watchedFor += dt;
			else _creep = Mathf.MoveToward(_creep, 1f, dt / 9f);
			float stare = Mathf.SmoothStep(0f, 2.6f, _watchedFor);
			_gape = 0.18f * _creep + 0.55f * stare;
			_tip = Mathf.MoveToward(_tip, 0.2f * _creep + 0.25f * stare, dt * 0.25f);
		}
		else if (Watched || WalkPhase < 0f && TrackTarget != null)
		{
			// the pursuer stopped dead under your eyes: the head tipping over, the slit opening
			_watchedFor = Watched ? _watchedFor + dt : Mathf.Max(0f, _watchedFor - dt * 0.5f);
			float stare = Mathf.SmoothStep(0f, 3f, _watchedFor);
			_gape = Mathf.MoveToward(_gape, 0.45f * stare, dt * 0.4f);
			_tip = Mathf.MoveToward(_tip, 0.25f * stare, dt * 0.25f);
			_creep = Mathf.MoveToward(_creep, 0f, dt);
		}
		else
		{
			_gape = Mathf.MoveToward(_gape, 0f, dt * 0.8f);
			_tip = Mathf.MoveToward(_tip, 0f, dt * 0.5f);
			_creep = Mathf.MoveToward(_creep, 0f, dt);
			_watchedFor = 0f;
		}
		// (its neck no longer draws out unwatched: the owner, 2026-10-07, "the creature following the player is like extending
		// his head ... it looks really bad whatever he is doing". Unwatched, it only leans its head over, a little)
		// the head follows you
		if (TrackTarget is { } target)
		{
			Vector3 head = _skel.GetBoneGlobalPose(_bHead).Origin;
			Vector3 want = (_skel.GlobalTransform.AffineInverse() * target - head).Normalized();
			if (!want.IsFinite()) want = _trackDir;
			if (!_trackStarted || !_trackDir.IsFinite()) { _trackDir = want; _trackStarted = true; }
			_trackDir = Turn(_trackDir, want, 1f - Mathf.Exp(-2.2f * dt));
			if (_bNeck2 >= 0) AimBone(_bNeck2, _trackDir, 0.25f);
			AimBone(_bHead, _trackDir, 0.7f);
		}
		// tipping over onto its shoulder (about the way the face looks)
		if (_tip > 0.001f)
		{
			Vector3 face = _skel.GetBoneGlobalPose(_bHead).Basis.Y.Normalized();
			TurnBone(_bHead, face, (_peekSide == "R" ? 1f : -1f) * _tip);
		}
		// the jaw (its clip's opening and this): about the figure's side axis, opening down
		if (_bJaw >= 0 && _gape > 0.001f) TurnBone(_bJaw, Vector3.Right, -_gape);
		GiantPost(dt);
	}

	/// <summary>A hand on the bark drums its fingers on it, unwatched: each finger lifting and coming down in turn.
	/// Watched, they stop where they are.</summary>
	private void Fingers(float dt)
	{
		if (Watched) { _drumFrozen = true; }
		else { _drumFrozen = false; _drum += dt; }
		foreach (var g in _grips)
		{
			if (g.W < 0.99f || g.Fingers == null) continue;
			for (int k = 0; k < g.Fingers.Length; k++)
			{
				int f = g.Fingers[k];
				if (f < 0) continue;
				float ph = _drum * 5.5f - k * 0.85f;
				float lift = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(ph)), 6f) * 0.45f * (0.6f + 0.4f * Mathf.Sin(_drum * 0.7f + k));
				if (lift < 0.002f) continue;
				Vector3 axis = _skel.GetBoneGlobalPose(f).Basis.X.Normalized();
				TurnBone(f, axis, -lift);
			}
		}
	}
	private float _drum;
	private bool _drumFrozen;

	private static readonly string[] SampleBones = { "head", "neck2", "chest", "clav_R", "clav_L", "hips", "shin_R", "shin_L", "hand_R", "hand_L" };

	/// <summary>Points spread over the figure as it stands now (the remodel's bones: head, neck, chest, shoulders, hips,
	/// knees, hands), for the visibility rules; null for the old figure (its fixed sample points then).</summary>
	public Vector3[] SamplePointsWorld()
	{
		if (_skel == null) return null;
		var pts = new Vector3[SampleBones.Length];
		var xf = _skel.GlobalTransform;
		for (int i = 0; i < SampleBones.Length; i++)
		{
			int b = Bone(SampleBones[i]);
			pts[i] = b >= 0 ? xf * _skel.GetBoneGlobalPose(b).Origin : GlobalPosition;
		}
		// the head's point at the eyes
		pts[0] = ModelEyesWorld();
		return pts;
	}
}

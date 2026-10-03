using System.Collections.Generic;
using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// The remodelled stalker (the owner, 2026-10-02: on par with the wendigo). tools/Blender/stalker.py's figure
/// (assets/models/stalker/stalker.glb): about 85,000 triangles, one hide fused over its bones, a small mask of a face
/// with deep sockets, the eyes far back in them, the shoulders' crowns of root spikes, rags on chains of bones; skinned
/// to a skeleton with its clips (idle, peek_R/L, duck_R/L, walk, shove, loom, stare). Every surface is still drawn
/// with <see cref="Skin"/> (the black, the dissolve, the eyes), now with the model's baked maps for the faint edge
/// light up close.
///
/// On top of the clips, every frame:
/// - the head's drift and twitch and the look (<see cref="LookTarget"/>), as the old body's;
/// - the rags: each chain swings on a damped spring under gravity and the body's own movement, with a breath of wind;
/// - the grip (<see cref="Peek"/>): the near hand put on the bark at the trunk's edge by a two-bone reach, the long
///   fingers curled round it;
/// - the eyeshine: its eyes answer the lantern (lit, near, and the head turned toward you), never on their own.
/// </summary>
public partial class StalkerBody
{
	public const string ModelPath = "res://assets/models/stalker/stalker.glb";

	/// <summary>True when it's the remodel (a skeleton and clips), false for the old pieces (the editor).</summary>
	public bool Rigged => _skel != null;
	/// <summary>The clip playing (tests).</summary>
	public string Clip => _anim?.AssignedAnimation ?? "";
	/// <summary>A step of the walk clip (metres at this size): walkers advance <see cref="WalkPhase"/> by pi per step.</summary>
	public float StepLength => 0.6f * Size;
	/// <summary>World point the near hand is on (null: not gripping).</summary>
	public Vector3? GripPoint => _grips.Count > 0 && _grips[0].On ? _grips[0].Target : null;
	/// <summary>Where the gripping hand's wrist is now (tests).</summary>
	public Vector3 GripHandWorld => _skel != null && _grips.Count > 0 ? _skel.GlobalTransform * _skel.GetBoneGlobalPose(_grips[0].Hand).Origin : GlobalPosition;
	/// <summary>Peeks with a hand on the bark, and how far the hand was off its grip when last held there (tests).</summary>
	public int GripCount { get; private set; }
	public float LastGripError { get; private set; } = -1f;
	/// <summary>The rags' largest swing this side of rest, in degrees (tests: they move).</summary>
	public float RagSwingDegrees { get; private set; }
	/// <summary>The eyeshine right now (tests).</summary>
	public float Eyeshine { get; private set; }

	private Node3D _model;
	private Skeleton3D _skel;
	private AnimationPlayer _anim;
	private int _bHead = -1, _bNeck2 = -1, _bChest = -1, _bJaw = -1;
	private readonly Dictionary<string, int> _bones = new();
	private Vector3 _eyesModel;      // between the eyes, in the head bone's space
	private string _hold;            // a clip that holds its last frame (peek, loom, stare)
	private float _walkRate;

	private sealed class RagChain
	{
		public int[] Bones;
		public Vector3[] Dir;       // each bone's swinging direction (skeleton space)
		public Vector3[] Vel;
		public float Hang;          // how far it hangs toward plain down (the sleeves more)
		public Vector3 LastRoot, RootVel;
		public bool Started;
	}
	private readonly List<RagChain> _rags = new();

	/// <summary>A hand on the bark: its arm's bones, where it holds, which way its fingers lie, and (the horror pass)
	/// whether its elbow bends the wrong way.</summary>
	private sealed class Grip
	{
		public int Upper, Fore, Hand;
		public int[] Fingers;
		public Vector3 Target, Wrap;
		public bool On, Wrong;
		public float W;
	}
	private readonly List<Grip> _grips = new();

	private Grip MakeGrip(string side, Vector3 target, Vector3 wrap, bool wrong)
	{
		var g = new Grip { Upper = Bone("upper_" + side), Fore = Bone("fore_" + side), Hand = Bone("hand_" + side), Target = target, Wrap = wrap, Wrong = wrong, On = true, W = 1f };
		g.Fingers = new[] { Bone($"f0_1_{side}"), Bone($"f1_1_{side}"), Bone($"f2_1_{side}"), Bone($"f3_1_{side}") };
		return g.Upper < 0 || g.Fore < 0 || g.Hand < 0 ? null : g;
	}

	private bool LoadModel()
	{
		if (Engine.IsEditorHint() || CreatureModels.Disabled || !ResourceLoader.Exists(ModelPath) || System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--old-stalker") >= 0) return false;
		foreach (var c in GetChildren())
			if (c.HasMeta("stalker_generated")) { RemoveChild(c); c.QueueFree(); }
		if (Skin == null)
		{
			Skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
			Skin.SetShaderParameter("visibility", 1f);
		}
		_model = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
		_model.Name = "Model";
		_model.SetMeta("stalker_generated", true);
		// Blender's front (+Y) comes in as -Z; the figure faces +Z
		_model.Rotation = new Vector3(0, Mathf.Pi, 0);
		_model.Scale = Vector3.One * Size;
		AddChild(_model);
		AddToGroup("warm_up");   // its shader compiled before the fade-in, though it starts unseen
		_skel = _model.FindChildren("*", "Skeleton3D", true, false) is { Count: > 0 } sk ? (Skeleton3D)sk[0] : null;
		_anim = _model.FindChildren("*", "AnimationPlayer", true, false) is { Count: > 0 } ap ? (AnimationPlayer)ap[0] : null;
		if (_skel == null) { _model.QueueFree(); _model = null; return false; }
		for (int i = 0; i < _skel.GetBoneCount(); i++) _bones[_skel.GetBoneName(i)] = i;
		_bHead = Bone("head"); _bNeck2 = Bone("neck2"); _bChest = Bone("chest"); _bJaw = Bone("jaw");
		TriangleCount = 0;
		var uniforms = new HashSet<string>();
		foreach (var u in Skin.Shader.GetShaderUniformList()) uniforms.Add((string)u.AsGodotDictionary()["name"]);
		foreach (var n in _model.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)n;
			if (mi.Mesh == null) continue;
			mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
			{
				var arrays = mi.Mesh.SurfaceGetArrays(s);
				TriangleCount += ((int[])arrays[(int)Mesh.ArrayType.Index]).Length / 3;
				// the baked maps onto the skin (every surface shares them)
				if (mi.Mesh.SurfaceGetMaterial(s) is StandardMaterial3D m && uniforms.Contains("detail_albedo"))
				{
					Skin.SetShaderParameter("detail_albedo", m.AlbedoTexture);
					Skin.SetShaderParameter("detail_normal", m.NormalTexture);
					Skin.SetShaderParameter("use_maps", m.AlbedoTexture != null);
				}
				FindEyes(arrays);
			}
			mi.MaterialOverride = Skin;
		}
		// the rags' chains (rag<n>_0..2)
		for (int r = 0; ; r++)
		{
			int b0 = Bone($"rag{r}_0");
			if (b0 < 0) break;
			var rag = new RagChain { Bones = new[] { b0, Bone($"rag{r}_1"), Bone($"rag{r}_2") }, Dir = new Vector3[3], Vel = new Vector3[3] };
			string parent = _skel.GetBoneName(_skel.GetBoneParent(b0));
			rag.Hang = parent.StartsWith("upper") || parent.StartsWith("fore") ? 0.75f : 0.3f;
			_rags.Add(rag);
		}
		if (_anim != null)
		{
			_anim.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
			foreach (var clip in new[] { "idle", "walk" })
				if (_anim.GetAnimation(clip) is { } a) a.LoopMode = Animation.LoopModeEnum.Linear;
			_anim.Play("idle");
			_anim.Seek(_idleRng.RandfRange(0f, 5.5f), true);
		}
		FindJawHinge();
		return true;
	}

	private int Bone(string name) => _bones.TryGetValue(name, out int i) ? i : -1;

	/// <summary>The eyes: the vertices marked with alpha 0 (the game's tone convention), their middle in the head bone's
	/// rest space.</summary>
	private void FindEyes(Godot.Collections.Array arrays)
	{
		if (_bHead < 0 || arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil) return;
		var cols = (Color[])arrays[(int)Mesh.ArrayType.Color];
		var verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
		Vector3 sum = Vector3.Zero, sumL = Vector3.Zero, sumR = Vector3.Zero;
		int n = 0, nl = 0, nr = 0;
		for (int i = 0; i < cols.Length; i++)
			if (cols[i].A < 0.5f) { sum += verts[i]; n++; }
		if (n == 0) return;
		Vector3 mid = sum / n;
		for (int i = 0; i < cols.Length; i++)
			if (cols[i].A < 0.5f)
			{
				if (verts[i].X < mid.X) { sumL += verts[i]; nl++; } else { sumR += verts[i]; nr++; }
			}
		// mesh space is the skeleton's rest space here (the skin binds at the rest pose)
		var toHead = _skel.GetBoneGlobalRest(_bHead).AffineInverse();
		_eyesModel = toHead * mid;
		_eyeA = toHead * (nl > 0 ? sumL / nl : mid);
		_eyeB = toHead * (nr > 0 ? sumR / nr : mid);
		_eyesFound = true;
	}
	private bool _eyesFound;
	private Vector3 _eyeA, _eyeB;

	/// <summary>The bunker's and Act 11's lit eyes on the remodel: two beads in the sockets and a faint light, on the head bone.</summary>
	private void GlowModelEyes(Color colour, float energy)
	{
		if (_skel.GetNodeOrNull("EyeGlow") != null || _bHead < 0) return;
		var att = new BoneAttachment3D { Name = "EyeGlow", BoneName = "head" };
		_skel.AddChild(att);
		var rest = _skel.GetBoneGlobalRest(_bHead);
		// the attachment carries the head bone's pose (rest-relative), so the points are given in its space
		var mat = new StandardMaterial3D
		{
			AlbedoColor = colour, EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = energy,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		foreach (var e in new[] { _eyeA, _eyeB })
		{
			var k = new World.MeshKit();
			k.Mat(mat);
			k.Blob(e, new Vector3(0.0045f, 0.0038f, 0.004f), 6, 0f, false);   // (inside the eye: its own glow is the skin's, and the pupil stays)
			k.CommitTo(att, "Eye", false);
		}
		att.AddChild(new OmniLight3D
		{
			Name = "Light", LightColor = colour, LightEnergy = 0.9f, OmniRange = 2.2f * Size, OmniAttenuation = 1.6f,
			// (the face looks down the skeleton's -Z: Blender's front)
			Position = _eyesModel + (rest.Basis.Inverse() * new Vector3(0, 0, -0.12f)), ShadowEnabled = false,
		});
	}

	private Vector3 ModelEyesWorld()
	{
		if (!_eyesFound) return _skel.GlobalTransform * _skel.GetBoneGlobalPose(_bHead).Origin;
		return _skel.GlobalTransform * (_skel.GetBoneGlobalPose(_bHead) * _eyesModel);
	}

	// ─────────────────────────────── what the story asks of it ───────────────────────────────

	/// <summary>Plays a clip (blended in); one that doesn't loop holds its last frame.</summary>
	public void Play(string clip, float blend = 0.2f, float speed = 1f)
	{
		if (_anim == null || !_anim.HasAnimation(clip)) return;
		_hold = clip is "idle" or "walk" ? null : clip;
		_anim.Play(clip, blend, speed);
	}

	/// <summary>Straight into a held pose (out of view: no blend), e.g. the peek it is found in.</summary>
	public void Snap(string clip)
	{
		if (_anim == null || !_anim.HasAnimation(clip)) return;
		_hold = clip is "idle" or "walk" ? null : clip;
		_anim.Play(clip, 0.0);
		_anim.Seek(_anim.GetAnimation(clip).Length, true);
		_anim.Advance(0.0);
		Capture();
		foreach (var r in _rags) r.Started = false;
	}

	/// <summary>How it's found (the horror pass): round the side of a trunk; folded low at its foot; its body turned away
	/// and its head right round on its neck to look at you; or up the trunk, clinging, a hand on either edge.</summary>
	public enum PeekKind { Side, Low, Owl, Cling }
	public PeekKind Kind { get; private set; }

	/// <summary>Peeking out from behind a trunk: <paramref name="sideWorld"/> points out past the trunk's edge (the side
	/// that shows); <paramref name="grip"/> is where on the bark the near hand goes (null: no hand on it), and for a
	/// cling <paramref name="farGrip"/> the other hand, on the trunk's other edge. <paramref name="wrongElbow"/>: the
	/// near arm reaches round with its elbow bent the wrong way.</summary>
	public void Peek(Vector3 sideWorld, Vector3? grip, PeekKind kind = PeekKind.Side, Vector3? farGrip = null, bool wrongElbow = false)
	{
		if (_skel == null) return;
		string side = SideOf(sideWorld), other = side == "R" ? "L" : "R";
		Kind = kind;
		Snap(kind switch { PeekKind.Low => "peek_low_" + side, PeekKind.Cling => "cling", _ => "peek_" + side });
		_grips.Clear();
		// the fingers lie forward round the bark toward whoever is looking, curling in across its face
		Vector3 fwd = GlobalBasis.Z.Normalized() * (kind == PeekKind.Owl ? -1f : 1f);
		if (grip.HasValue && MakeGrip(side, grip.Value, (fwd - sideWorld.Normalized() * 0.7f + Vector3.Down * 0.15f).Normalized(), wrongElbow) is { } g) { _grips.Add(g); GripCount++; }
		if (farGrip.HasValue && MakeGrip(other, farGrip.Value, (fwd + sideWorld.Normalized() * 0.7f + Vector3.Down * 0.15f).Normalized(), false) is { } g2) _grips.Add(g2);
		_peekSide = side;
		BeginPeek();
	}
	private string _peekSide = "R";

	/// <summary>Caught looking: it snatches itself back behind the trunk (the side it peeked from).</summary>
	public void Duck()
	{
		foreach (var g in _grips) g.On = false;
		Play(Kind == PeekKind.Cling ? "cling" : "duck_" + _peekSide, 0.03f, 1.6f);
	}

	/// <summary>Back to standing (its idle).</summary>
	public void Rest() { foreach (var g in _grips) g.On = false; Kind = PeekKind.Side; Play("idle", 0.35f); }

	/// <summary>"R" or "L": which of its hands is on the side <paramref name="sideWorld"/> points to.</summary>
	private string SideOf(Vector3 sideWorld)
	{
		int r = Bone("hand_R"), l = Bone("hand_L");
		if (r < 0 || l < 0) return "R";
		Vector3 pr = _skel.GlobalTransform * _skel.GetBoneGlobalRest(r).Origin, pl = _skel.GlobalTransform * _skel.GetBoneGlobalRest(l).Origin;
		return (pr - pl).Dot(sideWorld) >= 0f ? "R" : "L";
	}

	// ─────────────────────────────── every frame ───────────────────────────────

	private void ModelProcess(float dt)
	{
		// stop-motion (the giant's grab): frozen where it is but for the frames let through
		if (Hold)
		{
			if (!_step) return;
			_step = false;
		}
		if (_anim != null)
		{
			// walking: the clip's feet locked to the walker's phase (a footfall at every pi)
			if (WalkPhase >= 0f)
			{
				var walk = _anim.GetAnimation("walk");
				if (walk != null)
				{
					if (_anim.CurrentAnimation != "walk") { _hold = null; _anim.Play("walk", 0.3); _lastPhase = WalkPhase; }
					float len = (float)walk.Length;
					float rate = (WalkPhase - _lastPhase) / Mathf.Max(dt, 1e-4f) / Mathf.Tau * len;   // clip seconds a second
					_walkRate = Mathf.Lerp(_walkRate, rate, 1f - Mathf.Exp(-6f * dt));
					float want = Mathf.PosMod(WalkPhase / Mathf.Tau, 1f) * len;
					float diff = Mathf.PosMod(want - (float)_anim.CurrentAnimationPosition + len * 0.5f, len) - len * 0.5f;
					_anim.SpeedScale = Mathf.Clamp(_walkRate + diff * 3f, 0f, 4f);
				}
			}
			else
			{
				_anim.SpeedScale = 1f;
				if (_anim.CurrentAnimation == "walk") _anim.Play("idle", 0.4);
			}
			_lastPhase = WalkPhase;
			// a clip that's run its course holds its last frame: the player stops writing the bones then, so its pose is
			// kept and put back every frame (the overlays below must start from it, never from last frame's result);
			// the shove goes back to standing
			if (_hold != null && !_anim.IsPlaying() && _hold != "shove") Restore();
			else
			{
				if (_hold == "shove" && !_anim.IsPlaying()) Play("idle", 0.4f);
				// from rest each frame: a channel the clip doesn't key (the head's and the jaw's positions) would
				// otherwise keep last frame's overlays and compound them (the neck drawn out, the jaw hung)
				_skel.ResetBonePoses();
				_anim.Advance(dt);
				Capture();
			}
		}
		// the head: drift, twitch, the look
		float drift = Mathf.DegToRad(HeadDriftDegrees);
		if (_bHead >= 0)
		{
			var q = Quaternion.FromEuler(new Vector3(drift * 0.4f * Mathf.Sin(_t * 0.21f + 2f), drift * 0.5f * Mathf.Sin(_t * 0.17f), drift * Mathf.Sin(_t * 0.13f + 0.5f)) + _twitch);
			_skel.SetBonePoseRotation(_bHead, _skel.GetBonePoseRotation(_bHead) * q);
		}
		float wantLook = 0f;
		if (LookTarget is { } target && IsInsideTree() && _bHead >= 0)
		{
			Vector3 d = GlobalTransform.Basis.Inverse() * (target - ModelEyesWorld());
			wantLook = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), Mathf.DegToRad(-70f), Mathf.DegToRad(35f));
		}
		_lookPitch = Mathf.Lerp(_lookPitch, wantLook, 1f - Mathf.Exp(-2.5f * dt));
		if (Mathf.Abs(_lookPitch) > 1e-4f && TrackTarget == null)
		{
			// pitched about the body's own side axis: the neck takes some, the head the rest, the chest bows a little
			Vector3 side = _skel.GlobalBasis.Inverse() * GlobalBasis.X;
			if (_bChest >= 0) TurnBone(_bChest, side, -_lookPitch * LookBow * 0.5f);
			if (_bNeck2 >= 0) TurnBone(_bNeck2, side, -_lookPitch * 0.35f);
			TurnBone(_bHead, side, -_lookPitch * (0.65f - LookBow * 0.5f));
		}
		Horror(dt);
		foreach (var g in _grips) Reach(g, dt);
		Fingers(dt);
		Rags(dt);
		Shine(dt);
	}
	private float _lastPhase = -1f;
	private Vector3[] _basePos;
	private Quaternion[] _baseRot;
	private Vector3[] _baseScale;

	/// <summary>The clip's pose, as the player last wrote it.</summary>
	private void Capture()
	{
		int n = _skel.GetBoneCount();
		if (_basePos == null || _basePos.Length != n) { _basePos = new Vector3[n]; _baseRot = new Quaternion[n]; _baseScale = new Vector3[n]; }
		for (int i = 0; i < n; i++) { _basePos[i] = _skel.GetBonePosePosition(i); _baseRot[i] = _skel.GetBonePoseRotation(i); _baseScale[i] = _skel.GetBonePoseScale(i); }
	}

	private void Restore()
	{
		if (_basePos == null) { Capture(); return; }
		for (int i = 0; i < _basePos.Length; i++) { _skel.SetBonePosePosition(i, _basePos[i]); _skel.SetBonePoseRotation(i, _baseRot[i]); _skel.SetBonePoseScale(i, _baseScale[i]); }
	}

	/// <summary>Turns a bone about an axis given in the skeleton's space (its pose after the clip), carrying its children.</summary>
	private void TurnBone(int bone, Vector3 axisSkel, float angle)
	{
		if (Mathf.Abs(angle) < 1e-6f || axisSkel.LengthSquared() < 1e-8f) return;
		Basis g = _skel.GetBoneGlobalPose(bone).Basis.Orthonormalized();
		Vector3 local = (g.Inverse() * axisSkel).Normalized();
		_skel.SetBonePoseRotation(bone, (_skel.GetBonePoseRotation(bone) * new Quaternion(local, angle)).Normalized());
	}

	/// <summary>Turns a bone so its length (+Y) points from where it does now toward <paramref name="dirSkel"/>, by <paramref name="w"/>.</summary>
	private void AimBone(int bone, Vector3 dirSkel, float w = 1f)
	{
		Vector3 now = _skel.GetBoneGlobalPose(bone).Basis.Y.Normalized();
		Vector3 want = dirSkel.Normalized();
		Vector3 axis = now.Cross(want);
		float s = axis.Length();
		if (s < 1e-5f) return;
		float angle = Mathf.Atan2(s, now.Dot(want)) * w;
		TurnBone(bone, axis / s, angle);
	}

	/// <summary>A hand onto the bark: the upper arm and forearm turned (keeping the elbow's own side, or, the wrong
	/// way, flipping it) so the wrist reaches the grip point.</summary>
	private void Reach(Grip g, float dt)
	{
		g.W = Mathf.MoveToward(g.W, g.On ? 1f : 0f, dt * 4f);
		if (g.W <= 0f) return;
		var inv = _skel.GlobalTransform.AffineInverse();
		Vector3 S = _skel.GetBoneGlobalPose(g.Upper).Origin;
		Vector3 E = _skel.GetBoneGlobalPose(g.Fore).Origin;
		Vector3 Wr = _skel.GetBoneGlobalPose(g.Hand).Origin;
		Vector3 T = Wr.Lerp(inv * g.Target, g.W);
		float l1 = S.DistanceTo(E), l2 = E.DistanceTo(Wr);
		Vector3 toT = T - S;
		float d = Mathf.Clamp(toT.Length(), Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
		Vector3 dir = toT.Normalized();
		// the elbow stays on its side of the line from shoulder to target (or goes over to the wrong one)
		Vector3 pole = (E - S) - dir * (E - S).Dot(dir);
		if (pole.LengthSquared() < 1e-6f) pole = Vector3.Down;
		pole = pole.Normalized() * (g.Wrong ? -1f : 1f);
		float a = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f));
		Vector3 E2 = S + dir * (l1 * Mathf.Cos(a)) + pole * (l1 * Mathf.Sin(a));
		AimBoneFrom(g.Upper, E - S, E2 - S);
		Vector3 Enew = _skel.GetBoneGlobalPose(g.Fore).Origin;
		Vector3 Wnow = _skel.GetBoneGlobalPose(g.Hand).Origin;
		AimBoneFrom(g.Fore, Wnow - Enew, S + dir * d - Enew);
		AimBone(g.Hand, (_skel.GlobalBasis.Inverse() * g.Wrap).Normalized(), g.W);
		if (g.W >= 1f && g == _grips[0]) LastGripError = (_skel.GlobalTransform * _skel.GetBoneGlobalPose(g.Hand).Origin).DistanceTo(g.Target);
	}

	private void AimBoneFrom(int bone, Vector3 from, Vector3 to)
	{
		Vector3 axis = from.Cross(to);
		float s = axis.Length();
		if (s < 1e-6f) return;
		TurnBone(bone, axis / s, Mathf.Atan2(s, from.Dot(to)));
	}

	/// <summary>The rags: each chain's bones swing toward where the clip holds them, pulled down by gravity, thrown by
	/// the body's movement (the chain's root's acceleration) and stirred by a little wind; damped, so they settle.</summary>
	private void Rags(float dt)
	{
		if (_rags.Count == 0 || dt <= 0f) return;
		var basisInv = _skel.GlobalBasis.Inverse();
		Vector3 down = (basisInv * Vector3.Down).Normalized();
		float scale = Mathf.Max(Size, 0.1f);
		foreach (var rag in _rags)
		{
			Vector3 root = _skel.GlobalTransform * _skel.GetBoneGlobalPose(rag.Bones[0]).Origin;
			Vector3 acc = Vector3.Zero;
			if (rag.Started)
			{
				Vector3 v = (root - rag.LastRoot) / dt;
				if (v.Length() > 20f * scale) v = rag.RootVel;   // a teleport, not a movement
				acc = (v - rag.RootVel) / dt;
				rag.RootVel = rag.RootVel.Lerp(v, 0.5f);
			}
			rag.LastRoot = root;
			// the effective pull (gravity less the root's acceleration), in the skeleton's space, per unit of size
			Vector3 pull = basisInv * (new Vector3(0, -9.8f, 0) - acc.LimitLength(30f) / scale * 0.6f);
			// the air: a slow drift, and gusts rolling through that lift the strips and let them fall (the owner,
			// 2026-10-03: the shreds of cloth moving around); each chain catches them a little after the last
			float ph = _t + rag.Bones[0] * 0.37f;
			float gust = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(ph * 0.45f) * 0.6f + Mathf.Sin(ph * 1.1f + 2f) * 0.4f), 2f);
			float wind = 1.2f + 3.6f * gust;
			pull += (basisInv * new Vector3(Mathf.Sin(ph * 0.9f + rag.Bones[0] * 1.3f), 0.25f * gust, Mathf.Cos(ph * 0.6f + rag.Bones[0]))) * wind;
			Vector3 pullDir = pull.Normalized();
			for (int j = 0; j < 3; j++)
			{
				int b = rag.Bones[j];
				if (b < 0) continue;
				Vector3 clipDir = _skel.GetBoneGlobalPose(b).Basis.Y.Normalized();
				// where it would hang: the clip's way, leaning toward the pull (more down the chain, and for the sleeves)
				float k = Mathf.Clamp(rag.Hang + 0.15f * j, 0f, 0.95f);
				Vector3 rest = clipDir + (down - clipDir) * k;
				Vector3 target = (rest + (pullDir - down) * (0.6f + 0.25f * j)).Normalized();
				// the torn ends flutter
				if (j == 2) target = (target + new Vector3(Mathf.Sin(_t * 7.3f + b), Mathf.Sin(_t * 5.1f + b * 2f), Mathf.Cos(_t * 6.7f + b)) * (0.05f + 0.12f * (wind - 1.2f) / 3.6f)).Normalized();
				if (!rag.Started) { rag.Dir[j] = target; rag.Vel[j] = Vector3.Zero; }
				float stiff = 20f - 5f * j, damp = 3.4f;
				rag.Vel[j] += (target - rag.Dir[j]) * stiff * dt;
				rag.Vel[j] *= Mathf.Exp(-damp * dt);
				rag.Dir[j] = (rag.Dir[j] + rag.Vel[j] * dt).Normalized();
				// never folded back up through the body: no more than 75 degrees off the clip's way
				if (rag.Dir[j].Dot(clipDir) < 0.26f) rag.Dir[j] = Turn(clipDir, rag.Dir[j], 0.75f);
				if (rag.Started) RagSwingDegrees = Mathf.Max(RagSwingDegrees * (1f - dt * 0.2f), Mathf.RadToDeg(rag.Dir[j].AngleTo(clipDir)));
				AimBone(b, rag.Dir[j]);
			}
			rag.Started = true;
		}
	}

	/// <summary>Eyeshine: the lantern lit (its flame, not the blacklight), the eyes within its reach, the head turned
	/// toward the light. Smoothed: never a flicker.</summary>
	private void Shine(float dt)
	{
		float want = 0f;
		var cam = GetViewport()?.GetCamera3D();
		var lantern = _lantern != null && IsInstanceValid(_lantern) ? _lantern : (_lantern = FindLantern());
		if (cam != null && lantern != null && lantern.IsOn && !lantern.Blacklight && lantern.Shining && _bHead >= 0)
		{
			Vector3 eyes = ModelEyesWorld();
			float dist = eyes.DistanceTo(cam.GlobalPosition);
			Vector3 face = (_skel.GlobalBasis * _skel.GetBoneGlobalPose(_bHead).Basis.Y).Normalized();
			float toward = face.Dot((cam.GlobalPosition - eyes).Normalized());
			float reach = Mathf.Clamp(1f - dist / ShineRange, 0f, 1f);
			want = Mathf.Sqrt(reach) * Mathf.SmoothStep(0.2f, 0.85f, toward) * ShineStrength;
		}
		Eyeshine = Mathf.MoveToward(Eyeshine, want, dt * 1.5f);
		Skin?.SetShaderParameter("eyeshine", Eyeshine);
	}
	private Player.Lantern _lantern;
	private double _lanternLookAt;
	/// <summary>How far the lantern reaches its eyes (metres), and how bright they answer at most.</summary>
	[Export] public float ShineRange = 28f;
	[Export] public float ShineStrength = 1.8f;

	private Player.Lantern FindLantern()
	{
		if (Time.GetTicksMsec() < _lanternLookAt) return null;
		_lanternLookAt = Time.GetTicksMsec() + 1000;
		var p = GetTree().GetFirstNodeInGroup("player");
		return p?.FindChildren("*", "Node3D", true, false) is { } kids
			? System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<Player.Lantern>(kids)) : null;
	}
}

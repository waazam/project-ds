using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// The chase out of the bunker (the owner, 2026-10-29: "the stalker can't enter a spotlight's beam: it slowly walks
/// around them, shielding its eyes"; "the stalker grabs the player from behind"). Two poses over the clips, on the
/// remodel only, both by the same two-bone reach the bark grips use:
/// - <see cref="ShieldEyes"/>: both forearms crossed up in front of its face, the head turned down and away from the
///   light, the chest cringing over;
/// - <see cref="HandsTo"/>: its hands sent to points in the world every frame (the grab: round the player's head from
///   behind, over their eyes).
/// </summary>
public partial class StalkerBody
{
	/// <summary>0..1: how far it shields its eyes from a light (eased in and out here).</summary>
	public float ShieldEyes { get; set; }
	/// <summary>How far the shield is up right now (tests).</summary>
	public float ShieldAmount => _shieldR?.W ?? 0f;

	private Grip _shieldR, _shieldL;
	private float _cringe;
	private Vector3? _handR, _handL;
	private Vector3 _wrapR, _wrapL;

	/// <summary>Sends its hands to world points (null: back to the clip), the fingers lying along <paramref name="wrapR"/>
	/// / <paramref name="wrapL"/>. Called every frame while it holds; overrides the shield.</summary>
	public void HandsTo(Vector3? right, Vector3? left, Vector3 wrapR, Vector3 wrapL)
	{
		_handR = right; _handL = left; _wrapR = wrapR; _wrapL = wrapL;
	}

	/// <summary>Where its right hand's wrist is now (tests, the grab's shots).</summary>
	public Vector3 HandWorld(string side) => _skel != null && Bone("hand_" + side) is int b && b >= 0 ? _skel.GlobalTransform * _skel.GetBoneGlobalPose(b).Origin : GlobalPosition;

	private void Chase(float dt)
	{
		if (_skel == null || _bHead < 0) return;
		_shieldR ??= MakeGrip("R", GlobalPosition, Vector3.Up, false);
		_shieldL ??= MakeGrip("L", GlobalPosition, Vector3.Up, false);
		if (_shieldR == null || _shieldL == null) return;
		_shieldR.Fitted = _shieldL.Fitted = true;   // the targets ride the head: never slid along a trunk
		bool hands = _handR.HasValue || _handL.HasValue;
		float want = hands ? 0f : Mathf.Clamp(ShieldEyes, 0f, 1f);
		_cringe = Mathf.MoveToward(_cringe, want, dt * 2.5f);
		Vector3 fwd = GlobalBasis.Z.Normalized();
		// its right (the side its right hand rests on)
		Vector3 right = (_skel.GlobalTransform.Basis * (_skel.GetBoneGlobalRest(Bone("hand_R")).Origin - _skel.GetBoneGlobalRest(Bone("hand_L")).Origin)) with { Y = 0 };
		right = right.LengthSquared() > 1e-6f ? right.Normalized() : -GlobalBasis.X.Normalized();
		if (_cringe > 0.001f)
		{
			// the cringe: the chest bowed over, the head ducked down and turned off to its left, away from the glare
			Vector3 side = _skel.GlobalBasis.Inverse() * right;
			Vector3 up = (_skel.GlobalBasis.Inverse() * Vector3.Up).Normalized();
			if (_bChest >= 0) TurnBone(_bChest, side, -0.28f * _cringe);
			TurnBone(_bHead, side, -0.45f * _cringe);
			TurnBone(_bHead, up, 0.5f * _cringe);
		}
		if (hands)
		{
			_shieldR.On = _handR.HasValue; _shieldL.On = _handL.HasValue;
			if (_handR is { } r) { _shieldR.Target = r; _shieldR.Wrap = _wrapR; }
			if (_handL is { } l) { _shieldL.Target = l; _shieldL.Wrap = _wrapL; }
		}
		else
		{
			_shieldR.On = _shieldL.On = want > 0.5f;
			// the forearms crossed up in front of the face: the right hand's back to the light over the eyes, the left
			// a hand's breadth higher and further out, its fingers splayed up across the brow
			Vector3 eyes = ModelEyesWorld();
			_shieldR.Target = eyes + fwd * 0.17f * Size - right * 0.03f * Size + Vector3.Up * 0.02f * Size;
			_shieldR.Wrap = (-right + Vector3.Up * 0.35f).Normalized();
			_shieldL.Target = eyes + fwd * 0.24f * Size + right * 0.05f * Size + Vector3.Up * 0.13f * Size;
			_shieldL.Wrap = (right + Vector3.Up * 0.6f).Normalized();
		}
		Reach(_shieldR, dt);
		Reach(_shieldL, dt);
	}
}

using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// When the HUD is wanted (2026-10-10c, the cinematic brief: "Minimize screen clutter to maintain filmic framing. Design a
/// contextual HUD that fades out completely during exploration"). With <see cref="GameSettings.ContextualHud"/> on (the
/// default), the compass and the inventory fade away while the player is on the move and come back softly when they're
/// wanted: standing still a moment (looking round, getting their bearings), a new objective, the compass going wrong near
/// the stairs (that's the story), a switch of item or a pickup. Off, they're always there.
/// </summary>
public static class HudPresence
{
	private static double _stillSince = -1, _now;
	private static Vector3 _lastPos;
	private static ulong _frame;

	public static bool Contextual => GameSettings.Instance?.ContextualHud ?? true;

	/// <summary>Seconds the player has stood still (0 moving). Updated once a frame by whichever HUD asks first.</summary>
	public static float StillFor(Node from, double delta)
	{
		ulong f = Engine.GetProcessFrames();
		if (f != _frame)
		{
			_frame = f;
			_now += delta;
			var p = from.GetTree().GetFirstNodeInGroup("player") as PlayerController;
			if (p != null)
			{
				bool moving = p.GroundSpeed > 0.35f || p.GlobalPosition.DistanceTo(_lastPos) > 0.5f;
				_lastPos = p.GlobalPosition;
				if (moving) _stillSince = -1;
				else if (_stillSince < 0) _stillSince = _now;
			}
		}
		return _stillSince < 0 ? 0f : (float)(_now - _stillSince);
	}

	/// <summary>Eases a HUD's alpha toward shown or hidden: in over 0.5 s, out over 1.4 s (never a pop).</summary>
	public static float Ease(float alpha, bool want, double delta)
		=> Mathf.MoveToward(alpha, want ? 1f : 0f, (float)delta / (want ? 0.5f : 1.4f));
}

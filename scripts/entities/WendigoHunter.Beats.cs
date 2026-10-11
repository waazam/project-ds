using Godot;
using ProjectDS.Audio;

namespace ProjectDS.Entities;

/// <summary>
/// What it does when it isn't after them (2026-10-11, the owner's list: "Give the wendigo idle behaviour. When it isn't
/// hunting, have it sniff the air, crouch over prints, test a wall with its claws, and freeze and tilt its head at sounds.
/// That would make it feel smart and keep it on screen longer without it attacking"). At the end of a stretch of its patrol,
/// or at each stop of a search, now and then it stops a few seconds for one of them:
/// <list type="bullet">
/// <item><b>sniff</b>: its head lifted to the air ahead, a long breath in;</item>
/// <item><b>the prints</b>: down into a crouch, its head to the snow at its feet;</item>
/// <item><b>the wall</b>: turned to the nearest wall of the tunnel, a slow rake of its claws down the snow;</item>
/// <item><b>listen</b>: stock still, its head cocked to one side.</item>
/// </list>
/// Seeing them or hearing them ends it at once (any change of mode does).
/// </summary>
public partial class WendigoHunter
{
	public enum Beat { None, Sniff, Prints, Wall, Listen }
	public Beat Doing { get; private set; }
	public int Beats { get; private set; }
	private float _beatT;
	private Vector3 _beatLook;

	/// <summary>Maybe a beat now (a stretch of its way done). <paramref name="chance"/> of one.</summary>
	private bool MaybeBeat(float chance)
	{
		if (_justBeat) { _justBeat = false; return false; }   // (one at a stop: then on)
		if (Body == null || _rng.Randf() > chance) return false;
		var kind = (Beat)_rng.RandiRange(1, 4);
		Doing = kind;
		Beats++;
		_beatT = kind switch { Beat.Sniff => 2.6f, Beat.Prints => 3.0f, Beat.Wall => 2.8f, _ => 2.0f };
		var head = Body.GlobalPosition + Vector3.Up * 2.8f * BodyScale;
		var fwd = _cave.GlobalBasis * Fwd.Normalized();
		switch (kind)
		{
			case Beat.Sniff:
				_beatLook = head + fwd * 2f + Vector3.Up * 1.6f;
				AudioDirector.OneShot(this, "wendigo_mumble", 4, head, -16f, "Voice", 4f, 0.1f);
				break;
			case Beat.Prints:
				_beatLook = Body.GlobalPosition + fwd * 1.2f;
				Body.Hold("crouch", 0.78f, 0.45);
				break;
			case Beat.Wall:
			{
				// the nearest wall: four rays round it at shoulder height
				var space = Body.GetWorld3D().DirectSpaceState;
				Vector3 best = fwd; float bd = 99f; Vector3 hitAt = head;
				for (int i = 0; i < 6; i++)
				{
					var d = new Basis(Vector3.Up, i * Mathf.Tau / 6f) * fwd;
					var q = PhysicsRayQueryParameters3D.Create(Body.GlobalPosition + Vector3.Up * 1.4f, Body.GlobalPosition + Vector3.Up * 1.4f + d * 2.6f, 1);
					var h = space.IntersectRay(q);
					if (h.Count > 0) { float dd = ((Vector3)h["position"]).DistanceTo(Body.GlobalPosition); if (dd < bd) { bd = dd; best = d; hitAt = (Vector3)h["position"]; } }
				}
				_beatLook = hitAt;
				_wallDir = _cave.GlobalBasis.Inverse() * best;
				Body.Play("smash", 2.2f, 0.4);
				AudioDirector.OneShot(this, "claw_rake", 2, hitAt, -8f, "Events", 5f, 0.08f);
				break;
			}
			default:
			{
				var side = fwd.Cross(Vector3.Up).Normalized() * (_rng.Randf() < 0.5f ? 1f : -1f);
				_beatLook = head + side * 3f + Vector3.Up * 0.4f;
				break;
			}
		}
		Body.LookAtWorld = _beatLook;
		Body.LookWeight = 1f;
		GD.Print($"[wendigo] stops: {kind}");
		return true;
	}

	private Vector3 _wallDir;
	private bool _justBeat;

	/// <summary>The beat going on: still (slowing to a stop), its head where it's attending, for its few seconds.</summary>
	private bool RunBeat(float dt)
	{
		if (Doing == Beat.None) return false;
		_beatT -= dt;
		_vel = _vel.MoveToward(Vector3.Zero, 5f * dt);
		Body.GlobalPosition = _cave.ToGlobal((Pos + _vel * dt) with { Y = 0f });
		if (Doing == Beat.Wall) Face(_wallDir, dt, 5f);
		Body.LookWeight = 1f;
		if (_beatT <= 0f) EndBeat();
		return true;
	}

	private void EndBeat()
	{
		if (Doing == Beat.None) return;
		Doing = Beat.None;
		_justBeat = true;
		if (Body != null) { Body.LookAtWorld = null; _gait = null; }
	}
}

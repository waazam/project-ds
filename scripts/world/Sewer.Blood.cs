using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-31, the owner: "Wet tunnels would be great, we should make use of the new blood system here and do some fun
/// scares with swapping out the water for blood in some sections, like after the red light green light hallway") Halfway
/// down the pipe the water turns. A gurgle far ahead in the dark, a long wet slide, and a low front of blood rolls up the
/// pipe toward them over the grey water, past their legs, and on behind them to the steps; the cistern beyond is blood
/// too. Then the pipe bleeds: runs creep down its walls, drops fall from its crown, and the wading gets heavier.
/// Once a visit (it's back to water on a load, to turn again).
/// </summary>
public partial class Sewer
{
	/// <summary>How far down the pipe (local z) they've come when it turns.</summary>
	private const float TideAt = 42f;
	/// <summary>Where the front stops, behind them (just short of the steps).</summary>
	private const float TideStop = 4.5f;
	public bool Tide { get; private set; }
	private float _front = 1e6f;

	private void ProcessBloodTide(PlayerController player, float dt)
	{
		if (_waterMat == null) return;
		if (!Tide)
		{
			if (Dropped || !Inside(player.GlobalPosition) || ToLocal(player.GlobalPosition).Z < TideAt) return;
			Tide = true;
			_front = RoomZ1 + 4f;
			_waterMat.SetShaderParameter("blood_dir", GlobalBasis.Z.Normalized());
			_waterMat.SetShaderParameter("blood_origin", GlobalPosition);
			Sfx("drain_gurgle", 2, ToGlobal(new Vector3(0, Water + 0.3f, TideAt + 30f)), 2f);
			GetTree().CreateTimer(0.9).Timeout += () => { if (IsInstanceValid(this)) Sfx("blood_slide", 8, ToGlobal(new Vector3(0, Water + 0.3f, TideAt + 22f)), 4f); };
			GD.Print("[story] Act 17: the sewer's water turns to blood, rolling up the pipe toward them");
		}
		if (_front > TideStop)
		{
			// quick out of the far dark, slowing as it comes: past their legs in a few seconds, then crawling on to the steps
			float pz = ToLocal(player.GlobalPosition).Z;
			float speed = _front > pz + 6f ? 14f : 2.4f;
			float was = _front;
			_front = Mathf.Max(TideStop, _front - speed * dt);
			_waterMat.SetShaderParameter("blood_front", _front);
			if (was > pz && _front <= pz)
			{
				Sfx("blood_suck", 2, player.GlobalPosition + Vector3.Up * 0.2f, -2f);
				UI.StyleFx.RedBeat(0.7f);
				Bleed();
			}
		}
	}

	/// <summary>The pipe bleeds once the blood's in it: runs down its walls, drops from its crown.</summary>
	private void Bleed()
	{
		var rng = new RandomNumberGenerator { Seed = 1717 };
		for (float z = 8f; z < TunnelLen - 2f; z += rng.RandfRange(5f, 9f))
		{
			float side = rng.Randf() < 0.5f ? -1f : 1f;
			// the wall at shoulder height (the pipe's curve: x = ±R·cos, the centre PipeCy up)
			float a = rng.RandfRange(0.15f, 0.45f);
			var top = new Vector3(side * PipeR * Mathf.Cos(a) * 0.97f, PipeCy + PipeR * Mathf.Sin(a), z);
			var n = new Vector3(-side * Mathf.Cos(a), -Mathf.Sin(a), 0).Normalized();
			Blood.Run(this, ToGlobal(top), GlobalBasis * n, rng.RandfRange(0.25f, 0.5f), rng.RandfRange(0.9f, 1.6f), rng.RandfRange(6f, 12f));
			if (rng.Randf() < 0.5f)
				FlowFx.Drips(this, new Vector3(rng.RandfRange(-0.4f, 0.4f), PipeCy + PipeR - 0.08f, z + rng.RandfRange(-1f, 1f)), FlowFx.Liquid.Blood, rng.RandfRange(0.6f, 1.6f), PipeCy + PipeR - Water, 0.03f, $"BloodDrip{z:0}");
		}
		WadeDrag = 0.42f;   // (it's thick)
	}

	private void Sfx(string name, int takes, Vector3 at, float db) => AudioDirector.OneShot(this, name, takes, at, db, "Events", 6f, 0.05f);
}

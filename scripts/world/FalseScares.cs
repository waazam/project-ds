using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// False scares (2026-10-31, the owner: "More false scares would be great throughout the game and ramped up in the ski
/// lodge"). Things that make them jump and are nothing: a lamp near them stutters out and back; a door somewhere behind
/// settles on its hinges; something gets knocked over in the next room; three soft knocks inside the wall beside them; dust
/// sifting down from right above; a heavy thud overhead, a clang far off; something small skittering across the floor
/// behind them; a breath at the back of the neck. Each is just that: nothing comes after it (nothing hears it either: they
/// aren't lures). One director to an area, only while they're inside it, never while a cutscene, a scare or a death has
/// them, and spaced so they never become wallpaper: a minute or two apart in the tunnels and the station, a good deal
/// thicker in the lodge (<see cref="Heat"/>).
/// </summary>
public partial class FalseScares : Node3D
{
	public enum Kind { LightStutter, DoorSettles, KnockedOver, WallKnocks, DustFalls, ThudAbove, Skitter, Breath, Boards }

	private Aabb _bounds;
	/// <summary>0..1: how thick they come (the lodge's 1, the tunnels' about a third).</summary>
	public float Heat = 0.35f;
	private readonly RandomNumberGenerator _rng = new();
	private double _now, _next, _check;
	private Kind? _last;
	public readonly List<Kind> Played = new();

	public static FalseScares Haunt(Node3D root, Aabb bounds, int seed, float heat)
	{
		if (root == null || !IsInstanceValid(root) || root.HasMeta("false_scares")) return null;
		root.SetMeta("false_scares", true);
		var f = new FalseScares { Name = "FalseScares", _bounds = bounds.Grow(1f), Heat = heat };
		f._rng.Seed = (ulong)seed;
		f._next = f.Gap() * 0.5;
		root.AddChild(f);
		return f;
	}

	private double Gap() => Mathf.Lerp(110f, 32f, Heat) * _rng.RandfRange(0.7f, 1.3f);

	public override void _Process(double delta)
	{
		_now += delta;
		if (_now < _next || (_check -= delta) > 0) return;
		_check = 1.0;
		var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (player == null || !_bounds.HasPoint(player.GlobalPosition)) return;
		if (!player.PlayerInput.Enabled || Systems.PlayerDeath.Dying || player.IsRunning) return;   // (not mid-chase, not in a beat)
		var cam = player.CameraRig?.Camera;
		if (cam == null) return;
		// pick one (not the same twice running)
		Kind k;
		int tries = 0;
		do { k = (Kind)_rng.RandiRange(0, (int)Kind.Boards); } while (k == _last && tries++ < 4);
		if (!Play(k, player, cam)) { _next = _now + 4; return; }
		_last = k;
		Played.Add(k);
		_next = _now + Gap();
		GD.Print($"[story] a false scare: {k} ({GetParent().Name})");
	}

	private bool Play(Kind k, PlayerController player, Camera3D cam)
	{
		Vector3 eye = cam.GlobalPosition, fwd = (-cam.GlobalBasis.Z with { Y = 0 }).Normalized(), right = fwd.Cross(Vector3.Up);
		Vector3 behind = player.GlobalPosition - fwd * _rng.RandfRange(4f, 8f) + right * _rng.RandfRange(-3f, 3f);
		Vector3 aside = player.GlobalPosition + right * (_rng.Randf() < 0.5f ? -1 : 1) * _rng.RandfRange(1.2f, 2.2f) + Vector3.Up * 1.2f;
		switch (k)
		{
			case Kind.LightStutter:
			{
				OmniLight3D best = null;
				float bd = 12f;
				foreach (var n in GetParent().FindChildren("*", "OmniLight3D", true, false))
					if (n is OmniLight3D l && l.Visible && l.LightEnergy > 0.15f && l.GlobalPosition.DistanceTo(eye) is var d && d < bd) { best = l; bd = d; }
				if (best == null) return false;
				Stutter(best);
				return true;
			}
			case Kind.DoorSettles:
				Sfx("hinge_creak", 6, behind + Vector3.Up * 1.2f, -8f);
				return true;
			case Kind.KnockedOver:
			{
				string[] what = { "can_clatter", "glass_clink", "wood_bump", "mop_clatter" };
				int[] takes = { 3, 5, 3, 1 };
				int i = _rng.RandiRange(0, what.Length - 1);
				Sfx(what[i], takes[i], behind + Vector3.Up * 0.3f, -6f);
				return true;
			}
			case Kind.WallKnocks:
			{
				var q = PhysicsRayQueryParameters3D.Create(eye, eye + right * (_rng.Randf() < 0.5f ? -1 : 1) * 4f, 1);
				var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
				if (hit.Count == 0) return false;
				var at = (Vector3)hit["position"];
				for (int n = 0; n < 3; n++)
				{
					int m = n;
					GetTree().CreateTimer(m * 0.45).Timeout += () => { if (IsInstanceValid(this)) Sfx("wall_knock", 3, at, -12f); };
				}
				return true;
			}
			case Kind.DustFalls:
			{
				var up = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye + fwd * 1.5f, eye + fwd * 1.5f + Vector3.Up * 6f, 1));
				if (up.Count == 0) return false;
				var at = (Vector3)up["position"] + Vector3.Down * 0.05f;
				Sfx("dust_trickle", 3, at, -6f);
				Dust(at);
				return true;
			}
			case Kind.ThudAbove:
				if (_rng.Randf() < 0.5f) Sfx("body_thump", 2, eye + Vector3.Up * 4f + fwd * _rng.RandfRange(-3f, 3f), -6f);
				else Sfx("far_clang", 2, behind + (behind - player.GlobalPosition).Normalized() * 12f, -4f);
				return true;
			case Kind.Skitter:
				Sfx("roach_skitter", 1, behind + Vector3.Up * 0.1f, -10f);
				return true;
			case Kind.Breath:
				Sfx("shadow_breath", 2, eye - fwd * 0.6f + Vector3.Up * 0.1f, -20f);
				return true;
			case Kind.Boards:
				// footsteps crossing the boards overhead: three or four, slow, then nothing
				int steps = _rng.RandiRange(3, 4);
				Vector3 from = eye + Vector3.Up * 3.5f - right * 2f, to = eye + Vector3.Up * 3.5f + right * 2f;
				for (int n = 0; n < steps; n++)
				{
					float u = n / (float)(steps - 1);
					var at = from.Lerp(to, u);
					GetTree().CreateTimer(n * 0.7).Timeout += () => { if (IsInstanceValid(this)) Sfx("haunt_boards", 3, at, -10f); };
				}
				return true;
		}
		return false;
	}

	/// <summary>A lamp stutters out, and comes back (sometimes a second time), with its buzz.</summary>
	private async void Stutter(OmniLight3D l)
	{
		float e = l.LightEnergy;
		Sfx("bulb_sputter", 3, l.GlobalPosition, -8f);
		double[] pattern = _rng.Randf() < 0.5f ? new[] { 0.06, 0.05, 0.12, 0.08, 0.5 } : new[] { 0.09, 0.04, 0.7 };
		bool on = false;
		foreach (var w in pattern)
		{
			if (!IsInstanceValid(l)) return;
			l.LightEnergy = on ? e : e * 0.04f;
			on = !on;
			await ToSignal(GetTree().CreateTimer(w), SceneTreeTimer.SignalName.Timeout);
		}
		if (IsInstanceValid(l)) l.LightEnergy = e;
	}

	/// <summary>A sift of dust down through the light from the ceiling just ahead.</summary>
	private void Dust(Vector3 at)
	{
		var p = new GpuParticles3D
		{
			Name = "DustSift", Amount = 60, Lifetime = 2.2, OneShot = true, Explosiveness = 0.6f, LocalCoords = false,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.35f, 0.02f, 0.35f),
				Direction = Vector3.Down, Spread = 15f, InitialVelocityMin = 0.1f, InitialVelocityMax = 0.4f, Gravity = new Vector3(0, -1.6f, 0),
				DampingMin = 0.5f, DampingMax = 1.2f, ScaleMin = 0.4f, ScaleMax = 1.1f,
			},
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One * 0.035f,
				Material = new StandardMaterial3D
				{
					AlbedoColor = new Color(0.6f, 0.56f, 0.5f, 0.55f), AlbedoTexture = SoftPuff.Texture, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(p);
		p.GlobalPosition = at;
		p.Emitting = true;
		GetTree().CreateTimer(3.0).Timeout += () => { if (IsInstanceValid(p)) p.QueueFree(); };
	}

	private void Sfx(string name, int takes, Vector3 at, float db) => AudioDirector.OneShot(this, name, takes, at, db, "Events", 3f, 0.08f);
}

using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The style pass (2026-10-22, the owner: "we need to bring more style to our game in general ... implement 3 stylized
/// things to our game to make it pop"). Three screen touches, all of a piece with the blood (<see cref="World.Blood"/>):
/// <list type="number">
/// <item><b>Blood on the lens</b> (<see cref="LensBlood"/>, lens_blood.gdshader): when blood flies close (a slam on the
/// catwalk beside them, the red rain) drops hit the camera, bend the picture through them, and run down it in fits
/// and starts, viscous, leaving thinning trails, then dry away.</item>
/// <item><b>The red beat</b> (<see cref="RedBeat"/>, red_beat.gdshader): a near miss or a hard scare drains the world to
/// grey for a heartbeat, all but its reds, and the edges close in with the heart's throb.</item>
/// <item><b>Dying</b> (<see cref="DeathDrips"/>, death_drips.gdshader): thick blood runs down the screen as it goes to
/// black, from the top, slowing as it goes.</item>
/// </list>
/// An autoload (it outlives the reload after a death). The lens and the beat sit under the PS2 post (it grains them with
/// the picture); the drips just under the fader's black. Off with <c>--no-style</c>.
/// </summary>
public partial class StyleFx : Node
{
	public static StyleFx Instance { get; private set; }
	/// <summary>For tests: how many times each has played.</summary>
	public static int LensHits, Beats, Deaths;
	public static float BeatAmount => Instance?._beat ?? 0f;
	public static int DropsOnLens => Instance?._drops.Count ?? 0;

	private ShaderMaterial _lens, _red, _drips, _smoke;
	private ColorRect _lensRect, _redRect, _dripRect, _smokeRect;
	/// <summary>For tests: the smoke in the eyes now (0..1).</summary>
	public static float SmokeAmount { get; private set; }
	private bool _off;

	private sealed class Drop { public Vector2 P; public float R, Alpha = 1f, Top, Wait, Speed, Age, Life; }
	private readonly List<Drop> _drops = new();
	private readonly RandomNumberGenerator _rng = new() { Seed = 2210 };
	private float _beat, _beatTarget, _beatT = -1f, _beatCool;
	private float _dripT = -1f;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_off = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-style") >= 0;
		(_smoke, _smokeRect) = Layer("SmokeEyes", 7, "res://assets/shaders/smoke_eyes.gdshader");
		(_lens, _lensRect) = Layer("Lens", 8, "res://assets/shaders/lens_blood.gdshader");
		(_red, _redRect) = Layer("RedBeat", 9, "res://assets/shaders/red_beat.gdshader");
		(_drips, _dripRect) = Layer("DeathDrips", 19, "res://assets/shaders/death_drips.gdshader");
	}

	private (ShaderMaterial, ColorRect) Layer(string name, int layer, string shader)
	{
		var cl = new CanvasLayer { Name = name, Layer = layer };
		AddChild(cl);
		var m = new ShaderMaterial { Shader = GD.Load<Shader>(shader) };
		var r = new ColorRect { Material = m, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Color = Colors.White };
		r.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		cl.AddChild(r);
		return (m, r);
	}

	// ------------------------------------------------------------------ the calls

	/// <summary>(2026-10-24, the cabin fire) Smoke in the eyes, 0..1: blurred, browned, the edges closing in. 0 clears it.</summary>
	public static void SmokeEyes(float amount)
	{
		var s = Instance;
		if (s == null || s._smoke == null) return;
		amount = s._off ? 0f : Mathf.Clamp(amount, 0f, 1f);
		SmokeAmount = amount;
		s._smokeRect.Visible = amount > 0.004f;
		s._smoke.SetShaderParameter("amount", amount);
		s._smoke.SetShaderParameter("time_s", (float)(Time.GetTicksMsec() / 1000.0));
	}

	/// <summary>Blood hits the lens: <paramref name="drops"/> drops, the bigger the nearer (<paramref name="strength"/> 0..1).</summary>
	public static void LensBlood(int drops, float strength = 1f)
	{
		var s = Instance;
		if (s == null || s._off || drops <= 0) return;
		LensHits++;
		for (int i = 0; i < drops && s._drops.Count < 24; i++)
		{
			var d = new Drop
			{
				P = new Vector2(s._rng.RandfRange(0.05f, 0.95f), s._rng.RandfRange(0.04f, 0.7f)),
				R = s._rng.RandfRange(0.008f, 0.03f) * (0.6f + 0.6f * strength),
				Wait = s._rng.RandfRange(0.2f, 1.2f), Speed = s._rng.RandfRange(0.015f, 0.06f), Life = s._rng.RandfRange(5f, 9f),
			};
			d.Top = d.P.Y;
			s._drops.Add(d);
		}
	}

	/// <summary>A heartbeat of red (<paramref name="strength"/> 0..1); not again for a few seconds.</summary>
	public static void RedBeat(float strength = 1f)
	{
		var s = Instance;
		if (s == null || s._off || s._beatCool > 0f) return;
		Beats++;
		s._beatTarget = Mathf.Clamp(strength, 0.3f, 1f);
		s._beatT = 0f;
		s._beatCool = 6f;
	}

	/// <summary>Dying: the blood runs down the screen.</summary>
	public static void DeathDrips()
	{
		var s = Instance;
		if (s == null || s._off) return;
		Deaths++;
		s._dripT = 0f;
	}

	/// <summary>Everything off at once (the reload after a death; a level change).</summary>
	public static void Clear()
	{
		var s = Instance;
		if (s == null) return;
		s._drops.Clear();
		s._dripT = -1f;
		s._beatT = -1f; s._beat = 0f;
	}

	// ------------------------------------------------------------------ running them

	public override void _Process(double delta)
	{
		float dt = Mathf.Min((float)delta, 0.05f);
		bool paused = GetTree().Paused;
		_beatCool -= dt;
		UpdateLens(paused ? 0f : dt);
		UpdateBeat(dt);
		UpdateDrips(dt);
	}

	private readonly Vector4[] _dropArr = new Vector4[24], _trailArr = new Vector4[24];
	private readonly float[] _trailAlpha = new float[24];

	private void UpdateLens(float dt)
	{
		for (int i = _drops.Count - 1; i >= 0; i--)
		{
			var d = _drops[i];
			d.Age += dt;
			// it clings, then gives and slides, then clings again (viscous: in fits and starts), slowing as it thins
			d.Wait -= dt;
			if (d.Wait <= 0f)
			{
				d.P.Y += d.Speed * dt * (1.5f + 3f * d.R / 0.03f);
				if (_rng.Randf() < dt * 0.8f) d.Wait = _rng.RandfRange(0.3f, 1.4f);
			}
			d.R = Mathf.Max(0.004f, d.R - dt * 0.0006f);
			d.Alpha = Mathf.Clamp((d.Life - d.Age) / 2.5f, 0f, 1f);
			if (d.Age > d.Life || d.P.Y > 1.1f) _drops.RemoveAt(i);
		}
		_lensRect.Visible = _drops.Count > 0;
		if (!_lensRect.Visible) return;
		for (int i = 0; i < 24; i++)
		{
			if (i < _drops.Count)
			{
				var d = _drops[i];
				_dropArr[i] = new Vector4(d.P.X, d.P.Y, d.R, d.Alpha);
				_trailArr[i] = new Vector4(d.P.X, d.Top, d.P.Y, d.R * 0.45f);
				_trailAlpha[i] = d.Alpha * 0.7f * Mathf.Clamp((d.P.Y - d.Top) / 0.02f, 0f, 1f);
			}
			else { _dropArr[i] = Vector4.Zero; _trailAlpha[i] = 0f; }
		}
		_lens.SetShaderParameter("drops", _dropArr);
		_lens.SetShaderParameter("trails", _trailArr);
		_lens.SetShaderParameter("trail_alpha", _trailAlpha);
		_lens.SetShaderParameter("count", _drops.Count);
	}

	private void UpdateBeat(float dt)
	{
		if (_beatT < 0f) { if (_redRect.Visible) _redRect.Visible = false; return; }
		_beatT += dt;
		// in fast (0.12 s), held through the heart's two beats, out slowly (1.2 s)
		float t = _beatT;
		float a = t < 0.12f ? t / 0.12f : t < 0.75f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.75f) / 1.2f);
		_beat = a * _beatTarget;
		float beat = Mathf.Exp(-Mathf.Pow((t - 0.15f) / 0.07f, 2f)) + 0.7f * Mathf.Exp(-Mathf.Pow((t - 0.5f) / 0.08f, 2f));
		_redRect.Visible = _beat > 0.001f;
		_red.SetShaderParameter("amount", _beat);
		_red.SetShaderParameter("pulse", Mathf.Clamp(beat, 0f, 1f) * _beatTarget);
		if (t > 2f) _beatT = -1f;
	}

	private void UpdateDrips(float dt)
	{
		if (_dripT < 0f) { if (_dripRect.Visible) _dripRect.Visible = false; return; }
		_dripT += dt;
		_dripRect.Visible = true;
		_drips.SetShaderParameter("t", _dripT);
		_drips.SetShaderParameter("amount", Mathf.Clamp(_dripT / 0.3f, 0f, 1f));
		if (_dripT > 12f) _dripT = -1f;
	}
}

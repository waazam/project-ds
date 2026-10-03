using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Entities;

/// <summary>
/// The last staircase's giant (the owner, 2026-10-03: "the giant should slowly raise itself up from the woods beyond
/// the stairs then reach out and grab you, then the screen blacks out ... We need the giant's movements to be more
/// horrific and unnatural feeling. He needs to grab the player with a janky haste that will freak us out").
///
/// 1. The rise: up out of the trees beyond the landing, head first, never smoothly: it holds, lurches up a few metres in
///    an instant, holds again, now and then sinks back a little before the next lurch, and once oozes up slowly where
///    nothing that size could move so smoothly. It comes up stooped and listing, its head hanging upside down on a
///    drawn-out neck; the spine straightens in jerks, the head snaps over a quarter turn at a time, a shoulder hitched, and the head
///    jolts now and then while the rest hangs still. Its eyes are dark; its head follows you all the way up.
/// 2. The stare: still. The eyes open red (eased, not a flash), the torn jaw hanging.
/// 3. The grab: stop-motion: its whole figure frozen between poses, the arm with its elbow bent the wrong way flung at
///    you in five juddering poses over half a second, the body lurching in after it, the hand filling the view. The
///    owner cuts to black on the last.
///
/// Shared by Act11Ending and the creature preview (<c>-- --giant-grab</c>).
/// </summary>
public static class GiantRise
{
	public sealed class Stage
	{
		/// <summary>The camera (world), every frame.</summary>
		public Func<Vector3> Eye;
		/// <summary>Level, from the player toward the woods beyond the landing.</summary>
		public Vector3 Forward = Vector3.Forward;
		/// <summary>The ground it rises from (world Y).</summary>
		public float GroundY;
		public float Distance = 40f;
		/// <summary>Time scale on every beat (the autotest's &lt; 1).</summary>
		public float Speed = 1f;
		/// <summary>Each frame: where the view should turn (its eyes, its hand).</summary>
		public Action<Vector3> Look;
		/// <summary>Each frame of the stare: how strong the trembling is (0..1).</summary>
		public Action<float> Tremble;
		/// <summary>The story's beats: "rising", "risen", "stare", "grab", "grabbed".</summary>
		public Action<string> Beat;
		/// <summary>Plays a sound at a world point (path, point, volume dB, pitch).</summary>
		public Action<string, Vector3, float, float> Sound;
		/// <summary>The rise's length (seconds at Speed 1).</summary>
		public float RiseSeconds = 11f;
		public float StareSeconds = 2.2f;
	}

	/// <summary>How big it must be for its eyes to stand <paramref name="lookUpDeg"/> above the eye at
	/// <paramref name="distance"/> from ground at <paramref name="groundY"/> (design: the eyes ~1.93 x Size up).</summary>
	public static float SizeFor(float eyeY, float groundY, float distance, float lookUpDeg = 38f)
		=> Mathf.Clamp((eyeY + Mathf.Tan(Mathf.DegToRad(lookUpDeg)) * distance - groundY) / 1.93f, 18f, 48f);

	public static async Task Run(Node host, StalkerBody body, ShaderMaterial skin, Stage st, CancellationToken ct)
	{
		var rng = new RandomNumberGenerator { Seed = 1129 };
		float S = body.Size, sp = Mathf.Max(st.Speed, 0.05f);
		Vector3 eye0 = st.Eye();
		Vector3 fwd = new Vector3(st.Forward.X, 0f, st.Forward.Z).Normalized();
		Vector3 spot = new Vector3(eye0.X, 0f, eye0.Z) + fwd * st.Distance;
		float yUp = st.GroundY - 0.12f * S, yDown = st.GroundY - 2.15f * S;   // standing in the undergrowth .. wholly under it
		body.GlobalPosition = new Vector3(spot.X, yDown, spot.Z);
		Vector3 toP = eye0 - body.GlobalPosition;
		body.Rotation = new Vector3(0f, Mathf.Atan2(toP.X, toP.Z), 0f);
		body.TrackTarget = eye0;
		body.Watched = true;   // (no drumming fingers; the pursuer's stare rules: the head tipping, the slit opening)
		body.SpineBow = 0.5f;
		body.HeadRoll = 2.6f;
		body.NeckStretch = 0.7f;
		skin.SetShaderParameter("eye_glow", 0f);

		// ---- 1. the rise: a schedule of holds and lurches over its progress (0 under the trees .. 1 standing)
		var keys = new List<(float t, float p)> { (0f, 0f) };
		float t = 0f, p = 0f;
		bool oozed = false;
		while (p < 1f)
		{
			t += rng.RandfRange(0.35f, 1.0f);   // hold
			keys.Add((t, p));
			if (p > 0.15f && rng.Randf() < 0.25f)   // sinking back a little first
			{
				t += 0.12f; p -= rng.RandfRange(0.015f, 0.035f);
				keys.Add((t, p));
				t += 0.25f; keys.Add((t, p));
			}
			if (!oozed && p > 0.42f)
			{
				// once: oozing up, slow and smooth, where nothing that size moves smoothly
				oozed = true;
				t += 2.2f; p = Mathf.Min(1f, p + 0.16f);
			}
			else
			{
				t += rng.RandfRange(0.05f, 0.12f);   // the lurch: an instant
				p = Mathf.Min(1f, p + rng.RandfRange(0.07f, 0.15f));
			}
			keys.Add((t, p));
		}
		float scale = st.RiseSeconds / Mathf.Max(t, 0.1f) * sp;
		float Progress(float time)
		{
			for (int i = 1; i < keys.Count; i++)
				if (time <= keys[i].t * scale)
				{
					float a = keys[i - 1].t * scale, b = keys[i].t * scale;
					float u = b > a ? (time - a) / (b - a) : 1f;
					return Mathf.Lerp(keys[i - 1].p, keys[i].p, u);
				}
			return 1f;
		}
		st.Beat?.Invoke("rising");
		st.Sound?.Invoke("res://assets/audio/sfx/creature_giant_moan_02.wav", body.GlobalPosition + Vector3.Up * S, -4f, 0.62f);
		float total = keys[^1].t * scale, clock = 0f, lastP = 0f, joltTimer = 0.6f;
		while (clock < total)
		{
			await Cutscene.Frame(host, ct);
			float dt = (float)host.GetProcessDeltaTime();
			clock += dt;
			float pr = Progress(clock);
			// a lurch is heard: the creak of something that size bending
			if (pr - lastP > 0.02f && rng.Randf() < 0.5f)
				st.Sound?.Invoke($"res://assets/audio/sfx/trunk_creak_0{rng.RandiRange(1, 3)}.wav", body.EyesWorld, 2f, rng.RandfRange(0.35f, 0.5f));
			lastP = pr;
			body.GlobalPosition = new Vector3(spot.X, Mathf.Lerp(yDown, yUp, pr), spot.Z);
			// bent double, unbending in steps; the head upside down, snapping over a quarter at a time; a shoulder hitched
			body.SpineBow = Mathf.Lerp(0.5f, 0.12f, Mathf.Clamp((pr - 0.35f) / 0.6f, 0f, 1f));
			body.HeadRoll = pr < 0.5f ? 2.6f : pr < 0.72f ? 1.55f : pr < 0.9f ? 0.95f : 0.5f;
			body.NeckStretch = Mathf.Lerp(0.7f, 0.45f, pr);
			// listing over to one side, and back the other way at a lurch
			body.ChestRoll = 0.34f * Mathf.Sin(Mathf.Floor(pr * 9f) * 2.1f);
			// the head jolts while the rest hangs still: one frame out of place, now and then
			joltTimer -= dt;
			if (joltTimer <= 0f)
			{
				body.HeadJolt = new Vector3(rng.RandfRange(-0.25f, 0.25f), rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(-0.35f, 0.35f));
				joltTimer = rng.RandfRange(0.5f, 1.4f) * sp;
			}
			else body.HeadJolt = Vector3.Zero;
			body.TrackTarget = st.Eye();
			st.Look?.Invoke(body.EyesWorld);
		}
		body.HeadJolt = Vector3.Zero;
		st.Beat?.Invoke("risen");

		// ---- 2. the stare: still; the eyes open (eased), the jaw hanging
		st.Beat?.Invoke("stare");
		float stare = st.StareSeconds * sp;
		body.GlowEyes(new Color(1f, 0.06f, 0.02f), 7f);
		clock = 0f;
		while (clock < stare)
		{
			await Cutscene.Frame(host, ct);
			clock += (float)host.GetProcessDeltaTime();
			float u = Mathf.Clamp(clock / stare, 0f, 1f);
			skin.SetShaderParameter("eye_glow", Mathf.SmoothStep(0f, 1f, Mathf.Clamp(clock / (0.6f * sp), 0f, 1f)) * 7.5f);
			body.TrackTarget = st.Eye();
			st.Look?.Invoke(body.EyesWorld);
			st.Tremble?.Invoke(u);
		}

		// ---- 3. the grab: stop-motion; the arm flung at you in juddering poses, the body lurching in behind it
		st.Beat?.Invoke("grab");
		Vector3 eye = st.Eye();
		const string side = "R";
		Vector3 shoulder0 = body.BoneWorld("upper_" + side), hand0 = body.BoneWorld("hand_" + side);
		Vector3 grabAt = eye + (shoulder0 - eye).Normalized() * 0.3f * S;   // the wrist a hand's length off: its fingers close round the view
		float reach = body.ArmLength(side) * 0.9f;
		Vector3 origin0 = body.GlobalPosition;
		Vector3 want = grabAt + (shoulder0 - grabAt).Normalized() * reach;   // where its shoulder has to be
		Vector3 lunge = want - shoulder0;
		float[] at = { 0f, 0.1f, 0.22f, 0.31f, 0.46f, 0.55f };
		float[] reachF = { 0.12f, 0.38f, 0.6f, 0.84f, 1f };
		float[] lungeF = { 0.15f, 0.42f, 0.68f, 0.9f, 1f };
		body.Hold = true;
		st.Sound?.Invoke("res://assets/audio/sfx/creature_jumpscare_02.wav", eye + (shoulder0 - eye).Normalized() * 4f, 6f, 0.9f);
		clock = 0f;
		int k = 0;
		while (k < reachF.Length)
		{
			await Cutscene.Frame(host, ct);
			clock += (float)host.GetProcessDeltaTime();
			if (clock < at[k + 1] * sp * 1.6f) { st.Look?.Invoke(body.EyesWorld.Lerp(body.BoneWorld("hand_" + side), 0.5f)); continue; }
			body.GlobalPosition = origin0 + lunge * lungeF[k];
			Vector3 hand = hand0.Lerp(grabAt, reachF[k]) + lunge * lungeF[k] * (1f - reachF[k]);
			body.Grab(hand, (grabAt - shoulder0).Normalized(), side);
			body.SpineBow = Mathf.Lerp(0.18f, 0.7f, reachF[k]);
			body.HeadRoll = 0.5f + (k % 2 == 0 ? 0.35f : -0.25f);
			body.HeadJolt = new Vector3(rng.RandfRange(-0.2f, 0.2f), rng.RandfRange(-0.2f, 0.2f), rng.RandfRange(-0.3f, 0.3f));
			body.TrackTarget = eye;
			body.Step();
			if (k == reachF.Length - 1) st.Beat?.Invoke("grabbed");
			k++;
		}
		// the last pose shown for a beat (a hair of a second), then the owner cuts
		await Cutscene.Frame(host, ct);
		await Cutscene.Wait(host, 0.12 * sp, ct);
	}
}

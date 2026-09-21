using System.Collections.Generic;
using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// Owns the forest's "aliveness". Each frame it asks every SilenceZone how
/// silent the listener's position is, smooths that into one Silence value
/// (0 = living forest, 1 = dead quiet), and fades the nature buses in a
/// staggered order: birds go first, then distant life and insects, and the
/// wind goes last. It also dulls the whole Nature bus (low-pass), so whatever
/// is left sounds far away.
///
/// Anything routed to a category bus reacts automatically, including 3D
/// sources such as a stream. Other systems read Silence / CategoryGain; they
/// never set these bus volumes themselves.
///
/// Scripted silence: <see cref="RequestSilence"/> / <see cref="ReleaseSilence"/>. Each
/// request has an owner and a priority; the highest-priority request wins (ties: the
/// deepest silence), so the storm and a set piece can overlap without clobbering
/// each other: when one releases, the other's request still stands.
///
/// Indoors: <see cref="SetIndoor"/> (or an <see cref="IndoorZone"/>) muffles and ducks
/// the Nature bed and the Weather bus, as heard through walls. Also owner-based.
/// </summary>
public partial class ForestAmbienceManager : Node
{
	public static ForestAmbienceManager Instance { get; private set; }

	public enum Category { Birds, Insects, Wind, Distant, Water }

	// Fade window per category, in silence units: x = starts fading, y = fully faded.
	[Export] public Vector2 BirdsFade = new(0.02f, 0.42f);
	[Export] public Vector2 DistantFade = new(0.08f, 0.55f);
	[Export] public Vector2 InsectsFade = new(0.22f, 0.72f);
	[Export] public Vector2 WaterFade = new(0.30f, 0.85f);
	[Export] public Vector2 WindFade = new(0.40f, 0.95f);
	/// <summary>Level a category rests at when fully faded. Wind never quite dies.</summary>
	[Export] public float WindFloorDb = -46f;
	[Export] public float NatureCutoffLiving = 20000f;
	[Export] public float NatureCutoffSilent = 1400f;
	/// <summary>Seconds for the smoothed silence to cover ~63% of a change.</summary>
	[Export] public float SmoothingTime = 1.6f;

	[ExportGroup("Indoors")]
	/// <summary>Low-pass on the Nature and Weather buses when fully indoors.</summary>
	[Export] public float IndoorCutoffHz = 700f;
	[Export] public float IndoorNatureDuckDb = -9f;
	[Export] public float IndoorWeatherDuckDb = -7f;
	/// <summary>Seconds for the indoor muffling to cover ~63% of a change.</summary>
	[Export] public float IndoorSmoothingTime = 0.35f;

	/// <summary>
	/// Legacy single-slot override (negative = off). Kept for older callers: it is a
	/// lowest-priority request owned by the manager itself. New code uses <see cref="RequestSilence"/>.
	/// </summary>
	public float SilenceOverride
	{
		get => EffectiveOverride(out float v) ? v : -1f;
		set { if (value >= 0f) RequestSilence(_legacyOwner, value, int.MinValue); else ReleaseSilence(_legacyOwner); }
	}

	public float Silence { get; private set; }
	/// <summary>Current startle level (a gunshot, a crash): a temporary hush on top of the zones.</summary>
	public float StartleLevel { get; private set; }
	public float TargetSilence { get; private set; }
	/// <summary>0 = outdoors .. 1 = fully indoors (smoothed).</summary>
	public float Indoor { get; private set; }
	public bool IsIndoor => _indoorOwners.Count > 0;

	private readonly object _legacyOwner = new();
	private readonly object _defaultIndoorOwner = new();
	private readonly Dictionary<object, (float level, int priority)> _silenceRequests = new();
	private readonly HashSet<object> _indoorOwners = new();

	private Node3D _listener;
	private AudioEffectLowPassFilter _natureLowPass;
	private AudioEffectLowPassFilter _weatherLowPass;
	private int _natureBus = -1, _weatherBus = -1;
	// Bus base levels from the layout, read once per run (a scene change can happen mid-duck).
	private static float? _natureBaseDbStatic, _weatherBaseDbStatic;
	private float _natureBaseDb, _weatherBaseDb;
	private readonly int[] _categoryBus = new int[5];
	private readonly float[] _gains = new float[5];
	private float _startleHold;
	private float _startleFall;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		_natureBus = AudioServer.GetBusIndex("Nature");
		if (_natureBus < 0) { GD.PushError("ForestAmbienceManager: bus layout missing 'Nature'"); return; }
		_natureBaseDb = _natureBaseDbStatic ??= AudioServer.GetBusVolumeDb(_natureBus);
		_natureLowPass = FindLowPass(_natureBus);
		_weatherBus = AudioServer.GetBusIndex("Weather");
		if (_weatherBus >= 0)
		{
			_weatherBaseDb = _weatherBaseDbStatic ??= AudioServer.GetBusVolumeDb(_weatherBus);
			_weatherLowPass = FindLowPass(_weatherBus);
		}
		string[] names = { "Birds", "Insects", "Wind", "Distant", "Water" };
		for (int i = 0; i < names.Length; i++) _categoryBus[i] = AudioServer.GetBusIndex(names[i]);
	}

	private static AudioEffectLowPassFilter FindLowPass(int bus)
	{
		for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
			if (AudioServer.GetBusEffect(bus, i) is AudioEffectLowPassFilter lp) return lp;
		return null;
	}

	public float CategoryGain(Category c) => _gains[(int)c];

	/// <summary>
	/// Forces the forest to exactly <paramref name="level"/> silence while held (replaces the
	/// zones). The highest <paramref name="priority"/> among live requests wins. Re-requesting with the same
	/// owner updates it.
	/// </summary>
	public void RequestSilence(object owner, float level, int priority = 0)
		=> _silenceRequests[owner] = (Mathf.Clamp(level, 0f, 1f), priority);

	public void ReleaseSilence(object owner) => _silenceRequests.Remove(owner);

	/// <summary>Marks the listener as indoors (or not) on behalf of <paramref name="owner"/>; indoors while any owner says so.</summary>
	public void SetIndoor(object owner, bool indoor)
	{
		if (indoor) _indoorOwners.Add(owner);
		else _indoorOwners.Remove(owner);
	}

	/// <summary>Single-owner convenience for scripts that only ever toggle one interior.</summary>
	public void SetIndoor(bool indoor) => SetIndoor(_defaultIndoorOwner, indoor);

	private bool EffectiveOverride(out float level)
	{
		level = -1f;
		int best = int.MinValue; bool any = false;
		foreach (var (lvl, prio) in _silenceRequests.Values)
		{
			if (!any || prio > best || (prio == best && lvl > level)) { best = prio; level = lvl; any = true; }
		}
		return any;
	}

	/// <summary>
	/// Something loud just happened: hush the forest to at least <paramref name="strength"/>,
	/// hold it for a while, then let life creep back over the rest of <paramref name="seconds"/>.
	/// </summary>
	public void Startle(float strength, float seconds)
	{
		StartleLevel = Mathf.Max(StartleLevel, strength);
		_startleHold = seconds * 0.4f;
		_startleFall = StartleLevel / Mathf.Max(seconds * 0.6f, 0.1f);
	}

	public override void _Process(double delta)
	{
		if (_listener == null || !IsInstanceValid(_listener))
			_listener = GetTree().GetFirstNodeInGroup("player") as Node3D;
		float dt = (float)delta;
		if (_startleHold > 0f) _startleHold -= dt;
		else StartleLevel = Mathf.Max(0f, StartleLevel - _startleFall * dt);
		TargetSilence = EffectiveOverride(out float forced) ? forced : Mathf.Max(SampleZones(), StartleLevel);
		float k = 1f - Mathf.Exp(-dt / Mathf.Max(SmoothingTime, 0.01f));
		Silence = Mathf.Lerp(Silence, TargetSilence, k);
		float ki = 1f - Mathf.Exp(-dt / Mathf.Max(IndoorSmoothingTime, 0.01f));
		Indoor = Mathf.Lerp(Indoor, IsIndoor ? 1f : 0f, ki);
		Apply();
	}

	private float SampleZones()
	{
		if (_listener == null) return 0f;
		float s = 0f;
		Vector3 at = _listener.GlobalPosition;
		foreach (var zone in SilenceZone.All) s = Mathf.Max(s, zone.SilenceAt(at));
		return s;
	}

	private void Apply()
	{
		if (_natureBus < 0) return;
		SetCategory(Category.Birds, BirdsFade, -80f);
		SetCategory(Category.Distant, DistantFade, -80f);
		SetCategory(Category.Insects, InsectsFade, -80f);
		SetCategory(Category.Water, WaterFade, -80f);
		SetCategory(Category.Wind, WindFade, WindFloorDb);

		// Exponential sweeps sound even to the ear.
		float indoorCutoff = NatureCutoffLiving * Mathf.Pow(IndoorCutoffHz / NatureCutoffLiving, Indoor);
		if (_natureLowPass != null)
		{
			float t = Mathf.SmoothStep(0.25f, 1f, Silence);
			float silenceCutoff = NatureCutoffLiving * Mathf.Pow(NatureCutoffSilent / NatureCutoffLiving, t);
			_natureLowPass.CutoffHz = Mathf.Min(silenceCutoff, indoorCutoff);
		}
		AudioServer.SetBusVolumeDb(_natureBus, _natureBaseDb + IndoorNatureDuckDb * Indoor);
		if (_weatherBus >= 0)
		{
			if (_weatherLowPass != null) _weatherLowPass.CutoffHz = indoorCutoff;
			AudioServer.SetBusVolumeDb(_weatherBus, _weatherBaseDb + IndoorWeatherDuckDb * Indoor);
		}
	}

	private void SetCategory(Category c, Vector2 fade, float floorDb)
	{
		float gain = 1f - Mathf.SmoothStep(fade.X, fade.Y, Silence);
		_gains[(int)c] = gain;
		float floorLinear = Mathf.DbToLinear(floorDb);
		float db = Mathf.LinearToDb(Mathf.Max(floorLinear + (1f - floorLinear) * gain, 0.0001f));
		int idx = _categoryBus[(int)c];
		if (idx >= 0) AudioServer.SetBusVolumeDb(idx, db);
	}
}

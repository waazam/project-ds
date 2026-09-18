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
	[Export] public float WindFloorDb = -34f;
	[Export] public float NatureCutoffLiving = 20000f;
	[Export] public float NatureCutoffSilent = 1400f;
	/// <summary>Seconds for the smoothed silence to cover ~63% of a change.</summary>
	[Export] public float SmoothingTime = 1.6f;

	/// <summary>Scripted override (events, cutscenes). Negative = off.</summary>
	public float SilenceOverride = -1f;

	public float Silence { get; private set; }
	public float TargetSilence { get; private set; }

	private Node3D _listener;
	private AudioEffectLowPassFilter _natureLowPass;
	private readonly float[] _gains = new float[5];

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		int nature = AudioServer.GetBusIndex("Nature");
		if (nature < 0) { GD.PushError("ForestAmbienceManager: bus layout missing 'Nature'"); return; }
		for (int i = 0; i < AudioServer.GetBusEffectCount(nature); i++)
			if (AudioServer.GetBusEffect(nature, i) is AudioEffectLowPassFilter lp) _natureLowPass = lp;
	}

	public float CategoryGain(Category c) => _gains[(int)c];

	public override void _Process(double delta)
	{
		_listener ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
		TargetSilence = SilenceOverride >= 0f ? SilenceOverride : SampleZones();
		float k = 1f - Mathf.Exp(-(float)delta / Mathf.Max(SmoothingTime, 0.01f));
		Silence = Mathf.Lerp(Silence, TargetSilence, k);
		Apply();
	}

	private float SampleZones()
	{
		if (_listener == null) return 0f;
		float s = 0f;
		foreach (var node in GetTree().GetNodesInGroup("silence_zones"))
			if (node is SilenceZone zone) s = Mathf.Max(s, zone.SilenceAt(_listener.GlobalPosition));
		return s;
	}

	private void Apply()
	{
		SetCategory(Category.Birds, "Birds", BirdsFade, -80f);
		SetCategory(Category.Distant, "Distant", DistantFade, -80f);
		SetCategory(Category.Insects, "Insects", InsectsFade, -80f);
		SetCategory(Category.Water, "Water", WaterFade, -80f);
		SetCategory(Category.Wind, "Wind", WindFade, WindFloorDb);

		if (_natureLowPass != null)
		{
			// Exponential sweep sounds even to the ear.
			float t = Mathf.SmoothStep(0.25f, 1f, Silence);
			_natureLowPass.CutoffHz = NatureCutoffLiving * Mathf.Pow(NatureCutoffSilent / NatureCutoffLiving, t);
		}
	}

	private void SetCategory(Category c, string bus, Vector2 fade, float floorDb)
	{
		float gain = 1f - Mathf.SmoothStep(fade.X, fade.Y, Silence);
		_gains[(int)c] = gain;
		float floorLinear = Mathf.DbToLinear(floorDb);
		float db = Mathf.LinearToDb(Mathf.Max(floorLinear + (1f - floorLinear) * gain, 0.0001f));
		int idx = AudioServer.GetBusIndex(bus);
		if (idx >= 0) AudioServer.SetBusVolumeDb(idx, db);
	}
}

using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>The murky under-the-surface layer (one per level, made on first use). Also muffles
/// everything with a low-pass on the master bus while submerged; it takes the filter off again when
/// it surfaces or leaves the tree (a reload mid-drowning).</summary>
public partial class UnderwaterView : CanvasLayer
{
	private ShaderMaterial _mat;
	private ColorRect _rect;
	private int _fx = -1;
	private AudioEffectLowPassFilter _lp;
	public float Amount { get; private set; }

	public static UnderwaterView For(Node n)
	{
		var root = Cutscene.SceneRoot(n);
		if (root.GetNodeOrNull<UnderwaterView>("Underwater") is { } u) return u;
		u = new UnderwaterView { Name = "Underwater", Layer = 9 };
		root.AddChild(u);
		return u;
	}

	public override void _Ready()
	{
		_mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/underwater.gdshader") };
		_rect = new ColorRect { Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_rect);
	}

	/// <summary>0 = in the air, 1 = fully under. <paramref name="color"/> is the water's own colour
	/// (peaty lake green, or blood).</summary>
	public void Set(float amount, Color color, float darkness = 0f)
	{
		Amount = amount;
		_rect.Visible = amount > 0.001f || darkness > 0.001f;
		_mat.SetShaderParameter("amount", amount);
		_mat.SetShaderParameter("water_color", new Vector3(color.R, color.G, color.B));
		_mat.SetShaderParameter("darkness", darkness);
		Muffle(amount > 0.3f);
	}

	private void Muffle(bool on)
	{
		int master = AudioServer.GetBusIndex("Master");
		if (on && _fx < 0)
		{
			_lp = new AudioEffectLowPassFilter { CutoffHz = 700f };
			AudioServer.AddBusEffect(master, _lp);
			_fx = AudioServer.GetBusEffectCount(master) - 1;
		}
		else if (!on && _fx >= 0) RemoveMuffle();
	}

	private void RemoveMuffle()
	{
		int master = AudioServer.GetBusIndex("Master");
		for (int i = AudioServer.GetBusEffectCount(master) - 1; i >= 0; i--)
			if (AudioServer.GetBusEffect(master, i) == _lp) { AudioServer.RemoveBusEffect(master, i); break; }
		_fx = -1;
		_lp = null;
	}

	public override void _ExitTree()
	{
		if (_fx >= 0) RemoveMuffle();
	}
}

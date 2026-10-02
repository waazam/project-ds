using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// A small thing in a room the player can pick up and look at (the detail pass, 2026-10-01: the clutter, made
/// interactable): a bottle, a book, a mug. E lifts it: its sound (glass, pages, china) and a line of what they
/// see; again, the next line (the last one repeats). Nothing is taken: it's put back.
/// </summary>
public partial class Curio : Interactable
{
	public enum Kind { Glass, Book, Ceramic }

	private string[] _lines;
	private Kind _kind;
	private int _next;
	private AudioStreamPlayer3D _sound;
	/// <summary>For tests: how many times anything has been looked at.</summary>
	public static int Examined { get; private set; }
	public int Looks => _next;

	/// <summary>Adds one at <paramref name="local"/> (the parent's space), its prompt, its lines and its sound.</summary>
	public static Curio Add(Node3D parent, Vector3 local, string prompt, Kind kind, params string[] lines)
	{
		var c = new Curio { Name = "Curio_" + prompt.Replace(' ', '_'), Prompt = prompt, _kind = kind, _lines = lines, PickRadius = 0.16f, MaxDistance = 2.2f, Position = local };
		parent.AddChild(c);
		return c;
	}

	public override void _Ready()
	{
		base._Ready();
		_sound = new AudioStreamPlayer3D { Name = "Sound", Bus = "Events", VolumeDb = -8f, UnitSize = 1.5f, MaxDistance = 12f };
		AddChild(_sound);
	}

	public override void Interact(PlayerController player)
	{
		base.Interact(player);
		string set = _kind switch { Kind.Glass => "glass_clink", Kind.Book => "book_handle", _ => "ceramic_set" };
		int count = _kind == Kind.Glass ? 5 : 3;
		string path = $"res://assets/audio/sfx/{set}_{GD.RandRange(1, count):00}.wav";
		if (ResourceLoader.Exists(path))
		{
			_sound.Stream = GD.Load<AudioStream>(path);
			_sound.PitchScale = (float)GD.RandRange(0.95, 1.05);
			_sound.Play();
		}
		if (_lines.Length > 0)
			_ = Subtitle.Instance?.Show(_lines[Mathf.Min(_next, _lines.Length - 1)], 0.3f, 2.8f, 0.7f);
		_next++;
		Examined++;
	}
}

using System;
using Godot;
using ProjectDS.Player;
using ProjectDS.UI;

namespace ProjectDS.Systems;

/// <summary>
/// A note, page, card or board the player can read with E. Add it as a child of
/// the paper's mesh (or use <see cref="World.PaperKit"/> to build the paper and
/// this together); pressing E opens the text in <see cref="NoteOverlay"/>, and
/// E or Esc puts it down again. The world keeps running underneath: reading
/// never pauses the game and never takes the player's control away, it just
/// makes their input idle while the page is up (<see cref="PlayerInput.Modal"/>).
///
/// Text is shown verbatim, so write the lines exactly as the paper would carry
/// them. <see cref="Style"/> picks the face: handwritten notes in the italic
/// serif, typed logs in the monospace, printed cards and signs in the plain serif.
///
/// If <see cref="ReadFlag"/> is set, that story flag is set the first time the
/// player reads it (saved; other systems and the autotest can key off it).
/// </summary>
[GlobalClass]
public partial class Readable : Interactable
{
	public enum NoteStyle { Handwritten, Typed, Printed }

	/// <summary>Small heading above the text ("Trail register"). Empty for none.</summary>
	[Export] public string Title = "";
	[Export(PropertyHint.MultilineText)] public string Text = "";
	[Export] public NoteStyle Style = NoteStyle.Handwritten;
	/// <summary>Optional StoryManager flag set on first read (e.g. "read_door_note").</summary>
	[Export] public string ReadFlag = "";

	/// <summary>Raised when the player opens it (every time).</summary>
	public event Action<PlayerController> Read;

	public bool HasBeenRead => !string.IsNullOrEmpty(ReadFlag) && (StoryManager.Instance?.HasFlag(ReadFlag) ?? false);

	public override void _Ready()
	{
		if (Prompt == "Interact") Prompt = "Read";
		base._Ready();
	}

	public override void Interact(PlayerController player)
	{
		if (!string.IsNullOrEmpty(ReadFlag) && StoryManager.Instance is { } s && !s.HasFlag(ReadFlag)) s.SetFlag(ReadFlag);
		NoteOverlay.Instance?.Open(this, player);
		Read?.Invoke(player);
		base.Interact(player);
	}
}

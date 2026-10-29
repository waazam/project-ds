using System.Collections.Generic;
using System.Linq;

namespace ProjectDS.Systems;

/// <summary>
/// (2026-10-29, the playtest: "a small journal listing the shots the story needs in Acts 1-3 ... each ticked with its
/// print when taken") The photographer's shot list for the opening, kept in the album as a notebook page
/// (<see cref="UI.PhotoLogPage"/>): the assignment's three (the valley from the overlook, the falls up close, the trail at
/// last light: the cabin's sheet, <see cref="World.Act1Walks"/>), the three birds on the park's card and the doe, the
/// steps (Act 2) and R.H.'s camp (Act 3). The album is still not a checklist of the seventy (STORY.md): this is only what
/// the story itself sends the player to photograph.
///
/// A line is ticked by what the game already records: a subject's photo flag (<see cref="PhotoLog.Has"/>) or the
/// assignment's own flags. Its print is the best one of that subject in the album; for the assignment's places, which
/// are no subject of their own, the print is the shot that ticked it, remembered as a story flag
/// (<see cref="PrintFlagPrefix"/>) when it's taken (<see cref="NoteShot"/>), so it survives a Continue.
/// </summary>
public static class PhotoJournal
{
	public sealed record Entry(string Id, string Line, int Act, string Subject = null, string Flag = null);

	public static readonly IReadOnlyList<Entry> All = new List<Entry>
	{
		new("overlook", "the view from the overlook", 1, Flag: World.Act1Walks.ShotOverlookFlag),
		new("falls", "the falls, up close", 1, Subject: "waterfall"),
		new("dusk", "the trail at dusk", 1, Flag: World.Act1Walks.ShotDuskFlag),
		new("cardinal", "a cardinal", 1, Subject: "bird_red"),
		new("bunting", "an indigo bunting", 1, Subject: "bird_blue"),
		new("finch", "a purple finch", 1, Subject: "bird_purple"),
		new("doe", "the doe", 1, Subject: "deer"),
		new("steps", "the old stone steps", 2, Subject: "stairs"),
		new("camp", "R.H.'s camp", 3, Subject: "camp"),
	};

	/// <summary>"journal_print_&lt;id&gt;_&lt;number&gt;": the album's print that ticked a place's line.</summary>
	public const string PrintFlagPrefix = "journal_print_";

	public static bool Done(Entry e)
	{
		if (e.Subject != null) return PhotoLog.Instance?.Has(e.Subject) ?? StoryManager.Instance?.HasFlag(StoryManager.Flag.Photo(e.Subject)) ?? false;
		return StoryManager.Instance?.HasFlag(e.Flag) ?? false;
	}

	public static int DoneCount => All.Count(Done);

	/// <summary>The print beside a ticked line: the best of its subject, or the shot that ticked a place; null if none.</summary>
	public static PhotoLog.Photo PrintFor(Entry e)
	{
		var log = PhotoLog.Instance;
		if (log == null) return null;
		if (e.Subject != null)
			return log.Photos.Where(p => p.SubjectId == e.Subject).OrderByDescending(p => p.Total).ThenBy(p => p.Number).FirstOrDefault();
		var story = StoryManager.Instance;
		if (story == null) return null;
		string prefix = PrintFlagPrefix + e.Id + "_";
		foreach (var f in story.Flags)
			if (f.StartsWith(prefix) && int.TryParse(f[prefix.Length..], out int n))
				return log.Photos.FirstOrDefault(p => p.Number == n);
		return null;
	}

	private static HashSet<string> _before;

	/// <summary>A photo just went into the album (<see cref="PhotoLog.Taken"/>): a place's line it ticked (its flag new since
	/// the last shot) keeps it as its print.</summary>
	public static void NoteShot(PhotoLog.Photo p)
	{
		var story = StoryManager.Instance;
		if (story == null || p == null) return;
		foreach (var e in All)
		{
			if (e.Flag == null || !story.HasFlag(e.Flag) || (_before != null && _before.Contains(e.Flag))) continue;
			string prefix = PrintFlagPrefix + e.Id + "_";
			if (story.Flags.Any(f => f.StartsWith(prefix))) continue;
			story.SetFlag(prefix + p.Number);
		}
		Remember();
	}

	/// <summary>The places already ticked before the next shot (an old save's ticks are not that shot's doing).</summary>
	public static void Remember()
	{
		var story = StoryManager.Instance;
		_before = story == null ? null : All.Where(e => e.Flag != null && story.HasFlag(e.Flag)).Select(e => e.Flag).ToHashSet();
	}
}

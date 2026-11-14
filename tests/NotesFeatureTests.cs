using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

// Compile alongside the app with /main:NotesFeatureTests (KinetikNotes.cs and SuiteSupport.cs). Works in temporary folders only.
// With a folder as the first argument it also draws the preview, search and quick note windows there as PNGs.
static class NotesFeatureTests
{
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    [STAThread]
    static void Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "notes-test-" + Guid.NewGuid().ToString("N"));
        var notesDir = Path.Combine(root, "notes"); var mirrorDir = Path.Combine(root, "mirror");
        Directory.CreateDirectory(notesDir);
        try
        {
            File.WriteAllText(Path.Combine(notesDir, "Groceries.md"), "---\ncolor: green\npinned: true\ntags: [home]\n---\n# Groceries\n\n- [x] Oat milk\n- [ ] Sourdough bread\n- [ ] Coffee beans\n");
            File.WriteAllText(Path.Combine(notesDir, "Marking.md"), "# Marking\n\nMark the **Week 3** quizzes, see [[Rubric]] and [the portal](https://example.com).\n\n> Be kind.\n\n```\nscore = 1\n```\n\n1. First\n2. Second\n");
            File.WriteAllText(Path.Combine(notesDir, "Ideas.md"), "# Ideas\n\nA note about sourdough starters #baking\n");
            var notes = Notes.Load(notesDir, false);
            Check(notes.Count == 3, "The test notes load");
            var texts = Find.ReadAll(notes);

            // search
            var hits = Find.Search(notes, texts, "sourdough");
            Check(hits.Count == 2 && hits.All(h => h.Snippet.ToLower().Contains("sourdough")), "A word is found in the body, with a snippet");
            Check(hits[0].Snippet != null && !hits[0].Snippet.StartsWith("- "), "The snippet drops the list marker");
            Check(Find.Search(notes, texts, "sourdough coffee").Single().N.Title == "Groceries", "Every word has to match");
            Check(Find.Search(notes, texts, "GROCERIES").Count == 1 && Find.Search(notes, texts, "#home").Count == 1, "Titles and tags match, ignoring case");
            Check(Find.Search(notes, texts, "").Count == 0 && Find.Search(notes, texts, "zebra").Count == 0, "Nothing for an empty or unmatched search");
            Check(!Find.Search(notes, texts, "oat milk")[0].Snippet.Contains("[x]"), "A ticked task's marker is dropped too");

            // markdown
            Check(MarkdownView.Body("---\ncolor: pink\n---\n# Hi\n") == "# Hi\n" && MarkdownView.Body("# No front matter") == "# No front matter" && MarkdownView.Body("---\nunclosed") == "---\nunclosed", "Front matter is removed, only when it is closed");
            var runs = MarkdownView.Runs("a **b** *c* `d` [e](http://x) [[f|g]] [[h]]");
            Check(string.Join("|", runs.Select(r => r.Text)) == "a |b| |c| |d| |e| |g| |h", "Inline Markdown is split into runs");
            Check(runs.First(r => r.Text == "b").Bold && runs.First(r => r.Text == "c").Italic && runs.First(r => r.Text == "d").Code && runs.First(r => r.Text == "e").Link && runs.First(r => r.Text == "g").Link, "Runs carry their style");

            // quick capture
            var one = Notes.QuickNote(notesDir, "Call the plumber\nTuesday morning\nbring the invoice");
            Check(Path.GetFileName(one) == "Call the plumber.md" && File.ReadAllText(one) == "# Call the plumber\n\nTuesday morning\nbring the invoice\n", "The first line becomes the title and file name");
            var two = Notes.QuickNote(notesDir, "Call the plumber");
            Check(Path.GetFileName(two) == "Call the plumber 2.md" && File.ReadAllText(two) == "# Call the plumber\n\n", "A second note with the same title is numbered");
            Check(Notes.QuickNote(notesDir, "  \n ") == null, "An empty capture makes no file");
            var odd = Notes.QuickNote(notesDir, "## What: a \"test\"? / yes\nbody");
            Check(Path.GetFileName(odd) == "What- a -test-- - yes.md", "Characters a file name can't have are replaced");
            Check(Path.GetFileName(Notes.QuickNote(notesDir, new string('x', 200))).Length <= 83, "A very long title is cut for the file name");

            // mirror
            Check(Mirror.Sync(notesDir, mirrorDir, false).StartsWith("7 copied") && File.Exists(Path.Combine(mirrorDir, "Groceries.md")), "Notes are copied to the other folder");
            Check(Mirror.Sync(notesDir, mirrorDir, false) == "0 copied", "Nothing is copied when nothing changed");
            var g = Path.Combine(notesDir, "Groceries.md");
            File.AppendAllText(g, "- [ ] Basil\n"); File.SetLastWriteTimeUtc(g, DateTime.UtcNow.AddMinutes(1));
            Check(Mirror.Sync(notesDir, mirrorDir, false) == "1 copied" && File.ReadAllText(Path.Combine(mirrorDir, "Groceries.md")).Contains("Basil"), "A changed note is copied again");
            var m = Path.Combine(mirrorDir, "Marking.md");
            File.AppendAllText(m, "edited in the vault\n"); File.SetLastWriteTimeUtc(m, DateTime.UtcNow.AddMinutes(5));
            File.AppendAllText(Path.Combine(notesDir, "Marking.md"), "and here\n");
            var kept = Mirror.Sync(notesDir, mirrorDir, false);
            Check(kept.Contains("1 newer") && File.ReadAllText(m).Contains("edited in the vault") && !File.ReadAllText(m).Contains("and here"), "A note edited in the other folder is not overwritten");
            File.Delete(Path.Combine(notesDir, "Ideas.md"));
            Mirror.Sync(notesDir, mirrorDir, false);
            Check(File.Exists(Path.Combine(mirrorDir, "Ideas.md")), "Nothing is ever deleted from the other folder");
            try { Mirror.Sync(notesDir, Path.Combine(notesDir, "inside"), false); Check(false, "A folder inside the notes is refused"); } catch (IOException) { }
            try { Mirror.Sync(notesDir, root, false); Check(false, "A folder containing the notes is refused"); } catch (IOException) { }

            Console.WriteLine("PASS: search, Markdown, quick capture and the folder mirror.");
            if (args.Length == 0) return;

            // the windows, drawn
            Directory.CreateDirectory(args[0]);
            notes = Notes.Load(notesDir, false);
            Action<Form, string> shot = (f, name) =>
            {
                f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, -3000);
                f.Show(); Application.DoEvents(); Thread.Sleep(300); Application.DoEvents();
                using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(Path.Combine(args[0], name + ".png")); }
                f.Close();
            };
            shot(new PreviewForm(notes.First(n => n.Title == "Marking"), () => { }), "notes-preview");
            var search = new SearchForm(() => notes, keys => { }, n => { }, n => { });
            shot(search, "notes-search");
            shot(new CaptureForm(notesDir, p => { }), "notes-capture");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}

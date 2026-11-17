using System;
using System.IO;

namespace BannerlordAgents.WarRoom
{
    internal static partial class SelfTest
    {
        /// <summary>
        /// War room 0.19.2 (Nov 16: the lieutenant, in a conversation begun before module 0.20.0, said no order of its could wait on the walls, though
        /// besiege could by then): a conversation keeps the instructions it began with, so when they change its next message tells each line that is new or
        /// reads differently, once, and not the agent's own tools (told apart); a call that never reached the conversation tells it again.
        /// </summary>
        private static void InstructionsTests(string root)
        {
            string before = "# War council\n\n## Doctrine\n\n1. **Hold.** Old words.\n2. **Strike.** Same.\n\nORDERS YOU CAN GIVE\n\nTools:\n- **besiege** (at: a castle): storm 0 to 2.\n- **raid**: raids.\n- **dropped**: gone now.\n\nYOUR OWN TOOLS (yours)\n- **loop** (yours, v1): a loop.\n";
            string now = "# War council\n\n## Doctrine\n\n1. **Hold.** New words.\n2. **Strike.** Same.\n31. **Break the walls.** New.\n\nORDERS YOU CAN GIVE\n\nTools:\n- **besiege** (at: a castle): storm 0 to 3.\n- **raid**: raids.\n\nYOUR OWN TOOLS (yours)\n- **loop** (yours, v2): a loop, changed.\n";
            string diff = Room.InstructionsDiff(before, now, out int n);
            Check(diff != null && n == 4 && diff.StartsWith("YOUR INSTRUCTIONS CHANGED", StringComparison.Ordinal)
                  && diff.Contains("In Doctrine:\n(reads differently now) 1. **Hold.** New words.\n(new) 31. **Break the walls.** New.")
                  && diff.Contains("In Tools:\n(reads differently now) - **besiege** (at: a castle): storm 0 to 3.")
                  && diff.Contains("No longer in your instructions:\n- - **dropped**: gone now.")
                  && !diff.Contains("Strike") && !diff.Contains("raids") && !diff.Contains("loop"),
                  "a change of the instructions is told line by line under its heading: the lines that read differently and the new ones as they now stand, the dropped ones, never the unchanged or the agent's own tools (" + n + "): " + diff);
            Check(Room.InstructionsDiff(now, now, out _) == null, "unchanged instructions tell nothing");

            // told once, from the text the conversation knows; again after a call that never reached it; nothing to tell from a lost text
            string dir = Path.Combine(root, "instructions");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "system-AAAA.md"), before);
            File.WriteAllText(Path.Combine(dir, "system-BBBB.md"), now);
            var talk = new Talk { Started = true, SystemHash = "AAAA", SystemFile = Path.Combine(dir, "system-AAAA.md") };
            object[] mark = talk.Mark();
            string first = Room.InstructionsChange(talk, Path.Combine(dir, "system-BBBB.md"), "BBBB", dir);
            talk.Restore(mark);
            string again = Room.InstructionsChange(talk, Path.Combine(dir, "system-BBBB.md"), "BBBB", dir);
            string third = Room.InstructionsChange(talk, Path.Combine(dir, "system-BBBB.md"), "BBBB", dir);
            var lost = new Talk { Started = true, SystemHash = "CCCC", SystemFile = Path.Combine(dir, "system-CCCC.md") };
            string none = Room.InstructionsChange(lost, Path.Combine(dir, "system-BBBB.md"), "BBBB", dir);
            var fresh = new Talk { Started = false, SystemHash = "AAAA" };
            Check(first != null && again == first && third == null && talk.ToldHash == "BBBB" && talk.SystemHash == "AAAA" && none == null && lost.ToldHash == "BBBB"
                  && Room.InstructionsChange(fresh, Path.Combine(dir, "system-BBBB.md"), "BBBB", dir) == null,
                  "the change is told once (again only when its call never reached the conversation), the conversation keeps its own system prompt, and a lost old text or an unbegun conversation tells nothing");
        }
    }
}

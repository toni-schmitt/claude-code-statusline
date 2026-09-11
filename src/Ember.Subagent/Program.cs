using System.Text;
using System.Text.Json;
using Ember.Core;
using Ember.Core.Data;
using Ember.Core.Render;

namespace Ember.Subagent;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var icons = Icons.For(options.Icons);

            string stdinText;
            try { stdinText = Console.In.ReadToEnd(); } catch { stdinText = ""; }

            SubagentStdinPayload payload;
            try { payload = JsonSerializer.Deserialize(stdinText, SubagentJsonContext.Default.SubagentStdinPayload) ?? new SubagentStdinPayload(); }
            catch { payload = new SubagentStdinPayload(); }

            int columns = payload.Columns is > 0 ? payload.Columns.Value : 80; // defensive; §11.1 says this is always supplied

            var sb = new StringBuilder();
            foreach (var task in payload.Tasks ?? [])
            {
                if (string.IsNullOrEmpty(task.Id)) continue; // can't override a row without its id

                string content;
                try { content = Row.Compose(task, icons, options.Icons, columns); }
                catch { continue; } // omit the row so Claude Code keeps its default rendering

                sb.Append(JsonSerializer.Serialize(
                    new SubagentRowOutput { Id = task.Id, Content = content },
                    SubagentJsonContext.Default.SubagentRowOutput));
                sb.Append('\n');
            }

            Console.Out.Write(sb.ToString());
            return 0;
        }
        catch
        {
            return 0; // worst case, every row keeps its default rendering
        }
    }
}

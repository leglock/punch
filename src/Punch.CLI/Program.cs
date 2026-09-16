using System.Text;
using Spectre.Console.Cli;

namespace Punch.CLI;

class Program
{
    static int Main(string[] args)
    {
        // Windows consoles default to a legacy OEM code page that can't encode
        // the timeline tick (╵) and gauge (▰▱) glyphs, rendering them as '?'.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (IOException)
            {
                // No console attached (e.g. redirected output); keep the default.
            }
        }

        var app = new CommandApp<PunchCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("punch");
        });

        return app.Run(args);
    }
}

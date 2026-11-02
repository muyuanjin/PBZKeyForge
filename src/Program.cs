using System;
using System.IO;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public static class Program
    {
        public const string Version = "1.0.0";

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                string cmd = args[0];
                if (cmd == "--ui-smoke")
                {
                    Application.EnableVisualStyles();
                    using (var f = new MainForm()) f.UiSmoke(args[1]);
                    Console.WriteLine("ui-smoke ok");
                    return 0;
                }
                if (cmd == "--ui-autoapply")
                {
                    Application.EnableVisualStyles();
                    using (var f = new MainForm())
                        f.AutoApply(args[1], args[2], args[3], args.Length > 4 ? args[4] : null,
                                    args.Length > 5 && args[5] == "--activate");
                    return 0;
                }
                return Cli.Run(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}

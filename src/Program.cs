using System;
using System.IO;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public static class Program
    {
        public static readonly string Version = typeof(Program).Assembly.GetName().Version.ToString(3);

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                string cmd = args[0];
                if (cmd == "--gui" && args.Length > 1)
                {
                    Application.EnableVisualStyles();
                    var form = new MainForm();
                    form.UiSmoke(args[1]);
                    Application.Run(form);
                    return 0;
                }
                if (cmd == "--shot" && args.Length > 2)
                {
                    Application.EnableVisualStyles();
                    var form = new MainForm();
                    form.UiSmoke(args[1]);
                    form.Show();
                    Application.DoEvents();
                    var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
                    bmp.Dispose();
                    form.Close();
                    Console.WriteLine("shot saved: " + args[2]);
                    return 0;
                }
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



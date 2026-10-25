using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Animation;

namespace EdgeShelf;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Animations run at a steady 60 fps (also keeps CPU low on high-refresh screens)
        Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(Timeline), new FrameworkPropertyMetadata(60));

        // نسخة واحدة بس من البرنامج تشتغل في نفس الوقت
        _mutex = new Mutex(true, "EdgeShelf_SingleInstance_Mutex", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        // لو حصل أي خطأ: نسجله في ملف ونعرض رسالة بدل ما البرنامج يقفل فجأة
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                string log = Path.Combine(AppSettings.DataDir, "crash.log");
                File.AppendAllText(log, $"[{DateTime.Now}] {args.Exception}{Environment.NewLine}{Environment.NewLine}");
                MessageBox.Show(
                    "EdgeShelf ran into an error:\n" + args.Exception.Message + "\n\nDetails were saved to:\n" + log,
                    "EdgeShelf");
            }
            catch
            {
                // لو التسجيل نفسه فشل نتجاهله
            }
            args.Handled = true;
        };

        new MainWindow().Show();
    }
}

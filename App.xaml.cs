using System.Threading;
using System.Windows;

namespace LexiFrames
{
    public partial class App : Application
    {
        private Mutex _singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _singleInstance = new Mutex(true, "LexiFrames_SingleInstance", out bool isNew);
            if (!isNew)
            {
                MessageBox.Show("Lexi Frames is already running (check the system tray).", "Lexi Frames");
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = new MainWindow();
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}

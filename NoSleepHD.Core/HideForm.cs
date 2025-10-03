using NoSleepHD.Core.Global;
using NoSleepHD.Core.Language;
using NoSleepHD.Core.Manager;
using System.ComponentModel;

namespace NoSleepHD.Core
{
    public partial class HideForm : Form
    {
        public HideForm()
        {
            InitializeComponent();

            Opacity = 0;
            ShowInTaskbar = false;
        }

        private void LoadLanguage()
        {
            LanguageModel language = LanguageCoreManager.Language;

            OpenToolStripMenuItem.Text = language.NotifyOpenText;
            CloseToolStripMenuItem.Text = language.NotifyCloseText;
        }

        private void LoadRegistry()
        {
            readTimer.Interval = MainGlobal.Interval * 1000;
        }

		private void readTimer_Tick(object sender, EventArgs e)
		{
			foreach (string disk in MainGlobal.Disks)
			{
				try
				{
					if (Directory.Exists(disk))
					{
						string markerFilePath = Path.Combine(disk, MainGlobal.MarkerFileName);

						if (File.Exists(markerFilePath))
						{
							// REFINED LOGIC: This is the new, low-impact keep-awake action.
							// Updating the last access time is a metadata write, which is
							// sufficient to bypass the OS cache and reset the drive's idle timer.
							File.SetLastAccessTimeUtc(markerFilePath, DateTime.UtcNow);
						}
					}
				}
				catch
				{
					// Ignore errors (e.g., file is temporarily locked)
					// and continue to the next disk.
				}
			}
		}

		private void timingTimer_Tick(object sender, EventArgs e)
        {
            if (MainGlobal.OnTiming)
            {
                DateTime now = DateTime.Now;
                TimeSpan start = TimeSpan.FromHours(MainGlobal.StartHour) + TimeSpan.FromMinutes(MainGlobal.StartMinute);
                TimeSpan end = TimeSpan.FromHours(MainGlobal.EndHour) + TimeSpan.FromMinutes(MainGlobal.EndMinute);

                if (start != end)
                {
                    bool enabled = false;

                    if (start > end)
                    {
                        if (now.TimeOfDay > end)
                        {
                            enabled = false;
                        }

                        if (now.TimeOfDay > start)
                        {
                            enabled = true;
                        }
                    }
                    else
                    {
                        if (now.TimeOfDay > start)
                        {
                            enabled = true;
                        }

                        if (now.TimeOfDay > end)
                        {
                            enabled = false;
                        }
                    }

                    readTimer.Enabled = enabled;
                }
            }
        }

        private void OpenToolStripClick(object sender, EventArgs e)
        {
            CoreManager.OpenMain();
        }

        private void CloseToolStripClick(object sender, EventArgs e)
        {
            CoreManager.CloseMain();
            Application.Exit();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            Hide();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            LoadLanguage();
            LoadRegistry();

            readTimer.Start();
            timingTimer.Start();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            readTimer.Stop();
            timingTimer.Stop();
        }
    }
}

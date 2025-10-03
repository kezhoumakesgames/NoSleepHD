using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NoSleepHD.Model;
using NoSleepHD.View;
using NoSleepHD.Manager;
using NoSleepHD.Core.Global;
using NoSleepHD.Core.Manager;
using NoSleepHD.Core.Language;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace NoSleepHD.ViewModel
{
	public partial class WindowViewModel : ObservableObject
	{
		#region P/Invoke for Volume Enumeration
		// We use the Windows API to find all volumes, including those mounted as folders.

		private const int MAX_PATH = 260;

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern IntPtr FindFirstVolume(
			StringBuilder lpszVolumeName,
			uint cchBufferLength);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern bool FindNextVolume(
			IntPtr hFindVolume,
			StringBuilder lpszVolumeName,
			uint cchBufferLength);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool FindVolumeClose(IntPtr hFindVolume);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern bool GetVolumePathNamesForVolumeName(
			string lpszVolumeName,
			[Out] char[]? lpszVolumePathNames,
			uint cchBufferLength,
			out uint lpcchReturnLength);

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		private static extern DriveType GetDriveType(string lpRootPathName);
		#endregion

		public ObservableCollection<DiskModel> DiskLists { get; }
			= new ObservableCollection<DiskModel>();

		[ObservableProperty]
		private bool isStarted;

		[ObservableProperty]
		private object content;

		#region For Setting

		public List<LanguageModel> Languages
		{
			get
			{
				return LanguageCoreManager.Languages;
			}
		}

		public LanguageModel Language
		{
			get
			{
				return LanguageManager.CurrentLanguage!;
			}
			set
			{
				LanguageManager.UpdateLanguage(value, true);
			}
		}

		public bool OnStartup
		{
			get
			{
				return MainGlobal.TryStartupCurrent();
			}
			set
			{
				MainGlobal.TryStartupCurrent(value);
			}
		}

		public List<int> Hour
		{
			get
			{
				var hour = new List<int>();
				for (int i = 0; i < 24; i++)
				{
					hour.Add(i);
				}

				return hour;
			}
		}

		public List<int> Minute
		{
			get
			{
				var minute = new List<int>();
				for (int i = 0; i < 60; i++)
				{
					minute.Add(i);
				}

				return minute;
			}
		}

		public bool OnTiming
		{
			get
			{
				return MainGlobal.OnTiming;
			}
			set
			{
				MainGlobal.OnTiming = value;
			}
		}

		public int StartHour
		{
			get
			{
				return MainGlobal.StartHour;
			}
			set
			{
				MainGlobal.StartHour = value;
			}
		}

		public int StartMinute
		{
			get
			{
				return MainGlobal.StartMinute;
			}
			set
			{
				MainGlobal.StartMinute = value;
			}
		}

		public int EndHour
		{
			get
			{
				return MainGlobal.EndHour;
			}
			set
			{
				MainGlobal.EndHour = value;
			}
		}

		public int EndMinute
		{
			get
			{
				return MainGlobal.EndMinute;
			}
			set
			{
				MainGlobal.EndMinute = value;
			}
		}

		public int Interval
		{
			get
			{
				return MainGlobal.Interval;
			}
			set
			{
				MainGlobal.Interval = value;
			}
		}

		#endregion

		private List<string> _disks = new List<string>();

		private MainView _mainView = new MainView();
		private SettingView _settingView = new SettingView();

		private ISnackbarService _snackbarService;

		public WindowViewModel(ISnackbarService snackbarService)
		{
			_snackbarService = snackbarService;

			IsStarted = CoreManager.IsCoreRunning();
			Content = _mainView;

			LoadRegistry();
			LoadSSD();
		}

		[RelayCommand]
		private void OnButton(string command)
		{
			switch (command)
			{
				case "Setting":
					Content = _settingView;
					break;

				case "BackToMain":
					Content = _mainView;
					break;

				case "Switch":

					if (IsStarted)
					{
						StopDiskNoSleep();
						break;
					}

					if (_disks.Count == 0)
					{
						_snackbarService.Show
						(
							LanguageManager.GetStringByKey("text_warning"),
							LanguageManager.GetStringByKey("you_have_not_selected_any_hdd"),
							ControlAppearance.Caution,
							null,
							TimeSpan.FromSeconds(3)
						);

						break;
					}

					StartDiskNoSleep();
					break;
			}
		}

		[RelayCommand]
		private void OnDiskButton(DiskModel disk)
		{
			if (disk.IsChecked)
			{
				if (!_disks.Contains(disk.Path))
				{
					_disks.Add(disk.Path);
				}
			}
			else
			{
				_disks.Remove(disk.Path);
			}

			MainGlobal.Disks = _disks.ToArray();
		}

		/// <summary>
		/// REFINED: This method now uses the Windows API to find all fixed-disk volumes,
		/// including those mounted as folders without a drive letter.
		/// </summary>
		private void LoadSSD()
		{
			DiskLists.Clear();
			var volumeName = new StringBuilder(MAX_PATH);
			IntPtr findHandle = FindFirstVolume(volumeName, MAX_PATH);

			if (findHandle == new IntPtr(-1))
			{
				// Could show an error message if needed
				return;
			}

			try
			{
				do
				{
					// Get all mount points for the current volume
					// A volume can have multiple mount points (e.g., D:\ and C:\Mounts\MyData)
					GetVolumePathNamesForVolumeName(volumeName.ToString(), null, 0, out uint pathNamesLength);
					if (pathNamesLength == 0) continue;

					var pathNames = new char[pathNamesLength];
					if (!GetVolumePathNamesForVolumeName(volumeName.ToString(), pathNames, pathNamesLength, out _))
					{
						continue;
					}

					// The API returns a multi-string buffer (null-terminated strings, ending with a double null).
					// We split it into individual paths.
					string multiString = new string(pathNames).TrimEnd('\0');
					string[] mountPoints = multiString.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

					foreach (var mountPoint in mountPoints)
					{
						// Check if the drive type is 'Fixed' (a hard disk)
						if (GetDriveType(mountPoint) == DriveType.Fixed)
						{
							DiskLists.Add(new DiskModel(mountPoint, _disks.Contains(mountPoint)));
						}
					}

				} while (FindNextVolume(findHandle, volumeName, MAX_PATH));
			}
			finally
			{
				if (findHandle != IntPtr.Zero && findHandle != new IntPtr(-1))
				{
					FindVolumeClose(findHandle);
				}
			}
		}

		private void LoadRegistry()
		{
			_disks.Clear();
			_disks.AddRange(MainGlobal.Disks);
		}

		public void StartDiskNoSleep()
		{
			if (IsStarted)
				return;

			if (CoreManager.OpenCore())
			{
				IsStarted = true;
			}
			else
			{
				_snackbarService.Show
				(
					LanguageManager.GetStringByKey("text_warning"),
					LanguageManager.GetStringByKey("text_open_core_failed"),
					ControlAppearance.Danger,
					null,
					TimeSpan.FromSeconds(3)
				);
			}
		}

		public void StopDiskNoSleep()
		{
			if (!IsStarted)
				return;

			CoreManager.CloseCore();
			IsStarted = false;
		}
	}
}
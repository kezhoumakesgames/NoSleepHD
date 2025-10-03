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
using System.Linq;

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

		public ObservableCollection<DiskModel> NormalDisks { get; } = new();
		public ObservableCollection<DiskModel> MountedDisks { get; } = new();

		[ObservableProperty]
		private bool hasMountedDisks;

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
			var normalDisksTemp = new List<DiskModel>();
			var mountedDisksTemp = new List<DiskModel>();

			var volumeName = new StringBuilder(MAX_PATH);
			IntPtr findHandle = FindFirstVolume(volumeName, MAX_PATH);

			if (findHandle == new IntPtr(-1)) return;

			try
			{
				do
				{
					GetVolumePathNamesForVolumeName(volumeName.ToString(), null, 0, out uint pathNamesLength);
					if (pathNamesLength == 0) continue;

					var pathNames = new char[pathNamesLength];
					if (!GetVolumePathNamesForVolumeName(volumeName.ToString(), pathNames, pathNamesLength, out _)) continue;

					string multiString = new string(pathNames).TrimEnd('\0');
					string[] mountPoints = multiString.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

					foreach (var mountPoint in mountPoints)
					{
						if (GetDriveType(mountPoint) == DriveType.Fixed)
						{
							var diskModel = new DiskModel(mountPoint, _disks.Contains(mountPoint));

							// A "normal" disk path is 3 chars long (e.g., "C:\").
							// Anything longer is a mounted folder.
							if (mountPoint.Length == 3 && mountPoint.EndsWith(@"\"))
							{
								normalDisksTemp.Add(diskModel);
							}
							else
							{
								mountedDisksTemp.Add(diskModel);
							}
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

			// Clear existing collections and populate them with the sorted lists
			NormalDisks.Clear();
			MountedDisks.Clear();

			foreach (var disk in normalDisksTemp.OrderBy(d => d.Path))
			{
				NormalDisks.Add(disk);
			}

			foreach (var disk in mountedDisksTemp.OrderBy(d => d.Path))
			{
				MountedDisks.Add(disk);
			}

			// Update the visibility property for the UI
			HasMountedDisks = MountedDisks.Any();
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

			// NEW LOGIC: Create the marker file on each selected disk
			foreach (var diskPath in _disks)
			{
				try
				{
					string markerFilePath = Path.Combine(diskPath, MainGlobal.MarkerFileName);
					string content = "这是一个防止硬盘休眠的文件 ——NoSleepHD";
					File.WriteAllText(markerFilePath, content);
				}
				catch (Exception ex)
				{
					// Optionally, log or show a non-critical error that a marker file couldn't be created
					System.Diagnostics.Debug.WriteLine($"Failed to create marker file on {diskPath}: {ex.Message}");
				}
			}

			if (CoreManager.OpenCore())
			{
				IsStarted = true;
			}
			else
			{
				// If core fails to start, clean up any marker files we just created
				StopDiskNoSleep();

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
			// Delete the marker file from all disks that might have it.
			// We use the currently selected list of disks as the source for cleanup.
			foreach (var diskPath in _disks)
			{
				try
				{
					string markerFilePath = Path.Combine(diskPath, MainGlobal.MarkerFileName);
					if (File.Exists(markerFilePath))
					{
						File.Delete(markerFilePath);
					}
				}
				catch (Exception ex)
				{
					// Optionally, log a non-critical error
					System.Diagnostics.Debug.WriteLine($"Failed to delete marker file on {diskPath}: {ex.Message}");
				}
			}

			if (!IsStarted && CoreManager.IsCoreRunning() == false) // Prevent recursion if called from StartDiskNoSleep
				return;

			CoreManager.CloseCore();
			IsStarted = false;
		}
	}
}
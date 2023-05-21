using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace FlashDriveApp;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        FormatOptionsComboBox.Items.Add("FAT32");
        FormatOptionsComboBox.Items.Add("NTFS");
        FormatOptionsComboBox.Items.Add("exFAT");
        RefreshDriveList();
    }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
        [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
        [MarshalAs(UnmanagedType.U4)] FileAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    private const int FileShareRead = 1;
    private const int FileShareWrite = 2;
    // private const int OpenExisting = 3;
    private const int FileAttributeNormal = 128;
    private const int FileFlagBackupSemantics = 33554432;
    private const int InvalidHandleValue = -1;
    private const int IoctlStorageEjectMedia = 2967560;

    private void RefreshDriveList()
    {
        FlashDriveList.Items.Clear();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.Removable)
            {
                FlashDriveList.Items.Add(new CheckBox
                    { Content = $"{drive.Name} ({drive.VolumeLabel}, {drive.TotalSize / (1024 * 1024 * 1024)} GB)" });
            }
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDriveList();
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            ValidateNames = false,
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Folder Selection."
        };

        if (dialog.ShowDialog() == true)
        {
            SourceFolderTextBox.Text = Path.GetDirectoryName(dialog.FileName);
        }
    }

    private void CopyFilesButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (CheckBox checkBox in FlashDriveList.Items)
        {
            if (checkBox.IsChecked != true) continue;
            var driveLetter = checkBox.Content.ToString()?[..2];
            if (driveLetter == null) continue;
            FormatDrive(driveLetter, FormatOptionsComboBox.Text, DriveNameTextBox.Text);
            CopyFiles(SourceFolderTextBox.Text, driveLetter);
            EjectDrive(driveLetter);
        }
    }

    // The rest of the methods remain the same as before
    private void FormatDrive(string driveLetter, string fileSystem, string newLabel)
    {
        // Change the cursor to a wait cursor
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            p?.StandardInput.WriteLine($"echo Y | format {driveLetter} /FS:{fileSystem} /V:{newLabel} /Q");
            p?.StandardInput.Close();
            p?.WaitForExit();
        }
        finally
        {
            // Restore the cursor
            Mouse.OverrideCursor = null;
        }
    }

    private void CopyFiles(string sourcePath, string driveLetter)
    {
        // Get the subdirectories for the specified directory.
        DirectoryInfo dir = new DirectoryInfo(sourcePath);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " +
                                                 sourcePath);
        }

        DirectoryInfo[] dirs = dir.GetDirectories();

        // If the destination directory doesn't exist, create it.
        if (!Directory.Exists(driveLetter))
        {
            Directory.CreateDirectory(driveLetter);
        }

        // Get the files in the directory and copy them to the new location.
        FileInfo[] files = dir.GetFiles();

        foreach (FileInfo file in files)
        {
            string temppath = Path.Combine(driveLetter, file.Name);
            file.CopyTo(temppath, false);
        }

        // Copy subdirectories and their contents to new location.
        foreach (DirectoryInfo subdir in dirs)
        {
            string temppath = Path.Combine(driveLetter, subdir.Name);
            CopyFiles(subdir.FullName, temppath);
        }
    }

    private static void EjectDrive(string driveLetter)
    {
        var path = @"\\.\" + driveLetter[0] + ":";
        var handle = CreateFile(path, FileAccess.Read, (FileShare)(FileShareRead | FileShareWrite), IntPtr.Zero, FileMode.Open, (FileAttributes)(FileAttributeNormal | FileFlagBackupSemantics), IntPtr.Zero);
        if ((int)handle == InvalidHandleValue) return;
        // ReSharper disable once NotAccessedOutParameterVariable
        uint returnedBytes;
        DeviceIoControl(handle, IoctlStorageEjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out returnedBytes, IntPtr.Zero);
        CloseHandle(handle);
    }
}
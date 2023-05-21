using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
// using System.Linq;
using System.Runtime.InteropServices;
// using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;
// using System.Windows.Forms;

namespace FlashDriveApp;

public partial class MainWindow
{
    private Dictionary<string, string> transferTimes = new Dictionary<string, string>();
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
            try
            {
                if (drive.DriveType == DriveType.Removable)
                {
                    FlashDriveList.Items.Add(new CheckBox
                    {
                        Content = $"{drive.Name} ({drive.VolumeLabel}, {drive.TotalSize / (1024 * 1024 * 1024)} GB)"
                    });
                }
            }
            catch (IOException)
            {
                // Drive is not ready, ignore it
            }
        }
    }

    // private string CalculateMd5(string directory)
    // {
    //     using var md5 = MD5.Create();
    //     var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories).OrderBy(p => p).ToList();
    //
    //     StringBuilder hash = new StringBuilder();
    //
    //     foreach (var file in files)
    //     {
    //         byte[] contentBytes = File.ReadAllBytes(file);
    //         byte[] hashBytes = md5.ComputeHash(contentBytes);
    //
    //         foreach (var b in hashBytes)
    //             hash.Append(b.ToString("x2"));
    //     }
    //
    //     return hash.ToString();
    // }

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
    
    
    
    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool selectAll = this.SelectAllCheckBox.IsChecked == true;

        foreach (CheckBox checkBox in FlashDriveList.Items)
        {
            checkBox.IsChecked = selectAll;
        }
    }

    private string ComputeMd5(string directoryPath)
    {
        // Create a new instance of the MD5CryptoServiceProvider object
        using (var md5 = MD5.Create())
        {
            // Convert the input directoryPath to a byte array and compute the hash
            byte[] data = md5.ComputeHash(Encoding.UTF8.GetBytes(directoryPath));

            // Create a new StringBuilder to collect the bytes and create a string
            var sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data and format each one as a hexadecimal string
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }

            // Return the hexadecimal string
            return sBuilder.ToString();
        }
    }

private void CopyFilesButton_Click(object sender, RoutedEventArgs e)
{
    var formatOption = FormatOptionsComboBox.Dispatcher.Invoke(() => FormatOptionsComboBox.Text);
    var sourceFolder = SourceFolderTextBox.Dispatcher.Invoke(() => SourceFolderTextBox.Text);
    var driveName = DriveNameTextBox.Dispatcher.Invoke(() => DriveNameTextBox.Text);
    var verifyEnabled = VerifyCheckBox.Dispatcher.Invoke(() => VerifyCheckBox.IsChecked == true);
    var tasks = new List<Task>();

    foreach (CheckBox checkBox in FlashDriveList.Items)
    {
        tasks.Add(Task.Run(() =>
        {
            string? driveLetter = null;

            try
            {
                if (checkBox.Dispatcher.Invoke(() => checkBox.IsChecked == true))
                {
                    driveLetter = checkBox.Dispatcher.Invoke(() => checkBox.Content.ToString()?[..2]);
                    if (driveLetter != null)
                    {
                        FormatDrive(driveLetter, formatOption, driveName);
                        CopyFiles(sourceFolder, driveLetter);
                        if (verifyEnabled)
                        {
                            var sourceMd5 = ComputeMd5(sourceFolder);
                            var targetMd5 = ComputeMd5(driveLetter);
                            if (sourceMd5 != targetMd5)
                            {
                                throw new Exception("MD5 checksum failed.");
                            }
                        }
                        EjectDrive(driveLetter);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                if (driveLetter != null)
                {
                    FormatDrive(driveLetter, formatOption, "Error");
                }
            }
        }));
    }

    Task.WhenAll(tasks).ContinueWith(t =>
    {
        // If you are running this on a background thread, as you are doing here,
        // you will need to marshal the call to the UI thread
        this.Dispatcher.Invoke(() =>
        {
            MessageBox.Show("All file transfers are complete.", "Operation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    });
}

    // The rest of the methods remain the same as before
    private void FormatDrive(string driveLetter, string fileSystem, string newLabel)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Change the cursor to a wait cursor
            Mouse.OverrideCursor = Cursors.Wait;
        });

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
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Restore the cursor
                Mouse.OverrideCursor = null;
            });
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
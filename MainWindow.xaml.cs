// Copyright (c) 2015, Dijji, and released under Ms-PL.  This can be found in the root of this distribution. 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace RepairTasks
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// It is important that the solution platform is Any CPU, as,
    /// to work correctly, this program needs to run
    /// in 64-bit on 64-bit machines, and 32-bit on 32-bit machines.
    /// </summary>
    public partial class MainWindow : Window
    {
        private State state = new State();

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = state;
        }

        private void scan_Click(object sender, RoutedEventArgs e)
        {
            state.Reset();
            state.CanScan = false;
            state.CanRepair = false;
            state.Status = "Scanning...";

            ThreadPool.QueueUserWorkItem(Scan, state);
        }

        private void repair_Click(object sender, RoutedEventArgs e)
        {
            if (rbRecycle.IsChecked == true)
                state.Source = Source.Recycle;
            else  // other
                state.Source = Source.Other;

            // See if they want a backup
            if (!Properties.Settings.Default.HasBackedUp)
            {
                MessageBoxResult r = MessageBox.Show("Repair will make changes to your system. If you have not made a backup of your task files, " +
                    "using the Backup Tasks button below, now might be a good time. Do you want to proceed with the Repair?", 
                    "Proceed?", MessageBoxButton.YesNo);
                if (r == MessageBoxResult.No)
                    return;
            }

            // We are using our downloaded zip file of Windows 7 task files, or an independent source of task XML files,
            // so prompt for the zip file or folder that contains them
            if (state.Source == Source.Other)
            {
                var dialog = new Ionic.Utils.FolderBrowserDialogEx
                {
                    Description = "Select the downloaded zip file of Windows 7 task files, or a directory containing backed up task files.",
                    ShowNewFolderButton = false,
                    ShowEditBox = true,
                    NewStyle = true,
                    RootFolder = Environment.SpecialFolder.Desktop,
                    SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    ShowBothFilesAndFolders = true,
                    ShowFullPathInEditBox = false,
                    DontExpandZip = true,
                    ValidExtensions = new string[1] { "zip" },
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                else
                {
                    if (Path.GetExtension(dialog.SelectedPath).ToLower() == ".zip")
                    {
                        if (!VerifyZip(state, dialog.SelectedPath))
                            return;

                        state.Source = Source.Zip;
                    }

                    state.SourcePath = dialog.SelectedPath;
                }
            }

            state.Reports.Clear();
            state.CanScan = false;
            state.CanRepair = false;
            state.Status = "Repairing...";

            ThreadPool.QueueUserWorkItem(Repair, state);
        }

        private void unplug_Click(object sender, RoutedEventArgs e)
        {
            var target = (Target)((Report)lbReports.SelectedItem).Tag;
            var result = MessageBox.Show(String.Format("This will unplug '{0}' from Task Scheduler. It will still be reported and available " +
                "for repair by RepairTasks. Do you want to proceed?", target.FullName), "Unplug Task", MessageBoxButton.YesNo);

            if (result == MessageBoxResult.Yes)
            {
                state.Reports.Clear();
                state.CanScan = true;
                state.CanRepair = false;

                Dictionary<string, string> dictRegKeys = new Dictionary<string, string>();

                // Find the registry entries
                FindRegistryEntries(target, dictRegKeys);

                // Delete registry keys
                foreach (string key in dictRegKeys.Keys)
                    Registry.LocalMachine.DeleteSubKey(key);

                state.Reports.Add(new Report(String.Format("Task '{0}' is now unplugged from the Task Scheduler", target.FullName)));
                state.Status = "Unplug completed";
            }
        }

        private void backup_Click(object sender, RoutedEventArgs e)
        {
            state.Reports.Clear();
            state.Status = "";
            System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                ShowNewFolderButton = false,
                RootFolder = Environment.SpecialFolder.Desktop,
                SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Description = "Select a folder to create task backups in."
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            BackupTasks(dialog.SelectedPath);
        }

        private void save_Click(object sender, RoutedEventArgs e)
        {
            // Save the results of the last Scan or Repair
            System.Windows.Forms.SaveFileDialog dialog = new System.Windows.Forms.SaveFileDialog();

            dialog.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
            dialog.FilterIndex = 0;
            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                StreamWriter  sw = new StreamWriter (dialog.OpenFile());
                foreach (var r in state.Reports)
                {
                    sw.WriteLine(r.Text);
                }
                sw.WriteLine(state.Status);
                sw.Close();
            }
        }

        private void lbReports_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var report = lbReports.SelectedItem as Report;
            if (report != null)
            {
                var target = report.Tag as Target;
                if (target != null)
                {
                    state.CanUnplug = true;
                    return;
                }
            }

            state.CanUnplug = false;
        }

        private static string rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks") + @"\";
        private static string hiddenSuffix = "._hidden_";

        private static void Scan(Object obj)
        {
            State state = (State)obj;
            bool abnormal = false;

            try
            {
                DirectoryInfo root = new DirectoryInfo(rootDir);

                ScanDirectory(state, root);
            }
            catch (System.Exception ex)
            {
                AddReport(state, String.Format("Scan terminated by unexpected error '{0}'", ex.Message));
                abnormal = true;
            }

            Application.Current.Dispatcher.Invoke(new Action(delegate
            {
                state.Status = String.Format("Scan completed{0}: {1} problems found", abnormal ? " abnormally" : "", state.Targets.Count);
                state.CanScan = true;
                state.CanRepair = state.Targets.Count > 0;

                if (IsGangOfFive(state))
                    MessageBox.Show("These five tasks are typically left in an unusable state by reversion from Windows 10. To fix them, " +
                        "download Windows7 Tasks.zip from my site, if you have not done so already, " +
                        "then check the 'Take tasks from backup' radio button, click Repair, " + 
                        "and select the downloaded zip file in the dialog that pops up", "Typical Windows 10 reversion errors");
            }));
        }

        private static void ScanDirectory(State state, DirectoryInfo di)
        {
            string relPath = di.FullName.Replace(rootDir, "");
            if (relPath.Length > 0)
                relPath += @"\";

            // Our backstop mechanism to avoid losing track of task files is to rename them with a special suffix,
            // rather than deleting them, when we want to try reinstallation.
            // Then, at the next scan, we clean the hidden files up, or restore them 
            List<FileInfo> hidden = new List<FileInfo>(di.GetFiles().Where(fi => fi.FullName.EndsWith(hiddenSuffix)));
            foreach (var fi in hidden)
            {
                string realName = fi.FullName.Substring(0, fi.FullName.Length - hiddenSuffix.Length);
                if (File.Exists(realName))
                    fi.Delete();
                else
                    fi.CopyTo(realName);
            }

            foreach (var fi in di.GetFiles())
            {
                if (fi.FullName.EndsWith(hiddenSuffix))
                {
                    continue;
                }

                try
                {
                    // Start the child process.
                    Process p = new Process();
                    // Redirect the output stream of the child process.
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.FileName = Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\schtasks.exe";
                    p.StartInfo.Arguments = "/query /tn \"" + relPath + fi.Name + "\"";
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd().Replace("\r", "").Replace("\n", "");
                    p.WaitForExit();

                    if (p.ExitCode != 0)
                    {
                        var target = new Target { RelativePath = relPath, Name = fi.Name };
                        state.Targets.Add(target);
                        Application.Current.Dispatcher.Invoke(new Action(delegate
                        {
                            // TODO: break the dependence on the specific English form of these messages
                            if (error.StartsWith("ERROR: The task image is corrupt or has been tampered with."))
                                state.Reports.Add(new Report("Task image corrupt: " + relPath + fi.Name, target));
                            else if (error.StartsWith("ERROR: The system cannot find the file specified"))
                                state.Reports.Add(new Report("Task not installed: " + relPath + fi.Name));
                            else
                                state.Reports.Add(new Report(String.Format("Task {0} reported '{1}'", relPath + fi.Name, error), target));
                        }));    
                    }
                }
                catch (System.Exception ex)
                {
                    state.Targets.Add(new Target { RelativePath = relPath, Name = fi.Name });
                    AddReport(state, String.Format("Scan of task {0} terminated by unexpected error '{1}'", relPath + fi.Name, ex.Message));
                }
            }

            foreach (var cdi in di.GetDirectories())
                ScanDirectory(state, cdi);
        }

        private static string TaskCache = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\";
        private static string[] Groups = { @"Plain\", @"Boot\", @"Logon\" };
        private static string Tasks = @"Tasks\";

        private static void Repair(Object obj)
        {
            State state = (State)obj;
            int successCount = 0;
            int failCount = 0;
            bool abnormal = false;

            try
            {
                foreach (Target target in state.Targets)
                {
                    string tempFilePath = Path.GetTempPath() + @"\" + target.Name;
                    Dictionary<string, string> dictRegKeys = new Dictionary<string, string>();
                    bool success = false;
                    bool renamedTask = false;
                    bool deletedKeys = false;

                    try
                    {
                        // Identify the task file to be installed
                        string taskFilePath = null;
                        if (state.Source == Source.Recycle)
                        {
                            try
                            {
                                // Copy the existing file to the temp folder
                                File.Copy(rootDir + target.FullName, tempFilePath);
                            }
                            catch (System.Exception e)
                            {
                                AddReport(state, String.Format("Cannot copy task file '{0}', {1}", rootDir + target.FullName, e.Message));

                                continue;
                            }
                            taskFilePath = tempFilePath;
                        }
                        else if (state.Source == Source.Zip)
                        {
                            // Try to unzip the required file into the temp folder
                            if (!UnzipTask(state, target.FullName, tempFilePath, state.SourcePath))
                                continue;

                            taskFilePath = tempFilePath;
                        }
                        else // if (state.Source == Source.Other)
                        {
                            taskFilePath = LocateTaskFileUnder(target, state.SourcePath);
                            if (taskFilePath == null)
                            {
                                AddReport(state, String.Format("Cannot find '{0}' under '{1}'", target.FullName, state.SourcePath));
                                continue;
                            }
                            else
                            {
                                AddReport(state, String.Format("Using '{0}' to repair '{1}'", taskFilePath, target.FullName));
                            }
                        }

                        // Find the registry entries
                        FindRegistryEntries(target, dictRegKeys);

                        // Save the registry entries
                        uint result = RegKey.ExportRegKeys(dictRegKeys);
                        if (result != 0)
                        {
                            AddReport(state, String.Format("Error {0} backing up registry keys for task {1}", result, target.FullName));
                            continue;
                        }

                        // Rename or delete existing task files
                        try
                        {
                            string hiddenTaskFile = rootDir + target.FullName + hiddenSuffix;
                            if (File.Exists(hiddenTaskFile))
                                File.Delete(hiddenTaskFile);

                            File.Move(rootDir + target.FullName, hiddenTaskFile);

                            // zap \windows\tasks\foo.job as well - if present, blocks create
                            string jobFile = Path.GetDirectoryName(Environment.SystemDirectory) + @"\tasks\" + target.Name + ".job";
                            if (File.Exists(jobFile))
                                File.Delete(jobFile);

                            renamedTask = true;
                        }
                        catch (System.Exception e)
                        {
                            AddReport(state, String.Format("Cannot delete task file '{0}', '{1}'", rootDir + target.FullName, e.Message));
                            continue;
                        }

                        // Delete registry keys
                        foreach (string key in dictRegKeys.Keys)
                            Registry.LocalMachine.DeleteSubKey(key);
                        deletedKeys = true;

                        Process p = new Process();
                        // Redirect the output stream of the child process.
                        p.StartInfo.UseShellExecute = false;
                        p.StartInfo.RedirectStandardOutput = true;
                        p.StartInfo.RedirectStandardError = true;
                        p.StartInfo.CreateNoWindow = true;
                        p.StartInfo.FileName = Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\schtasks.exe";
                        p.StartInfo.Arguments = "/create /tn \"" + target.FullName + "\" /xml \"" + taskFilePath + "\"";
                        p.Start();
                        string output = p.StandardOutput.ReadToEnd();
                        string error = p.StandardError.ReadToEnd().Replace("\r", "").Replace("\n", "");
                        p.WaitForExit();

                        if (p.ExitCode == 0)
                        {
                            AddReport(state, "Recovered task: " + target.FullName);
                            successCount++;
                            success = true;
                        }
                        else
                            AddReport(state, String.Format("Recovery of task {0} failed with '{1}'", target.FullName, error), target);

                    }
                    catch (System.Exception ex)
                    {
                        AddReport(state, String.Format("Repair of task {0} terminated by unexpected error '{1}'", target.FullName, ex.Message));
                    }
                    finally
                    {
                        // If setup failed, restore the state so that the next scan picks it up
                        if (!success)
                        {
                            failCount++;

                            if (renamedTask)
                                RestoreTaskFile(state, tempFilePath, rootDir + target.FullName);

                            if (deletedKeys)
                            {
                                uint result = RegKey.RestoreRegKeys(dictRegKeys);
                                if (result != 0)
                                    AddReport(state, String.Format("Error {0} restoring registry keys for task {1}", result, target.FullName));
                            }
                        }

                        // Clean up temp files
                        File.Delete(tempFilePath);
                        foreach (string tempFile in dictRegKeys.Values)
                        {
                            if (tempFile != null)
                                File.Delete(tempFile);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                AddReport(state, String.Format("Repair terminated by unexpected error '{0}'", ex.Message));
                abnormal = true;
            }

            Application.Current.Dispatcher.Invoke(new Action(delegate
            {
                state.Status = String.Format("Repair completed{0}: {1} tasks repaired; {2} repairs failed",
                                             abnormal ? " abnormally" : "", successCount, failCount);
                state.CanScan = true;
            }));
        }

        private static void AddReport(State state, string report, object tag = null)
        {
            Application.Current.Dispatcher.Invoke(new Action(delegate
            {
                state.Reports.Add(new Report(report, tag));
            }));
        }

        private static bool VerifyZip(State state, string zipFile)
        {
            bool success = false;

            try
            {
                using (Package package = Package.Open(zipFile, FileMode.Open, FileAccess.Read))
                {
                    if (package.GetParts().Count() == 0)
                        MessageBox.Show(String.Format("'{0}' does not contain files in the correct format. " +
                                        "Please check that you have downloaded the latest version of Windows7 Tasks.zip", zipFile), "Error");
                    else
                        success = true;
                }
            }
            catch (System.Exception e)
            {
                AddReport(state, String.Format("Cannot open zip file '{0}: {1}", zipFile, e.Message));
            }

            return success;
        }

        private static bool UnzipTask(State state, string fullName, string tempFilePath, string zipFile)
        {
            bool success = false;
            string zipUri = "/" + fullName.Replace(" ", "_").Replace(@"\", "/");
            var partUri = new Uri(zipUri, UriKind.Relative);

            try
            {
                using (Package package = Package.Open(zipFile, FileMode.Open, FileAccess.Read))
                {
                    if (package.PartExists(partUri))
                    {
                        var part = package.GetPart(partUri);

                        using (Stream source = part.GetStream(FileMode.Open, FileAccess.Read))
                        using (Stream destination = File.OpenWrite(tempFilePath))
                        {
                            byte[] buffer = new byte[0x1000];
                            int read;
                            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                destination.Write(buffer, 0, read);
                            }
                            success = true;
                        }
                    }
                    else
                        AddReport(state, String.Format("Zip file '{0}' does not contain task file '{1}'", zipFile, fullName));
                }
            }
            catch (System.Exception e)
            {
                AddReport(state, String.Format("Cannot extract task file '{0}' from zip file '{1}': {2}", fullName, zipFile, e.Message));
            }

            return success;
        }

        private static string LocateTaskFileUnder(Target target, string path)
        {
            // Try for full path, and if not, look in root of folder
            string fullPath = path + @"\" + target.FullName;
            string shortPath = path + @"\" + target.Name;
            if (File.Exists(fullPath))
                return fullPath;
            else if (File.Exists(shortPath))
                return shortPath;

            // No luck, walk the tree
            DirectoryInfo di = new DirectoryInfo(path);
            return LocateTaskFileHelper(target, di);
        }

        private static string LocateTaskFileHelper(Target target, DirectoryInfo di)
        {
            var file = di.GetFiles().Where(fi => fi.Name == target.Name).FirstOrDefault();
            if (file != null)
            {
                // demand that immediately containing folders match
                string dir = target.RelativePath.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Last();
                if (dir == di.Name)
                    return file.FullName;
            }

            foreach (var dic in di.GetDirectories())
            {
                string f = LocateTaskFileHelper(target, dic);
                if (f != null)
                    return f;
            }

            return null;
        }

        private static void FindRegistryEntries(Target target, Dictionary<string, string> dictRegKeys)
        {
            // Find the registry entries
            string treeKeyPath = TaskCache + @"Tree\" + target.FullName;
            string keyPath = null;

            var treeKey = Registry.LocalMachine.OpenSubKey(treeKeyPath);
            if (treeKey != null)
            {
                dictRegKeys.Add(treeKeyPath, null);
                string id = treeKey.GetValue("Id") as string;
                if (id != null)
                {
                    // Check under Tasks
                    keyPath = TaskCache + Tasks + id;
                    if (null != Registry.LocalMachine.OpenSubKey(keyPath))
                        dictRegKeys.Add(keyPath, null);

                    // and the groups
                    foreach (string group in Groups)
                    {
                        keyPath = TaskCache + group + id;
                        if (null != Registry.LocalMachine.OpenSubKey(keyPath))
                            dictRegKeys.Add(keyPath, null);
                    }
                }
            }
        }

        private static void RestoreTaskFile(State state, string tempFile, string taskFile)
        {
            try
            {
                File.Copy(taskFile + hiddenSuffix, taskFile);
            }
            catch (System.Exception e)
            {
                AddReport(state, String.Format("Cannot restore task file '{0}', {1}", taskFile, e.Message));
            }
        }

        private static bool IsGangOfFive(State state)
        {
            if (state.Targets.Count == 5)
            {
                var names = state.Targets.Select(t => t.FullName).ToList();
                names.Sort();
                return names[0] == @"Microsoft\Windows\PerfTrack\BackgroundConfigSurveyor" &&
                       names[1] == @"Microsoft\Windows\RAC\RacTask" &&
                       names[2] == @"Microsoft\Windows\Shell\WindowsParentalControls" &&
                       names[3] == @"Microsoft\Windows\Tcpip\IpAddressConflict1" &&
                       names[4] == @"Microsoft\Windows\Tcpip\IpAddressConflict2";
            }

            return false;
        }

        private void BackupTasks(string path)
        {
            string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
            string target = Path.Combine(path, String.Format("Tasks {0:yyyy-MM-dd HHmmss}", DateTime.Now));

            Process p = new Process();
            p.StartInfo.UseShellExecute = true;
            p.StartInfo.CreateNoWindow = false;
            p.StartInfo.FileName = "xcopy";
            p.StartInfo.Arguments = "/S /I /E \"" + source +  "\" \"" + target + "\"";
            p.Start();
            p.WaitForExit();

            if (p.ExitCode == 0)
            {
                AddReport(state, "Backed up tasks to: " + target);
                Properties.Settings.Default.HasBackedUp = true;
                Properties.Settings.Default.Save();
            }
            else
            {
                AddReport(state, String.Format("Task back up failed with error '{0}'", p.ExitCode));
            }
        }
    }
}

// Copyright (c) 2015, Dijji, and released under Ms-PL.  This can be found in the root of this distribution. 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            state.Reports.Clear();
            state.UseCopy = (rbCopy.IsChecked == true);

            // See if they want a backup
            MessageBoxResult r = MessageBox.Show("Repair will make changes to your system. If you have not made a backup of your task files, " + 
                "using the Backup Tasks button below, now might be a good time. Do you want to proceed with the Repair?", "Proceed?", MessageBoxButton.YesNo);
            if (r == MessageBoxResult.No)
                return;

            // If we are using an independent source of task XML files, prompt for the folder that contains them
            if (state.UseCopy)
            {
                System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    ShowNewFolderButton = false,
                    RootFolder = Environment.SpecialFolder.MyDocuments,
                    Description = "Select the directory containing the backed up task files to be installed."
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                else
                    state.CopyPath = dialog.SelectedPath;
            }

            state.CanScan = false;
            state.CanRepair = false;
            state.Status = "Repairing...";

            ThreadPool.QueueUserWorkItem(Repair, state);
        }

        private void backup_Click(object sender, RoutedEventArgs e)
        {
            state.Reports.Clear();
            state.Status = "";
            System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                ShowNewFolderButton = false,
                RootFolder = Environment.SpecialFolder.MyDocuments,
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
                sw.Close();
            }
        }

        private static string rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks") + @"\";

        private static void Scan(Object obj)
        {
            State state = (State)obj;

            try
            {
                DirectoryInfo root = new DirectoryInfo(rootDir);

                ScanDirectory(state, root);
            }
            catch (System.Exception ex)
            {
                AddReport(state, String.Format("Scan terminated by unexpected error '{0}'", ex.Message));
            }

            Application.Current.Dispatcher.Invoke(new Action(delegate
            {
                state.Status = String.Format("Scan completed: {0} problems found", state.Targets.Count);
                state.CanScan = true;
                state.CanRepair = state.Targets.Count > 0;
            }));
        }

        private static void ScanDirectory(State state, DirectoryInfo di)
        {
            string relPath = di.FullName.Replace(rootDir, "");
            if (relPath.Length > 0)
                relPath += @"\";

            foreach (var fi in di.GetFiles())
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
                    state.Targets.Add(new Target { RelativePath = relPath, Name = fi.Name });
                    Application.Current.Dispatcher.Invoke(new Action(delegate
                    {
                        // TODO: break the dependence on the specific English form of these messages
                        if (error.StartsWith("ERROR: The task image is corrupt or has been tampered with."))
                            state.Reports.Add(new Report("Task image corrupt: " + relPath + fi.Name));
                        else if (error.StartsWith("ERROR: The system cannot find the file specified"))
                            state.Reports.Add(new Report("Task not installed: " + relPath + fi.Name));
                        else 
                            state.Reports.Add(new Report(String.Format("Task {0} reported '{1}'", relPath + fi.Name, error)));
                    }));                    
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

            try
            {
                foreach (Target target in state.Targets)
                {
                    // Copy the existing file to the temp folder
                    string tempFilePath = Path.GetTempPath() + @"\" + target.Name;
                    Dictionary<string, string> dictRegKeys = new Dictionary<string, string>();
                    bool success = false;
                    bool deletedTask = false;
                    bool deletedKeys = false;

                    try
                    {
                        File.Copy(rootDir + target.FullName, tempFilePath);
                    }
                    catch (System.Exception e)
                    {
                        AddReport(state, String.Format("Cannot copy task file '{0}', {1}", rootDir + target.FullName, e.Message));

                        continue;
                    }

                    try
                    {
                        // Identify the task file to be installed
                        string taskFilePath = null;
                        if (state.UseCopy)
                        {
                            string fullPath = state.CopyPath + @"\" + target.FullName;
                            string shortPath = state.CopyPath + @"\" + target.Name;
                            if (File.Exists(fullPath))
                                taskFilePath = fullPath;
                            else if (File.Exists(shortPath))
                                taskFilePath = shortPath;
                            else
                            {
                                AddReport(state, String.Format("Cannot find either '{0}' or '{1}'", shortPath, fullPath));

                                // Clean up temp file
                                File.Delete(tempFilePath);

                                continue;
                            }
                        }
                        else
                        {
                            taskFilePath = tempFilePath;
                        }

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
                        else
                            treeKeyPath = null;

                        // Save the registry entries
                        uint result = RegKey.ExportRegKeys(dictRegKeys);
                        if (result != 0)
                        {
                            AddReport(state, String.Format("Error {0} backing up registry keys for task {1}", result, target.FullName));
                            continue;
                        }

                        // Delete existing task files
                        try
                        {
                            File.Delete(rootDir + target.FullName);

                            // zap \windows\tasks\foo.job as well - if present, blocks create
                            string jobFile = Path.GetDirectoryName(Environment.SystemDirectory) + @"\tasks\" + target.Name + ".job";
                            if (File.Exists(jobFile))
                                File.Delete(jobFile);

                            deletedTask = true;
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
                            success = true;
                        }
                        else
                            AddReport(state, String.Format("Recovery of task {0} failed with '{1}'", target.FullName, error));

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
                            if (deletedTask)
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

                Application.Current.Dispatcher.Invoke(new Action(delegate
                {
                    state.Status = "Repair completed";
                    state.CanScan = true;
                }));
            }
            catch (System.Exception ex)
            {
                AddReport(state, String.Format("Repair terminated by unexpected error '{0}'", ex.Message));
            }
        }

        private static void AddReport(State state, string report)
        {
            Application.Current.Dispatcher.Invoke(new Action(delegate
            {
                state.Reports.Add(new Report(report));
            }));
        }

        private static void RestoreTaskFile(State state, string tempFile, string taskFile)
        {
            try
            {
                File.Copy(tempFile, taskFile);
            }
            catch (System.Exception e)
            {
                AddReport(state, String.Format("Cannot restore task file '{0}', {1}", taskFile, e.Message));
            }
        }

        private void BackupTasks(string path)
        {
            string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
            string target = Path.Combine(path, String.Format("Tasks {0:yyyy-MM-dd HHmmss}", DateTime.Now));

            Process p = new Process();
            p.StartInfo.UseShellExecute = true;
            //p.StartInfo.RedirectStandardOutput = true;
            //p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = false;
            p.StartInfo.FileName = "xcopy";
            p.StartInfo.Arguments = "/S /I /E \"" + source +  "\" \"" + target + "\"";
            p.Start();
            //string output = p.StandardOutput.ReadToEnd();
            //string error = p.StandardError.ReadToEnd().Replace("\r", "").Replace("\n", "");
            p.WaitForExit();

            if (p.ExitCode == 0)
            {
                AddReport(state, "Backed up tasks to: " + target);
            }
            else
            {
                AddReport(state, String.Format("Task back up failed with error '{0}'", p.ExitCode));
            }
        }
    }
}

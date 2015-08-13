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

            // If we are using an independent source of task XML files, prompt for the folder that contains them
            if (state.UseCopy)
            {
                System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    ShowNewFolderButton = false,
                    RootFolder = Environment.SpecialFolder.MyComputer,
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

            DirectoryInfo root = new DirectoryInfo(rootDir);

            ScanDirectory(state, root);

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
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                // TODO: break the dependence on the specific English form of these messages
                if (output.Length == 0 && error.StartsWith("ERROR: The task image is corrupt or has been tampered with."))
                {
                    state.Targets.Add(new Target { RelativePath = relPath, Name = fi.Name });
                    Application.Current.Dispatcher.Invoke(new Action(delegate
                    {
                        state.Reports.Add(new Report("Task image corrupt: " + relPath + fi.Name));
                    }));                    
                }
                else if (output.Length == 0 && error.StartsWith("ERROR: The system cannot find the file specified"))
                {
                    state.Targets.Add(new Target { RelativePath = relPath, Name = fi.Name });
                    Application.Current.Dispatcher.Invoke(new Action(delegate
                    {
                        state.Reports.Add(new Report("Task not installed: " + relPath + fi.Name));
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

            foreach (Target target in state.Targets)
            {
                // First, identify or set up the task file to be installed
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
                        Application.Current.Dispatcher.Invoke(new Action(delegate
                        {
                            state.Reports.Add(new Report(String.Format("Cannot find either '{0}' or '{1}'", shortPath, fullPath)));
                        }));
                        continue;
                    }
                }
                else
                { 
                    // Copy the existing file to the temp folder
                    taskFilePath = Path.GetTempPath() + @"\" + target.Name;

                    try
                    {
                        File.Copy(rootDir + target.FullName, taskFilePath);
                    }
                    catch (System.Exception e)
                    {    
                        Application.Current.Dispatcher.Invoke(new Action(delegate
                        {
                            state.Reports.Add(new Report(String.Format("Cannot copy task file '{0}', {1}", rootDir + target.FullName, e.Message)));
                        }));

                        continue;
                    }
                }

                // Delete existing task files
                try
                {
                    File.Delete(rootDir + target.FullName);

                    // zap \windows\tasks\foo.job as well - if present, blocks create
                    string jobFile = Path.GetDirectoryName(Environment.SystemDirectory) + @"\tasks\" + target.Name + ".job";
                    if (File.Exists(jobFile))
                        File.Delete(jobFile);
                }
                catch (System.Exception e)
                {
                    Application.Current.Dispatcher.Invoke(new Action(delegate
                    {
                        state.Reports.Add(new Report(String.Format("Cannot delete task file '{0}', '{1}'", rootDir + target.FullName, e.Message)));
                    }));

                    // Clean up any temp file
                    if (!state.UseCopy)
                        File.Delete(taskFilePath);

                    continue;
                }

                // Clean up registry keys
                string treeKeyPath = TaskCache + @"Tree\" + target.FullName;
                var treeKey = Registry.LocalMachine.OpenSubKey(treeKeyPath);
                if (treeKey != null)
                {
                    string id = treeKey.GetValue("Id") as string;
                    if (id != null)
                    {
                        Registry.LocalMachine.DeleteSubKey(TaskCache + Tasks + id);

                        int index;
                        for (index = 0; index < 3; index++)
                        {
                            if (null != Registry.LocalMachine.OpenSubKey(TaskCache + Groups[index] + id))
                                break;
                        }

                        if (index < 3)
                        {
                            Registry.LocalMachine.DeleteSubKey(TaskCache + Groups[index] + id);
                        }
                    }
                    Registry.LocalMachine.DeleteSubKey(treeKeyPath);
                }

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

                Application.Current.Dispatcher.Invoke(new Action(delegate
                {
                    if (output.StartsWith("SUCCESS"))
                        state.Reports.Add(new Report("Recovered task: " + target.FullName));
                    else
                        state.Reports.Add(new Report("Error '" + error + "'recovering task: " + target.FullName));
                }));

                // Clean up any temp file
                if (!state.UseCopy)
                    File.Delete(taskFilePath);
            }

            Application.Current.Dispatcher.Invoke(new Action(delegate
            {
                state.Status = "Repair completed";
                state.CanScan = true;
            })); 
        }
    }
}

This walk-through is for fixing problems after reverting to Windows 7 or 8.1 from Windows 10, and was contributed by Norwood451.

Fixed in 14 steps. See below. 
# Issue was Caused by reverting from Windows 10 back to Windows 7 (in my case windows 7 64 Bit) 
# Download Repair Tasks by Dijji at  https://repairtasks.codeplex.com/ 
# For Windows 7, download Windows7 Tasks.zip (DO NOT UNZIP) at  [https://repairtasks.codeplex.com/releases/view/617575](https://repairtasks.codeplex.com/releases/view/617575). For Windows 8, download Windows8.1 Tasks.zip at [https://repairtasks.codeplex.com/releases/view/624140](https://repairtasks.codeplex.com/releases/view/624140)
# Create a Folder called AAAAATASK in your Documents (Which can be found START > Documents) C:\Users\David\Documents\AAAAATASK
# Open the downloaded RepairTasks.zip file From step 2 and copy both files (RepairTasks.exe and RepairTasks.exe.config to the AAAAATASK folder in your documents folder.
# Copy the entire downloaded zip file Windows7 Tasks.zip (or, for Windows 8.1, Windows8.1 Tasks.zip) to the AAAAATASK folder in your documents folder. 
# Right click and Run as administrator RepairTasks.exe 
# Click the Scan Button to get list of corrupted files 
# Click the repair Button. (most of the tasks should be repaired now. If some remain, go to step 10. 
# Click the Radio button> Take tasks from backup 
# Click Scan for a list of the remaining corrupted files. 
# Click Repair again. 
# You will get a pop-up window asking where the RepairTasks.zip is located-- the file you created AAAAATASK, which should be on the very top – of course, as reason for the name of the folder. It is important to select the zip file itself in the pop-up, and not the folder containing it.
# You can test by running Both Scans and if you do not get anymore lists of files. Boom! You are done. 
Reverting from more recent Windows 10 builds seems to leave a list of 11 tasks unrepaired, reported by Scan as 'not installed' . They are Windows 10 tasks without a Windows 7 equivalent, and can be safely left alone. They are listed here: [Windows 10 only tasks](Windows-10-only-tasks).


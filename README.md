# Repair Tasks

This project is a utility that makes repairs to the Windows 7 (or 8.1) Task schedule.

In particular, it fixes problems where opening the Task Scheduler, or trying to configure Windows Backup, 
results in the message "The task image is corrupt or has been tampered with" (0x80041321).

Searching the web reveals that this message has been seen from time to time, and the (rather laborious) set of steps that can be taken to correct it are fairly well-documented (see  [here](https://support.microsoft.com/en-gb/kb/2305420) and script for it  [here](https://gallery.technet.microsoft.com/scriptcenter/Repair-CorruptedTampered-c8d2e975)).

However, it turns out that reverting to Windows 7 or 8.1 from Windows 10 generates this problem in spades. It can leave more than 40 scheduled tasks in a corrupt state (see [this thread](https://social.technet.microsoft.com/Forums/windowsserver/en-US/80e4f83d-1529-4405-b8e3-d1d636f8b71c/task-scheduler-is-broken-after-windows-10-downgrade?forum=win10itprogeneral)). This is because many task registry keys and the task definitions to which they refer are updated by a Windows 10 upgrade, but only the registry keys are restored on reversion, so Task Scheduler finds that, for these tasks, the task registry keys and task definitions are now inconsistent.

The general recommendation in response to this reversion problem seems to be to restore the system from backups. However, I'm never sure about overwriting chunks of my system from backups, and would rather go forwards.  So, since the fix is well-known, and the main problem is just repeated execution, I decided to write a utility to automate the set of steps required.

And now, I'm sharing my work in case it is of use to fellow sufferers, pending Microsoft getting their act together and fixing reversion.

Open source is particularly appropriate for this type of project, as the code necessarily delves into system settings, making complete transparency as to what it does crucial. You are encouraged to download the source, which is actually not huge, and understand what it does. However, I also provide a [release](releases) of the executable, and [a wiki](wiki) which describes how to use the program.

Thank you to all the kind reviewers of the evolving versions, and the folks that have given me such great feedback and so helped make the program better.  But if you have a problem, feel free to raise it as an Issue.  I will try and make any needed improvements to the program in response.



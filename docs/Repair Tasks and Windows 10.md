## Repair Tasks and Windows 10

Several people have wondered if Repair Tasks could be of any use on a Windows 10 system, so I’ve spent some time on the question. The short answer is, ‘not really’. For the longer version, read on.

Firstly, the problem that Repair Tasks is explicitly designed to resolve is unlikely to occur on a Windows 10 system. This is because of changes in Windows 10 to the way tasks work. Before Windows 10, Task Scheduler used the registry as an index of the installed tasks, but relied on the task definition file in Windows\System32\Tasks for the complete definition. Inconsistency between the registry and the task definition file was precisely what gave rise to the problem Repair Tasks was designed to solve.

In Windows 10, when the task is installed, additional registry values are created that mean that Task Scheduler has no runtime dependency on the task definition file, which is still stored in Windows\System32\Tasks. You can delete the definition file, if you want, and Task Scheduler will continue to show you the task and all its properties. Thus, there is no simple way in which an inconsistency between the registry and the task definition file can cause a problem.

So I wondered what happened if the registry keys were damaged? In my experiments, if you delete one of the new values defining what the task does, Task Scheduler does not report an error, it simply regards the task as not installed: it disappears from the UI.  Under these circumstances, Repair Tasks could be used to reinstall the task from the definition file. However, the existing executable will not work because Administrator access to the registry keys that Task Scheduler uses has been removed. This also makes it considerably less likely that these registry keys will ever be corrupted in the first place.

So, the original concept of Repair Tasks is almost completely inapplicable in Windows 10.

This leads to a second question. Are there any task related problems in Windows 10 which Repair Tasks could be repurposed to solve?

I have not experienced any such problems myself, but there are reports on the Internet of problems occurring with tasks after installation of the Anniversary Update. A quick survey indicates that the problems are always to do with custom tasks, rather than tasks installed as part of Windows, and are typically to do with:
* Tasks that are set up to run whether a user is logged on not, but actually only run successfully when a user is logged on. One workaround for this is to run the task under a system identity instead. A fix is likely in an upcoming monthly update
* Tasks that are supposed to run multiple times during the day, but do not. Again, a fix is likely in an upcoming monthly update
None of this looks to be a likely target for Repair Tasks, because the problems have no clear, consistent, automatable solution; and in any case are likely to be fixed as acknowledged bugs.

In conclusion, then, Repair Tasks ends its life with Windows 8.1. And that’s a good thing. If Microsoft had seen the problems earlier, or reacted to them sooner, Repair Tasks would never have existed. But better late than never, I suppose.




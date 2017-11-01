As Windows 10 evolves, tasks are being added that have no counterpart in Windows 7, and so cannot be repaired after reversion. These tasks are reported as not installed by a Scan, and attempts to Repair them fail.  They are already effectively in an unplugged state, and will not interfere with the operation of the Task Scheduler. They can be safely left alone, or, if you want to tidy up, deleted from System32\Tasks.

The current list of such tasks is:

Microsoft\Windows\AppID\EDP Policy Manager 
 Microsoft\Windows\AppID\SmartScreenSpecific 
 Microsoft\Windows\CertificateServicesClient\AikCertEnrollTask 
 Microsoft\Windows\CertificateServicesClient\CryptoPolicyTask 
 Microsoft\Windows\CertificateServicesClient\KeyPreGenTask 
 Microsoft\Windows\Location\WindowsActionDialog 
 Microsoft\Windows\MemoryDiagnostic\RunFullMemoryDiagnostic 
 Microsoft\Windows\Shell\FamilySafetyMonitor 
 Microsoft\Windows\Shell\FamilySafetyRefresh 
 Microsoft\Windows\Shell\IndexerAutomaticMaintenance 
 Microsoft\Windows\Time Synchronization\ForceSynchronizeTime 

Thanks to jthomas11, who reported this list.  Below, for each task, I give the description shown in Task Scheduler in Windows 10, and the executable that implements their action. For most of them, Task Scheduler merely shows ‘Custom Handler’, and won’t give you any more detail than that. But if you look in the XML file, it gives a GUID that leads via the registry to a DLL implementing the action, which is what I list.

 Almost none of the executables implementing the actions appear on a Windows 7 system, which is pretty good evidence that the tasks did not exist. The only exception is System32\srchadmin.dll, which is used by Microsoft\Windows\Shell\IndexerAutomaticMaintenance. However, it does not register the COM object invoked in Windows 10. The earliest references that I can find to this task are from Windows 8; I think that the Windows 7 indexing service did not implement it.

 Microsoft\Windows\AppID\EDP Policy Manager 
 This task performs steps necessary to configure Enterprise Data Protection 
 System32\AppLockerCsp.dll
 I think that this feature was added in Windows 10

 Microsoft\Windows\AppID\SmartScreenSpecific 
 Task that collects data for SmartScreen in Windows.
 system32\apprepsync.dll
 Windows 8 introduced SmartScreen filtering at the desktop level, performing reputation checks by default on any file or application downloaded from the Internet

 Microsoft\Windows\CertificateServicesClient\AikCertEnrollTask 
 This task enrolls a certificate for Attestation Identity Key.
 system32\ngctasks.dll
 See  https://msdn.microsoft.com/en-us/library/dn410314.aspx

 Microsoft\Windows\CertificateServicesClient\CryptoPolicyTask 
 This task synchronizes cryptographic policy.
 system32\ngctasks.dll

 Microsoft\Windows\CertificateServicesClient\KeyPreGenTask 
 This task pre-generates TPM based Attestation Identity Key (AIK) and Storage Key (SK).
 system32\ngctasks.dll

 Microsoft\Windows\Location\WindowsActionDialog 
 Location Notification
 System32\WindowsActionDialog.exe

 Microsoft\Windows\MemoryDiagnostic\RunFullMemoryDiagnostic 
 Detects and mitigates problems in physical memory (RAM).
 System32\MemoryDiagnostic.dll

 Microsoft\Windows\Shell\FamilySafetyMonitor 
 Initialises Family Safety monitoring and enforcement.
 System32\wpcmon.exe

 Microsoft\Windows\Shell\FamilySafetyRefresh 
 Synchronises the latest settings with the Family Safety website.
 System32\WpcWebSync.dll

 Microsoft\Windows\Shell\IndexerAutomaticMaintenance 
 Keeps the search index up to date
 System32\srchadmin.dll

 Microsoft\Windows\Time Synchronization\ForceSynchronizeTime
 This task performs time synchronization.
 system32\TimeSyncTask.dll


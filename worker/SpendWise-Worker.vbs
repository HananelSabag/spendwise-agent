' SpendWise Worker — silent launcher (no console window).
' Optional argument "autostart" starts the sync loop immediately and opens
' minimised to the tray — used by the Windows-startup registration.
Dim shell, fso, here, ps1, extra
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
here = fso.GetParentFolderName(WScript.ScriptFullName)
ps1 = here & "\SpendWise-Worker.ps1"
extra = ""
If WScript.Arguments.Count > 0 Then
  If LCase(WScript.Arguments(0)) = "autostart" Then extra = " -AutoStart"
End If
shell.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & ps1 & """" & extra, 0, False

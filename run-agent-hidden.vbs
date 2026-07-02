' spendwise-agent — silent launcher for Windows Task Scheduler.
' Runs run-agent.bat without flashing a console window.
Set shell = CreateObject("WScript.Shell")
shell.CurrentDirectory = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName)
shell.Run "cmd /c run-agent.bat", 0, False

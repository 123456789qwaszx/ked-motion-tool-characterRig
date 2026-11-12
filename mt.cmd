@echo off
rem mt — 명령줄 도구를 짧게 부른다: mt validate a.motion-authoring.json
rem 빌드된 것이 없으면 먼저 빌드한다. 소스를 고쳤거나 받아 왔으면 mt-build 를 한 번 부른다.
setlocal
set "DLL=%~dp0src\Mt.Cli\bin\Release\net10.0\mt.dll"
if not exist "%DLL%" dotnet build "%~dp0src\Mt.Cli\Mt.Cli.csproj" -c Release --nologo -v quiet 1>&2
dotnet "%DLL%" %*
exit /b %ERRORLEVEL%

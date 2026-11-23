@echo off
rem library의 산출물을 다시 낸다 - 툴에서 저작본을 고쳐 저장한 뒤 한 번: library\export.cmd
rem 저작본 cards.motion-authoring.json -> export\motions.json(몸짓) . export\emojis.json(감정 아이콘). LibraryTests가 둘이 저작본과 같은지 본다.
setlocal
call "%~dp0..\mt.cmd" export "%~dp0cards.motion-authoring.json" "%~dp0export\motions.json" || exit /b 1
call "%~dp0..\mt.cmd" emojis "%~dp0cards.motion-authoring.json" "%~dp0export\emojis.json" || exit /b 1

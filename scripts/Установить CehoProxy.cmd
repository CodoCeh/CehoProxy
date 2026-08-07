@echo off
chcp 65001 >nul
title Установка CehoProxy
setlocal

rem Установщик для тех, кто не работает с командной строкой: двойной клик, согласие на
rem повышение прав, дальше мастер настройки. Всё, что он делает, делает и «cehoproxy install»,
rem поэтому логика не раздваивается.

set "HERE=%~dp0"
set "EXE=%HERE%cehoproxy.exe"

if not exist "%EXE%" (
  echo Рядом с этим файлом должен лежать cehoproxy.exe
  echo Положите оба файла в одну папку и запустите установку снова.
  echo.
  pause
  exit /b 1
)

rem Права администратора: без них не создать сетевой интерфейс и не прописать PATH
net session >nul 2>&1
if errorlevel 1 (
  echo Для установки нужны права администратора, сейчас появится запрос.
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b 0
)

"%EXE%" install
echo.
pause

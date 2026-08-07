# Установка CehoProxy на Windows — одной командой в PowerShell от имени администратора:
#
#   irm https://raw.githubusercontent.com/CodoCeh/CehoProxy/main/scripts/install.ps1 | iex
#
# Локальный файл тоже подойдёт:
#   .\install.ps1 -Source .\cehoproxy.exe
#
# Сразу за установкой открывается настройка. Тем, кто не работает с командной строкой,
# есть второй путь: «Установить CehoProxy.cmd» рядом с cehoproxy.exe — двойным щелчком.

param(
    [string]$Source = "",
    [string]$Repo = "CodoCeh/CehoProxy"
)

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Write-Host "Нужны права администратора: откройте PowerShell от имени администратора и повторите."
    exit 1
}

$root = Join-Path $env:ProgramData 'CehoProxy'
$exe  = Join-Path $root 'cehoproxy.exe'

if (-not $Source) {
    $arch = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
    if ($arch -ne 'x64') { Write-Host "Поддерживается только 64-разрядная Windows."; exit 1 }

    $url = "https://github.com/$Repo/releases/latest/download/cehoproxy-win-x64.exe"
    $tmp = Join-Path $env:TEMP 'cehoproxy-download.exe'
    Write-Host "Скачиваю программу из релизов $Repo…"
    try {
        Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
    } catch {
        Write-Host ""
        Write-Host "Скачать не удалось: $url"
        Write-Host "Так бывает, если релизов ещё нет или репозиторий закрыт."
        Write-Host "Тогда соберите программу сами и повторите с путём к файлу:"
        Write-Host "  .\install.ps1 -Source .\cehoproxy.exe"
        exit 1
    }
    $Source = $tmp
}

if (-not (Test-Path $Source)) { Write-Host "Не найден файл программы: $Source"; exit 1 }

New-Item -ItemType Directory -Force -Path $root | Out-Null
Copy-Item -Path $Source -Destination $exe -Force

# всё остальное — папка, короткая команда chp, PATH — делает сама программа,
# чтобы установка из скрипта и установка двойным щелчком не разъезжались
& $exe install

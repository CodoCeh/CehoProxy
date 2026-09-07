param(
    [string]$Source = "",
    [string]$Repo = "CodoCeh/CehoProxy"
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Write-Host "Нужны права администратора: откройте PowerShell от имени администратора и повторите."
    exit 1
}

$root = Join-Path $env:ProgramData 'CehoProxy'
$exe  = Join-Path $root 'cehoproxy.exe'
$engine = Join-Path $root 'ceho-engine.exe'
$engineLegacy = Join-Path $root 'sing-box.exe'

if (Test-Path $exe) {
    $old = & $exe version 2>$null | Select-Object -First 1
    if ($old) { Write-Host "Была установлена версия $old — заменяю её." }
}

# Прошлую версию надо остановить целиком: и задачу планировщика, и сам процесс.
# Работающий exe Windows заменить не даёт, а два экземпляра рядом — источник путаницы.
& schtasks /end /tn CehoProxy 2>$null | Out-Null
$running = Get-Process -Name 'cehoproxy','ceho-engine' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Останавливаю работающий CehoProxy перед заменой..."
    if (Test-Path $exe) { & $exe stop 2>$null | Out-Null }
    Start-Sleep -Milliseconds 800
    $running = Get-Process -Name 'cehoproxy','ceho-engine' -ErrorAction SilentlyContinue
    if ($running) { $running | Stop-Process -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 600
}

# Запись в «Установке и удалении программ» от прежнего установщика осталась бы висеть
# рядом с новой версией и показывала бы старый номер. Данные при этом не трогаем.
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6E2C3F41-8B7A-4E2D-9C1F-2A5D7B0E9C33}_is1'
if (Test-Path $uninstallKey) {
    Write-Host "Убираю запись прежнего установщика из списка программ."
    Remove-Item -Path $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $root -Filter 'unins*.*' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

if (-not $Source) {
    if (-not [Environment]::Is64BitOperatingSystem) {
        Write-Host "Поддерживается только 64-разрядная Windows."
        exit 1
    }

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

$hadConfig = Test-Path (Join-Path $root 'config.json')

New-Item -ItemType Directory -Force -Path $root | Out-Null

# Файл могли ещё не отпустить: пробуем несколько раз, а не падаем на первой попытке.
$copied = $false
for ($i = 1; $i -le 5 -and -not $copied; $i++) {
    try {
        Copy-Item -Path $Source -Destination $exe -Force
        $copied = $true
    } catch {
        Start-Sleep -Milliseconds 700
    }
}
if (-not $copied) {
    Write-Host "Не удалось заменить $exe — файл занят. Перезагрузите компьютер и повторите."
    exit 1
}

$sourceDir = Split-Path -Parent (Resolve-Path $Source)
$nearbyEngine = Join-Path $sourceDir 'ceho-engine.exe'
if (-not (Test-Path $nearbyEngine)) { $nearbyEngine = Join-Path $sourceDir 'sing-box.exe' }
if (Test-Path $nearbyEngine) {
    Copy-Item -Path $nearbyEngine -Destination $engine -Force
    Write-Host "Движок установлен из локального источника: $engine"
}

if ((Test-Path $engine) -or (Test-Path $engineLegacy)) { Write-Host "Движок уже установлен." }

Write-Host "Страница продукта: https://github.com/$Repo"

# Команда сама затирает файлы прошлой сборки, оставляет config.json и sub-*.txt,
# возвращает автозапуск и скачивает движок, если его ещё нет. Скачиванием занимается
# программа, а не скрипт: место одно, и оно одинаково работает на всех системах.
if ($hadConfig) {
    & $exe install --no-setup --with-engine
    Write-Host ""
    Write-Host "Обновление завершено, прежние настройки и подписки на месте."
    Write-Host "  chp             # состояние"
    Write-Host "  chp subs        # подписки, сроки и трафик"
    Write-Host "  chp log         # журнал и падения"
} else {
    & $exe install --with-engine
}

# Сюда попадаем, если движок скачать не вышло. На экране должна остаться
# одна команда, а не разбор, где его искать.
if (-not (Test-Path $engine) -and -not (Test-Path $engineLegacy) -and -not (Get-Command 'sing-box' -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "Движок sing-box скачать не удалось, без него туннель не поднимется."
    Write-Host "Повторить одной командой (PowerShell от имени администратора):"
    Write-Host "  chp engine"
}

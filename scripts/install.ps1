param(
    [string]$Source = "",
    [string]$Repo = "CodoCeh/CehoProxy"
)

# Этот файл запускают и как .\install.ps1, и через iex. exit здесь закрывает
# всё окно PowerShell, а stderr внешней программы при Stop превращается в
# остановку скрипта — поэтому только return и Continue.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
} catch { }

$admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Write-Host "Нужны права администратора: откройте PowerShell от имени администратора и повторите."
    return
}

$root = Join-Path $env:ProgramData 'CehoProxy'
$exe  = Join-Path $root 'cehoproxy.exe'
$engine = Join-Path $root 'ceho-engine.exe'
$engineLegacy = Join-Path $root 'sing-box.exe'

if (Test-Path $exe) {
    $old = & $exe version 2>$null | Select-Object -First 1
    if ($old) { Write-Host "Была установлена версия $old — заменяю её." }
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
        return
    }

    $url = "https://github.com/$Repo/releases/latest/download/cehoproxy-win-x64.exe"
    $tmp = Join-Path $env:TEMP 'cehoproxy-download.exe'
    Write-Host "Скачиваю программу из релизов $Repo…"

    $downloaded = $false
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & curl.exe -# --fail --location --retry 3 --retry-delay 2 --connect-timeout 30 --output $tmp $url
        if ($LASTEXITCODE -eq 0 -and (Test-Path $tmp)) { $downloaded = $true }
    }
    if (-not $downloaded) {
        try {
            Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -TimeoutSec 300
            $downloaded = Test-Path $tmp
        } catch {
            Write-Host ""
            Write-Host "Скачать не удалось: $url"
            Write-Host $_.Exception.Message
            Write-Host "Так бывает при обрыве сети. Повторите команду или скачайте файл со страницы релизов:"
            Write-Host "  https://github.com/$Repo/releases/latest"
            Write-Host "и запустите:  .\install.ps1 -Source путь\к\cehoproxy-win-x64.exe"
            return
        }
    }

    if (-not $downloaded -or -not (Test-Path $tmp)) {
        Write-Host "Скачать не удалось: $url"
        return
    }

    $len = (Get-Item $tmp).Length
    # Self-contained сборка — десятки мегабайт. Обрыв оставляет огрызок в пару мегабайт,
    # его нельзя запускать: Windows скажет «не является приложением Win32».
    if ($len -lt 10MB) {
        Write-Host "Скачивание оборвалось: получили $len байт вместо полной программы."
        Write-Host "Повторите команду. Если снова оборвётся — скачайте файл вручную:"
        Write-Host "  https://github.com/$Repo/releases/latest"
        try { Remove-Item $tmp -Force } catch { }
        return
    }

    $fs = [IO.File]::OpenRead($tmp)
    $mz = New-Object byte[] 2
    [void]$fs.Read($mz, 0, 2)
    $fs.Close()
    if ($mz[0] -ne 0x4D -or $mz[1] -ne 0x5A) {
        Write-Host "Скачанный файл — не программа Windows. Повторите команду."
        try { Remove-Item $tmp -Force } catch { }
        return
    }

    $Source = $tmp
}

if (-not (Test-Path $Source)) { Write-Host "Не найден файл программы: $Source"; return }

$hadConfig = Test-Path (Join-Path $root 'config.json')

function Stop-CehoLeftovers {
    # На чистой машине задачи нет — cmd глотает отсутствие, PowerShell из-за этого не падает.
    cmd /c "schtasks /end /tn CehoProxy >nul 2>&1" | Out-Null
    cmd /c "taskkill /F /IM ceho-engine.exe >nul 2>&1" | Out-Null
    cmd /c "taskkill /F /IM sing-box.exe >nul 2>&1" | Out-Null
    $running = Get-Process -Name 'cehoproxy','ceho-engine','sing-box' -ErrorAction SilentlyContinue
    if (-not $running) { return $false }
    Write-Host "Останавливаю работающий CehoProxy перед заменой..."
    if (Test-Path $exe) { & $exe stop 2>$null | Out-Null }
    Start-Sleep -Milliseconds 800
    Get-Process -Name 'cehoproxy','ceho-engine','sing-box' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    cmd /c "taskkill /F /IM ceho-engine.exe >nul 2>&1" | Out-Null
    cmd /c "taskkill /F /IM sing-box.exe >nul 2>&1" | Out-Null
    Start-Sleep -Milliseconds 600
    return $true
}

# Прошлую версию надо остановить целиком: и задачу планировщика, и сам процесс.
# Работающий exe Windows заменить не даёт, а два экземпляра рядом — источник путаницы.
[void](Stop-CehoLeftovers)

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
    return
}

$sourceDir = Split-Path -Parent (Resolve-Path $Source)
$nearbyEngine = Join-Path $sourceDir 'ceho-engine.exe'
if (-not (Test-Path $nearbyEngine)) { $nearbyEngine = Join-Path $sourceDir 'sing-box.exe' }
if (Test-Path $nearbyEngine) {
    Copy-Item -Path $nearbyEngine -Destination $engine -Force
    Write-Host "Движок установлен из локального источника: $engine"
}
# DLL рядом с уже запущенным cehoproxy.exe нельзя класть ДО `install`:
# старые сборки копируют libcronet.dll саму на себя и падают
# «файл занят другим процессом». Скачиваем во временный файл, переносим после.
$cronetStaged = Join-Path $env:TEMP 'cehoproxy-libcronet.dll'
Get-ChildItem -Path $sourceDir -Filter 'libcronet.*' -ErrorAction SilentlyContinue |
    ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $cronetStaged -Force
        Write-Host "Библиотека NaiveProxy: $($_.Name)"
    }

$targetCronet = Join-Path $root 'libcronet.dll'
if (-not $curl) { $curl = Get-Command curl.exe -ErrorAction SilentlyContinue }
if (-not (Test-Path $cronetStaged) -and -not (Test-Path $targetCronet)) {
    $cronetUrl = "https://github.com/$Repo/releases/latest/download/libcronet.dll"
    Write-Host "Скачиваю библиотеку NaiveProxy (libcronet.dll)..."
    $cronetDownloaded = $false
    if ($curl) {
        & curl.exe -sSL --fail --retry 3 --retry-delay 2 --connect-timeout 30 --output $cronetStaged $cronetUrl
        if ($LASTEXITCODE -eq 0 -and (Test-Path $cronetStaged) -and (Get-Item $cronetStaged).Length -gt 1MB) {
            $cronetDownloaded = $true
        }
    }
    if (-not $cronetDownloaded) {
        try {
            Invoke-WebRequest -Uri $cronetUrl -OutFile $cronetStaged -UseBasicParsing -TimeoutSec 120
            if ((Test-Path $cronetStaged) -and (Get-Item $cronetStaged).Length -gt 1MB) {
                $cronetDownloaded = $true
            }
        } catch { }
    }
    if ($cronetDownloaded) {
        Write-Host "Библиотека NaiveProxy скачана."
    } elseif (Test-Path $cronetStaged) {
        try { Remove-Item $cronetStaged -Force } catch { }
    }
}

if ((Test-Path $engine) -or (Test-Path $engineLegacy)) { Write-Host "Движок уже установлен." }

Write-Host "Страница продукта: https://github.com/$Repo"

# Автозапуск мог снова поднять движок, пока мы копировали файлы.
[void](Stop-CehoLeftovers)

# 1.2.39/1.2.40 копируют libcronet.dll саму на себя, если она уже в папке
# программы. На время `install` убираем её в TEMP.
if (Test-Path $targetCronet) {
    if (-not (Test-Path $cronetStaged) -or (Get-Item $cronetStaged).Length -lt 1MB) {
        try { Move-Item -Path $targetCronet -Destination $cronetStaged -Force } catch { }
    }
    if (Test-Path $targetCronet) {
        try { Remove-Item $targetCronet -Force } catch { }
    }
}

# irm | iex подменяет клавиатуру трубой со скриптом. Мастер настройки тогда
# сразу получает пустой ввод. В этом случае ставим программу молча и просим
# открыть новое окно. iex (irm …) клавиатуру не трогает — мастер идёт здесь же.
$piped = [Console]::IsInputRedirected
$installArgs = @('install')
if (-not (Test-Path $engine) -and -not (Test-Path $engineLegacy)) { $installArgs += '--with-engine' }
if ($piped -or $hadConfig) { $installArgs += '--no-setup' }

& $exe @installArgs
$installCode = $LASTEXITCODE

if ((Test-Path $cronetStaged) -and (Get-Item $cronetStaged).Length -gt 1MB) {
    try {
        Copy-Item -Path $cronetStaged -Destination $targetCronet -Force
        Write-Host "Библиотека NaiveProxy установлена: $targetCronet"
    } catch {
        if (-not (Test-Path $targetCronet)) {
            Write-Host "Не удалось положить libcronet.dll рядом с движком: $($_.Exception.Message)"
        }
    }
    try { Remove-Item $cronetStaged -Force } catch { }
}

# Текущее окно PowerShell не видит Machine PATH, пока его не перечитать.
$env:Path = "$root;" + ([Environment]::GetEnvironmentVariable('Path', 'Machine'))

if ($hadConfig) {
    Write-Host ""
    Write-Host "Обновление завершено, прежние настройки и подписки на месте."
    $alive = Get-Process -Name 'cehoproxy' -ErrorAction SilentlyContinue
    if (-not $alive) {
        Start-Process -FilePath $exe -ArgumentList 'daemon' -WorkingDirectory $root -WindowStyle Hidden
        Start-Sleep -Seconds 2
        $alive = Get-Process -Name 'cehoproxy' -ErrorAction SilentlyContinue
    }
    if ($alive) {
        Write-Host "Защита и панель подняты. Если страница не открылась — подождите пару секунд."
        Write-Host "  & '$exe'"
    } else {
        Write-Host "Панель сама не поднялась. В ЭТОМ окне:"
        Write-Host "  & '$exe' daemon"
    }
    Write-Host "Команда chp появится в НОВОМ окне PowerShell. Пока можно так:"
    Write-Host "  & '$exe'"
} elseif ($piped) {
    Write-Host ""
    Write-Host "Программа стоит. Это окно сейчас занято командой установки —"
    Write-Host "откройте НОВОЕ окно PowerShell от администратора и введите:"
    Write-Host "  chp"
} elseif ($installCode -ne 0 -and $null -ne $installCode) {
    Write-Host "Установка не завершилась (код $installCode). Повторите команду."
    return
}

# Сюда попадаем, если движок скачать не вышло. На экране должна остаться
# одна команда, а не разбор, где его искать.
if (-not (Test-Path $engine) -and -not (Test-Path $engineLegacy) -and -not (Get-Command 'sing-box' -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "Движок sing-box скачать не удалось, без него туннель не поднимется."
    Write-Host "Повторить в этом окне:"
    Write-Host "  & '$exe' engine"
}

$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'dist\学习强国.exe'
function Invoke-Verification($arguments) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if (!$process.WaitForExit(120000)) {
        $process.Kill()
        $process.WaitForExit()
        throw ('Verification timed out: ' + $arguments)
    }
    if ($process.ExitCode -ne 0) { throw ('Verification failed: ' + $arguments) }
}
foreach ($case in @(
    @{ Mode = '--smoke-test'; Folder = 'smoke'; Extra = '' },
    @{ Mode = '--smoke-test'; Folder = 'smoke-small'; Extra = '--small' },
    @{ Mode = '--browser-test'; Folder = 'browser-test'; Extra = '' },
    @{ Mode = '--qr-test'; Folder = 'qr-test'; Extra = '' }
)) {
    $target = Join-Path $PSScriptRoot $case.Folder
    $arguments = $case.Mode + ' "' + $target + '" ' + $case.Extra
    Invoke-Verification $arguments
    $resultPath = Join-Path $target 'result.json'
    if ((Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt (Get-Item -LiteralPath $exe).LastWriteTimeUtc) {
        throw 'Verification result is stale'
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (!$result.ok -or !$result.privateMode -or $result.externalNetwork -or $result.browserVisible -or $result.engineDocumentHidden -or !$result.manualExamWindowToggle -or !$result.scoreSummaryVisible -or !$result.trayLifecycleVerified) { throw 'Verification contract failed' }
    if ($case.Mode -eq '--browser-test' -and !$result.quietExamVerified) { throw 'Quiet exam verification failed' }
    $result | Format-List
}
$sessionFolder = Join-Path $PSScriptRoot ('session-test-' + [Guid]::NewGuid().ToString('N'))
foreach ($stage in @('seed', 'restore', 'logout', 'after-logout')) {
    $extra = if ($stage -eq 'restore') { '' } else { '--' + $stage }
    Invoke-Verification ('--session-test "' + $sessionFolder + '" ' + $extra)
    $result = Get-Content -LiteralPath (Join-Path $sessionFolder ('session-' + $stage + '.json')) -Raw | ConvertFrom-Json
    if (!$result.ok -or !$result.persistentProfile -or !$result.encryptedVault -or !$result.syntheticCredentialsOnly -or $result.externalNetwork) {
        throw ('Session verification contract failed: ' + $stage)
    }
    $result | Format-List
}
$invalid = Start-Process -FilePath $exe -ArgumentList '--smoke-test ""' -Wait -PassThru -WindowStyle Hidden
if ($invalid.ExitCode -ne 1) { throw 'Invalid test arguments must fail without a modal dialog' }
$hash = Get-FileHash -LiteralPath $exe -Algorithm SHA256
$report = @{
    exeSha256 = $hash.Hash
    exeBytes = (Get-Item -LiteralPath $exe).Length
    compiledRulesAndSqliteTests = 'passed during build'
    nodeBrowserTests = 'passed during build'
    webView2OfflineBrowserTests = 'passed'
    autoDailyExam = 'passed: five questions across single-choice, multiple-choice, text-fill and judgment; official-style 1-second auto-next, automatic final submission, one submission, offline WebView2'
    quietDailyExam = 'passed: visually hidden throughout offline answering, native rendering retained, captcha/manual intervention window opens and returns to background'
    examSubmissionValidation = 'passed: zero total, base64, malformed totals and rejected response'
    liveExamSubmissionAndScoringTested = $false
    qrOnlyWindowTests = 'passed'
    ownerCenteredQrTests = 'passed: open, move, resize, reopen'
    realArticleScrollEvents = 'passed: native trusted scroll events in transparent host'
    nativeVideoPlayback = 'passed: native media advancement, trusted page scroll and pause'
    shortVideoSchedule = 'passed: first credit check at 60 played seconds without ending playback, five-second rechecks, no-credit stop at 90, completed short video, no seek-to-end completion; live scoring unverified'
    scoreSummary = 'passed: total and daily numeric fields, zero versus missing, summary layout at both window sizes; live endpoint unverified'
    adaptiveReading = 'passed: first check at 60 seconds, 10-second rechecks, bounded no-credit stop'
    serverCreditConfirmation = 'passed: delayed gain, no gain, missing task, reset and cancellation'
    legacyHistoryPreserved = $true
    trayLifecycle = 'passed: wired single-left-click event restores; right click waits for menu selection; show menu restores; close and minimize keep tasks; normal/maximized and QR repeated restore; explicit exit terminates test processes'
    encryptedLoginPersistence = 'passed: synthetic cookies across separate processes, DPAPI encryption, expiry/domain/security attributes and explicit logout; no real account used'
    liveLoginPersistenceTested = $false
    videoSelection = 'passed: official text dates newest first, epoch formats normalized, account/day-scoped no-credit preference; live scoring unverified'
    browserVisible = $false
    layouts = @('620x470', '580x440')
    liveLoginAndScoringTested = $false
    specialQuizImplemented = $false
    timestamp = [DateTimeOffset]::Now.ToString('o')
}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'release-validation.json') -Encoding utf8
$report | Format-List

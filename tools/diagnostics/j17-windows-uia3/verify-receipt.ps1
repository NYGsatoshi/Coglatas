function Assert-SyntheticWindowsReceipt {
    param([Parameter(Mandatory = $true)]$Receipt)
    if ($Receipt.scope -cne 'ISOLATED_SYNTHETIC_WINDOWS_UIA3_POC' -or
        $Receipt.qualification -cne 'PASS' -or $Receipt.actualWindowsUiAutomation -cne 'PASS' -or
        $Receipt.stage -cne 'window-and-process-close' -or $Receipt.errorType -cne 'None' -or
        $Receipt.imeComposition -cne 'NOT_RUN' -or $Receipt.productClientAcceptance -cne 'BLOCKED' -or
        $Receipt.screenReaderAcceptance -cne 'NOT_RUN' -or $Receipt.dependencyAdoption -cne 'NOT_ADOPTED' -or
        $Receipt.release -cne 'NO_GO' -or $Receipt.cleanupSucceeded -isnot [bool] -or -not $Receipt.cleanupSucceeded) {
        throw 'Synthetic evidence axes are incomplete or inconsistent.'
    }
    $names = @('interactiveDesktop', 'ownedProcessAndAutomationIds', 'uiaAccessibleName', 'actualKeyboardText', 'tabFocusTraversal', 'keyboardActivation', 'closedWindowAndProcess')
    if (@($Receipt.checks.PSObject.Properties).Count -ne $names.Count) { throw 'Synthetic check roster differs.' }
    foreach ($name in $names) {
        if ($Receipt.checks.$name -isnot [bool] -or $Receipt.checks.$name -ne $true) { throw 'A required synthetic UI observation is absent.' }
    }
}

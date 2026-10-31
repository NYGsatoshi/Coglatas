param(
    [string]$ProjectId = "YOUR_GCP_PROJECT_ID",
    [string]$Zone = "us-central1-a",
    [string]$VmName = "coglatas-dev",
    [string]$MachineType = "e2-medium",
    [string]$BootDiskSize = "30GB",
    [string]$ImageFamily = "ubuntu-2404-lts-amd64",
    [string]$ImageProject = "ubuntu-os-cloud",
    [string]$NetworkTag = "coglatas-web",
    [string]$RepoUrl = "https://github.com/NYGsatoshi/Coglatas.git",
    [switch]$RunBootstrap,
    [switch]$RunDeploy
)

$ErrorActionPreference = "Stop"
$GcloudCommand = "gcloud.cmd"

if ($ProjectId -eq "YOUR_GCP_PROJECT_ID") {
    throw "Set -ProjectId to your GCP project ID."
}

function Invoke-Gcloud {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
    & $GcloudCommand @Args
    if ($LASTEXITCODE -ne 0) {
        throw "gcloud command failed: gcloud $($Args -join ' ')"
    }
}

function Invoke-GcloudOptionalValue {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $GcloudCommand @Args 2>$null
        if ($LASTEXITCODE -eq 0) {
            return ($output | Out-String).Trim()
        }

        return $null
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

Write-Host "Using project $ProjectId in zone $Zone"
Invoke-Gcloud config set project $ProjectId
Invoke-Gcloud services enable compute.googleapis.com

$existingVm = Invoke-GcloudOptionalValue compute instances describe $VmName --zone $Zone --format "value(name)"
if ([string]::IsNullOrWhiteSpace($existingVm)) {
    Write-Host "Creating VM $VmName..."
    Invoke-Gcloud compute instances create $VmName `
        --zone $Zone `
        --machine-type $MachineType `
        --boot-disk-size $BootDiskSize `
        --image-family $ImageFamily `
        --image-project $ImageProject `
        --tags $NetworkTag
} else {
    Write-Host "VM $VmName already exists; skipping create."
}

Write-Host "No public web firewall rule is created. Development access requires an encrypted SSH tunnel."

Write-Host "Copying GCP helper scripts to VM home directory..."
$remoteUser = Invoke-GcloudOptionalValue compute ssh $VmName --zone $Zone --quiet --command "whoami"
if ([string]::IsNullOrWhiteSpace($remoteUser)) {
    throw "Could not determine the remote SSH user for $VmName."
}

$remoteBase = "/home/$remoteUser/coglatas-gcp"
Invoke-Gcloud compute ssh $VmName --zone $Zone --command "mkdir -p '$remoteBase'"
Invoke-Gcloud compute scp --recurse ".\deploy\gcp" "${VmName}:$remoteBase" --zone $Zone
Invoke-Gcloud compute ssh $VmName --zone $Zone --command "chmod +x '$remoteBase/gcp/'*.sh"

if ($RunBootstrap) {
    Write-Host "Running bootstrap-vm.sh on VM..."
    Invoke-Gcloud compute ssh $VmName --zone $Zone --command "bash '$remoteBase/gcp/bootstrap-vm.sh'"
}

if ($RunDeploy) {
    Write-Host "Running deploy-app.sh on VM..."
    Invoke-Gcloud compute ssh $VmName --zone $Zone --command "REPO_URL='$RepoUrl' bash '$remoteBase/gcp/deploy-app.sh'"
}

Write-Host ""
Write-Host "VM is ready."
Write-Host "SSH:"
Write-Host "  gcloud compute ssh $VmName --zone $Zone"
Write-Host ""
Write-Host "On the VM:"
Write-Host "  bash ~/coglatas-gcp/gcp/bootstrap-vm.sh"
Write-Host "  REPO_URL='$RepoUrl' bash ~/coglatas-gcp/gcp/deploy-app.sh"
Write-Host ""
Write-Host "Development access after deploy (keep this SSH session open):"
Write-Host "  gcloud compute ssh $VmName --zone $Zone -- -N -L 8080:127.0.0.1:8080 -o ExitOnForwardFailure=yes"
Write-Host "  Open http://127.0.0.1:8080 on your local computer."
Write-Host "Existing firewall rules are unchanged; any target-specific cleanup requires operator approval."

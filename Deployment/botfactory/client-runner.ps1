# Appended after the existing client001 VPN guard; placeholders are pinned on publication.
$ErrorActionPreference = 'Stop'
$out = '{output-folder}'
New-Item -ItemType Directory -Force $out | Out-Null
$phase = 'validate-input'
$ssh = $null
$sftp = $null
$remote = $null
$completed = $false
try {
    $commandName = '{input-string}'.Trim().TrimEnd(';')
    if ($commandName -notin @('BUSINESSENTITY-INSPECT','BUSINESSENTITY-INSTALL')) { throw 'Unknown command' }
    $mode = if ($commandName -eq 'BUSINESSENTITY-INSTALL') { 'install' } else { 'inspect' }
    $manifests = @(Get-ChildItem -LiteralPath '{input-folder}' -Filter manifest.json -File -Recurse)
    if ($manifests.Count -ne 1) { throw 'Manifest count' }
    $manifestPath = $manifests[0].FullName
    if ((Get-FileHash $manifestPath -Algorithm SHA256).Hash -ne '__MANIFEST_SHA256__') { throw 'Manifest pin mismatch' }
    $base = $manifests[0].DirectoryName
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files.PSObject.Properties) {
        if ($entry.Name -notin @('host_install.py','request.json','source.zip')) { throw 'Unexpected payload file' }
        if ((Get-FileHash (Join-Path $base $entry.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) { throw 'Payload pin mismatch' }
    }
    $approval = ''
    if ($mode -eq 'install') {
        $review = Get-Content (Join-Path $base 'approved-plan.json') -Raw | ConvertFrom-Json
        if (-not $review.success -or $review.approval -notmatch '^[a-f0-9]{64}$' -or $review.plan.payloadSha256 -ne $manifest.files.'source.zip') { throw 'Reviewed plan required' }
        $approval = [string]$review.approval
    }
    $phase = 'connect-client001'
    $vpn = Ensure-UserHubVpn
    if (-not $vpn.success) { throw 'VPN unavailable' }
    Add-Type -Path 'C:\Develop\code-base-enlighter\3.7.0\Renci.SshNet.dll'
    $profiles = Get-Content (Join-Path $env:APPDATA 'Geo4WebDeployer\hosts.json') -Raw | ConvertFrom-Json
    $found = @($profiles.hosts | Where-Object { $_.hostName -eq '192.168.55.15' -and $_.port -eq 22 })
    if ($found.Count -ne 1) { throw 'Host profile unavailable' }
    $profile = $found[0]
    if (-not ('BusinessEntityHostPin' -as [type])) {
        Add-Type -TypeDefinition @'
public static class BusinessEntityHostPin {
    // Bind both SSH and SFTP to the already verified target host key.
    public static void Apply(object client) {
        var ev = client.GetType().GetEvent("HostKeyReceived");
        ev.AddEventHandler(client, System.Delegate.CreateDelegate(ev.EventHandlerType, typeof(BusinessEntityHostPin).GetMethod("Verify")));
    }
    public static void Verify(object sender, object e) {
        var key = (byte[])e.GetType().GetProperty("HostKey").GetValue(e);
        var value = "SHA256:" + System.Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(key)).TrimEnd('=');
        e.GetType().GetProperty("CanTrust").SetValue(e, value == "SHA256:QOuiJp4IdHFLDAuQIBM6zdTK4MLA6q+XMK4SR7shgeI");
    }
}
'@
    }
    $ssh = [Renci.SshNet.SshClient]::new('192.168.55.15',22,[string]$profile.userName,[string]$profile.password)
    [BusinessEntityHostPin]::Apply($ssh)
    $ssh.ConnectionInfo.Timeout = [TimeSpan]::FromSeconds(25)
    $ssh.Connect()
    $created = $ssh.RunCommand('mktemp -d /tmp/be-bot-XXXXXXXXXX')
    $remote = $created.Result.Trim()
    if ($created.ExitStatus -ne 0 -or $remote -notmatch '^/tmp/be-bot-[A-Za-z0-9]{10}$') { throw 'Remote staging failed' }
    $phase = 'transfer-pinned-payload'
    $sftp = [Renci.SshNet.SftpClient]::new('192.168.55.15',22,[string]$profile.userName,[string]$profile.password)
    [BusinessEntityHostPin]::Apply($sftp)
    $sftp.Connect()
    foreach ($entry in $manifest.files.PSObject.Properties) {
        $stream = [IO.File]::OpenRead((Join-Path $base $entry.Name))
        try { $sftp.UploadFile($stream, "$remote/$($entry.Name)", $false) } finally { $stream.Dispose() }
    }
    $phase = "host-$mode"
    $hashJson = $manifest.files | ConvertTo-Json -Compress
    $hashBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($hashJson))
    # All interpolated shell/Python values below are fixed enums, validated paths or base64.
    $python = @"
import base64, hashlib, json, pathlib, runpy, sys
root=pathlib.Path('$remote')
expected=json.loads(base64.b64decode('$hashBase64'))
for name, sha in expected.items():
    if name not in ('host_install.py','request.json','source.zip'): raise SystemExit(2)
    with (root/name).open('rb') as stream:
        if hashlib.file_digest(stream,'sha256').hexdigest()!=sha: raise SystemExit(2)
sys.argv=[str(root/'host_install.py'),'$mode','--request',str(root/'request.json'),'--source',str(root/'source.zip'),'--approval','$approval']
runpy.run_path(str(root/'host_install.py'),run_name='__main__')
"@
    $probe = $ssh.RunCommand('sudo -n true >/dev/null 2>&1')
    $needsPassword = $probe.ExitStatus -ne 0
    $remoteCommand = $ssh.CreateCommand($(if ($needsPassword) { "sudo -S -p '' python3 -B -" } else { 'sudo -n python3 -B -' }))
    $remoteCommand.CommandTimeout = [TimeSpan]::FromMinutes(110)
    $async = $remoteCommand.BeginExecute()
    $inputStream = $remoteCommand.CreateInputStream()
    $body = $(if ($needsPassword) { [string]$profile.password + "`n" } else { '' }) + $python
    $bytes = [Text.Encoding]::UTF8.GetBytes($body)
    $inputStream.Write($bytes, 0, $bytes.Length)
    $inputStream.Dispose()
    $remoteCommand.EndExecute($async)
    $completed = $true
    $result = $remoteCommand.Result | ConvertFrom-Json
    if ($remoteCommand.ExitStatus -ne 0 -and $result.success) { throw 'Host status mismatch' }
    $result | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $out 'result.json') -Encoding utf8
} catch {
    @{success=$false;failedPhase=$phase;errorType=$_.Exception.GetType().Name;remoteCompletionKnown=$completed} |
        ConvertTo-Json | Set-Content (Join-Path $out 'result.json') -Encoding utf8
} finally {
    if ($null -ne $sftp) { $sftp.Dispose() }
    if ($null -ne $ssh) {
        if ($completed -and $ssh.IsConnected -and $remote -match '^/tmp/be-bot-[A-Za-z0-9]{10}$') { $null = $ssh.RunCommand("rm -rf -- '$remote'") }
        $ssh.Dispose()
    }
}

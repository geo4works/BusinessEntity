$script:UserHubVpnUser = @'
{vpn-g05-username}
'@
$script:UserHubVpnPassword = @'
{vpn-g05-password}
'@
$script:UserHubVpnUser = $script:UserHubVpnUser.Trim()
$script:UserHubVpnPassword = $script:UserHubVpnPassword.TrimEnd([char]13,[char]10)
if (-not ('UserHubRasNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

// Shared by the managed deployer and its generated PowerShell preflight.
// The existing G05 phonebook entry supplies server, PSK and routing. Only the
// already configured client-vault username/password are supplied in memory.
public static class UserHubRasNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DialParameters
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Entry;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string Phone;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string Callback;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Username;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Password;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string Domain;
        public uint Subentry;
        public UIntPtr CallbackId;
        public uint InterfaceIndex;
    }
    [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RasDialW")]
    private static extern uint RasDial(IntPtr extensions, string phonebook, ref DialParameters parameters,
        uint notifierType, IntPtr notifier, out IntPtr connection);
    [DllImport("rasapi32.dll", EntryPoint = "RasHangUpW")]
    private static extern uint RasHangUp(IntPtr connection);
    public static uint Dial(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) || username.Length > 256 || password.Length > 256) return 87;
        var parameters = new DialParameters {Entry = "G05", Phone = "", Callback = "", Username = username, Password = password, Domain = ""};
        parameters.Size = (uint)Marshal.SizeOf<DialParameters>();
        var code = RasDial(IntPtr.Zero, null!, ref parameters, 0, IntPtr.Zero, out var connection);
        parameters.Username = ""; parameters.Password = "";
        if (code != 0 && connection != IntPtr.Zero) RasHangUp(connection);
        // A successful connection deliberately remains available for the user's environment.
        return code;
    }
}

'@
}
# Shared client-side guard. Reuses the existing G05 profile and credentials from the existing client vault.
# It never creates/edits a VPN profile, WireGuard configuration or explicit routes.
function Test-UserHubServerReachable {
    foreach ($port in @(22,8443)) {
        $tcp = [Net.Sockets.TcpClient]::new()
        try { $pending = $tcp.ConnectAsync('192.168.55.15',$port); if ($pending.Wait(2500) -and $tcp.Connected) { return $true } }
        catch { }
        finally { $tcp.Dispose() }
    }
    return $false
}
function Invoke-UserHubRasdial([bool]$disconnect) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $env:SystemRoot 'System32\rasdial.exe'
    $start.Arguments = $(if ($disconnect) {'G05 /disconnect'} else {'G05'})
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true; $start.RedirectStandardInput = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.Close() # No interactive username/password prompts.
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill(); return -1 }
        $null = $stdout.GetAwaiter().GetResult(); $null = $stderr.GetAwaiter().GetResult()
        return $process.ExitCode
    } finally { $process.Dispose() }
}
function Ensure-UserHubVpn {
    if (Test-UserHubServerReachable) { return @{success=$true; action='already-reachable'; profile='G05'} }
    $mutex = [Threading.Mutex]::new($false, 'Local\UserHub.G05.NetworkGuard')
    $owned = $false
    try {
    try { $owned=$mutex.WaitOne([TimeSpan]::FromSeconds(90)) } catch [Threading.AbandonedMutexException] { $owned=$true }
    if (-not $owned) { return @{success=$false;action='guard-busy';profile='G05'} }
    if (Test-UserHubServerReachable) { return @{success=$true; action='already-reachable'; profile='G05'} }
    # The user explicitly authorized reconnecting this existing corporate VPN on loss.
    $null = Invoke-UserHubRasdial $true
    $code = [UserHubRasNative]::Dial($script:UserHubVpnUser, $script:UserHubVpnPassword)
    if ($code -ne 0) { return @{success=$false; action='connect-failed'; profile='G05'; code=$code} }
    $until = [DateTime]::UtcNow.AddSeconds(30)
    do {
        if (Test-UserHubServerReachable) { return @{success=$true; action='reconnected'; profile='G05'} }
        Start-Sleep -Milliseconds 750
    } while ([DateTime]::UtcNow -lt $until)
    return @{success=$false; action='server-unreachable-after-connect'; profile='G05'; code=0}
    } finally { if($owned){$mutex.ReleaseMutex()};$mutex.Dispose() }
}

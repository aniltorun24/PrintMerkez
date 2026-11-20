[CmdletBinding()]
param([string]$Domain='trnavm.com',[string]$Destination)
$ErrorActionPreference='Stop'
Import-Module ActiveDirectory -ErrorAction Stop
$domainInfo=Get-ADDomain -Identity $Domain -ErrorAction Stop
if (!$Destination) {
    $projectRoot=Split-Path -Parent $PSScriptRoot
    $folder=Join-Path $projectRoot 'src/PrintMerkez.Web/App_Data/directory-import'
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $Destination=Join-Path $folder ('ad-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.json')
}
if(Test-Path -LiteralPath $Destination){throw 'Aktarım dosyası zaten var; üzerine yazılmadı.'}
$users=Get-ADUser -Filter * -SearchBase $domainInfo.DistinguishedName -Properties DisplayName,Department,Office,Mail,Manager,isCriticalSystemObject
$records=@($users | ForEach-Object {
    [ordered]@{ObjectGuid=$_.ObjectGuid.ToString();SamAccountName=$_.SamAccountName;Name=$_.Name;
        DisplayName=$_.DisplayName;Department=$_.Department;Office=$_.Office;Email=$_.Mail;Manager=$_.Manager;
        DistinguishedName=$_.DistinguishedName;Enabled=[bool]$_.Enabled;CriticalSystem=[bool]$_.isCriticalSystemObject;Sid=$_.SID.Value}
})
$payload=[ordered]@{DomainDns=$domainInfo.DNSRoot;DomainNetBios=$domainInfo.NetBIOSName;BaseDn=$domainInfo.DistinguishedName;
    ExportedUtc=[DateTime]::UtcNow.ToString('O');ExportedBy=[Security.Principal.WindowsIdentity]::GetCurrent().Name;Users=$records}
$payload | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Destination -Encoding UTF8
Write-Output ('AD salt okunur aktarımı: '+$records.Count+' hesap. Dosya: '+$Destination)

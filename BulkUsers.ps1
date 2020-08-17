<#
Created By: Mariel Borodkin
Created Date: 20/3/2020

.SYNOPSIS
    Bulk creates 365 Users from SQL
.DESCRIPTION
    Bulk script using SQL including your organization to bulk all users
.EXAMPLE
.LINK
    Git:            https://github.com/ahyamrel/BulkUsers
#>
 
# Required variables from admin
Set-Variable -Name domain         -Value ""  -Option AllScope
Set-Variable -Name areacode		  -Value ""  -Option AllScope
Set-Variable -Name Logfile        -Value "." -Option AllScope
Set-Variable -Name UsageLocation  -Value ""  -Option AllScope

#Query data from SQL
$Params = @{
   'ServerInstance' = '';
   'Database' = '';
   'Username' = '';
   'Password' = '';
   'Query' = '';
}

$output = Invoke-Sqlcmd @params

# TODO approve
#region Approvals before starting
    #region Users approve
    Write-Host "The following steps are required: `
    * Approve the table to insert by properties (OK to continue)" -ForegroundColor Yellow
    $approve = $output | Out-GridView -Title Approval -PassThru
    if ($null -eq $approve) {
        Write-Host "** FAILED: Didn't approve the table, please modify the fields before running - Exist Error $($EXIT_USER_LEFT)" -ForegroundColor red
        exit
    } else {
        Write-Host "Approved table fields" -ForegroundColor Green
    }
        #endregion .. Users approve  
    #endregion .. Approvals before starting
     
#region split array # TODO modify to variables & based on RAM and CPU usage
$parts = 250 
$PartSize = [Math]::Ceiling($output.count / $parts)
$outArray = @()

for ($i=1; $i -le $parts; $i++) {
    $start = (($i-1)*$PartSize)
    $end = (($i)*$PartSize) - 1
    if ($end -ge $output.count) {$end = $output.count}
    $outArray+=,@($output[$start..$end])
}
#endregion

#region Scriptblock
$scriptblock = {      
    $Users = $args 
    
    # Import modules
    install-packageprovider -name nuget -minimumversion 2.8.5.201 -force
    Install-Module MSOnline -Force
    import-module MSOnline  

    # User with Create User permissions
    $User = ""
    $PWord = ConvertTo-SecureString -String "" -AsPlainText -Force 
    $UserCredential = New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $User, $PWord

    # Auth to service
    Connect-MsolService -Credential $UserCredential


    foreach ($User in $Users)
    {
        
        $upnName = $User.TZ -replace '\s',''
        $UPN = $upnName + "$domain"
        $fullname = $User.FirstName + " " + $User.LastName
		
        $phone = "$($areacode) $($User.MobilePhone)"        
        
        try {
            if (-Not (Get-MsolUser -UserPrincipalName $UPN -ErrorAction Ignore)) {
                # Create the new account into temp object to not print the details in shell.
                $tempvar = New-MsolUser -UserPrincipalName $UPN -DisplayName $fullname -FirstName $User.FirstName -LastName $User.LastName -PhoneNumber $phone -MobilePhone $phone  -UsageLocation $UsageLocation
                start-sleep -Seconds 2
            } else {
            Set-MsolUser -UserPrincipalName $UPN -DisplayName $fullname -FirstName $User.FirstName -LastName $User.LastName -UsageLocation $UsageLocation
            start-sleep -Seconds 1
            }
        } catch {
            $User | export-csv -Path .\failed.csv -Append -Encoding UTF8
            Start-Sleep -Seconds 5
        }
    }
}
#endregion Scriptblock

$outArray | ForEach-Object {
    Start-Job -Name Import -ScriptBlock $scriptblock -ArgumentList $_
}

get-job | Wait-Job
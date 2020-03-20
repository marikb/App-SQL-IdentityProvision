<#
Created By: Mariel Borodkin
Created Date: 20/3/2020

.SYNOPSIS
    Bulk creates 365 Users from Excel table
.DESCRIPTION
    Bulk script using excel including your organization to bulk all users
.EXAMPLE
.LINK
    Git:            https://github.com/ahyamrel/BulkUsers
    Excel Template: TODO Update
#>
 
#TODO modify to excel
$Params = @{
   'ServerInstance' = '';
   'Database' = '';
   'Username' = '';
   'Password' = '';
   'Query' = '';
}

#region split array
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
    #region Auhenticate 

    # User with Create User permissions
    $User = ""
    $PWord = ConvertTo-SecureString -String "" -AsPlainText -Force 
    $UserCredential = New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $User, $PWord

    # Auth service
    Connect-MsolService -Credential $UserCredential


    foreach ($User in $Users)
    {
        #region User Properties
        $upnName = $User.TZ -replace '\s',''
        $UPN = $upnName + "@example.com"
        $fullname = $User.FirstName + " " + $User.LastName

        $phone = "+972 $($User.MobilePhone)"        
        
        #endregion .. User Properties
        try {
            if (-Not (Get-MsolUser -UserPrincipalName $UPN -ErrorAction Ignore)) {
                # Create the new account into temp object to not print the details in shell.
                $tempvar = New-MsolUser -UserPrincipalName $UPN -DisplayName $fullname -FirstName $User.FirstName -LastName $User.LastName -PhoneNumber $phone -MobilePhone $phone  -UsageLocation Israel
                start-sleep -Seconds 2
            } else {
            Set-MsolUser -UserPrincipalName $UPN -DisplayName $fullname -FirstName $User.FirstName -LastName $User.LastName -UsageLocation Israel
            start-sleep -Seconds 1
            }
        } catch {
            $User | export-csv -Path .\failed.csv -Append -Encoding UTF8
            Start-Sleep -Seconds 5
        }
        
        #endregion .. Set User

        #endregion .. Define on fields
        
    }
}
   #endregion .. Create Users
#endregion Scriptblock

$outArray | ForEach-Object {
    Start-Job -Name Import -ScriptBlock $scriptblock -ArgumentList $_
}

get-job | Wait-Job
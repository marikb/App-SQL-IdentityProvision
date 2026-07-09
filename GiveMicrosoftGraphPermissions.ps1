$vmObjectId = "<your-vm-managed-identity-object-id>"

Connect-MgGraph -Scopes "AppRoleAssignment.ReadWrite.All","Application.Read.All"
$graph = Get-MgServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'"
$permissions = @("User.ReadWrite.All", "User.EnableDisableAccount.All", "GroupMember.ReadWrite.All", "Directory.Read.All", "Mail.Send")

foreach($permission in $permissions){
    $role = $graph.AppRoles | Where-Object Value -eq $permission
    New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $vmObjectId -PrincipalId $vmObjectId -ResourceId $graph.Id -AppRoleId $role.Id
}

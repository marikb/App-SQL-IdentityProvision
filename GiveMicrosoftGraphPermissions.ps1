$vmObjectId = "<your-vm-managed-identity-object-id>"

Connect-MgGraph -Scopes "AppRoleAssignment.ReadWrite.All","Application.Read.All"
$graph = Get-MgServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'"
$permissions = @("User.ReadWrite.All", "Mail.Send", "GroupMember.ReadWrite.All", "Group.ReadWrite.All", "Directory.ReadWrite.All")

foreach($permission in $permissions){
    $role = $graph.AppRoles | Where-Object Value -eq $permission
    New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $vmObjectId -PrincipalId $vmObjectId -ResourceId $graph.Id -AppRoleId $role.Id
}

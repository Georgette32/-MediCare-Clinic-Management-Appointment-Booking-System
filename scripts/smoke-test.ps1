# End-to-end smoke test for the MediCare authentication, approval and authorization flows.
# Requires the web application to be running (default http://localhost:5132).
param(
    [string]$BaseUrl = 'http://localhost:5132'
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Net.Http

function New-Client {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.CookieContainer = [System.Net.CookieContainer]::new()
    return [System.Net.Http.HttpClient]::new($handler)
}

function Get-Page([System.Net.Http.HttpClient]$Client, [string]$Path) {
    $response = $Client.GetAsync("$BaseUrl$Path").GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{
        Status   = [int]$response.StatusCode
        Location = [string]$response.Headers.Location
        Body     = $body
    }
}

function Get-Token([string]$Html) {
    $match = [regex]::Match($Html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if (-not $match.Success) {
        throw 'Antiforgery token was not found in the page.'
    }
    return $match.Groups[1].Value
}

function Post-Form([System.Net.Http.HttpClient]$Client, [string]$Path, [hashtable]$Fields) {
    $pairs = [System.Collections.Generic.Dictionary[string, string]]::new()
    foreach ($key in $Fields.Keys) {
        $pairs[$key] = [string]$Fields[$key]
    }

    $content = [System.Net.Http.FormUrlEncodedContent]::new($pairs)
    $response = $Client.PostAsync("$BaseUrl$Path", $content).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{
        Status   = [int]$response.StatusCode
        Location = [string]$response.Headers.Location
        Body     = $body
    }
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
    Write-Host "  OK  $Message" -ForegroundColor Green
}

function Assert-Redirect($Response, [string]$ExpectedPrefix, [string]$Message) {
    $location = [string]$Response.Location
    if ($location -match '^https?://') {
        $location = ([System.Uri]$location).PathAndQuery
    }

    Assert-True ($Response.Status -in 302) "$Message (expected redirect, got $($Response.Status))"
    Assert-True ($location.StartsWith($ExpectedPrefix)) "$Message (expected $ExpectedPrefix, got $location)"
}

function Register-Doctor([string]$Email, [string]$License) {
    $client = New-Client
    $page = Get-Page $client '/Account/RegisterDoctor'
    Assert-True ($page.Status -eq 200) 'Doctor registration page renders'

    $token = Get-Token $page.Body
    $specialization = [regex]::Match($page.Body, '<option value="([0-9a-fA-F-]{36})"').Groups[1].Value
    Assert-True (-not [string]::IsNullOrWhiteSpace($specialization)) 'Registration page lists active specializations'

    $result = Post-Form $client '/Account/RegisterDoctor' @{
        '__RequestVerificationToken' = $token
        'FullName'                   = 'Dr. Test Doctor'
        'Email'                      = $Email
        'PhoneNumber'                = '+1 555 0100'
        'SpecializationId'           = $specialization
        'LicenseNumber'              = $License
        'ConsultationFee'            = '150'
        'YearsOfExperience'          = '8'
        'Bio'                        = 'Smoke test doctor'
        'Password'                   = 'Password1'
        'ConfirmPassword'            = 'Password1'
    }

    Assert-Redirect $result '/Account/Login' 'Doctor registration succeeds'
    return $client
}

function Register-Patient([string]$Email) {
    $client = New-Client
    $page = Get-Page $client '/Account/RegisterPatient'
    Assert-True ($page.Status -eq 200) 'Patient registration page renders'

    $token = Get-Token $page.Body
    $result = Post-Form $client '/Account/RegisterPatient' @{
        '__RequestVerificationToken' = $token
        'FullName'                   = 'Test Patient'
        'Email'                      = $Email
        'PhoneNumber'                = '+1 555 0101'
        'DateOfBirth'                = '1990-05-01'
        'Gender'                     = '1'
        'Address'                    = '12 Main Street'
        'Password'                   = 'Password1'
        'ConfirmPassword'            = 'Password1'
    }

    Assert-Redirect $result '/Account/Login' 'Patient registration succeeds'
    return $client
}

function Login([string]$Email, [string]$Password) {
    $client = New-Client
    $page = Get-Page $client '/Account/Login'
    Assert-True ($page.Status -eq 200) 'Login page renders'

    $result = Post-Form $client '/Account/Login' @{
        '__RequestVerificationToken' = (Get-Token $page.Body)
        'Email'                      = $Email
        'Password'                   = $Password
        'RememberMe'                 = 'false'
    }

    return [pscustomobject]@{ Client = $client; Response = $result }
}

Write-Host "`nMediCare smoke test against $BaseUrl`n" -ForegroundColor Cyan

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$doctorEmail = "doctor.$stamp@example.com"
$patientEmail = "patient.$stamp@example.com"
$adminEmail = 'admin@medicare.local'
$adminPassword = 'Admin@12345'
$doctorPassword = 'Password1'

Write-Host '1. Anonymous pages' -ForegroundColor Cyan
$anonymous = New-Client
Assert-True ((Get-Page $anonymous '/').Status -eq 200) 'Home page renders'
Assert-True ((Get-Page $anonymous '/Account/Login').Status -eq 200) 'Login page is reachable anonymously'
Assert-True ((Get-Page $anonymous '/Admin/Dashboard').Status -eq 302) 'Anonymous admin access is redirected'

Write-Host '2. Doctor registration and pending state' -ForegroundColor Cyan
$doctorClient = Register-Doctor $doctorEmail "MED-$stamp"
$doctorLogin = Login $doctorEmail $doctorPassword
Assert-Redirect $doctorLogin.Response '/Account/PendingApproval' 'Pending doctor is routed to the application status page'
$dashboardAttempt = Get-Page $doctorLogin.Client '/Doctor/Dashboard'
Assert-Redirect $dashboardAttempt '/Account/AccessDenied' 'Pending doctor cannot reach the doctor dashboard'

Write-Host '3. Patient registration and role separation' -ForegroundColor Cyan
$patientClient = Register-Patient $patientEmail
$patientLogin = Login $patientEmail 'Password1'
Assert-Redirect $patientLogin.Response '/Patient' 'Patient is routed to the patient dashboard'
Assert-True ((Get-Page $patientLogin.Client '/Patient/Dashboard').Status -eq 200) 'Patient dashboard renders'
Assert-Redirect (Get-Page $patientLogin.Client '/Admin/Dashboard') '/Account/AccessDenied' 'Patient cannot reach the admin dashboard'

Write-Host '4. Admin approval workflow' -ForegroundColor Cyan
$adminLogin = Login $adminEmail $adminPassword
Assert-Redirect $adminLogin.Response '/Admin' 'Administrator is routed to the admin dashboard'

$pending = Get-Page $adminLogin.Client '/Admin/Doctors/Pending'
Assert-True ($pending.Status -eq 200) 'Pending applications page renders'
Assert-True ($pending.Body.Contains($doctorEmail)) 'New doctor application is listed as pending'

$doctorId = [regex]::Match(
    $pending.Body,
    "(?s)$([regex]::Escape($doctorEmail)).*?/Admin/Doctors/Details/([0-9a-fA-F-]{36})").Groups[1].Value
Assert-True (-not [string]::IsNullOrWhiteSpace($doctorId)) 'Pending application exposes the doctor id'

$details = Get-Page $adminLogin.Client "/Admin/Doctors/Details/$doctorId"
$approveAction = [System.Net.WebUtility]::HtmlDecode(
    [regex]::Match($details.Body, 'action="([^"]*Approve[^"]*)"').Groups[1].Value)
Assert-True (-not [string]::IsNullOrWhiteSpace($approveAction)) 'Approve action is rendered on the details page'

$approve = Post-Form $adminLogin.Client $approveAction @{ '__RequestVerificationToken' = (Get-Token $details.Body) }
Assert-True ($approve.Status -eq 302) 'Approval POST is accepted'
Assert-True ((Get-Page $adminLogin.Client "/Admin/Doctors/Details/$doctorId").Body.Contains('Approved')) 'Doctor is now approved'

Write-Host '5. Approval grants immediate doctor access' -ForegroundColor Cyan
$doctorDashboard = Get-Page $doctorLogin.Client '/Doctor/Dashboard'
Assert-True ($doctorDashboard.Status -eq 200) 'Approved doctor can reach the doctor dashboard'
Assert-True ($doctorDashboard.Body.Contains('Approved doctor')) 'Doctor dashboard shows the approved state'

Write-Host '6. Deactivation revokes access immediately' -ForegroundColor Cyan
$details = Get-Page $adminLogin.Client "/Admin/Doctors/Details/$doctorId"
$deactivateAction = [System.Net.WebUtility]::HtmlDecode(
    [regex]::Match($details.Body, 'action="([^"]*Deactivate[^"]*)"').Groups[1].Value)
Assert-True (-not [string]::IsNullOrWhiteSpace($deactivateAction)) 'Deactivate action is rendered'

$deactivate = Post-Form $adminLogin.Client $deactivateAction @{ '__RequestVerificationToken' = (Get-Token $details.Body) }
Assert-True ($deactivate.Status -eq 302) 'Deactivation POST is accepted'

$blocked = Get-Page $doctorLogin.Client '/Doctor/Dashboard'
Assert-Redirect $blocked '/Account/Login?reason=inactive' 'Deactivated user is signed out by the active-user middleware'

$relogin = Login $doctorEmail $doctorPassword
Assert-True ($relogin.Response.Status -eq 200) 'Deactivated doctor cannot sign in again'
Assert-True ($relogin.Response.Body.Contains('deactivated')) 'Deactivated sign-in explains the account state'

Write-Host '7. Admin and account pages render' -ForegroundColor Cyan
$adminPages = @(
    '/Admin/Dashboard',
    '/Admin/Doctors',
    '/Admin/Users',
    '/Admin/Specializations',
    '/Admin/Specializations/Create',
    "/Admin/Doctors/Edit/$doctorId",
    '/Account/Profile',
    '/Account/ChangePassword'
)

foreach ($path in $adminPages) {
    $page = Get-Page $adminLogin.Client $path
    Assert-True ($page.Status -eq 200) "GET $path renders"
}

Write-Host '8. Password recovery' -ForegroundColor Cyan
$forgot = Get-Page $anonymous '/Account/ForgotPassword'
$forgotResult = Post-Form $anonymous '/Account/ForgotPassword' @{
    '__RequestVerificationToken' = (Get-Token $forgot.Body)
    'Email'                      = $patientEmail
}
Assert-Redirect $forgotResult '/Account/ForgotPasswordConfirmation' 'Password recovery request is accepted'

Write-Host "`nAll smoke checks passed.`n" -ForegroundColor Green

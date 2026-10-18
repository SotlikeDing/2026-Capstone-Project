using WebAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// Core authentication and account lifecycle: sign-in/refresh/logout (JWT-based,
/// with revocation via ITokenBlacklistService), registration with email
/// verification, forgot/reset password (single-use, time-limited tokens), IP-based
/// sign-in rate limiting and per-account lockout after repeated failures, and role
/// selection/lookup. Email-verification and password-reset tokens are held in
/// process memory (static dictionaries) rather than the database - see the fields
/// just above each token's usage - so they don't survive an app restart or scale
/// across multiple instances.
/// </summary>
[Authorize]
[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly User_db _userDb;
    private readonly WebAPI.Services.IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly WebAPI.Services.ITokenBlacklistService _tokenBlacklist;
    private readonly WebAPI.Services.ISessionStore _sessionStore;
    private readonly WebAPI.Services.ISystemSettingsCache _settingsCache;
    private readonly ResourceOwnership_db? _ownership;
    private readonly BookingCompanions_db? _bookingCompanions;

    // Security & State Management
    private static readonly ConcurrentDictionary<string, List<DateTime>> _ipRateLimits = new();
    private static readonly ConcurrentDictionary<string, (int FailedAttempts, DateTime LockoutEnd)> _accountLockouts = new();

    public UserController(User_db userDb, WebAPI.Services.IEmailService emailService, IConfiguration configuration,
        WebAPI.Services.ITokenBlacklistService tokenBlacklist, WebAPI.Services.ISessionStore sessionStore,
        WebAPI.Services.ISystemSettingsCache settingsCache, ResourceOwnership_db? ownership = null,
        BookingCompanions_db? bookingCompanions = null, WebAPI.Services.IVerificationCodeStore? verificationCodes = null)
    {
        _verificationCodes = verificationCodes ?? new WebAPI.Services.InMemoryVerificationCodeStore();
        _ownership = ownership;
        _bookingCompanions = bookingCompanions;
        _userDb = userDb;
        _emailService = emailService;
        _configuration = configuration;
        _tokenBlacklist = tokenBlacklist;
        _sessionStore = sessionStore;
        _settingsCache = settingsCache;
    }

    /// <summary>Sliding-window (1 minute) sign-in attempt limiter per client IP, threshold configurable via SignInRateLimitPerMinute.</summary>
    private bool IsRateLimited(string clientIp)
    {
        int maxRequestsPerMinute = _settingsCache.GetInt("SignInRateLimitPerMinute", 15);
        var now = DateTime.UtcNow;
        var timestamps = _ipRateLimits.GetOrAdd(clientIp, _ => new List<DateTime>());
        lock (timestamps)
        {
            timestamps.RemoveAll(t => (now - t).TotalMinutes > 1);
            if (timestamps.Count >= maxRequestsPerMinute)
            {
                return true;
            }
            timestamps.Add(now);
            return false;
        }
    }

    /// <summary>Whether an account is currently locked out from a previous run of failed login attempts, and until when.</summary>
    private (bool IsLocked, DateTime LockoutEnd) CheckAccountLockout(string email)
    {
        string key = email.ToLowerInvariant();
        if (_accountLockouts.TryGetValue(key, out var info))
        {
            if (info.LockoutEnd > DateTime.UtcNow)
            {
                return (true, info.LockoutEnd);
            }
            _accountLockouts.TryRemove(key, out _);
        }
        return (false, DateTime.MinValue);
    }

    /// <summary>Increments an account's failed-attempt counter, triggering a timed lockout once LoginLockoutMaxAttempts is reached.</summary>
    private void RecordFailedLoginAttempt(string email)
    {
        int maxAttempts = _settingsCache.GetInt("LoginLockoutMaxAttempts", 5);
        int lockoutMinutes = _settingsCache.GetInt("LoginLockoutDurationMinutes", 15);

        string key = email.ToLowerInvariant();
        var current = _accountLockouts.GetOrAdd(key, _ => (0, DateTime.MinValue));
        int nextAttempts = current.FailedAttempts + 1;
        DateTime lockoutEnd = nextAttempts >= maxAttempts ? DateTime.UtcNow.AddMinutes(lockoutMinutes) : current.LockoutEnd;
        _accountLockouts[key] = (nextAttempts, lockoutEnd);
    }

    /// <summary>Clears an account's failed-attempt counter after a successful login.</summary>
    private void ResetAccountLockout(string email)
    {
        string key = email.ToLowerInvariant();
        _accountLockouts.TryRemove(key, out _);
    }

    /// <summary>
    /// Authenticates a user and issues a JWT access token + refresh token. Blocks on
    /// IP rate limiting, account lockout, a suspended account, or an unverified
    /// email (in which case a fresh verification code is emailed automatically).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("signin")]
    public async Task<IActionResult> SignIn([FromBody] LoginRequest request)
    {
        try
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (IsRateLimited(clientIp))
            {
                return StatusCode(429, new { message = "Te veel inlogpogings. Stadig asseblief 'n bietjie af en probeer weer.", success = false });
            }

            if (request == null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest(new { message = "Epos en wagwoord is verpligtend", success = false });
            }

            if (!request.Email.Contains('@'))
            {
                return BadRequest(new { message = "Ongeldige epos formaat", success = false });
            }

            var (isLocked, lockoutEnd) = CheckAccountLockout(request.Email);
            if (isLocked)
            {
                var remainingMinutes = Math.Ceiling((lockoutEnd - DateTime.UtcNow).TotalMinutes);
                return Unauthorized(new 
                { 
                    message = $"Jou rekening is tydelik gesluit weens te veel mislukte inlogpogings. Probeer asseblief weer oor {remainingMinutes} minute.", 
                    success = false 
                });
            }

            var user = await _userDb.AuthenticateUser(request);

            if (user == null)
            {
                RecordFailedLoginAttempt(request.Email);
                return Unauthorized(new { message = "Ongeldige epos of wagwoord", success = false });
            }

            ResetAccountLockout(request.Email);

            if (user.IsSuspended)
            {
                return Unauthorized(new { message = "Jou profiel is opgeskort. Kontak asseblief ondersteuning.", success = false });
            }

            if (!user.IsEmailVerified)
            {
                var verifyToken = await _verificationCodes.IssueAsync(user.Email);

                string verifyBody = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 8px;'>
                        <h2 style='color: #6C5037;'>MikPunt - Verifieer jou Epos</h2>
                        <p>Hallo {user.Name},</p>
                        <p>Jy het probeer inteken, maar jou e-posadres is nog nie geverifieer nie. Hier is jou verifikasie-kode:</p>
                        <div style='text-align: center; margin: 20px 0;'>
                            <span style='background-color: #F7E7CC; color: #4A3319; font-size: 28px; font-weight: bold; padding: 12px 24px; letter-spacing: 6px; border-radius: 6px; display: inline-block;'>{verifyToken}</span>
                        </div>
                        <p style='color: #777; font-size: 13px;'>Hierdie kode is vir 24 uur geldig.</p>
                    </div>";

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendEmailAsync(user.Email, "MikPunt Epos Verifikasie (Nuwe Kode)", verifyBody);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EMAIL BACKGROUND] Failed to resend verification email to {user.Email}: {ex.Message}");
                    }
                });

                return Unauthorized(new 
                { 
                    message = "Jou e-posadres is nog nie geverifieer nie. 'n Nuwe verifikasie-kode is na jou inboks gestuur.", 
                    success = false,
                    requiresVerification = true,
                    email = user.Email
                });
            }

            // Generate 24-hour JWT Access Token & 7-day Refresh Token
            var token = GenerateJwtToken(user);
            var refreshToken = _sessionStore.CreateRefreshToken(user.Email);

            return Ok(new SignInResponse
            { 
                Success = true,
                AccessToken = token,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(_settingsCache.GetInt("JwtExpiryHours", 24)),
                UserId = user.UserId,
                Email = user.Email,
                Roles = user.Roles != null ? user.Roles.Select(r => new UserRoleData
                {
                    Id = r.UserRoleId,
                    UserId = r.UserId,
                    RoleCode = r.RoleCode ?? "",
                    RoleName = r.RoleName ?? "",
                    Status = "Active",
                    AssignedDate = DateTime.UtcNow
                }).ToList() : new List<UserRoleData>(),
                Message = "Suksesvol ingeteken"
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return StatusCode(500, new { message = "Interne bedienerfout", success = false });
        }
    }

    /// <summary>
    /// Exchanges a valid refresh token for a new access + refresh token pair, minted
    /// for the real account (id + current roles) behind the email. Suspended accounts
    /// can't refresh.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refreshToken")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.RefreshToken))
        {
            return BadRequest(new { success = false, message = "E-pos en refresh token is verpligtend." });
        }

        if (_sessionStore.TryValidateRefreshToken(request.Email, request.RefreshToken))
        {
            var user = await _userDb.GetUserByEmail(request.Email);
            if (user == null || user.IsSuspended)
                return Unauthorized(new { success = false, message = "Ongeldige of vervalde refresh token." });
            string newAccessToken = GenerateJwtToken(user);
            string newRefreshToken = _sessionStore.CreateRefreshToken(request.Email);

            return Ok(new
            {
                success = true,
                accessToken = newAccessToken,
                refreshToken = newRefreshToken,
                expiresAt = DateTime.UtcNow.AddHours(_settingsCache.GetInt("JwtExpiryHours", 24))
            });
        }

        return Unauthorized(new { success = false, message = "Ongeldige of vervalde refresh token." });
    }
    
    /// <summary>
    /// Registers a new account. Enforces RFC-style email format and an enterprise
    /// password policy (12+ chars, upper/lower/digit/special), rejects duplicate
    /// emails, and emails a 6-digit verification code (valid 24h) - the account
    /// exists immediately but can't sign in until that code is confirmed via
    /// VerifyEmail. Boer/PJ/PH role selections additionally require admin
    /// verification later, noted in the response message.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        // Admin flag Enable_New_User_Registrations.
        var flags = HttpContext?.RequestServices?.GetService<IFeatureFlags>();
        if (flags != null && !await flags.IsEnabledAsync(FeatureFlagKeys.NewUserRegistrations, true))
            return StatusCode(StatusCodes.Status403Forbidden, new RegisterResponse
            {
                Message = "Nuwe registrasies is tydelik gesluit. Probeer asseblief later weer.",
                Success = false
            });

        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.FirstName)
                                || string.IsNullOrWhiteSpace(request.Surname)
                                || string.IsNullOrWhiteSpace(request.Email) 
                                || string.IsNullOrWhiteSpace(request.Password) 
                                || string.IsNullOrWhiteSpace(request.Phone))
            {
                return BadRequest(new RegisterResponse { Message = "Alle verpligte velde moet ingevul word.", Success = false });
            }

            if (CallerIdentity.IsAdminRoleCode(request.PrimaryRoleCode) || request.SelectedRoles.Any(CallerIdentity.IsAdminRoleCode))
            {
                return BadRequest(new RegisterResponse { Message = "Die administrateur-rol kan nie tydens registrasie gekies word nie.", Success = false });
            }

            if (!request.TermsAccepted)
            {
                return BadRequest(new RegisterResponse { Message = "U moet die Diensvoorwaardes en Privaatheidsbeleid aanvaar om te registreer.", Success = false });
            }

            // Enterprise RFC email validation regex
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                return BadRequest(new RegisterResponse { Message = "Voer asseblief 'n geldige e-posadres in.", Success = false });
            }

            // Enterprise Password policy: min 12 chars, 1 uppercase, 1 lowercase, 1 digit, 1 special char
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.Password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{12,}$"))
            {
                return BadRequest(new RegisterResponse { Message = "Wagwoord voldoen nie aan die sekuriteitsvereistes nie. Dit moet ten minste 12 karakters, 1 hoofletter, 1 kleinletter, 1 syfer en 1 spesiale teken bevat.", Success = false });
            }
        
            if (await _userDb.UserExists(request.Email))
            {
                return BadRequest(new RegisterResponse { Message = "'n Rekening met hierdie e-posadres bestaan reeds in MikPunt.", Success = false });
            }
        
            bool registrationSuccess = await _userDb.RegisterUser(request);
        
            if (!registrationSuccess)
            {
                return BadRequest(new RegisterResponse { Message = "Registrasie het misluk. Probeer asseblief weer.", Success = false });
            }
        
            var registeredUser = await _userDb.GetUserByEmail(request.Email);
            
            // Determine initial role status message
            string roleCode = string.IsNullOrWhiteSpace(request.PrimaryRoleCode) ? "Jagter" : request.PrimaryRoleCode;
            bool requiresAdminApproval = roleCode is "Boer" or "PJ" or "PH";
            string roleNotice = requiresAdminApproval
                ? " Let wel: Jou gekose rol vereis admin-keuring voor volle aktivering."
                : "";

            // Send Verification Email
            var verifyToken = await _verificationCodes.IssueAsync(request.Email);
            
            string verifyBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 8px;'>
                    <h2 style='color: #6C5037;'>Welkom by MikPunt!</h2>
                    <p>Hallo {request.FirstName},</p>
                    <p>Dankie dat jy geregistreer het op Suid-Afrika se voorste jag- en ekotoerisme platform.</p>
                    <p>Jou e-pos verifikasie-kode is:</p>
                    <div style='text-align: center; margin: 20px 0;'>
                        <span style='background-color: #F7E7CC; color: #4A3319; font-size: 28px; font-weight: bold; padding: 12px 24px; letter-spacing: 6px; border-radius: 6px; display: inline-block;'>{verifyToken}</span>
                    </div>
                    <p style='color: #777; font-size: 13px;'>Hierdie kode is vir 24 uur geldig.</p>
                </div>";
            
            _ = Task.Run(async () => 
            {
                try 
                {
                    await _emailService.SendEmailAsync(request.Email, "MikPunt Epos Verifikasie", verifyBody);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EMAIL BACKGROUND] Failed to send verification email to {request.Email}: {ex.Message}");
                }
            });
        
            return Ok(new RegisterResponse
            { 
                Success = true,
                Message = $"Rekening suksesvol geskep! Verifieer asseblief jou e-pos met die stuurkode.{roleNotice}",
                AccessToken = ""
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            if (e.Message.Contains("already exists"))
            {
                return BadRequest(new RegisterResponse { Message = e.Message, Success = false });
            }
            return StatusCode(500, new RegisterResponse { Message = "Interne bedienerfout", Success = false });
        }
    }

    /// <summary>Fetches role-specific data for a userRoleId (roleId selects which role table to read, e.g. "PH", "FARMER").</summary>
    [HttpGet("getUserData/{userRoleId}/{roleId}")]
    public async Task<IActionResult> GetUserData(int userRoleId, string roleId)
    {
        // Only the role's owner or an Admin (fails closed without the ownership service).
        if (!User.IsAdmin() && (_ownership == null || !await User.OwnsUserRoleOrAdminAsync(_ownership, userRoleId)))
            return Forbid();

        try
        {
            var result = await _userDb.GetUserDataRole(userRoleId, roleId);
            
            if (result == null || result.Count == 0)
            {
                return NotFound(new { 
                    Success = false,
                    Message = $"No data found for {roleId} with ID {userRoleId}",
                    UserRoleId = userRoleId,
                    Role = roleId
                });
            }
            
            return Ok(new
            {
                Success = true,
                Role = roleId,
                UserRoleId = userRoleId,
                Data = result
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            { 
                Success = false, 
                Message = ex.Message,
                ValidRoles = new[] { "HUNTER", "PH", "PJ", "FARMER", "VG", "AP", "ADMIN" }
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new
            { 
                Success = false, 
                Message = "An error occurred while processing your request"
            });
        }
    }
    
    /// <summary>Lists every role code/name a user could select during registration or role addition.</summary>
    [AllowAnonymous]
    [HttpGet("getAvailableRoles")]
    public async Task<IActionResult> GetAvailableRoles()
    {
        try
        {
            var roles = await _userDb.GetAvailableRoles();
            return Ok(roles);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { error = e.Message });
        }
    }
    
    /// <summary>Assigns an additional role to a user after validating the role code against the available-roles list.</summary>
    [HttpPost("selectRole")]
    public async Task<IActionResult> SelectRole([FromBody] SelectRoleRequest request)
    {
        try
        {
            if (request == null || request.UserId <= 0 || string.IsNullOrEmpty(request.RoleCode))
            {
                return BadRequest(new { Message = "UserId and RoleCode are required", Success = false });
            }

            // Users may only pick roles for themselves, and never the Admin role.
            if (!User.IsSelfOrAdmin(request.UserId))
                return StatusCode(403, new { Message = "Geen toegang om rolle vir hierdie gebruiker te kies nie.", Success = false });
            if (CallerIdentity.IsAdminRoleCode(request.RoleCode) && !User.IsAdmin())
                return StatusCode(403, new { Message = "Slegs 'n administrateur kan die administrateur-rol toeken.", Success = false });

            var rolesResult = await _userDb.GetAvailableRoles();
            var roleCodes = rolesResult.Select(r => r.RoleCode).ToList();
            if (!roleCodes.Contains(request.RoleCode.ToUpper()))
            {
                return BadRequest(new { Message = "Invalid role selected", Success = false });
            }
            
            bool assigned = await _userDb.AssignRoleToUser(request.UserId, request.RoleCode);
            
            if (!assigned)
            {
                return BadRequest(new { Message = "Failed to assign role. Role may not exist.", Success = false });
            }
            
            var user = await _userDb.GetUserById(request.UserId);
            
            var roles = user?.Roles.Select(r => new UserRoleData
            {
                Id = r.UserRoleId,
                UserId = r.UserId,
                RoleCode = r.RoleCode ?? "",
                RoleName = r.RoleName ?? "",
                Status = "Active",
                AssignedDate = DateTime.UtcNow
            }).ToList() ?? new List<UserRoleData>();
            
            return Ok(new SelectRoleResponse
            { 
                Id = 0,
                UserId = request.UserId,
                RoleCode = request.RoleCode,
                RoleName = request.RoleCode,
                Status = "Active",
                AssignedDate = DateTime.UtcNow
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { Message = e.Message, Success = false });
        }
    }

    /// <summary>Lists every user with their email/phone/verification status - real PII, hence admin-only.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("getAllUsers")]
    public async Task<IActionResult> GetAllUsers()
    {
        try
        {
            var users = await _userDb.GetAllUsers();

            if (users == null || users.Count == 0)
            {
                return NotFound(new
                {
                    Success = false,
                    Message = "No users found"
                });
            }

            return Ok(new
            {
                Success = true,
                Count = users.Count,
                Data = users
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);

            return StatusCode(500, new
            {
                Success = false,
                Message = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Pending e-mail verification codes (SQL-backed in the app, see SqlVerificationCodeStore):
    /// cryptographically random, hashed, 24h expiry, locked after 5 wrong guesses, and they
    /// survive restarts/deploys.
    /// </summary>
    private readonly WebAPI.Services.IVerificationCodeStore _verificationCodes;

    /// <summary>Confirms a registration/re-sent verification code, activating the account for sign-in.</summary>
    [AllowAnonymous]
    [HttpGet("verifyEmail")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            return BadRequest(new { Message = "Epos en kode is verpligtend", Success = false });

        switch (await _verificationCodes.CheckAsync(email, token))
        {
            case WebAPI.Services.VerificationCodeResult.Expired:
                return BadRequest(new { Message = "Kode het verval. Versoek asseblief 'n nuwe kode.", Success = false });
            case WebAPI.Services.VerificationCodeResult.Locked:
                return StatusCode(429, new { Message = "Te veel verkeerde kodes. Versoek asseblief 'n nuwe kode.", Success = false });
            case WebAPI.Services.VerificationCodeResult.Invalid:
                return BadRequest(new { Message = "Ongeldige kode", Success = false });
            case WebAPI.Services.VerificationCodeResult.Valid:
                if (await _userDb.VerifyEmail(email))
                {
                    await _verificationCodes.RemoveAsync(email);
                    await ClaimBookingInvitesAsync(email);
                    return Ok(new { Message = "Epos is suksesvol geverifieer. U kan nou aanteken.", Success = true });
                }
                break;
        }
        return BadRequest(new { Message = "Ongeldige verifikasie versoek", Success = false });
    }

    /// <summary>
    /// A main hunter may have invited this email onto their booking before the account
    /// existed: now that the email is verified, link those invites (only if it is a
    /// Hunter account). Best-effort - verification itself must never fail on this.
    /// </summary>
    private async Task ClaimBookingInvitesAsync(string email)
    {
        if (_bookingCompanions == null) return;
        try
        {
            await _bookingCompanions.ClaimPendingInvitesForEmail(email);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOOKING INVITES] Could not link invites for {email}: {ex.Message}");
        }
    }

    /// <summary>Issues a fresh verification code, invalidating any previously issued one for that email.</summary>
    [AllowAnonymous]
    [HttpPost("resendVerificationEmail")]
    public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendVerificationEmailRequest request)
    {
        try
        {
            var email = request?.Email;
            if (string.IsNullOrEmpty(email)) return BadRequest(new { Success = false, Message = "Epos is verpligtend" });

            var user = await _userDb.GetUserByEmail(email);
            if (user == null) return NotFound(new { Success = false, Message = "Gebruiker nie gevind nie" });
            if (user.IsEmailVerified) return BadRequest(new { Success = false, Message = "Epos is reeds geverifieer" });

            var verifyToken = await _verificationCodes.IssueAsync(email);
            
            string verifyBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                    <h2 style='color: #6C5037;'>Verifieer jou Epos (Nuwe Kode)</h2>
                    <p>Welkom terug by MikPunt! Jou nuwe verifikasie-kode is:</p>
                    <h1 style='background-color: #F7E7CC; padding: 10px; display: inline-block; letter-spacing: 5px; border-radius: 5px;'>{verifyToken}</h1>
                    <p>Hierdie kode sal verval oor 24 ure. Ouer kodes is nou ongeldig.</p>
                </div>";
            
            _ = Task.Run(async () => 
            {
                try 
                {
                    await _emailService.SendEmailAsync(email, "MikPunt Nuwe Epos Verifikasie", verifyBody);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EMAIL BACKGROUND] Failed to resend verification email to {email}: {ex.Message}");
                }
            });

            return Ok(new { Success = true, Message = "Nuwe verifikasie-kode is gestuur." });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return StatusCode(500, new { Success = false, Message = "Interne bedienerfout" });
        }
    }

    /// <summary>In-memory store of pending password-reset tokens, keyed by lowercased email. Single-use (IsUsed flips once redeemed) and 15-minute lived.</summary>
    private static readonly ConcurrentDictionary<string, (string Token, DateTime Expiry, bool IsUsed)> _passwordResetTokens = new();

    /// <summary>Checks whether a password-reset link is still valid, before the reset-password page lets the user type a new password.</summary>
    [AllowAnonymous]
    [HttpGet("validateResetToken")]
    public IActionResult ValidateResetToken([FromQuery] string email, [FromQuery] string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            return BadRequest(new { success = false, message = "E-pos en token is verpligtend." });
        }

        string key = email.ToLowerInvariant();
        if (_passwordResetTokens.TryGetValue(key, out var resetData))
        {
            if (resetData.Token == token && !resetData.IsUsed && resetData.Expiry > DateTime.UtcNow)
            {
                return Ok(new { success = true, message = "Geldige herstelskakel." });
            }
        }

        return BadRequest(new { success = false, message = "Hierdie wagwoord herstelskakel is ongeldig, alreeds gebruik, of het verval. Versoek asseblief 'n nuwe wagwoord herstelskakel." });
    }

    /// <summary>
    /// Starts the password-reset flow: generates a cryptographically random
    /// 256-bit token, emails a reset link built against the request's Origin (or
    /// Referer, or a configured fallback). Always returns success regardless of
    /// whether the email exists, to prevent account enumeration.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("forgotPassword")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Email))
                return BadRequest(new ForgotPasswordResponse { Success = false, Message = "E-pos is verpligtend" });

            if (!await _userDb.UserExists(request.Email))
            {
                // Prevent email enumeration attack by returning success
                Console.WriteLine($"[FORGOT PASSWORD] Email {request.Email} not found. No email sent.");
                return Ok(new ForgotPasswordResponse { Success = true, Message = "As die e-posadres bestaan, is 'n herstelskakel gestuur." });
            }

            // Generate cryptographically secure 256-bit token for link
            byte[] tokenBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(tokenBytes);
            }
            string token = Convert.ToHexString(tokenBytes);
            string key = request.Email.ToLowerInvariant();

            _passwordResetTokens[key] = (token, DateTime.UtcNow.AddMinutes(15), false);
            
            // Construct secure clickable URL link targeting WebApp reset-password page
            string clientOrigin = Request.Headers["Origin"].ToString();
            if (string.IsNullOrEmpty(clientOrigin) && Request.Headers.ContainsKey("Referer"))
            {
                try
                {
                    var referer = new Uri(Request.Headers["Referer"].ToString());
                    clientOrigin = $"{referer.Scheme}://{referer.Authority}";
                }
                catch { }
            }
            if (string.IsNullOrEmpty(clientOrigin))
            {
                clientOrigin = _configuration["WebAppBaseUrl"] ?? "https://localhost:7285";
            }

            string resetUrl = $"{clientOrigin.TrimEnd('/')}/herstel-wagwoord?email={Uri.EscapeDataString(request.Email)}&token={Uri.EscapeDataString(token)}";

            string subject = "MikPunt Wagwoord Herstelskakel";
            string body = $@"
                <div style='font-family: Arial, sans-serif; padding: 25px; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 8px; background-color: #fafafa;'>
                    <h2 style='color: #6C5037; margin-bottom: 10px;'>MikPunt Wagwoord Herstel</h2>
                    <p style='font-size: 15px; color: #555;'>Ons het 'n versoek ontvang om jou wagwoord te herstel.</p>
                    <p style='font-size: 15px; color: #555;'>Klik op die onderstaande knoppie om jou nuwe wagwoord in te stel. Hierdie skakel is slegs <strong>een keer</strong> geldig vir 15 minute:</p>
                    <div style='text-align: center; margin: 30px 0;'>
                        <a href='{resetUrl}' style='background-color: #6C5037; color: #ffffff; padding: 14px 28px; text-decoration: none; font-size: 16px; font-weight: bold; border-radius: 6px; display: inline-block;'>Stel Nuwe Wagwoord In</a>
                    </div>
                    <p style='font-size: 13px; color: #777;'>As die knoppie nie werk nie, kopieer en plak hierdie skakel in jou webblaaier:<br/><a href='{resetUrl}' style='color: #6C5037;'>{resetUrl}</a></p>
                    <p style='font-size: 13px; color: #888; margin-top: 25px;'>As jy nie hierdie versoek gerig het nie, kan jy hierdie e-pos veilig ignoreer.</p>
                </div>";
            
            _ = Task.Run(async () => 
            {
                try 
                {
                    await _emailService.SendEmailAsync(request.Email, subject, body);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EMAIL BACKGROUND] Failed to send reset email to {request.Email}: {ex.Message}");
                }
            });

            return Ok(new ForgotPasswordResponse { Success = true, Message = "As die e-posadres bestaan, is 'n herstelskakel gestuur." });
        }
        catch (Exception)
        {
            return StatusCode(500, new ForgotPasswordResponse { Success = false, Message = "Interne bedienerfout" });
        }
    }

    /// <summary>Completes the password-reset flow: validates the single-use token, marks it used immediately (before the DB write), then updates the password.</summary>
    [AllowAnonymous]
    [HttpPost("resetPassword")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Token) || string.IsNullOrEmpty(request.NewPassword))
                return BadRequest(new ResetPasswordResponse { Success = false, Message = "Alle velde is verpligtend" });

            string key = request.Email.ToLowerInvariant();
            if (_passwordResetTokens.TryGetValue(key, out var resetData))
            {
                if (resetData.Token != request.Token || resetData.IsUsed || resetData.Expiry < DateTime.UtcNow)
                {
                    return BadRequest(new ResetPasswordResponse 
                    { 
                        Success = false, 
                        Message = "Hierdie wagwoord herstelskakel is ongeldig, alreeds gebruik, of het verval. Versoek asseblief 'n nuwe wagwoord herstelskakel op die 'Wagwoord Vergeet' bladsy." 
                    });
                }

                // Immediately mark token as used and purge (Single-Use Enforcement!)
                _passwordResetTokens[key] = (resetData.Token, resetData.Expiry, true);
                _passwordResetTokens.TryRemove(key, out _);

                bool updated = await _userDb.UpdatePassword(request.Email, request.NewPassword);
                if (updated)
                {
                    return Ok(new ResetPasswordResponse { Success = true, Message = "Wagwoord is suksesvol opgedateer!" });
                }
            }
            
            return BadRequest(new ResetPasswordResponse { Success = false, Message = "Hierdie wagwoord herstelskakel is ongeldig, alreeds gebruik, of het verval. Versoek asseblief 'n nuwe herstelskakel." });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return StatusCode(500, new { Success = false, Message = "Interne bedienerfout" });
        }
    }

    /// <summary>Builds a signed JWT for the given user: NameIdentifier/Email/Name claims plus one Role claim per assigned role code, expiring after JwtExpiryHours.</summary>
    private string GenerateJwtToken(User user)
    {
        var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        string keyString = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Configuration 'Jwt:Key' not found.");
        var key = System.Text.Encoding.UTF8.GetBytes(keyString);
        var claims = new List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, user.Email),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.Name)
        };

        if (user.Roles != null)
        {
            foreach (var role in user.Roles)
            {
                if (!string.IsNullOrEmpty(role.RoleCode))
                {
                    claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role.RoleCode));
                }
            }
        }

        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(claims),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            Expires = DateTime.UtcNow.AddHours(_settingsCache.GetInt("JwtExpiryHours", 24)),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Cheap "is my session still valid?" check the website and app poll. A suspended user's
    /// token is refused before it gets here (Program.cs OnTokenValidated), so they get a 401
    /// and are signed out everywhere within a poll interval.
    /// </summary>
    [HttpGet("session")]
    public IActionResult Session()
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Ok(new { valid = true, userId = int.TryParse(id, out var userId) ? userId : 0 });
    }

    /// <summary>Revokes an access token before its natural expiry (added to the blacklist, checked on every authenticated request).</summary>
    [AllowAnonymous]
    [HttpPost("logout")]
    public IActionResult Logout([FromBody] LogoutRequest request)
    {
        if (string.IsNullOrEmpty(request.Token))
        {
            return BadRequest(new { Success = false, Message = "Token is required to logout." });
        }

        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            if (handler.CanReadToken(request.Token))
            {
                var jwtToken = handler.ReadJwtToken(request.Token);
                var expiry = jwtToken.ValidTo;

                _tokenBlacklist.Revoke(request.Token, expiry);

                return Ok(new { Success = true, Message = "Successfully logged out. Token revoked." });
            }
            
            return BadRequest(new { Success = false, Message = "Invalid token format." });
        }
        catch
        {
            return StatusCode(500, new { Success = false, Message = "Internal server error during logout." });
        }
    }

    /// <summary>Checks whether a token has been revoked (logged out) - lets the frontend guard against using a stale token before the server rejects it.</summary>
    [AllowAnonymous]
    [HttpGet("checkToken")]
    public IActionResult CheckTokenStatus([FromQuery] string token)
    {
        // Helper endpoint to check if a token is blacklisted (useful for frontend guards)
        if (_tokenBlacklist.IsRevoked(token))
        {
            return Unauthorized(new { IsValid = false, Message = "Token has been revoked." });
        }
        return Ok(new { IsValid = true, Message = "Token is active." });
    }
}

/// <summary>Payload for Logout - the access token to revoke.</summary>
public class LogoutRequest
{
    public string Token { get; set; } = string.Empty;
}


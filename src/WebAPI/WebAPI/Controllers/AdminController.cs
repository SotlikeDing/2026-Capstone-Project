using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebAPI.Database;
using WebAPI.Models;
using WebAPI.Services;

namespace WebAPI.Controllers
{
    /// <summary>
    /// The admin back-office: audit log (read/write/CSV export), feature-flag toggles,
    /// the two credential-verification queues (role applications and per-document
    /// PJ/PH/VG/Farmer uploads - both notify the affected user by email and in-app
    /// bell on decision), booking dispute correction, user suspension/ban, chat and
    /// rating-comment moderation, rating-dispute resolution, system settings
    /// (pricing/operational constants), and a health-check summary. Every write
    /// requires a Reason and is recorded via WriteAuditLogAsync. Locked down at the
    /// class level to the Admin role and a dedicated rate-limit policy.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("AdminPolicy")]
    public class AdminController : ControllerBase
    {
        private readonly AdminAudit_db _auditDb;
        private readonly FeatureFlags_db _flagsDb;
        private readonly RoleVerifications_db _verificationsDb;
        private readonly HealthCheckService _healthCheckService;
        private readonly ISessionStore _sessionStore;
        private readonly Bookings_db _bookingsDb;
        private readonly User_db _userDb;
        private readonly Chats_db _chatsDb;
        private readonly Rating_db _ratingDb;
        private readonly SystemSettings_db _settingsDb;
        private readonly ISystemSettingsCache _settingsCache;
        private readonly UserDocuments_db _documentsDb;
        private readonly WebAPI.Services.IEmailService _emailService;
        private readonly Team_db _teamDb;

        public AdminController(AdminAudit_db auditDb, FeatureFlags_db flagsDb, RoleVerifications_db verificationsDb,
            HealthCheckService healthCheckService, ISessionStore sessionStore,
            Bookings_db bookingsDb, User_db userDb, Chats_db chatsDb, Rating_db ratingDb,
            SystemSettings_db settingsDb, ISystemSettingsCache settingsCache,
            UserDocuments_db documentsDb, WebAPI.Services.IEmailService emailService, Team_db teamDb)
        {
            _auditDb = auditDb;
            _flagsDb = flagsDb;
            _verificationsDb = verificationsDb;
            _healthCheckService = healthCheckService;
            _sessionStore = sessionStore;
            _bookingsDb = bookingsDb;
            _userDb = userDb;
            _chatsDb = chatsDb;
            _ratingDb = ratingDb;
            _settingsDb = settingsDb;
            _settingsCache = settingsCache;
            _documentsDb = documentsDb;
            _emailService = emailService;
            _teamDb = teamDb;
        }

        /// <summary>
        /// Maps a role code to that role's dashboard route, for a notification's
        /// actionUrl. userNotifications is the same table Team_db (invites/removals)
        /// and Bookings_db (staff assignment/acceptance) already write to for the bell
        /// icon - every role's document manager lives on its own dashboard route.
        /// </summary>
        private static string DashboardUrlForRole(string roleCode) => roleCode switch
        {
            "FARMER" or "Boer" => "/boer/paneelbord",
            "PH" => "/ph/paneelbord",
            "PJ" => "/pj/paneelbord",
            "VG" => "/vg/paneelbord",
            _ => "/profiel"
        };

        /// <summary>
        /// DB-free smoke test for the [Authorize(Roles="Admin")] + JWT bearer pipeline,
        /// so it can be integration-tested without a live SQL Server.
        /// </summary>
        [HttpGet("ping")]
        public IActionResult Ping() => Ok();

        // Admin identity is always taken from the validated JWT, never from the request
        // body — a client cannot spoof who performed an action.
        /// <summary>The calling admin's own user id, from their validated JWT.</summary>
        private int CurrentAdminId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        /// <summary>The calling admin's display name, falling back to their email, then a generic "Admin" label.</summary>
        private string CurrentAdminName => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email) ?? "Admin";

        /// <summary>Records one admin-audit-log entry: who, what action, on what entity, why, plus the caller's IP/user-agent for traceability.</summary>
        private async Task WriteAuditLogAsync(string action, string targetType, string targetId, string reason)
        {
            await _auditDb.InsertAsync(new AdminAuditLogDto
            {
                AdminUserId = CurrentAdminId,
                AdminUserName = CurrentAdminName,
                Action = action,
                TargetEntityType = targetType,
                TargetEntityId = targetId,
                Reason = reason,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            });
        }

        // --- Audit Log ---

        /// <summary>Searches the admin audit log, optionally filtered by admin, action type, and date range.</summary>
        [HttpGet("audit-logs")]
        public async Task<IActionResult> GetAuditLogs([FromQuery] string? admin, [FromQuery] string? action, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            var logs = await _auditDb.GetAsync(admin, action, startDate, endDate);
            return Ok(logs);
        }

        /// <summary>Manually records a free-form audit-log entry (for admin actions not covered by a dedicated endpoint below).</summary>
        [HttpPost("audit-logs")]
        public async Task<IActionResult> CreateAuditLog([FromBody] CreateAuditLogRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest("'n Reden is verpligtend vir enige admin oudit-inskrywing.");

            await WriteAuditLogAsync(req.Action, req.TargetEntityType, req.TargetEntityId, req.Reason);
            return Ok(new { Success = true });
        }

        /// <summary>Exports the (filtered) audit log as a downloadable CSV file, up to 10,000 rows.</summary>
        [HttpGet("audit-logs/export")]
        public async Task<IActionResult> ExportAuditLogs([FromQuery] string? admin, [FromQuery] string? action, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            var logs = await _auditDb.GetAsync(admin, action, startDate, endDate, pageSize: 10000);

            var csv = new StringBuilder();
            csv.AppendLine("Id,AdminUserId,AdminUserName,Action,TargetEntityType,TargetEntityId,Reason,IpAddress,UserAgent,TimestampUtc");
            foreach (var log in logs)
            {
                csv.AppendLine(string.Join(",",
                    log.Id,
                    log.AdminUserId,
                    CsvEscape(log.AdminUserName),
                    CsvEscape(log.Action),
                    CsvEscape(log.TargetEntityType),
                    CsvEscape(log.TargetEntityId),
                    CsvEscape(log.Reason),
                    CsvEscape(log.IpAddress ?? ""),
                    CsvEscape(log.UserAgent ?? ""),
                    log.Timestamp.ToString("o")));
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"mikpunt-admin-audit-log-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }

        /// <summary>Quotes a CSV field if it contains a comma, quote, or newline, doubling any embedded quotes.</summary>
        private static string CsvEscape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        // --- Feature Flags ---

        /// <summary>Lists all feature flags and their current on/off state.</summary>
        [HttpGet("feature-flags")]
        public async Task<IActionResult> GetFeatureFlags()
        {
            return Ok(await _flagsDb.GetAllAsync());
        }

        /// <summary>Turns a feature flag on or off.</summary>
        [HttpPost("feature-flags/toggle")]
        public async Task<IActionResult> ToggleFeatureFlag([FromBody] ToggleFlagRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest("'n Reden is verpligtend vir enige stelselkenmerkwysiging.");

            var flag = await _flagsDb.ToggleAsync(req.Key, req.IsEnabled, CurrentAdminName);
            if (flag == null) return NotFound("Kenmerk nie gevind nie.");

            HttpContext?.RequestServices?.GetService<WebAPI.Services.IFeatureFlags>()?.Invalidate();
            await WriteAuditLogAsync("ToggleFeatureFlag", "FeatureFlag", req.Key, $"Vlag '{req.Key}' verander na {(req.IsEnabled ? "AAN" : "AF")}. Reden: {req.Reason}");

            return Ok(new { Success = true, Message = $"Kenmerk {req.Key} is nou {(req.IsEnabled ? "AAN" : "AF")}", Flag = flag });
        }

        // --- Role Verifications ---

        /// <summary>Lists all role-verification requests (a user applying for a PJ/PH/VG/Farmer role) across every status.</summary>
        [HttpGet("role-verifications")]
        public async Task<IActionResult> GetRoleVerifications()
        {
            return Ok(await _verificationsDb.GetAllAsync());
        }

        /// <summary>Approves or rejects a role-verification request; notifies the applicant by email and in-app bell either way.</summary>
        [HttpPost("role-verifications/{id}/decision")]
        public async Task<IActionResult> DecideRoleVerification(int id, [FromBody] RoleVerificationDecisionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest("'n Reden vir die verifikasiebepaling is verpligtend.");

            var updated = await _verificationsDb.DecideAsync(id, req.Approve, req.Reason);
            if (updated == null) return NotFound("Verifikasieversoek nie gevind nie, of is reeds afgehandel.");

            string action = req.Approve ? "ApproveRoleVerification" : "RejectRoleVerification";
            await WriteAuditLogAsync(action, "RoleVerification", id.ToString(),
                $"Rol: {updated.RequestedRoleCode} vir {updated.UserName}. Beslissing: {(req.Approve ? "Goedgekeur" : "Van hand gewys")}. Reden: {req.Reason}");

            // This decision previously notified nobody - neither email nor in-app -
            // unlike document verification's owner-facing notifications. Bring it to
            // the same standard.
            if (!string.IsNullOrWhiteSpace(updated.UserEmail))
            {
                string subject = req.Approve
                    ? $"MikPunt: U {updated.RequestedRoleName}-rol is goedgekeur"
                    : $"MikPunt: U {updated.RequestedRoleName}-rolaansoek is van hand gewys";
                string body = req.Approve
                    ? $"<p>Goeie dag {updated.UserName},</p><p>U aansoek vir die <strong>{updated.RequestedRoleName}</strong>-rol is deur 'n administrateur geverifieer en goedgekeur.</p>"
                    : $"<p>Goeie dag {updated.UserName},</p><p>U aansoek vir die <strong>{updated.RequestedRoleName}</strong>-rol is van hand gewys.</p><p><strong>Rede:</strong> {req.Reason}</p>";

                try { await _emailService.SendEmailAsync(updated.UserEmail, subject, body); }
                catch { /* best-effort notification - the decision itself already succeeded and was audited */ }
            }

            string notifTitle = req.Approve ? "Rol Goedgekeur" : "Rolaansoek Van Hand Gewys";
            string notifMessage = req.Approve
                ? $"U aansoek vir die {updated.RequestedRoleName}-rol is goedgekeur."
                : $"U aansoek vir die {updated.RequestedRoleName}-rol is van hand gewys. Rede: {req.Reason}";
            await _teamDb.InsertNotification(updated.UserId, CurrentAdminId,
                req.Approve ? "ROLE_APPROVED" : "ROLE_REJECTED",
                notifTitle, notifMessage, DashboardUrlForRole(updated.RequestedRoleCode));

            return Ok(new { Success = true, Verification = updated });
        }

        // --- Document Verifications (PJ/PH/VG/Farmer uploaded credentials) ---

        // status defaults to null (all statuses) rather than "Pending": when the query
        // string omits "status" entirely, ASP.NET Core model binding falls back to this
        // C# default rather than passing null through, so a "Pending" default here would
        // silently defeat AdminDashboard.razor's explicit request for the full history.
        /// <summary>Lists uploaded role-credential documents awaiting (or already given) a verification decision, optionally filtered by status/role.</summary>
        [HttpGet("document-verifications")]
        public async Task<IActionResult> GetDocumentVerifications([FromQuery] string? status = null, [FromQuery] string? roleCode = null)
        {
            return Ok(await _documentsDb.GetPendingVerificationQueue(status, roleCode));
        }

        /// <summary>Approves or rejects an uploaded document; notifies the owner by email and in-app bell either way.</summary>
        [HttpPost("document-verifications/{id}/decision")]
        public async Task<IActionResult> DecideDocumentVerification(int id, [FromBody] DocumentVerificationDecisionRequest req)
        {
            if (!req.Approve && string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest("'n Rede is verpligtend wanneer 'n dokument van hand gewys word.");

            var result = await _documentsDb.VerifyDocument(id, CurrentAdminId, req.Approve, req.Reason);
            if (result == null) return NotFound("Dokument nie gevind nie, of is reeds afgehandel.");

            string action = req.Approve ? "ApproveDocument" : "RejectDocument";
            await WriteAuditLogAsync(action, "UserDocument", id.ToString(),
                $"Dokument: {result.DocumentTypeName} ({result.RoleName}) vir {result.OwnerName}. Beslissing: {(req.Approve ? "Goedgekeur" : "Van hand gewys")}. Reden: {req.Reason}");

            if (!string.IsNullOrWhiteSpace(result.OwnerEmail))
            {
                string subject = req.Approve
                    ? $"MikPunt: U dokument '{result.DocumentTypeName}' is goedgekeur"
                    : $"MikPunt: U dokument '{result.DocumentTypeName}' is van hand gewys";
                string body = req.Approve
                    ? $"<p>Goeie dag {result.OwnerName},</p><p>U dokument <strong>{result.DocumentTypeName}</strong> vir u {result.RoleName}-rol is deur 'n administrateur geverifieer en goedgekeur.</p>"
                    : $"<p>Goeie dag {result.OwnerName},</p><p>U dokument <strong>{result.DocumentTypeName}</strong> vir u {result.RoleName}-rol is van hand gewys.</p><p><strong>Rede:</strong> {req.Reason}</p><p>Laai asseblief 'n vervangende dokument op vanaf u dashboard.</p>";

                try { await _emailService.SendEmailAsync(result.OwnerEmail, subject, body); }
                catch { /* best-effort notification - the decision itself already succeeded and was audited */ }
            }

            // In-app bell notification - email alone misses anyone who doesn't check
            // their inbox promptly; every other status-changing event in the app
            // (team invites/removals, booking staff assignment) also writes here.
            // InsertNotification swallows its own failures, so no try/catch needed.
            string notifTitle = req.Approve ? "Dokument Goedgekeur" : "Dokument Van Hand Gewys";
            string notifMessage = req.Approve
                ? $"U dokument '{result.DocumentTypeName}' vir u {result.RoleName}-rol is goedgekeur."
                : $"U dokument '{result.DocumentTypeName}' vir u {result.RoleName}-rol is van hand gewys. Rede: {req.Reason}";
            await _teamDb.InsertNotification(result.OwnerUserId, CurrentAdminId,
                req.Approve ? "DOCUMENT_APPROVED" : "DOCUMENT_REJECTED",
                notifTitle, notifMessage, DashboardUrlForRole(result.RoleCode));

            return Ok(new { Success = true, Result = result });
        }

        // --- Bookings (dispute review & correction) ---

        /// <summary>Lists every booking in the system, for admin review.</summary>
        [HttpGet("bookings")]
        public async Task<IActionResult> GetBookings()
        {
            return Ok(await _bookingsDb.GetAllBookingsForAdmin());
        }

        /// <summary>Overrides a booking's price/guest counts/status to resolve a dispute.</summary>
        [HttpPost("bookings/{id}/correct")]
        public async Task<IActionResult> CorrectBooking(int id, [FromBody] CorrectBookingRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest("'n Reden vir die regstelling is verpligtend.");

            var updateReq = new UpdateBookingRequest
            {
                TotalPrice = req.CorrectedTotalPrice,
                AccommodationPrice = req.CorrectedAccommodationTotal,
                HuntGuests = req.CorrectedHunterCount,
                NonHuntGuests = req.CorrectedObserverCount,
                Status = req.Status
            };

            var success = await _bookingsDb.UpdateBooking(id, updateReq);
            if (!success) return NotFound("Bespreking nie gevind nie.");

            string correction = $"Prys reggestel na ${req.CorrectedTotalPrice:N2}. Jagters: {req.CorrectedHunterCount}, Waarnemers: {req.CorrectedObserverCount}. Reden: {req.Reason}";
            await WriteAuditLogAsync("CorrectBookingDispute", "Booking", id.ToString(), correction);
            // Also on the booking's own trail, which is what the PJ/hunter see.
            await _bookingsDb.AddBookingAuditLog(id, "Admin Regstelling", User.GetUserId() ?? 0, "Administrateur", correction);

            return Ok(new { Success = true });
        }

        // --- Users: suspend / ban / reactivate ---

        /// <summary>Changes a user's account status (Approved/Suspended/Banned/etc.) with a required reason.</summary>
        [HttpPost("users/{id}/status")]
        public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest("'n Reden is verpligtend vir enige admin statuswysiging.");
            if (User.GetUserId() == id && !string.Equals(req.Status, "Active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(req.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "U kan nie u eie rekening skors of verban nie." });

            // Two states: Active and Suspended. "Approved" (what the old Aktiveer button sent) means Active.
            string status = req.Status switch
            {
                _ when string.Equals(req.Status, "Approved", StringComparison.OrdinalIgnoreCase) => "Active",
                _ when string.Equals(req.Status, "Active", StringComparison.OrdinalIgnoreCase) => "Active",
                _ when string.Equals(req.Status, "Suspended", StringComparison.OrdinalIgnoreCase) => "Suspended",
                _ when string.Equals(req.Status, "Banned", StringComparison.OrdinalIgnoreCase) => "Banned",
                _ => ""
            };
            if (status.Length == 0)
                return BadRequest(new { message = "Onbekende status. Gebruik 'Active' of 'Suspended'." });

            var success = await _userDb.UpdateAccountStatus(id, status, req.Reason);
            if (!success) return NotFound("Gebruiker nie gevind nie.");
            // Make the change effective for tokens already issued (checked in OnTokenValidated), and
            // end the app's sessions too: its refresh tokens go, so the app can't get a new token.
            HttpContext?.RequestServices?.GetService<WebAPI.Services.IUserStatusCache>()?.Invalidate(id);
            if (status != "Active")
            {
                try { await _userDb.DeleteAppSessionsAsync(id); }
                catch (Exception ex) { Console.WriteLine($"[ADMIN] Could not end app sessions for user {id}: {ex.Message}"); }
            }

            string action = status switch
            {
                "Active" => "ReactivateUser",
                "Suspended" => "SuspendUser",
                "Banned" => "BanUser",
                _ => $"UpdateStatus_{status}"
            };
            await WriteAuditLogAsync(action, "User", id.ToString(), $"Status verander na '{status}'. Reden: {req.Reason}");

            return Ok(new { Success = true });
        }

        // --- Chat moderation ---

        /// <summary>Lists chat messages flagged (by keyword filter or user report) for admin review.</summary>
        [HttpGet("flagged-messages")]
        public async Task<IActionResult> GetFlaggedMessages()
        {
            return Ok(await _chatsDb.GetFlaggedMessages());
        }

        /// <summary>Acts on a flagged chat message (e.g. dismiss the flag, or remove the message).</summary>
        [HttpPost("flagged-messages/{id}/moderate")]
        public async Task<IActionResult> ModerateFlaggedMessage(int id, [FromBody] ModerateMessageRequest req)
        {
            var success = await _chatsDb.ModerateMessage(id, req.Action, CurrentAdminId);
            if (!success) return NotFound("Boodskap nie gevind nie.");

            await WriteAuditLogAsync($"ModerateChat_{req.Action}", "ChatMessage", id.ToString(),
                $"Moderering aksie: {req.Action}. Reden: {req.Reason}");

            return Ok(new { Success = true });
        }

        // --- Rating comment moderation ---

        /// <summary>Lists rating comments flagged for admin review.</summary>
        [HttpGet("flagged-ratings")]
        public async Task<IActionResult> GetFlaggedRatingComments()
        {
            return Ok(await _ratingDb.GetFlaggedRatingComments());
        }

        /// <summary>Acts on a flagged rating comment.</summary>
        [HttpPost("flagged-ratings/{id}/moderate")]
        public async Task<IActionResult> ModerateFlaggedRatingComment(int id, [FromBody] ModerateMessageRequest req)
        {
            var success = await _ratingDb.ModerateRatingComment(id, req.Action, CurrentAdminId);
            if (!success) return NotFound("Gradering-kommentaar nie gevind nie.");

            await WriteAuditLogAsync($"ModerateRating_{req.Action}", "RatingComment", id.ToString(),
                $"Moderering aksie: {req.Action}. Reden: {req.Reason}");

            return Ok(new { Success = true });
        }

        // --- Rating disputes & edit history ---

        /// <summary>Lists rating disputes, optionally filtered by status.</summary>
        [HttpGet("rating-disputes")]
        public async Task<IActionResult> GetRatingDisputes([FromQuery] string? status = null)
        {
            return Ok(await _ratingDb.GetDisputes(status));
        }

        /// <summary>Resolves a rating dispute as Upheld (the rating is removed/corrected) or Denied (the rating stands).</summary>
        [HttpPost("rating-disputes/{id}/resolve")]
        public async Task<IActionResult> ResolveRatingDispute(int id, [FromBody] ResolveDisputeRequest req)
        {
            if (!string.Equals(req.Status, "Upheld", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(req.Status, "Denied", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Status moet 'Upheld' of 'Denied' wees.");
            }

            var success = await _ratingDb.ResolveDispute(id, req.Status, CurrentAdminId, req.Notes);
            if (!success) return NotFound("Dispuut nie gevind nie.");

            await WriteAuditLogAsync($"ResolveRatingDispute_{req.Status}", "RatingDispute", id.ToString(),
                $"Besluit: {req.Status}. Notas: {req.Notes}");

            return Ok(new { Success = true });
        }

        /// <summary>Edit history for a single rating (e.g. prior dispute outcomes/corrections).</summary>
        [HttpGet("ratings/{ratingId}/history")]
        public async Task<IActionResult> GetRatingHistory(int ratingId)
        {
            return Ok(await _ratingDb.GetRatingHistory(ratingId));
        }

        // --- System Settings (pricing + operational constants) ---

        /// <summary>Lists every system setting, public and admin-only alike.</summary>
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            return Ok(await _settingsDb.GetAllAsync());
        }

        /// <summary>Updates a system setting's value (must be a non-negative number) and refreshes the in-memory settings cache.</summary>
        [HttpPost("settings/update")]
        public async Task<IActionResult> UpdateSetting([FromBody] UpdateSettingRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return BadRequest("'n Reden is verpligtend vir enige instellingwysiging.");

            if (!decimal.TryParse(req.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                return BadRequest("Waarde moet 'n geldige, nie-negatiewe getal wees.");

            var updated = await _settingsDb.SetAsync(req.Key, req.Value, CurrentAdminName);
            if (updated == null) return NotFound("Instelling nie gevind nie.");

            _settingsCache.Set(req.Key, req.Value);

            await WriteAuditLogAsync("UpdateSystemSetting", "SystemSetting", req.Key, $"Waarde verander na '{req.Value}'. Reden: {req.Reason}");

            return Ok(new { Success = true, Setting = updated });
        }

        // --- Health ---

        /// <summary>Summarised system health for the admin dashboard: DB connectivity, response time, and active session count.</summary>
        [HttpGet("health")]
        public async Task<IActionResult> GetSystemHealth()
        {
            var report = await _healthCheckService.CheckHealthAsync();
            var dbCheck = report.Entries.TryGetValue("database", out var entry) ? entry : (HealthReportEntry?)null;
            double responseTimeMs = dbCheck?.Data.TryGetValue("responseTimeMs", out var v) == true ? Convert.ToDouble(v) : 0;

            var services = HttpContext?.RequestServices;
            var (mobileOk, mobileStatus, mobileMs) = await CheckMobileApiAsync(services);
            var flags = services?.GetService<WebAPI.Services.IFeatureFlags>();
            int appSessions = -1;
            try { appSessions = await _userDb.CountAppSessionsAsync(); } catch { /* shown as unknown */ }

            return Ok(new
            {
                DbConnected = report.Status == HealthStatus.Healthy,
                DbStatus = dbCheck?.Description ?? report.Status.ToString(),
                ApiUptimeHealthy = report.Status == HealthStatus.Healthy,
                ResponseTimeMs = (int)Math.Round(responseTimeMs),
                LastHeartbeat = DateTime.UtcNow,
                ActiveSessions = _sessionStore.CountActiveSessions(),
                AppSessions = appSessions,
                MobileApiHealthy = mobileOk,
                MobileApiStatus = mobileStatus,
                MobileApiResponseMs = mobileMs,
                MaintenanceMode = flags != null && await flags.IsEnabledAsync(WebAPI.Services.FeatureFlagKeys.MaintenanceMode, false),
                RegistrationsOpen = flags == null || await flags.IsEnabledAsync(WebAPI.Services.FeatureFlagKeys.NewUserRegistrations, true),
                PayFastSandbox = flags == null || await flags.IsEnabledAsync(WebAPI.Services.FeatureFlagKeys.PayFastSandbox, true),
                Environment = services?.GetService<IWebHostEnvironment>()?.EnvironmentName ?? "",
                ServerTimeUtc = DateTime.UtcNow
            });
        }

        /// <summary>Pings the MobileAPI's /health (InternalApi:MobileApiBaseUrl) - the app depends on it.</summary>
        private static async Task<(bool Ok, string Status, int Ms)> CheckMobileApiAsync(IServiceProvider? services)
        {
            var baseUrl = services?.GetService<Microsoft.Extensions.Options.IOptions<WebAPI.Services.InternalApiOptions>>()?.Value.MobileApiBaseUrl;
            var factory = services?.GetService<IHttpClientFactory>();
            if (string.IsNullOrWhiteSpace(baseUrl) || factory == null)
                return (false, "Nie opgestel nie (InternalApi:MobileApiBaseUrl)", 0);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var client = factory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                using var response = await client.GetAsync(baseUrl.TrimEnd('/') + "/health");
                return (response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "Gesond" : $"Fout {(int)response.StatusCode}", (int)timer.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                return (false, "Onbereikbaar: " + ex.GetType().Name, (int)timer.ElapsedMilliseconds);
            }
        }
    }
}

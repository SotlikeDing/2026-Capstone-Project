using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;
using WebApp.Models;

namespace WebApp.Services
{
    /// <summary>
    /// Scoped (per-circuit) auth/session state for the Blazor Server app. The actual JWT
    /// is issued by WebAPI's UserController - this service just holds it client-side and
    /// mirrors it into a JS-interop-backed browser storage helper ("mikpuntAuthStorage")
    /// so a session survives a Blazor Server circuit reconnect (prerender -> SignalR
    /// circuit) without a full re-login. _registeredUsers is leftover in-memory demo
    /// scaffolding, not a real user store - actual registration persists via
    /// MikpuntApiService/WebAPI's UserController.Register.
    /// </summary>
    public class AuthService
    {
        private readonly List<UserModel> _registeredUsers = new();
        private IJSRuntime? _js;

        /// <summary>"Onthou my": keep the session after the browser closes (localStorage) or not (sessionStorage).</summary>
        private bool _rememberMe = true;

        public UserModel? CurrentUser { get; private set; }
        public bool IsAuthenticated => CurrentUser != null;
        public string Token { get; private set; } = string.Empty;
        public bool IsInitialized { get; private set; } = false;

        public event Action? OnAuthStateChanged;

        /// <summary>
        /// Raised when the WebAPI refuses this circuit's token (401) - suspended, signed out
        /// elsewhere or expired. MainLayout then signs the browser out and shows the login page.
        /// </summary>
        public event Action? OnSessionRejected;

        /// <summary>Called by AuthTokenHandler on a 401 for a request that carried this session's token.</summary>
        public void NotifySessionRejected()
        {
            if (IsAuthenticated) OnSessionRejected?.Invoke();
        }

        /// <summary>
        /// Best-effort server-side pre-hydration from request cookies, so a freshly
        /// prerendered page can know a user is (probably) logged in before the Blazor
        /// circuit/JS interop is available to read full session JSON. The resulting
        /// CurrentUser is a stub (empty name/email) - InitializeAsync fills in the real
        /// data once JS interop is up.
        /// </summary>
        public AuthService(IHttpContextAccessor? httpContextAccessor = null)
        {
            CurrentUser = null;
            Token = string.Empty;

            try
            {
                if (httpContextAccessor?.HttpContext?.Request?.Cookies != null)
                {
                    var cookies = httpContextAccessor.HttpContext.Request.Cookies;
                    if (cookies.TryGetValue("mikpunt_uid", out var uidStr) && int.TryParse(uidStr, out var uid) && uid > 0)
                    {
                        cookies.TryGetValue("mikpunt_token", out var tok);
                        CurrentUser = new UserModel
                        {
                            UserId = uid,
                            FirstName = string.Empty,
                            Email = string.Empty
                        };
                        Token = tok ?? string.Empty;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Rehydrates CurrentUser/Token from browser storage on circuit startup, then
        /// re-fetches the latest profile/roles from the DB (if an api client is given)
        /// so a stale cached role list doesn't override a since-changed one - e.g. a
        /// role approved by an admin after the browser session was saved. Swallows all
        /// errors (console-logs only) so a storage/API hiccup never blocks app startup;
        /// IsInitialized is set true in the finally block regardless of outcome.
        /// </summary>
        public async Task InitializeAsync(IJSRuntime js, MikpuntApiService? api = null)
        {
            _js = js;

            try
            {
                var sessionJson = await js.InvokeAsync<string?>("mikpuntAuthStorage.getUser");
                if (!string.IsNullOrWhiteSpace(sessionJson))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var session = JsonSerializer.Deserialize<UserSessionModel>(sessionJson, options);
                    if (session?.User != null)
                    {
                        CurrentUser = session.User;
                        Token = session.Token ?? string.Empty;
                        try { _rememberMe = await js.InvokeAsync<bool>("mikpuntAuthStorage.isRemembered"); } catch { }
                    }
                }

                // Always fetch and sync latest roles from DB profile when initialized
                if (CurrentUser != null && api != null && CurrentUser.UserId > 0)
                {
                    var (success, dbProfile, _) = await api.GetProfileAsync(CurrentUser.UserId);
                    if (success && dbProfile != null)
                    {
                        SyncUserFromProfile(dbProfile, notifyStateChanged: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AUTH REHYDRATION ERROR] {ex.Message}");
            }
            finally
            {
                IsInitialized = true;
                OnAuthStateChanged?.Invoke();
            }
        }

        /// <summary>Checks the in-memory _registeredUsers scaffolding only - not a real duplicate-email check against the DB (WebAPI's Register endpoint enforces that server-side).</summary>
        public bool IsEmailRegistered(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            return _registeredUsers.Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Unused: no callers found (Grep-verified). Leftover demo/dev helper that logs in as a synthetic admin with every role pre-approved, bypassing real authentication entirely.</summary>
        public void LoadDemoAdminUser()
        {
            CurrentUser = new UserModel
            {
                UserId = 1,
                Email = "admin@gmail.com",
                FirstName = "System",
                LastName = "Admin",
                Phone = "+27 82 555 0192",
                IdPassportNum = "8804125019083",
                IsVerified = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-6),
                Roles = new List<UserRoleModel>
                {
                    new UserRoleModel { Id = 1, UserId = 1, RoleCode = "Jagter", RoleName = "Jagter / Hunter", Status = "Approved" },
                    new UserRoleModel { Id = 2, UserId = 1, RoleCode = "Boer", RoleName = "Boer / Farmer", Status = "Approved" },
                    new UserRoleModel { Id = 3, UserId = 1, RoleCode = "PJ", RoleName = "Pakket Uitruster", Status = "Approved" },
                    new UserRoleModel { Id = 4, UserId = 1, RoleCode = "PH", RoleName = "Professional Hunter", Status = "Approved" },
                    new UserRoleModel { Id = 5, UserId = 1, RoleCode = "VG", RoleName = "Videographer", Status = "Approved" },
                    new UserRoleModel { Id = 6, UserId = 1, RoleCode = "AP", RoleName = "Advertiser", Status = "Approved" },
                    new UserRoleModel { Id = 7, UserId = 1, RoleCode = "Admin", RoleName = "System Administrator", Status = "Approved" }
                }
            };
            Token = "demo-jwt-token-mikpunt";
            IsInitialized = true;
            _ = SaveSessionToStorageAsync();
            OnAuthStateChanged?.Invoke();
        }

        /// <summary>
        /// One code per role, whatever language it arrives in: JAGTER/HUNTER -> HUNTER, BOER/FARMER/GRONDEIENAAR
        /// -> FARMER, ADVERTEERDER/ADVERTISER/AP -> AP, ADMINISTRATEUR/ADMIN -> ADMIN, else upper-cased as is.
        /// </summary>
        public static string CanonicalRoleCode(string? roleCode)
        {
            string code = (roleCode ?? "").Trim().ToUpperInvariant();
            return code switch
            {
                "JAGTER" or "HUNTER" => "HUNTER",
                "BOER" or "FARMER" or "GRONDEIENAAR" => "FARMER",
                "AP" or "ADVERTISER" or "ADVERTEERDER" => "AP",
                "ADMIN" or "ADMINISTRATEUR" or "ADMINISTRATOR" => "ADMIN",
                _ => code
            };
        }

        /// <summary>Whether two role codes mean the same role (see CanonicalRoleCode).</summary>
        public static bool SameRole(string? a, string? b) => CanonicalRoleCode(a) == CanonicalRoleCode(b);

        /// <summary>
        /// Checks CurrentUser's roles for roleCode, matching on either RoleCode or
        /// RoleName and folding known Afrikaans/English synonym pairs (Jagter/Hunter,
        /// Boer/Farmer, AP/Advertiser/Adverteerder, Admin/Administrateur) together, since
        /// role data can arrive from either language depending on where it was seeded.
        /// </summary>
        public bool HasRole(string roleCode)
        {
            if (CurrentUser == null || CurrentUser.Roles == null) return false;

            // Compare whole codes/names after folding synonyms. (It used to match any role *name* containing
            // "JAGTER", so the PH role "Professionele Jagter" counted as Hunter for every PH.)
            string wanted = CanonicalRoleCode(roleCode);
            if (wanted.Length == 0) return false;
            return CurrentUser.Roles.Any(r => CanonicalRoleCode(r.RoleCode) == wanted || CanonicalRoleCode(r.RoleName) == wanted);
        }

        /// <summary>Sets the session after a successful WebAPI sign-in and awaits the browser-storage write, so a caller can be sure the session is persisted before e.g. navigating away.</summary>
        public async Task LoginAsync(UserModel user, string token, IJSRuntime? js = null, bool rememberMe = true)
        {
            if (js != null) _js = js;
            _rememberMe = rememberMe;
            CurrentUser = user;
            Token = token;
            IsInitialized = true;
            await SaveSessionToStorageAsync();
            OnAuthStateChanged?.Invoke();
        }

        /// <summary>Fire-and-forget variant of LoginAsync for call sites that can't/don't need to await the storage write.</summary>
        public void Login(UserModel user, string token)
        {
            CurrentUser = user;
            Token = token;
            IsInitialized = true;
            _ = SaveSessionToStorageAsync();
            OnAuthStateChanged?.Invoke();
        }

        /// <summary>
        /// Local-state-only sync called by Register.razor right after the real
        /// server-side registration (Api.RegisterUserAsync -> WebAPI UserController.Register)
        /// already succeeded - adds to the in-memory _registeredUsers scaffolding and sets
        /// a placeholder token so the UI has "someone" as CurrentUser while redirecting to
        /// email verification. Not itself how registration is persisted.
        /// </summary>
        public void Register(UserModel user)
        {
            if (IsEmailRegistered(user.Email))
            {
                throw new InvalidOperationException($"'n Rekening met die e-posadres '{user.Email}' bestaan reeds in MikPunt.");
            }

            user.Roles ??= new List<UserRoleModel>();
            _registeredUsers.Add(user);
            CurrentUser = user;
            Token = "new-reg-jwt-token";
            IsInitialized = true;
            _ = SaveSessionToStorageAsync();
            OnAuthStateChanged?.Invoke();
        }

        /// <summary>
        /// Revokes a token server-side (api/user/logout). Registered by MikpuntApiService, which
        /// owns the WebAPI HttpClient; AuthService can't depend on it directly (circular).
        /// </summary>
        public Func<string, Task>? RevokeTokenAsync { get; set; }

        /// <summary>Clears session state and awaits the browser-storage clear, so a caller can be sure storage is wiped before e.g. navigating to the login page.</summary>
        public async Task LogoutAsync(IJSRuntime? js = null)
        {
            if (js != null) _js = js;
            // Revoke first so a copied/stolen token stops working, not just this browser's copy.
            if (!string.IsNullOrWhiteSpace(Token) && RevokeTokenAsync != null)
            {
                try { await RevokeTokenAsync(Token); }
                catch { /* signing out locally must never fail because the API is unreachable */ }
            }
            CurrentUser = null;
            Token = string.Empty;
            if (_js != null)
            {
                try
                {
                    await _js.InvokeVoidAsync("mikpuntAuthStorage.clearUser");
                }
                catch { }
            }
            OnAuthStateChanged?.Invoke();
        }

        /// <summary>Fire-and-forget variant of LogoutAsync for call sites that can't/don't need to await the storage clear.</summary>
        public void Logout()
        {
            CurrentUser = null;
            Token = string.Empty;
            if (_js != null)
            {
                try
                {
                    _ = _js.InvokeVoidAsync("mikpuntAuthStorage.clearUser");
                }
                catch { }
            }
            OnAuthStateChanged?.Invoke();
        }

        /// <summary>Serializes CurrentUser+Token to JSON and hands it to the "mikpuntAuthStorage" JS helper. No-op (silently) if JS interop isn't wired up yet or CurrentUser is null - callers don't need to guard against that themselves.</summary>
        public async Task SaveSessionToStorageAsync()
        {
            if (_js != null && CurrentUser != null)
            {
                try
                {
                    var session = new UserSessionModel { User = CurrentUser, Token = Token };
                    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                    var json = JsonSerializer.Serialize(session, options);
                    await _js.InvokeVoidAsync("mikpuntAuthStorage.saveUser", json, CurrentUser.UserId, Token, _rememberMe);
                }
                catch { }
            }
        }

        /// <summary>
        /// Merges a freshly-fetched DB profile into CurrentUser: creates CurrentUser from
        /// scratch if there isn't one yet, otherwise only overwrites fields the profile
        /// actually has a value for (blank/null fields on the incoming profile leave the
        /// existing CurrentUser field untouched) and merges roles by RoleCode rather than
        /// replacing the list wholesale, so any purely local, not-yet-persisted role
        /// additions aren't clobbered.
        /// </summary>
        public void SyncUserFromProfile(UserProfileApiResponseModel profile, bool notifyStateChanged = true)
        {
            if (CurrentUser == null)
            {
                CurrentUser = new UserModel
                {
                    UserId = profile.UserId,
                    Email = profile.Email,
                    FirstName = profile.Name,
                    LastName = profile.Surname,
                    Phone = profile.Phone ?? "",
                    IdPassportNum = profile.IdPassportNum ?? "",
                    ProfilePictureUrl = profile.ProfilePictureUrl,
                    Roles = profile.Roles ?? new List<UserRoleModel>()
                };
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(profile.Name)) CurrentUser.FirstName = profile.Name;
                if (!string.IsNullOrWhiteSpace(profile.Surname)) CurrentUser.LastName = profile.Surname;
                if (!string.IsNullOrWhiteSpace(profile.Phone)) CurrentUser.Phone = profile.Phone;
                if (!string.IsNullOrWhiteSpace(profile.IdPassportNum)) CurrentUser.IdPassportNum = profile.IdPassportNum;
                if (profile.ProfilePictureUrl != null) CurrentUser.ProfilePictureUrl = profile.ProfilePictureUrl;
                if (profile.Roles != null && profile.Roles.Count > 0)
                {
                    CurrentUser.Roles ??= new List<UserRoleModel>();
                    foreach (var r in profile.Roles)
                    {
                        // Same role under another name (the form says "Jagter", the database "HUNTER"): update it,
                        // taking the database's code, instead of adding a second copy.
                        var existing = CurrentUser.Roles.FirstOrDefault(x => SameRole(x.RoleCode, r.RoleCode));
                        if (existing != null)
                        {
                            existing.RoleCode = r.RoleCode;
                            existing.RoleName = r.RoleName;
                            existing.Status = r.Status;
                            existing.Id = r.Id;
                        }
                        else
                        {
                            CurrentUser.Roles.Add(r);
                        }
                    }
                    // Sessions saved before this fix may already hold both "Jagter" and "HUNTER": keep one per role.
                    CurrentUser.Roles = CurrentUser.Roles
                        .GroupBy(x => CanonicalRoleCode(x.RoleCode))
                        .Select(g => g.FirstOrDefault(x => profile.Roles.Any(pr => pr.Id == x.Id && pr.Id > 0)) ?? g.First())
                        .ToList();
                }
            }
            _ = SaveSessionToStorageAsync();
            if (notifyStateChanged)
            {
                OnAuthStateChanged?.Invoke();
            }
        }

        /// <summary>Updates just the avatar URL on CurrentUser and re-persists the session - a lighter-weight alternative to a full SyncUserFromProfile call after an avatar upload.</summary>
        public void UpdateProfilePicture(string? url)
        {
            if (CurrentUser != null)
            {
                CurrentUser.ProfilePictureUrl = url;
                _ = SaveSessionToStorageAsync();
                OnAuthStateChanged?.Invoke();
            }
        }

        /// <summary>
        /// Optimistically adds a role to CurrentUser's local role list (e.g. right after
        /// a role-registration API call succeeds), auto-assigning a local sequential Id
        /// and marking it "Approved" client-side - this is purely local state, it does
        /// not call the API itself, so callers must ensure the role was actually
        /// persisted server-side first.
        /// </summary>
        public void AddRoleToCurrentUser(string roleCode, string roleName, string credentials = "")
        {
            if (CurrentUser == null) return;
            CurrentUser.Roles ??= new List<UserRoleModel>();

            if (!HasRole(roleCode))
            {
                CurrentUser.Roles.Add(new UserRoleModel
                {
                    Id = CurrentUser.Roles.Count + 1,
                    UserId = CurrentUser.UserId,
                    RoleCode = roleCode,
                    RoleName = roleName,
                    Status = "Approved",
                    CredentialsDetails = credentials,
                    AssignedDate = DateTime.UtcNow
                });
                _ = SaveSessionToStorageAsync();
                OnAuthStateChanged?.Invoke();
            }
        }

        /// <summary>Local-state-only role removal, same Jagter/Hunter and Boer/Farmer synonym folding as HasRole so a role added under one language variant can still be removed by the other.</summary>
        public void RemoveRoleFromCurrentUser(string roleCode)
        {
            if (CurrentUser == null || CurrentUser.Roles == null) return;
            string normalized = (roleCode ?? "").Trim().ToUpperInvariant();
            string altNormalized = normalized switch
            {
                "JAGTER" => "HUNTER",
                "HUNTER" => "JAGTER",
                "BOER" => "FARMER",
                "FARMER" => "BOER",
                _ => normalized
            };

            CurrentUser.Roles.RemoveAll(r =>
            {
                string rCode = (r.RoleCode ?? "").Trim().ToUpperInvariant();
                return rCode == normalized || rCode == altNormalized;
            });

            _ = SaveSessionToStorageAsync();
            OnAuthStateChanged?.Invoke();
        }
    }
}

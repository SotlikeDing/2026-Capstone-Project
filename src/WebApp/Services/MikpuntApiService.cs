using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WebApp.Models;

namespace WebApp.Services
{
    /// <summary>
    /// The Blazor app's single gateway to WebAPI - every page goes through this service
    /// rather than calling HttpClient directly. Most methods map 1:1 onto a WebAPI
    /// controller action (noted per-method below). A number of fields below
    /// (_farms/_packages/_bookings/_teamInvites/_messages/_permits/_sapsForms/_phPackages/
    /// _vgPackages/_subscriptions/_phRates) are leftover in-memory "robust operation"
    /// scaffolding from before the real API existed - several call sites still read/write
    /// them as a local cache or demo-data fallback, but the ads cache (_ads, static and
    /// shared across all circuits with a 3-minute TTL) is the one still load-bearing for
    /// real caching behavior. AttachAuthHeader must be called on every request to an
    /// [Authorize]-protected endpoint; a method that skips it against such an endpoint is
    /// a bug (see the notification-methods bug fixed earlier in this codebase's history).
    /// </summary>
    public class MikpuntApiService
    {
        private readonly HttpClient _http;

        // In-memory repositories for robust operation
        private readonly List<FarmModel> _farms = new();
        private readonly List<HuntingPackageModel> _packages = new();
        private readonly List<BookingModel> _bookings = new();
        private readonly List<TeamInviteModel> _teamInvites = new();
        private readonly List<MessageModel> _messages = new();
        private readonly List<PermitModel> _permits = new();
        private readonly List<Saps520FormModel> _sapsForms = new();
        private static readonly List<AdvertisementModel> _ads = new();
        private static DateTime _adsLastFetched = DateTime.MinValue;
        private static readonly TimeSpan _adsCacheTtl = TimeSpan.FromMinutes(3);
        private static readonly SemaphoreSlim _adsFetchLock = new(1, 1);
        /// <summary>The configured WebAPI base URL (trailing slash trimmed), used by pages that need to build an absolute link (e.g. an image/file URL) rather than a relative HttpClient request path.</summary>
        public string BaseApiUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? "http://localhost:5004";
        private readonly List<ServicePackageModel> _phPackages = new();
        private readonly List<ServicePackageModel> _vgPackages = new();
        private readonly Dictionary<string, RoleSubscriptionModel> _subscriptions = new();
        private readonly Dictionary<int, PhDailyRatesModel> _phRates = new();

        private readonly AuthService _auth;

        public MikpuntApiService(HttpClient http, AuthService auth)
        {
            _http = http;
            _auth = auth;
            _auth.RevokeTokenAsync ??= token => _http.PostAsJsonAsync("api/user/logout", new { token });
            // Every list starts empty and is filled only from the WebAPI. There used to be
            // hardcoded demo farms, packages and a booking here, which the marketplace and
            // farm pages showed next to (or instead of) the real ones.
            _adsLastFetched = DateTime.MinValue;
        }

        /// <summary>
        /// Attaches the signed-in user's JWT as a Bearer token. Required for every
        /// api/admin/* call now that those endpoints enforce [Authorize(Roles="Admin")].
        /// </summary>
        /// <summary>GET api/user/session: false only when the WebAPI refuses the token (401) - suspended, revoked or expired.</summary>
        public async Task<bool?> IsSessionValidAsync()
        {
            if (string.IsNullOrWhiteSpace(_auth.Token)) return null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "api/user/session");
                AttachAuthHeader(req);
                using var res = await _http.SendAsync(req);
                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized) return false;
                return res.IsSuccessStatusCode ? true : null;   // 503 maintenance etc.: not a verdict on the session
            }
            catch (Exception) { return null; }
        }

        /// <summary>Maintenance mode and open registrations (admin flags), cached briefly for every visitor.</summary>
        public async Task<SiteStatusModel> GetSiteStatusAsync()
        {
            var cached = _siteStatus;
            if (cached != null && DateTime.UtcNow - cached.LoadedAt < TimeSpan.FromSeconds(15)) return cached;
            try
            {
                var status = await _http.GetFromJsonAsync<SiteStatusModel>("api/settings/status");
                if (status != null)
                {
                    status.LoadedAt = DateTime.UtcNow;
                    _siteStatus = status;
                    return status;
                }
            }
            catch (Exception ex) { Console.WriteLine("Error fetching site status: " + ex.Message); }
            return cached ?? new SiteStatusModel();
        }

        private static SiteStatusModel? _siteStatus;

        private void AttachAuthHeader(HttpRequestMessage req)
        {
            if (!string.IsNullOrWhiteSpace(_auth.Token))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
            }
        }

        private async Task<(bool Success, string Message)> PostAdminActionAsync(string url, object body)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                string content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return (false, string.IsNullOrWhiteSpace(content) ? $"Fout: {response.StatusCode}" : content);
                }

                try
                {
                    using var doc = JsonDocument.Parse(content);
                    string msg = doc.RootElement.TryGetProperty("message", out var m) ? (m.GetString() ?? "Aksie suksesvol voltooi.") : "Aksie suksesvol voltooi.";
                    return (true, msg);
                }
                catch (JsonException)
                {
                    return (true, "Aksie suksesvol voltooi.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        // --- Farms ---
        /// <summary>GET api/farmer/all. On success, replaces the local _farms cache and filters by farmerUserId if given. If the call fails, the last list fetched from the server this session is used (never demo data).</summary>
        public async Task<List<FarmModel>> GetFarmsAsync(int? farmerUserId = null)
        {
            try
            {
                var apiRes = await _http.GetFromJsonAsync<List<FarmModel>>("api/farmer/all");
                if (apiRes != null)
                {
                    _farms.Clear();
                    _farms.AddRange(apiRes);

                    return farmerUserId.HasValue ? apiRes.Where(f => f.FarmerUserId == farmerUserId.Value).ToList() : apiRes;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GET FARMS API ERROR] {ex.Message}");
            }
            
            var query = _farms.AsEnumerable();
            if (farmerUserId.HasValue)
            {
                query = query.Where(f => f.FarmerUserId == farmerUserId.Value);
            }
            else
            {
                query = query.Where(f => f.IsActive);
            }
            return query.ToList();
        }

        /// <summary>
        /// One farm with full details. Uses api/farmer/all (the same shape GetFarmsAsync
        /// maps), from this session's cache or freshly fetched. The old call to
        /// api/farmer/{farmId} hit a route that doesn't exist (the API has getFarm/{id},
        /// with a smaller shape), so a farm opened by link never loaded.
        /// </summary>
        public async Task<FarmModel?> GetFarmByIdAsync(int farmId)
        {
            var cached = _farms.FirstOrDefault(f => f.Id == farmId);
            if (cached != null) return cached;
            return (await GetFarmsAsync()).FirstOrDefault(f => f.Id == farmId);
        }

        /// <summary>
        /// Returns whether the save actually reached the server successfully. Both
        /// branches used to silently swallow a failed HTTP call and mutate the local
        /// cache as if it had succeeded - the caller would navigate away believing the
        /// farm was saved/updated when the server had rejected or never received it.
        /// </summary>
        public async Task<bool> SaveFarmAsync(FarmModel farm)
        {
            bool success = false;
            try
            {
                var speciesPayload = farm.GameInventory.Select(sp => new
                {
                    SpeciesName = sp.SpeciesName,
                    PriceMale = sp.PriceMale,
                    PriceFemale = sp.PriceFemale,
                    PriceTrophy = sp.PriceTrophy,
                    AvailableCount = sp.AvailableCount
                }).ToList();

                if (farm.Id == 0)
                {
                    var createReq = new
                    {
                        FarmFarmerId = farm.FarmerUserId, // the API always uses the signed-in user (Admins may pass another userId)
                        Name = farm.Name,
                        Description = farm.Description,
                        Size = farm.SizeHectares,
                        DailyRateHunter = farm.DailyRateHunter,
                        Amenities = farm.Amenities,
                        Location = $"{farm.NearestTown}, {farm.Province}",
                        Address = new
                        {
                            AddressCity = farm.NearestTown,
                            AddressRegion = farm.Province,
                            AddressCountry = "South Africa"
                        },
                        Species = speciesPayload,
                        ImageUrls = FarmPhotosForSave(farm)
                    };

                    var res = await _http.PostAsJsonAsync("api/farmer/create", createReq);
                    if (res.IsSuccessStatusCode)
                    {
                        var resBody = await res.Content.ReadFromJsonAsync<CreateFarmApiResponse>();
                        if (resBody != null && resBody.FarmId > 0)
                        {
                            farm.Id = resBody.FarmId;
                            success = true;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[SAVE FARM API ERROR] Create failed: HTTP {(int)res.StatusCode}");
                    }
                }
                else
                {
                    var updateReq = new
                    {
                        Name = farm.Name,
                        Description = farm.Description,
                        Size = farm.SizeHectares,
                        Status = farm.Status,
                        ImageUrls = FarmPhotosForSave(farm)
                    };
                    var res = await _http.PostAsJsonAsync($"api/farmer/update/{farm.Id}", updateReq);
                    success = res.IsSuccessStatusCode;
                    if (!success)
                    {
                        Console.WriteLine($"[SAVE FARM API ERROR] Update failed: HTTP {(int)res.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SAVE FARM API ERROR] {ex.Message}");
            }

            if (!success) return false;

            if (farm.Id == 0)
            {
                farm.Id = _farms.Count > 0 ? _farms.Max(f => f.Id) + 1 : 1;
                _farms.Add(farm);
            }
            else
            {
                var index = _farms.FindIndex(f => f.Id == farm.Id);
                if (index >= 0) _farms[index] = farm;
                else _farms.Add(farm);
            }
            return true;
        }

        /// <summary>DELETE api/farmer/delete/{farmId}. Removes from the local cache unconditionally even if the API call throws - matches the same "always reflect intent locally" pattern used elsewhere in this file, though unlike SaveFarmAsync this doesn't report success/failure back to the caller.</summary>
        public async Task DeleteFarmAsync(int farmId)
        {
            try
            {
                await _http.DeleteAsync($"api/farmer/delete/{farmId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DELETE FARM API ERROR] {ex.Message}");
            }

            _farms.RemoveAll(f => f.Id == farmId);
        }

        /// <summary>
        /// The farm's photos for api/farmer/create|update: new ones as data URLs, kept ones as the
        /// plaasfoto/... paths the API handed out (the API swaps those back for the stored photo).
        /// The shared placeholder is not a photo of the farm, so it is never sent.
        /// </summary>
        private static List<string> FarmPhotosForSave(FarmModel farm) =>
            (farm.ImageUrls ?? new List<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.Contains("savanna_sunrise", StringComparison.OrdinalIgnoreCase))
                .ToList();

        /// <summary>Shape of api/farmer/create's response - just the new farm's id.</summary>
        private class CreateFarmApiResponse
        {
            [JsonPropertyName("farmId")]
            public int FarmId { get; set; }
        }

        // --- Packages ---
        public event Action? OnPackagesChanged;
        /// <summary>Lets a component (e.g. a dashboard) signal that packages changed elsewhere so any subscribed page can refresh; swallows subscriber exceptions so one broken handler can't block the others.</summary>
        public void NotifyPackagesChanged()
        {
            try { OnPackagesChanged?.Invoke(); } catch { }
        }

        public event Action? OnNotificationsChanged;
        /// <summary>Same pattern as NotifyPackagesChanged, for the notification bell.</summary>
        public void NotifyNotificationsChanged()
        {
            try { OnNotificationsChanged?.Invoke(); } catch { }
        }

        /// <summary>
        /// Turns the WebAPI's PackageResponse into the page model, filling farm details from
        /// the loaded _farms cache. IncludedItems is one delimited string on the wire: species
        /// names comma-separated before the first "|", then optional "|OBS_PRICE:&lt;amount&gt;"
        /// segments; with no IncludedItems the farm's own species list is used.
        /// </summary>
        private HuntingPackageModel ToPackageModel(PackageResponse p)
        {
            var farm = _farms.FirstOrDefault(f => f.Id == (p.FarmId ?? 0));
            var model = new HuntingPackageModel
            {
                Id = p.Id,
                Title = p.Title,
                Description = p.Description,
                PricePerHunter = p.StockPrice,
                DurationDays = p.DurationDays > 0 ? p.DurationDays : 4,
                AvailableSlots = p.MaxParticipants,
                FarmId = p.FarmId ?? 0,
                FarmName = !string.IsNullOrWhiteSpace(p.FarmName) ? p.FarmName : (farm?.Name ?? ""),
                Province = !string.IsNullOrWhiteSpace(p.Province) ? p.Province : (farm?.Province ?? "Limpopo"),
                Location = !string.IsNullOrWhiteSpace(p.Location) ? p.Location : (farm != null ? $"{farm.NearestTown}, {farm.Province}" : "Limpopo"),
                OrganiserName = !string.IsNullOrWhiteSpace(p.OrganiserName) ? p.OrganiserName : "Uitruster (PJ)",
                OrganiserUserId = p.OrganiserUserId,
                OrganiserDocumentsApproved = p.OrganiserDocumentsApproved,
                FarmerDocumentsApproved = p.FarmerDocumentsApproved,
                IsPublished = p.Status != "Inactive",
                // The farm's first photo (the farm cache carries plaasfoto/... paths); the shared placeholder otherwise.
                ImageUrl = farm?.ImageUrls.FirstOrDefault() ?? "images/savanna_sunrise.jpg",
                IncludedServices = SplitItemLines(p.Inclusions),
                ExcludedServices = SplitItemLines(p.Exclusions)
            };
            if (!string.IsNullOrWhiteSpace(p.IncludedItems))
            {
                var parts = p.IncludedItems.Split('|', StringSplitOptions.TrimEntries);
                model.AvailableFarmSpecies = parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                foreach (var part in parts.Skip(1))
                {
                    if (part.StartsWith("OBS_PRICE:", StringComparison.OrdinalIgnoreCase) &&
                        decimal.TryParse(part.Substring("OBS_PRICE:".Length), out var obsPrice))
                    {
                        model.PricePerObserver = obsPrice;
                    }
                }
            }
            else if (farm != null && farm.AvailableSpecies.Count > 0)
            {
                model.AvailableFarmSpecies = farm.AvailableSpecies.ToList();
            }
            return model;
        }

        /// <summary>"Ingesluit" / "Nie ingesluit nie" travel as one item per line.</summary>
        private static List<string> SplitItemLines(string? text) =>
            string.IsNullOrWhiteSpace(text)
                ? new List<string>()
                : text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static string JoinItemLines(IEnumerable<string>? items) =>
            string.Join("\n", (items ?? Enumerable.Empty<string>()).Select(i => i.Trim()).Where(i => i.Length > 0));

        /// <summary>Puts a package from the server into the local _packages cache (replace by id, or add at the front).</summary>
        private void CachePackage(HuntingPackageModel model)
        {
            var idx = _packages.FindIndex(x => x.Id == model.Id);
            if (idx >= 0) _packages[idx] = model;
            else _packages.Insert(0, model);
        }

        /// <summary>
        /// GET api/packets/getPacketsPaginated, mapped with ToPackageModel. Returns an empty
        /// page (not throwing) on any failure.
        /// </summary>
        public async Task<PaginatedPackageModel> GetPackagesPaginatedAsync(int page = 1, int pageSize = 10)
        {
            try
            {
                var apiRes = await _http.GetFromJsonAsync<PaginatedPackageResponse>($"api/packets/getPacketsPaginated?page={page}&pageSize={pageSize}");
                if (apiRes != null)
                {
                    return new PaginatedPackageModel
                    {
                        Data = apiRes.Data.Select(ToPackageModel).ToList(),
                        TotalCount = apiRes.TotalCount,
                        Page = apiRes.Page,
                        PageSize = apiRes.PageSize
                    };
                }
            }
            catch (Exception ex) { Console.WriteLine("Error fetching paginated packages: " + ex.Message); }
            return new PaginatedPackageModel { Data = new List<HuntingPackageModel>() };
        }

        /// <summary>
        /// GET api/packets/getPackets (unpaginated - use GetPackagesPaginatedAsync for large
        /// lists), synced into the local _packages cache before the full cache is returned.
        /// </summary>
        public async Task<List<HuntingPackageModel>> GetPackagesAsync()
        {
            try
            {
                var apiRes = await _http.GetFromJsonAsync<List<PackageResponse>>("api/packets/getPackets");
                if (apiRes != null)
                {
                    _packages.Clear();
                    _packages.AddRange(apiRes.Select(ToPackageModel));
                }
            }
            catch (Exception ex) { Console.WriteLine("Error fetching packages: " + ex.Message); }

            return _packages.ToList();
        }

        /// <summary>
        /// GET api/packets/mine: the signed-in PJ's own packages, including the ones the marketplace
        /// still hides until a required document is approved (see HuntingPackageModel.IsMarketplaceVisible).
        /// </summary>
        public async Task<List<HuntingPackageModel>> GetMyPackagesAsync()
        {
            try
            {
                var apiRes = await _http.GetFromJsonAsync<List<PackageResponse>>("api/packets/mine");
                if (apiRes != null) return apiRes.Select(ToPackageModel).ToList();
            }
            catch (Exception ex) { Console.WriteLine("Error fetching own packages: " + ex.Message); }
            return new List<HuntingPackageModel>();
        }

        /// <summary>GET api/packets/getPacket/{packageId}. Falls back to the local _packages cache if the API call fails or returns nothing.</summary>
        public async Task<HuntingPackageModel?> GetPackageByIdAsync(int packageId)
        {
            var mem = _packages.FirstOrDefault(p => p.Id == packageId);
            try
            {
                var p = await _http.GetFromJsonAsync<PackageResponse>($"api/packets/getPacket/{packageId}");
                if (p != null)
                {
                    var model = ToPackageModel(p);
                    CachePackage(model);
                    return model;
                }
            }
            catch (Exception ex) { Console.WriteLine($"Error fetching package {packageId}: " + ex.Message); }
            return mem;
        }

        /// <summary>
        /// Creates/updates a hunting package. IncludedItems is round-tripped through a
        /// single delimited string ("species, list | OBS_PRICE:&lt;amount&gt;") - see the
        /// matching decode logic in ToPackageModel.
        ///
        /// Returns whether the server accepted it. The local cache only changes after a
        /// successful save, so a rejected create/update (e.g. 403 for a non-PJ) can never
        /// look saved.
        /// </summary>
        public async Task<bool> SavePackageAsync(HuntingPackageModel pkg)
        {
            // Auto-enrich farm details if available
            if (pkg.FarmId > 0)
            {
                var farm = _farms.FirstOrDefault(f => f.Id == pkg.FarmId);
                if (farm != null)
                {
                    if (string.IsNullOrWhiteSpace(pkg.FarmName)) pkg.FarmName = farm.Name;
                    if (string.IsNullOrWhiteSpace(pkg.Province)) pkg.Province = farm.Province;
                    if (string.IsNullOrWhiteSpace(pkg.Location)) pkg.Location = !string.IsNullOrWhiteSpace(farm.NearestTown) ? $"{farm.NearestTown}, {farm.Province}" : farm.Province;
                    if (pkg.AvailableFarmSpecies == null || pkg.AvailableFarmSpecies.Count == 0)
                    {
                        pkg.AvailableFarmSpecies = farm.AvailableSpecies.ToList();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(pkg.ImageUrl))
            {
                pkg.ImageUrl = "images/savanna_sunrise.jpg";
            }

            bool isNew = pkg.Id == 0;
            try
            {
                var speciesStr = string.Join(", ", pkg.AvailableFarmSpecies ?? new List<string>());
                var includedPayload = $"{speciesStr} | OBS_PRICE:{pkg.PricePerObserver}";

                if (isNew)
                {
                    var req = new CreatePackageRequest
                    {
                        PTitle = pkg.Title,
                        PDescription = pkg.Description,
                        PStockPrice = pkg.PricePerHunter,
                        PDurationDays = pkg.DurationDays,
                        PMaxParticipants = pkg.AvailableSlots,
                        PIncludedItems = includedPayload,
                        PInclusions = JoinItemLines(pkg.IncludedServices),
                        PExclusions = JoinItemLines(pkg.ExcludedServices),
                        PStatus = pkg.IsPublished ? "Active" : "Inactive",
                        PFarmId = pkg.FarmId > 0 ? pkg.FarmId : null,
                        PPjId = pkg.OrganiserUserId > 0 ? pkg.OrganiserUserId : null,
                        PAvailableFrom = DateTime.Today,
                        PAvailableTill = DateTime.Today.AddMonths(12)
                    };
                    var res = await _http.PostAsJsonAsync("api/packets/createPacket", req);
                    if (!res.IsSuccessStatusCode) return false;
                    var data = await res.Content.ReadFromJsonAsync<CreatePackageResponse>();
                    if (data == null || data.Id <= 0) return false;
                    pkg.Id = data.Id;
                    _packages.Insert(0, pkg);
                }
                else
                {
                    var req = new UpdatePackageRequest
                    {
                        PTitle = pkg.Title,
                        PDescription = pkg.Description,
                        PStockPrice = pkg.PricePerHunter,
                        PDurationDays = pkg.DurationDays,
                        PMaxParticipants = pkg.AvailableSlots,
                        PIncludedItems = includedPayload,
                        PInclusions = JoinItemLines(pkg.IncludedServices),
                        PExclusions = JoinItemLines(pkg.ExcludedServices),
                        PStatus = pkg.IsPublished ? "Active" : "Inactive",
                        PFarmId = pkg.FarmId > 0 ? pkg.FarmId : null
                    };
                    var res = await _http.PostAsJsonAsync($"api/packets/updatePacket/{pkg.Id}", req);
                    if (!res.IsSuccessStatusCode) return false;
                    var idx = _packages.FindIndex(p => p.Id == pkg.Id);
                    if (idx >= 0) _packages[idx] = pkg;
                    else _packages.Insert(0, pkg);
                }
                NotifyPackagesChanged();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// DELETE api/packets/deletePacket/{packageId}. Success = the server accepted it.
        /// Deactivated = the package has bookings, so the server only deactivated it (it
        /// stays listed as inactive) instead of deleting it. The local cache only changes
        /// after the server accepted.
        /// </summary>
        public async Task<(bool Success, bool Deactivated)> DeletePackageAsync(int packageId)
        {
            bool deactivated = false;
            try
            {
                var res = await _http.DeleteAsync($"api/packets/deletePacket/{packageId}");
                if (!res.IsSuccessStatusCode) return (false, false);
                try
                {
                    var body = await res.Content.ReadFromJsonAsync<JsonElement>();
                    deactivated = body.ValueKind == JsonValueKind.Object
                        && body.TryGetProperty("deactivated", out var d) && d.ValueKind == JsonValueKind.True;
                }
                catch { } // older API without a body: treat as deleted
            }
            catch
            {
                return (false, false);
            }
            if (deactivated)
            {
                var pkg = _packages.FirstOrDefault(p => p.Id == packageId);
                if (pkg != null) pkg.IsPublished = false;
            }
            else
            {
                _packages.RemoveAll(p => p.Id == packageId);
            }
            NotifyPackagesChanged();
            return (true, deactivated);
        }

        // --- Bookings ---

        /// <summary>
        /// Turns the WebAPI's BookingResponse into the page model. No invented price: a booking
        /// with no real total keeps 0, and the UI shows a "contact admin" prompt for it.
        /// </summary>
        private static BookingModel ToBookingModel(BookingResponse b) => new()
        {
            Id = b.Id,
            HunterUserId = b.UserId,
            HunterName = b.UserName,
            HunterEmail = b.HunterEmail,
            HunterPhone = b.HunterPhone,
            PackageId = b.PakketId,
            PackageTitle = b.PakketName,
            FarmId = b.FarmId,
            FarmName = b.FarmName,
            StartDate = b.StartDate,
            EndDate = b.EndDate,
            HunterCount = b.HuntGuests > 0 ? b.HuntGuests : 1,
            ObserverCount = b.NonHuntGuests,
            TotalPrice = b.TotalAmount,
            Status = b.Status,
            AssignedPhUserId = b.AssignedPhUserId,
            AssignedPhName = b.AssignedPhName,
            AssignedVgUserId = b.AssignedVgUserId,
            AssignedVgName = b.AssignedVgName,
            PaymentReference = !string.IsNullOrWhiteSpace(b.PaymentReference) ? b.PaymentReference : $"MP-EXP-{b.Id:D6}",
            RejectionReason = b.RejectionReason,
            RejectionNotes = b.RejectionNotes,
            FirearmOption = b.FirearmOption ?? "Importing",
            SpecialNotes = b.SpecialNotes ?? "",
            ConfirmedAccommodationPrice = b.ConfirmedAccommodationPrice,
            PriceBreakdownJson = b.PriceBreakdownJson,
            WantsVideographer = b.WantsVideographer,
            BookedSpecies = b.BookedSpecies ?? new(),
            PjUserId = b.PjUserId ?? 0,
            PjName = b.PjName ?? "",
            PhAssignmentStatus = b.PhAssignmentStatus,
            VgAssignmentStatus = b.VgAssignmentStatus,
            PhDeclineReason = b.PhDeclineReason,
            VgDeclineReason = b.VgDeclineReason
        };

        /// <summary>GETs a booking list and maps it; an empty list (never someone else's cached data) on failure.</summary>
        private async Task<List<BookingModel>> GetBookingListAsync(string url, string label)
        {
            try
            {
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[{label} API ERROR] HTTP {(int)response.StatusCode} {response.StatusCode}");
                    return new();
                }
                var apiRes = await response.Content.ReadFromJsonAsync<List<BookingResponse>>(_adminJsonOptions) ?? new();
                return apiRes.Select(ToBookingModel).OrderByDescending(b => b.Id).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{label} API ERROR] {ex.Message}");
                return new();
            }
        }

        /// <summary>Admin-only: GET api/admin/bookings - every booking on the platform.</summary>
        public Task<List<BookingModel>> GetBookingsAsync()
            => GetBookingListAsync("api/admin/bookings", "ADMIN BOOKINGS");

        /// <summary>GET api/bookings/getBookings?id={pjUserId} - bookings on a PJ's own packages (that PJ or an Admin).</summary>
        public Task<List<BookingModel>> GetBookingsForPjAsync(int pjUserId)
            => GetBookingListAsync($"api/bookings/getBookings?id={pjUserId}", "PJ BOOKINGS");

        /// <summary>
        /// GET api/bookings/getFarmBookings?id={farmerUserId} - bookings on the farmer's own
        /// farms (only that farmer or an Admin). Replaces the Boer pages' use of the
        /// Admin-only api/admin/bookings, which a farmer always got 403 from.
        /// </summary>
        public Task<List<BookingModel>> GetFarmBookingsAsync(int farmerUserId)
            => GetBookingListAsync($"api/bookings/getFarmBookings?id={farmerUserId}", "FARM BOOKINGS");

        /// <summary>GET api/bookings/getHunterBookings?id={hunterUserId} - the hunter's own bookings.</summary>
        public Task<List<BookingModel>> GetHunterBookingsAsync(int hunterUserId)
            => hunterUserId > 0
                ? GetBookingListAsync($"api/bookings/getHunterBookings?id={hunterUserId}", "HUNTER BOOKINGS")
                : Task.FromResult(new List<BookingModel>());

        // ── Hunters on a booking (main hunter + added hunters) ─────────────────────

        /// <summary>GET api/bookings/{id}/hunters - the booking's hunters as the signed-in user may see them; null on failure.</summary>
        public async Task<BookingHuntersModel?> GetBookingHuntersAsync(int bookingId)
        {
            try
            {
                return await _http.GetFromJsonAsync<BookingHuntersModel>($"api/bookings/{bookingId}/hunters");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>POST api/bookings/{id}/hunters - the main hunter adds a hunter by name + email; returns the server's (Afrikaans) message.</summary>
        public async Task<(bool Success, string Message)> AddBookingHunterAsync(int bookingId, string name, string email)
        {
            try
            {
                var res = await _http.PostAsJsonAsync($"api/bookings/{bookingId}/hunters", new { name, email });
                string fallback = res.IsSuccessStatusCode ? "Jagter bygevoeg." : res.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "Slegs die hoofjagter kan jagters byvoeg."
                    : "Kon nie die jagter byvoeg nie.";
                return (res.IsSuccessStatusCode, await ReadErrorMessageAsync(res, fallback));
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>DELETE api/bookings/{id}/hunters/{companionId} - remove an added hunter (main hunter) or leave the trip (the hunter themselves).</summary>
        public async Task<(bool Success, string Message)> RemoveBookingHunterAsync(int bookingId, int companionId)
        {
            try
            {
                var res = await _http.DeleteAsync($"api/bookings/{bookingId}/hunters/{companionId}");
                return (res.IsSuccessStatusCode, await ReadErrorMessageAsync(res, res.IsSuccessStatusCode ? "Jagter verwyder." : "Kon nie die jagter verwyder nie."));
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>GET api/bookings/companionBookings - trips the signed-in hunter joins as an added hunter (no money fields).</summary>
        public async Task<List<CompanionTripModel>> GetCompanionTripsAsync()
        {
            try
            {
                return await _http.GetFromJsonAsync<List<CompanionTripModel>>("api/bookings/companionBookings") ?? new();
            }
            catch
            {
                return new();
            }
        }

        // ── VG photos/clips for the hunt story (relayed by WebAPI to MobileAPI) ──────

        /// <summary>GET api/vg/bookings/{id}/media - the booking's shared photos and clips; Error is set (and Items empty) on failure.</summary>
        public async Task<(List<BookingMediaItemModel> Items, string? Error)> GetVgBookingMediaAsync(int bookingId)
        {
            try
            {
                var res = await _http.GetAsync($"api/vg/bookings/{bookingId}/media");
                if (res.IsSuccessStatusCode)
                    return (await res.Content.ReadFromJsonAsync<List<BookingMediaItemModel>>(_adminJsonOptions) ?? new(), null);
                return (new(), await ReadErrorMessageAsync(res, res.StatusCode == System.Net.HttpStatusCode.Conflict
                    ? "Media kan eers bygevoeg word nadat die ekspedisie verby is."
                    : "Kon nie die bespreking se media laai nie."));
            }
            catch (Exception ex)
            {
                return (new(), $"Fout: {ex.Message}");
            }
        }

        /// <summary>
        /// POST api/vg/bookings/{id}/media/photos or /videos - uploads one file the page has
        /// already buffered to a server-side temp file (so a slow browser upload never
        /// counts against this HttpClient's timeout).
        /// </summary>
        public async Task<(bool Success, string Message)> UploadVgBookingMediaAsync(int bookingId, bool isVideo, string localPath,
            string fileName, string contentType, string? caption, DateTime? capturedAt)
        {
            try
            {
                await using var file = File.OpenRead(localPath);
                var fileContent = new StreamContent(file);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var type)
                    ? type
                    : new MediaTypeHeaderValue("application/octet-stream");
                using var form = new MultipartFormDataContent { { fileContent, "file", fileName } };
                if (!string.IsNullOrWhiteSpace(caption)) form.Add(new StringContent(caption.Trim()), "caption");
                if (capturedAt is { } at)
                    form.Add(new StringContent(at.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture)), "capturedAt");
                var res = await _http.PostAsync($"api/vg/bookings/{bookingId}/media/{(isVideo ? "videos" : "photos")}", form);
                return (res.IsSuccessStatusCode, await ReadErrorMessageAsync(res, res.IsSuccessStatusCode
                    ? (isVideo ? "Videogreep opgelaai." : "Foto opgelaai.")
                    : res.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge ? "Die lêer is te groot." : "Kon nie die lêer oplaai nie."));
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>PUT api/vg/bookings/{id}/media/{mediaId} - the uploader edits the caption and capture time (which orders it in the story).</summary>
        public async Task<(bool Success, string Message)> UpdateVgMediaDetailsAsync(int bookingId, long mediaId, string? caption, DateTime? capturedAt)
        {
            try
            {
                var res = await _http.PutAsJsonAsync($"api/vg/bookings/{bookingId}/media/{mediaId}",
                    new { caption, capturedAt = capturedAt?.ToUniversalTime() });
                return (res.IsSuccessStatusCode, await ReadErrorMessageAsync(res, res.IsSuccessStatusCode ? "Besonderhede gestoor." : "Kon nie die besonderhede stoor nie."));
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>DELETE api/vg/bookings/{id}/media/{mediaId} - the uploader removes their own photo/clip.</summary>
        public async Task<(bool Success, string Message)> DeleteVgMediaAsync(int bookingId, long mediaId)
        {
            try
            {
                var res = await _http.DeleteAsync($"api/vg/bookings/{bookingId}/media/{mediaId}");
                return (res.IsSuccessStatusCode, await ReadErrorMessageAsync(res, res.IsSuccessStatusCode ? "Verwyder." : "Kon nie die lêer verwyder nie."));
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>GET api/vg/bookings/{id}/media/{mediaId}/content - the private file's bytes for an in-page preview; null on failure.</summary>
        public async Task<(Stream Content, string ContentType)?> OpenVgMediaContentAsync(int bookingId, long mediaId)
        {
            try
            {
                var res = await _http.GetAsync($"api/vg/bookings/{bookingId}/media/{mediaId}/content", HttpCompletionOption.ResponseHeadersRead);
                if (!res.IsSuccessStatusCode) { res.Dispose(); return null; }
                return (await res.Content.ReadAsStreamAsync(), res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>GET api/vg/bookings/{id}/recaps - the booking's generated stories (read-only for a VG).</summary>
        public async Task<List<BookingRecapSummaryModel>> GetVgBookingRecapsAsync(int bookingId)
        {
            try
            {
                var res = await _http.GetAsync($"api/vg/bookings/{bookingId}/recaps");
                return res.IsSuccessStatusCode
                    ? await res.Content.ReadFromJsonAsync<List<BookingRecapSummaryModel>>(_adminJsonOptions) ?? new()
                    : new();
            }
            catch
            {
                return new();
            }
        }

        // ── Post-trip team ratings (Hunter <-> PH <-> VG) ─────────────────────────

        /// <summary>GET api/rating/booking/{id}/team - who the signed-in participant can rate, with their current ratings; Error set on failure.</summary>
        public async Task<(BookingTeamRatingModel? Team, string? Error)> GetBookingTeamRatingAsync(int bookingId)
        {
            try
            {
                var res = await _http.GetAsync($"api/rating/booking/{bookingId}/team");
                if (res.IsSuccessStatusCode)
                    return (await res.Content.ReadFromJsonAsync<BookingTeamRatingModel>(_adminJsonOptions), null);
                return (null, await ReadErrorMessageAsync(res, "Kon nie die span vir gradering laai nie."));
            }
            catch (Exception ex)
            {
                return (null, $"Fout: {ex.Message}");
            }
        }

        /// <summary>POST api/rating/setRoleRating - the signed-in participant rates (or re-rates) one teammate on a finished booking.</summary>
        public async Task<(bool Success, string Message)> SubmitTeamRatingAsync(int bookingId, int raterUserId, int rateeUserId,
            string rateeRoleCode, int stars, string? comment)
        {
            try
            {
                var res = await _http.PostAsJsonAsync("api/rating/setRoleRating", new
                {
                    BookingId = bookingId,
                    RaterUserId = raterUserId,
                    RateeUserId = rateeUserId,
                    RateeRoleCode = rateeRoleCode,
                    Stars = stars,
                    Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()
                });
                return (res.IsSuccessStatusCode, await ReadErrorMessageAsync(res, res.IsSuccessStatusCode
                    ? "Dankie! U gradering is gestoor."
                    : res.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? "Te veel graderings kort na mekaar - probeer oor 'n minuut weer." : "Kon nie die gradering stoor nie."));
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>
        /// PJ/admin assigns PH (and optionally VG) staff to a booking and locks in final
        /// pricing (POST api/bookings/approveWithStaff/{id}, [Authorize]). This no longer
        /// approves the booking outright - it moves to "PendingStaffAcceptance" until the
        /// assigned staff accept via RespondToStaffAssignmentAsync. On API failure, still
        /// applies the same state change to the local cache so the UI doesn't appear
        /// stuck (the caller typically re-fetches from the server right after anyway).
        /// </summary>
        public async Task<bool> ApproveBookingWithStaffAsync(int bookingId, int phUserId, int? vgUserId, decimal? confirmedTotalPrice = null, decimal? accommodationPrice = null, string? priceBreakdownJson = null)
        {
            try
            {
                var req = new ApproveWithStaffRequest
                {
                    PhUserId = phUserId,
                    VgUserId = vgUserId,
                    ConfirmedTotalPrice = confirmedTotalPrice,
                    ConfirmedAccommodationPrice = accommodationPrice,
                    PriceBreakdownJson = priceBreakdownJson
                };
                var httpReq = new HttpRequestMessage(HttpMethod.Post, $"api/bookings/approveWithStaff/{bookingId}") { Content = JsonContent.Create(req) };
                AttachAuthHeader(httpReq);
                var res = await _http.SendAsync(httpReq);
                if (res.IsSuccessStatusCode)
                {
                    var b = _bookings.FirstOrDefault(x => x.Id == bookingId);
                    if (b != null)
                    {
                        // Assigning staff no longer approves the booking outright - it now
                        // needs PH/VG acceptance first (falls back to whatever LoadData()
                        // re-fetches from the server right after this call anyway).
                        if (b.Status != "Approved" && b.Status != "Paid" && b.Status != "Completed")
                            b.Status = "PendingStaffAcceptance";
                        b.AssignedPhUserId = phUserId;
                        b.AssignedVgUserId = vgUserId;
                        if (confirmedTotalPrice.HasValue) b.TotalPrice = confirmedTotalPrice.Value;
                        b.ConfirmedAccommodationPrice = accommodationPrice;
                        b.PriceBreakdownJson = priceBreakdownJson;
                    }
                    return true;
                }
            }
            catch { }

            var local = _bookings.FirstOrDefault(x => x.Id == bookingId);
            if (local != null)
            {
                if (local.Status != "Approved" && local.Status != "Paid" && local.Status != "Completed")
                    local.Status = "PendingStaffAcceptance";
                local.AssignedPhUserId = phUserId;
                local.AssignedVgUserId = vgUserId;
                if (confirmedTotalPrice.HasValue) local.TotalPrice = confirmedTotalPrice.Value;
                local.ConfirmedAccommodationPrice = accommodationPrice;
                local.PriceBreakdownJson = priceBreakdownJson;
                return true;
            }
            return false;
        }

        /// <summary>
        /// PH/VG accepts or declines the staff assignment the PJ gave them.
        /// api/bookings/staffRespond/{id} is [Authorize]-gated server-side
        /// (BookingsController.StaffRespond also verifies the caller is the pending
        /// assignee for that role); the bearer token is attached by AuthTokenHandler.
        /// </summary>
        public async Task<(bool Success, bool BookingApproved, string Message)> RespondToStaffAssignmentAsync(int bookingId, string role, bool accept, string? declineReason = null)
        {
            try
            {
                var req = new RespondStaffAssignmentRequest { Role = role, Accept = accept, DeclineReason = declineReason };
                var res = await _http.PostAsJsonAsync($"api/bookings/staffRespond/{bookingId}", req);
                if (res.IsSuccessStatusCode)
                {
                    bool approved = false;
                    try
                    {
                        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
                        if (body.TryGetProperty("bookingApproved", out var el)) approved = el.GetBoolean();
                    }
                    catch { }
                    return (true, approved, accept ? "Toewysing aanvaar." : "Toewysing van hand gewys.");
                }
                var msg = await ReadErrorMessageAsync(res, "Kon nie op die toewysing reageer nie.");
                return (false, false, msg);
            }
            catch (Exception ex)
            {
                return (false, false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>
        /// Rejects a booking (POST api/bookings/rejectBooking/{id}) - the booking's PJ, its farmer
        /// or an Admin. True only when the server accepted it; the cache changes only then.
        /// </summary>
        public async Task<bool> RejectBookingAsync(int bookingId, string? reason = null, string? notes = null)
        {
            try
            {
                var req = new RejectBookingRequest
                {
                    ReasonCode = reason,
                    ReasonDescription = reason,
                    Notes = notes
                };
                var res = await _http.PostAsJsonAsync($"api/bookings/rejectBooking/{bookingId}", req);
                if (res.IsSuccessStatusCode)
                {
                    var b = _bookings.FirstOrDefault(x => x.Id == bookingId);
                    if (b != null)
                    {
                        b.Status = "Rejected";
                        b.RejectionReason = reason;
                        b.RejectionNotes = notes;
                    }
                    return true;
                }
                Console.WriteLine($"[REJECT BOOKING FAILED {(int)res.StatusCode}] booking {bookingId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[REJECT BOOKING ERROR] {ex.Message}");
            }
            return false;
        }

        /// <summary>GET api/bookings/getAuditLogs/{bookingId}. Returns an empty list (not null/throwing) on any failure.</summary>
        public async Task<List<BookingAuditLog>> GetBookingAuditLogsAsync(int bookingId)
        {
            try
            {
                var logs = await _http.GetFromJsonAsync<List<BookingAuditLog>>($"api/bookings/getAuditLogs/{bookingId}");
                if (logs != null) return logs;
            }
            catch { }
            return new List<BookingAuditLog>();
        }
        /// <summary>GET api/bookings/getPhBookings?id={phUserId}. Local fallback treats userId 1 specially (also matches a null AssignedPhUserId) - a leftover from the demo-data seeding where PH staff assignment wasn't always populated.</summary>
        public async Task<List<BookingModel>> GetPhBookingsAsync(int phUserId)
        {
            try
            {
                var apiRes = await _http.GetFromJsonAsync<List<BookingResponse>>($"api/bookings/getPhBookings?id={phUserId}");
                if (apiRes != null && apiRes.Count > 0)
                {
                    var list = new List<BookingModel>();
                    foreach (var b in apiRes)
                    {
                        list.Add(new BookingModel
                        {
                            Id = b.Id,
                            PackageId = b.PakketId,
                            PackageTitle = b.PakketName,
                            FarmName = b.FarmName,
                            HunterName = b.UserName,
                            StartDate = b.StartDate,
                            EndDate = b.EndDate,
                            HunterCount = b.HuntGuests > 0 ? b.HuntGuests : 1,
                            ObserverCount = b.NonHuntGuests,
                            // No invented fallback: a booking with no real price is a data
                            // problem, not something to paper over with a made-up number - the
                            // UI shows a "contact admin" prompt when this is <= 0.
                            TotalPrice = b.TotalAmount,
                            Status = b.Status,
                            PaymentReference = !string.IsNullOrWhiteSpace(b.PaymentReference) ? b.PaymentReference : $"MP-EXP-{b.Id:D6}",
                            AssignedPhUserId = phUserId,
                            PhAssignmentStatus = b.PhAssignmentStatus,
                            PhDeclineReason = b.PhDeclineReason,
                            PjUserId = b.PjUserId ?? 0,
                            PjName = b.PjName ?? ""
                        });
                    }
                    return list;
                }
            }
            catch { }

            return new List<BookingModel>();
        }
        
        /// <summary>
        /// Sends a booking request for a package (POST api/bookings/create, [Authorize]).
        /// Returns the new booking's id on success, or the server's (Afrikaans) reason on
        /// failure - it never invents a local booking, so a failed request can't look like
        /// a successful one.
        /// </summary>
        public async Task<(bool Success, int BookingId, string Message)> RequestPackageAsync(BookingModel booking)
        {
            try
            {
                var request = new CreateBookingRequest
                {
                    PakketId = booking.PackageId ?? 0,
                    HunterId = booking.HunterUserId,
                    StartDate = booking.StartDate,
                    EndDate = booking.EndDate,
                    HuntGuests = booking.HunterCount,
                    NonHuntGuests = booking.ObserverCount,
                    FirearmOption = booking.FirearmOption,
                    SpecialNotes = booking.SpecialNotes,
                    WantsVideographer = booking.WantsVideographer,
                    BookedSpecies = booking.BookedSpecies
                };
                var httpReq = new HttpRequestMessage(HttpMethod.Post, "api/bookings/create") { Content = JsonContent.Create(request) };
                AttachAuthHeader(httpReq);
                var res = await _http.SendAsync(httpReq);
                if (!res.IsSuccessStatusCode)
                    return (false, 0, await ReadErrorMessageAsync(res, res.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "Teken asseblief in om te bespreek."
                        : "Die besprekingsversoek kon nie gestuur word nie."));

                var result = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                int newId = result.TryGetProperty("id", out var idEl) ? idEl.GetInt32()
                          : result.TryGetProperty("Id", out var idEl2) ? idEl2.GetInt32() : 0;
                return newId > 0
                    ? (true, newId, "Besprekingsversoek gestuur.")
                    : (false, 0, "Die bediener het nie 'n besprekingsnommer teruggestuur nie.");
            }
            catch (Exception ex)
            {
                return (false, 0, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets a booking's status directly (POST api/bookings/approveBooking/{id}) - the farmer's
        /// approve/reject, or the hunter cancelling. The server decides who may set what (a hunter
        /// may only cancel, and only before paying); the local cache changes only on success.
        /// </summary>
        public async Task<(bool Success, string? Error)> UpdateBookingStatusAsync(int bookingId, string status)
        {
            try
            {
                var res = await _http.PostAsJsonAsync($"api/bookings/approveBooking/{bookingId}", status);
                if (res.IsSuccessStatusCode)
                {
                    var b = _bookings.FirstOrDefault(x => x.Id == bookingId);
                    if (b != null) b.Status = status;
                    return (true, null);
                }
                var (message, _) = await ReadPaymentErrorAsync(res, "Die bespreking kon nie opgedateer word nie.");
                return (false, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BOOKING STATUS ERROR] {ex.Message}");
                return (false, "Kon nie met die bediener kommunikeer nie.");
            }
        }

        // --- Payments (api/payments) ---
        // The server prices every payment and only credits it from a verified PayFast
        // notification (or, in Development, its simulator). Nothing here sends an amount.

        /// <summary>Error body shape of api/payments/*.</summary>
        private class PaymentErrorResponse
        {
            public string? Message { get; set; }
            public bool AlreadyPaid { get; set; }
        }

        private static object PaymentItem(string kind, int id, string? roleCode, int months, string? returnUrl = null, string? cancelUrl = null)
            => new { kind, id, roleCode, months, returnUrl, cancelUrl };

        private static async Task<(string Message, bool AlreadyPaid)> ReadPaymentErrorAsync(HttpResponseMessage res, string fallback)
        {
            try
            {
                var body = await res.Content.ReadFromJsonAsync<PaymentErrorResponse>(_adminJsonOptions);
                return (string.IsNullOrWhiteSpace(body?.Message) ? fallback : body!.Message!, body?.AlreadyPaid ?? false);
            }
            catch
            {
                return (fallback, false);
            }
        }

        /// <summary>
        /// POST api/payments/checkout: the PayFast form for a booking deposit ("booking", id =
        /// booking id), a listing subscription ("subscription", roleCode + months) or an ad
        /// ("ad", id = ad id, months). The amount and signature come from the server.
        /// </summary>
        public async Task<(PaymentCheckoutModel? Checkout, string? Error, bool AlreadyPaid)> CreatePaymentCheckoutAsync(
            string kind, int id, string? roleCode, int months, string returnUrl, string cancelUrl)
        {
            try
            {
                var res = await _http.PostAsJsonAsync("api/payments/checkout", PaymentItem(kind, id, roleCode, months, returnUrl, cancelUrl));
                if (res.IsSuccessStatusCode)
                    return (await res.Content.ReadFromJsonAsync<PaymentCheckoutModel>(_adminJsonOptions), null, false);
                var (message, alreadyPaid) = await ReadPaymentErrorAsync(res, "Kon nie die betaling begin nie.");
                return (null, message, alreadyPaid);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PAYMENT CHECKOUT ERROR] {ex.Message}");
                return (null, "Kon nie met die bediener kommunikeer nie. Probeer asseblief weer.", false);
            }
        }

        /// <summary>
        /// POST api/payments/simulate - Development only (the server answers 404 elsewhere):
        /// credits the item as if PayFast had confirmed it, at the server's price.
        /// </summary>
        public async Task<(bool Success, bool AlreadyPaid, string? Reference, string? Error)> SimulatePaymentAsync(string kind, int id, string? roleCode, int months)
        {
            try
            {
                var res = await _http.PostAsJsonAsync("api/payments/simulate", PaymentItem(kind, id, roleCode, months));
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadFromJsonAsync<PaymentSimulationModel>(_adminJsonOptions);
                    InvalidateAdsCache();
                    _subscriptions.Clear();
                    return (true, body?.AlreadyPaid ?? false, body?.Reference, null);
                }
                var (message, _) = await ReadPaymentErrorAsync(res, res.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "Die betaalsimulator is net in die ontwikkelingsomgewing beskikbaar."
                    : "Die gesimuleerde betaling het misluk.");
                return (false, false, null, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PAYMENT SIMULATE ERROR] {ex.Message}");
                return (false, false, null, "Kon nie met die bediener kommunikeer nie. Probeer asseblief weer.");
            }
        }

        // --- Team & LinkedIn-Style Browsing ---
        /// <summary>
        /// GET api/Team/browse?roles={roles} - the "browse team roles" directory. The server
        /// says which profiles have an active paid listing (IsSubscriptionActive), so this no
        /// longer asks the owner-only subscription endpoint for every profile (a 403 each,
        /// which used to fall back to treating hardcoded demo users as the only subscribers).
        /// PH rates are still added per PH profile. Empty list on failure - no demo profiles.
        /// </summary>
        public async Task<List<BrowseProfileResponse>> BrowseRolesAsync(string roles = "PH,Boer,VG")
        {
            List<BrowseProfileResponse> list;
            try
            {
                list = await _http.GetFromJsonAsync<List<BrowseProfileResponse>>($"api/Team/browse?roles={roles}") ?? new();
            }
            catch
            {
                return new();
            }

            foreach (var p in list.Where(p => p.RoleCode == "PH"))
            {
                var rates = await GetPhDailyRatesAsync(p.UserId);
                p.StandardDailyRate = rates.StandardDailyRate;
                p.DangerousGameDailyRate = rates.DangerousGameDailyRate;
                p.HasDangerousGameAccreditation = rates.HasDangerousGameAccreditation;
            }

            return list;
        }

        /// <summary>GET api/Team/getTeamMembers/{pjUserId}. Returns an empty list (not a demo fallback) on failure.</summary>
        public async Task<List<TeamMemberResponse>> GetPjTeamMembersAsync(int pjUserId)
        {
            try
            {
                var res = await _http.GetFromJsonAsync<List<TeamMemberResponse>>($"api/Team/getTeamMembers/{pjUserId}");
                if (res != null) return res;
            }
            catch { }

            return new List<TeamMemberResponse>();
        }

        /// <summary>DELETE api/Team/deleteTeamMember/{teamId} - simple immediate removal, no conflict checking. Prefer RemoveTeamMemberWithAuditAsync (paired with CheckTeamMemberRemovalConflictsAsync) for the full audited flow.</summary>
        public async Task<bool> DeleteTeamMemberAsync(int teamId)
        {
            try
            {
                var res = await _http.DeleteAsync($"api/Team/deleteTeamMember/{teamId}");
                return res.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>GET api/Team/checkRemovalConflicts/{teamId} - pre-flight check before removal; returns null on any failure (caller should treat that as "couldn't check", not "no conflicts").</summary>
        public async Task<RemovalConflictCheckResponse?> CheckTeamMemberRemovalConflictsAsync(int teamId)
        {
            try
            {
                var res = await _http.GetAsync($"api/Team/checkRemovalConflicts/{teamId}");
                if (res.IsSuccessStatusCode)
                {
                    return await res.Content.ReadFromJsonAsync<RemovalConflictCheckResponse>();
                }
                Console.WriteLine($"[API] checkRemovalConflicts failed: {res.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Error in CheckTeamMemberRemovalConflictsAsync: {ex.Message}");
            }
            return null;
        }

        /// <summary>GET api/Team/removalReasons; falls back to a hardcoded Afrikaans reason-code list if the API is unreachable, so the removal-reason dropdown always has options.</summary>
        public async Task<List<RemovalReasonOptionDto>> GetRemovalReasonsAsync()
        {
            try
            {
                var res = await _http.GetFromJsonAsync<List<RemovalReasonOptionDto>>("api/Team/removalReasons");
                if (res != null && res.Count > 0) return res;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Error in GetRemovalReasonsAsync: {ex.Message}");
            }

            return new List<RemovalReasonOptionDto>
            {
                new() { Code = "RESIGNATION", Description = "Vrywillige bedanking / Lid versoek om verwyder te word" },
                new() { Code = "UNAVAILABLE", Description = "Onbeskikbaar vir toekomstige ekspedisies" },
                new() { Code = "PERFORMANCE", Description = "Prestasie- of gedragsprobleme" },
                new() { Code = "CONTRACT_ENDED", Description = "Kontraktydperk of seisoen het verstryk" },
                new() { Code = "OTHER", Description = "Ander rede (spesifiseer in notas)" }
            };
        }

        /// <summary>POST api/Team/removeMember ([Authorize]) - the audited removal flow, pairing with CheckTeamMemberRemovalConflictsAsync/GetRemovalReasonsAsync. Extracts a server error message from either a "message" or "error" JSON field, or falls back to the raw response body.</summary>
        public async Task<(bool success, string message, TeamRemovalResult? result)> RemoveTeamMemberWithAuditAsync(RemoveTeamMemberRequest request)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Post, "api/Team/removeMember") { Content = JsonContent.Create(request) };
                AttachAuthHeader(httpReq);
                var res = await _http.SendAsync(httpReq);
                if (res.IsSuccessStatusCode)
                {
                    var result = await res.Content.ReadFromJsonAsync<TeamRemovalResult>();
                    return (true, result?.Message ?? "Spanlid is suksesvol verwyder.", result);
                }

                string errMsg = "Kon nie spanlid verwyder nie.";
                try
                {
                    var raw = await res.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        using var doc = JsonDocument.Parse(raw);
                        if (doc.RootElement.TryGetProperty("message", out var mProp))
                            errMsg = mProp.GetString() ?? errMsg;
                        else if (doc.RootElement.TryGetProperty("error", out var eProp))
                            errMsg = eProp.GetString() ?? errMsg;
                        else
                            errMsg = raw;
                    }
                }
                catch
                {
                    errMsg = $"Fout ({res.StatusCode}) tydens spanlid-verwydering.";
                }

                return (false, errMsg, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Error in RemoveTeamMemberWithAuditAsync: {ex.Message}");
                return (false, $"Netwerkfout: {ex.Message}", null);
            }
        }

        /// <summary>GET api/Team/notifications/{userId} ([Authorize]) - the in-app bell notification list.</summary>
        public async Task<List<UserNotificationDto>> GetUserNotificationsAsync(int userId, int page = 1)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, $"api/Team/notifications/{userId}?page={page}");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (!response.IsSuccessStatusCode) return new List<UserNotificationDto>();
                var res = await response.Content.ReadFromJsonAsync<List<UserNotificationDto>>();
                return res ?? new List<UserNotificationDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Error in GetUserNotificationsAsync: {ex.Message}");
                return new List<UserNotificationDto>();
            }
        }

        /// <summary>POST api/Team/notifications/markAsRead/{notificationId} ([Authorize], server enforces the caller owns the notification). HeaderNav.HandleNotificationClick relies on this returning the true server result before updating local read-state - see that call site.</summary>
        public async Task<bool> MarkNotificationAsReadAsync(int notificationId)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Post, $"api/Team/notifications/markAsRead/{notificationId}");
                AttachAuthHeader(httpReq);
                var res = await _http.SendAsync(httpReq);
                return res.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Error in MarkNotificationAsReadAsync: {ex.Message}");
                return false;
            }
        }


        /// <summary>GET api/Team/getInvitesForUser/{userId}, merged with any local-only _teamInvites not yet reflected server-side (deduped by invite id or PJ+target pair).</summary>
        public async Task<List<TeamInviteResponse>> GetInvitesForUserAsync(int userId)
        {
            var list = new List<TeamInviteResponse>();
            try
            {
                var res = await _http.GetFromJsonAsync<List<TeamInviteResponse>>($"api/Team/getInvitesForUser/{userId}");
                if (res != null) list.AddRange(res);
            }
            catch { }

            // Merge local _teamInvites for this target user
            var local = _teamInvites.Where(i => i.TargetUserId == userId).ToList();
            foreach (var loc in local)
            {
                if (!list.Any(r => r.InviteId == loc.Id || (r.PjUserId == loc.PjUserId && r.TargetUserId == loc.TargetUserId)))
                {
                    list.Add(new TeamInviteResponse
                    {
                        InviteId = loc.Id,
                        PjUserId = loc.PjUserId,
                        PjName = loc.PjName,
                        PjSurname = "",
                        PjEmail = "organiseerder@mikpunt.co.za",
                        TargetUserId = loc.TargetUserId,
                        TargetName = loc.TargetUserName,
                        TargetRoleCode = loc.TargetRoleCode,
                        Status = loc.Status,
                        InviteDate = loc.CreatedAt,
                        ExpeditionName = loc.ExpeditionName,
                        Terms = loc.Terms
                    });
                }
            }

            return list.OrderBy(i => i.Status == "Pending" ? 0 : 1).ThenByDescending(i => i.InviteDate).ToList();
        }

        /// <summary>GET api/Team/getSentInvites/{pjUserId}, falling back to local _teamInvites only when the API returns null/empty.</summary>
        public async Task<List<TeamInviteResponse>> GetSentInvitesForPjAsync(int pjUserId)
        {
            try
            {
                var res = await _http.GetFromJsonAsync<List<TeamInviteResponse>>($"api/Team/getSentInvites/{pjUserId}");
                if (res != null && res.Count > 0) return res;
            }
            catch { }

            var local = _teamInvites.Where(i => i.PjUserId == pjUserId).Select(loc => new TeamInviteResponse
            {
                InviteId = loc.Id,
                PjUserId = loc.PjUserId,
                PjName = loc.PjName,
                PjSurname = "",
                PjEmail = "organiseerder@mikpunt.co.za",
                TargetUserId = loc.TargetUserId,
                TargetName = loc.TargetUserName,
                TargetRoleCode = loc.TargetRoleCode,
                Status = loc.Status,
                InviteDate = loc.CreatedAt,
                ExpeditionName = loc.ExpeditionName,
                Terms = loc.Terms
            }).ToList();

            return local;
        }

        /// <summary>
        /// POST api/Team/sendInvite. Note this doesn't check the response status at all
        /// (not even IsSuccessStatusCode) - the local _teamInvites list is updated
        /// unconditionally regardless of whether the server call succeeded, so this
        /// always returns true.
        /// </summary>
        public async Task<bool> SendTeamInviteAsync(int pjUserId, int targetUserId, int memberUserRoleId = 0, string? expeditionName = null, string? terms = null, string? targetUserName = null, string? targetRoleCode = null)
        {
            try
            {
                var req = new SendInviteRequest
                {
                    PjUserId = pjUserId,
                    TargetUserId = targetUserId,
                    MemberUserRoleId = memberUserRoleId,
                    ExpeditionName = expeditionName,
                    Terms = terms
                };
                await _http.PostAsJsonAsync("api/Team/sendInvite", req);
            }
            catch { }

            // Always synchronize with local _teamInvites so the invite is guaranteed to be received
            var existing = _teamInvites.FirstOrDefault(i => i.PjUserId == pjUserId && i.TargetUserId == targetUserId && i.Status == "Pending");
            if (existing != null)
            {
                existing.ExpeditionName = expeditionName ?? existing.ExpeditionName;
                existing.Terms = terms ?? existing.Terms;
                existing.CreatedAt = DateTime.UtcNow;
                existing.Status = "Pending";
            }
            else
            {
                _teamInvites.Add(new TeamInviteModel
                {
                    Id = _teamInvites.Count > 0 ? _teamInvites.Max(t => t.Id) + 1 : 1,
                    PjUserId = pjUserId,
                    PjName = "Uitruster (PJ)",
                    TargetUserId = targetUserId,
                    TargetUserName = !string.IsNullOrWhiteSpace(targetUserName) ? targetUserName : $"Gebruiker #{targetUserId}",
                    TargetRoleCode = !string.IsNullOrWhiteSpace(targetRoleCode) ? targetRoleCode : "Spanlid",
                    ExpeditionName = expeditionName ?? "Jagekspedisie",
                    Terms = terms ?? "Standaard span-ooreenkoms",
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                });
            }

            NotifyNotificationsChanged();
            return true;
        }

        /// <summary>DELETE api/Team/cancelInvite/{inviteId}. Removes from local cache and returns true regardless of API outcome (same always-succeeds shape as SendTeamInviteAsync).</summary>
        public async Task<bool> CancelTeamInviteAsync(int inviteId)
        {
            try
            {
                var res = await _http.DeleteAsync($"api/Team/cancelInvite/{inviteId}");
                if (res.IsSuccessStatusCode)
                {
                    _teamInvites.RemoveAll(i => i.Id == inviteId);
                    NotifyNotificationsChanged();
                    return true;
                }
            }
            catch { }

            _teamInvites.RemoveAll(i => i.Id == inviteId);
            NotifyNotificationsChanged();
            return true;
        }

        /// <summary>POST api/Team/respondInvite/{inviteId}?accept={accept}. Updates the local invite status unconditionally regardless of the API call's outcome (doesn't even check the response status).</summary>
        public async Task<bool> RespondTeamInviteAsync(int inviteId, bool accept)
        {
            try
            {
                await _http.PostAsync($"api/Team/respondInvite/{inviteId}?accept={accept}", null);
            }
            catch { }

            var inv = _teamInvites.FirstOrDefault(i => i.Id == inviteId);
            if (inv != null)
            {
                inv.Status = accept ? "Accepted" : "Declined";
            }

            NotifyNotificationsChanged();
            return true;
        }

        // Backward compatibility for existing TeamInviteModel callers - local cache only, no API call.
        public Task<List<TeamInviteModel>> GetTeamInvitesAsync() => Task.FromResult(_teamInvites.ToList());

        /// <summary>Local cache only - see GetTeamInvitesAsync.</summary>
        public Task SendTeamInviteAsync(TeamInviteModel invite)
        {
            invite.Id = _teamInvites.Count > 0 ? _teamInvites.Max(t => t.Id) + 1 : 1;
            _teamInvites.Add(invite);
            NotifyNotificationsChanged();
            return Task.CompletedTask;
        }

        /// <summary>Local cache only - see GetTeamInvitesAsync.</summary>
        public Task RespondTeamInviteAsync(int inviteId, string status)
        {
            var inv = _teamInvites.FirstOrDefault(i => i.Id == inviteId);
            if (inv != null) inv.Status = status;
            NotifyNotificationsChanged();
            return Task.CompletedTask;
        }

        // --- Notification Aggregator ---
        /// <summary>
        /// Combines several independent counts (unread chats, pending team invites,
        /// pending bookings for a PJ, system notifications) into one summary for the
        /// header badge. Each sub-count is fetched in its own try/catch, so one failing
        /// source doesn't zero out the others. pendingInvites takes the max of the API
        /// count and the local _teamInvites count, in case the API undercounts a
        /// not-yet-synced local invite.
        /// </summary>
        public async Task<(int unreadChats, int pendingInvites, int pendingBookings, int systemNotifications, int total)> GetNotificationSummaryAsync(int userId, bool isPj = false)
        {
            int unreadChats = 0;
            int pendingInvites = 0;
            int pendingBookings = 0;
            int systemNotifications = 0;

            try
            {
                var chatStatus = await GetChatNotificationStatusAsync(userId);
                if (chatStatus != null) unreadChats = chatStatus.UnreadCount;
            }
            catch { }

            try
            {
                var invites = await _http.GetFromJsonAsync<List<TeamInviteResponse>>($"api/Team/getInvitesForUser/{userId}");
                if (invites != null) pendingInvites = invites.Count(i => i.Status == "Pending");
            }
            catch { }

            var localPending = _teamInvites.Count(i => i.TargetUserId == userId && i.Status == "Pending");
            if (localPending > pendingInvites) pendingInvites = localPending;

            if (isPj)
            {
                try
                {
                    var bookings = await _http.GetFromJsonAsync<List<BookingResponse>>($"api/bookings/getBookings?id={userId}");
                    if (bookings != null) pendingBookings = bookings.Count(b => b.Status == "Pending");
                }
                catch { }
            }

            try
            {
                var unreadCountReq = new HttpRequestMessage(HttpMethod.Get, $"api/Team/notifications/unreadCount/{userId}");
                AttachAuthHeader(unreadCountReq);
                var resp = await _http.SendAsync(unreadCountReq);
                if (resp.IsSuccessStatusCode)
                {
                    var content = await resp.Content.ReadAsStringAsync();
                    if (int.TryParse(content.Trim(), out var cnt))
                    {
                        systemNotifications = cnt;
                    }
                    else
                    {
                        using var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.ValueKind == JsonValueKind.Number)
                        {
                            systemNotifications = doc.RootElement.GetInt32();
                        }
                        else if (doc.RootElement.TryGetProperty("unreadCount", out var prop) ||
                                 doc.RootElement.TryGetProperty("count", out prop))
                        {
                            systemNotifications = prop.GetInt32();
                        }
                    }
                }
            }
            catch { }

            int total = unreadChats + pendingInvites + pendingBookings + systemNotifications;
            return (unreadChats, pendingInvites, pendingBookings, systemNotifications, total);
        }

        // --- Permits ---
        /// <summary>GET api/permits/{userId}.</summary>
        public async Task<List<PermitModel>> GetPermitsAsync(int userId)
        {
            try
            {
                var dtos = await _http.GetFromJsonAsync<List<PermitModel>>($"api/permits/{userId}");
                return dtos ?? new List<PermitModel>();
            }
            catch { return new List<PermitModel>(); }
        }
        
        /// <summary>POST api/permits.</summary>
        public async Task<bool> AddPermitAsync(PermitModel permit)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/permits", permit);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>Local cache only - no matching WebAPI DELETE endpoint exists yet for permits (GetPermitsAsync/AddPermitAsync are real API calls; this one isn't).</summary>
        public async Task<bool> DeletePermitAsync(int permitId)
        {
            // Placeholder: Not fully implemented in backend yet, but ready for extension.
            var idx = _permits.FindIndex(p => p.Id == permitId);
            if (idx >= 0)
            {
                _permits.RemoveAt(idx);
                return await Task.FromResult(true);
            }
            return await Task.FromResult(false);
        }// --- SAPS 520 ---
        /// <summary>Local cache only - no API call (there's no "list my SAPS 520 forms" endpoint; forms are submitted individually via SaveSapsFormAsync).</summary>
        public Task<List<Saps520FormModel>> GetSapsFormsAsync() => Task.FromResult(_sapsForms.ToList());

        /// <summary>Upserts locally first (so the UI/PDF export always has the latest data even if the API call fails), then POSTs to api/huntingpermit/submitSaps520 and reports whether the server accepted it.</summary>
        public async Task<(bool Success, string Message)> SaveSapsFormAsync(Saps520FormModel form)
        {
            if (form.Id == 0)
            {
                form.Id = _sapsForms.Count > 0 ? _sapsForms.Max(s => s.Id) + 1 : 1;
                _sapsForms.Add(form);
            }
            else
            {
                var idx = _sapsForms.FindIndex(s => s.Id == form.Id);
                if (idx >= 0) _sapsForms[idx] = form;
            }
            try
            {
                var res = await _http.PostAsJsonAsync("api/huntingpermit/submitSaps520", form);
                return (res.IsSuccessStatusCode, await ReadErrorMessageAsync(res, res.IsSuccessStatusCode
                    ? "SAPS 520 ingedien." : "Die SAPS 520 kon nie ingedien word nie. Probeer asseblief weer."));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Saps520 API] {ex.Message}");
                return (false, "Kon nie die bediener bereik nie. U vorm is plaaslik gestoor - probeer later weer indien.");
            }
        }

        /// <summary>Normalizes an ad category (accepting both English and Afrikaans input spellings) to its canonical Afrikaans display label. Unrecognized categories pass through unchanged.</summary>
        public static string FormatCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return "Algemeen";
            return category.Trim().ToLowerInvariant() switch
            {
                "gear" or "toerusting" or "toerusting & optika" => "Toerusting & Optika",
                "taxidermy" or "taksidermie" or "taksidermie & montage" => "Taksidermie & Montage",
                "transport" or "vervoer" or "vervoer & logistiek" => "Vervoer & Logistiek",
                "accommodation" or "akkommodasie" or "akkommodasie & jagkampe" or "verblyf" => "Akkommodasie & Jagkampe",
                "firearms" or "vuurwapens" or "vuurwapens & ammunisie" => "Vuurwapens & Ammunisie",
                _ => category
            };
        }

        /// <summary>Bootstrap badge CSS class for an ad's validity state (unpaid / expired / expiring within 7 days / active).</summary>
        public static string GetValidityBadgeClass(DateTime endDate, bool isActive, string paymentStatus)
        {
            if (!isActive || paymentStatus == "Pending Payment")
                return "bg-warning text-dark";
            if (endDate < DateTime.Today)
                return "bg-danger bg-opacity-25 text-danger";
            var days = (endDate - DateTime.Today).Days;
            if (days <= 7)
                return "bg-warning bg-opacity-25 text-warning";
            return "bg-success bg-opacity-25 text-success";
        }

        /// <summary>Afrikaans display text pairing GetValidityBadgeClass's badge (e.g. "3 dae oor", "Het 2 dae gelede verval").</summary>
        public static string GetValidityText(DateTime endDate, bool isActive, string paymentStatus)
        {
            if (!isActive || paymentStatus == "Pending Payment")
                return "Wag op Betaling";
            if (endDate < DateTime.Today)
            {
                int daysAgo = (DateTime.Today - endDate).Days;
                return daysAgo == 0 ? "Het vandag verval" : $"Het {daysAgo} {(daysAgo == 1 ? "dag" : "dae")} gelede verval";
            }
            int daysLeft = (endDate - DateTime.Today).Days;
            if (daysLeft == 0) return "Verval vandag";
            if (daysLeft <= 7) return $"{daysLeft} {(daysLeft == 1 ? "dag" : "dae")} oor (Verval binnekort)";
            return $"{daysLeft} {(daysLeft == 1 ? "dag" : "dae")} oor";
        }

        // --- Ads ---
        /// <summary>
        /// Replaces the PaymentHistory of the given ads with the signed-in caller's real
        /// receipts from GET api/Ads/getPayments (scoped server-side to the caller's own ads,
        /// or every ad for an Admin). The shared ads cache may have been filled by another
        /// circuit that couldn't see these receipts, so the AP dashboard calls this on load.
        /// Ads without receipts keep their existing (possibly synthesized) history.
        /// </summary>
        public async Task RefreshAdPaymentHistoryAsync(IReadOnlyCollection<AdvertisementModel> ads)
        {
            if (ads.Count == 0 || string.IsNullOrWhiteSpace(_auth.Token)) return;
            try
            {
                var records = await _http.GetFromJsonAsync<List<AdPaymentRecord>>("api/Ads/getPayments");
                if (records == null) return;
                foreach (var ad in ads)
                {
                    var receipts = records
                        .Where(p => p.AdId == ad.Id)
                        .OrderByDescending(p => p.PaymentDate)
                        .Select(p => new AdPaymentReceiptModel
                        {
                            PaymentRef = p.PaymentRef,
                            PaymentDate = p.PaymentDate,
                            Months = p.Months,
                            Amount = p.Amount,
                            PeriodStartDate = p.PeriodStartDate,
                            PeriodEndDate = p.PeriodEndDate
                        })
                        .ToList();
                    if (receipts.Count > 0) ad.PaymentHistory = receipts;
                }
            }
            catch { }
        }

        /// <summary>Forces the next GetAdsAsync call to bypass the 3-minute cache TTL and refetch, e.g. right after creating/paying for an ad.</summary>
        public void InvalidateAdsCache()
        {
            _adsLastFetched = DateTime.MinValue;
        }

        /// <summary>Reads from the static, process-wide _ads cache (shared across all circuits/users) without touching the network - callers that don't need freshness guarantees should prefer this over GetAdsAsync to avoid the cache-check/lock overhead.</summary>
        public List<AdvertisementModel> GetCachedAds(int? userId = null, string? status = null)
        {
            lock (_ads)
            {
                var result = _ads.AsEnumerable();
                if (userId.HasValue && userId.Value > 0)
                    result = result.Where(a => a.AdvertiserUserId == userId.Value);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (status.Equals("Aktief", StringComparison.OrdinalIgnoreCase) || status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                        result = result.Where(a => a.IsActive && a.EndDate >= DateTime.Today);
                    else if (status.Equals("Onaktief", StringComparison.OrdinalIgnoreCase))
                        result = result.Where(a => a.PaymentStatus == "Onaktief" || (!a.IsActive && a.PaymentStatus != "Pending Payment"));
                    else
                        result = result.Where(a => a.PaymentStatus.Contains(status, StringComparison.OrdinalIgnoreCase));
                }
                return result.ToList();
            }
        }

        /// <summary>
        /// GET api/Ads/getAds (+ api/Ads/getPayments for real payment-history receipts),
        /// merged into the static, 3-minute-TTL _ads cache shared by every circuit -
        /// double-checked locking (_adsFetchLock semaphore + a re-check of the TTL after
        /// acquiring it) avoids a stampede of identical requests when many users load an
        /// ads page at once. ImagesJson is a JSON-encoded string array on the wire,
        /// decoded here with a single placeholder image as fallback. Existing cache
        /// entries are merged field-by-field (not replaced wholesale) so any real payment
        /// receipts found take priority over a synthesized single-entry PaymentHistory.
        /// A 5-second timeout applies to both HTTP calls (CancellationTokenSource).
        /// </summary>
        public async Task<List<AdvertisementModel>> GetAdsAsync(int? userId = null, string? status = null, bool forceRefresh = false)
        {
            bool isFresh = !forceRefresh && _ads.Count > 0 && (DateTime.UtcNow - _adsLastFetched) < _adsCacheTtl;
            if (isFresh)
            {
                return GetCachedAds(userId, status);
            }

            await _adsFetchLock.WaitAsync();
            try
            {
                if (!forceRefresh && _ads.Count > 0 && (DateTime.UtcNow - _adsLastFetched) < _adsCacheTtl)
                {
                    return GetCachedAds(userId, status);
                }

                string url = "api/Ads/getAds";
                var queryParams = new List<string>();
                if (userId.HasValue && userId.Value > 0)
                    queryParams.Add($"userId={userId.Value}");
                if (!string.IsNullOrWhiteSpace(status))
                    queryParams.Add($"status={Uri.EscapeDataString(status)}");
                if (queryParams.Count > 0)
                    url += "?" + string.Join("&", queryParams);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _http.GetAsync(url, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var apiAds = await response.Content.ReadFromJsonAsync<List<AdResponseModel>>(cancellationToken: cts.Token);
                    if (apiAds != null)
                    {
                        // Fetch true payment history from adPayments table. The API only returns
                        // the signed-in caller's own payments (all of them for an Admin), and none
                        // to anonymous visitors - so this fills in whatever this circuit may see;
                        // ApDashboard refreshes the owner's receipts via RefreshAdPaymentHistoryAsync.
                        List<AdPaymentRecord> paymentRecords = new();
                        if (!string.IsNullOrWhiteSpace(_auth.Token))
                        {
                            try
                            {
                                string payUrl = "api/Ads/getPayments";
                                if (userId.HasValue && userId.Value > 0) payUrl += $"?userId={userId.Value}";
                                var payRes = await _http.GetAsync(payUrl, cts.Token);
                                if (payRes.IsSuccessStatusCode)
                                {
                                    var pList = await payRes.Content.ReadFromJsonAsync<List<AdPaymentRecord>>(cancellationToken: cts.Token);
                                    if (pList != null) paymentRecords = pList;
                                }
                            }
                            catch { }
                        }

                        lock (_ads)
                        {
                            foreach (var apiAd in apiAds)
                            {
                                List<string> images = new();
                                if (!string.IsNullOrWhiteSpace(apiAd.ImagesJson))
                                {
                                    try
                                    {
                                        var parsed = JsonSerializer.Deserialize<List<string>>(apiAd.ImagesJson);
                                        if (parsed != null && parsed.Count > 0) images = parsed;
                                    }
                                    catch { }
                                }
                                if (images.Count == 0)
                                {
                                    images.Add("images/savanna_sunrise.jpg");
                                }

                                int ownerUserId = apiAd.UserId.HasValue && apiAd.UserId.Value > 0 ? apiAd.UserId.Value : 0;
                                string advertiserDisplayName = !string.IsNullOrWhiteSpace(apiAd.AdvertiserName) ? apiAd.AdvertiserName : "Geregistreerde Borg";

                                var receiptsForAd = paymentRecords
                                    .Where(p => p.AdId == apiAd.Id)
                                    .OrderByDescending(p => p.PaymentDate)
                                    .Select(p => new AdPaymentReceiptModel
                                    {
                                        PaymentRef = p.PaymentRef,
                                        PaymentDate = p.PaymentDate,
                                        Months = p.Months,
                                        Amount = p.Amount,
                                        PeriodStartDate = p.PeriodStartDate,
                                        PeriodEndDate = p.PeriodEndDate
                                    })
                                    .ToList();

                                var existing = _ads.FirstOrDefault(a => a.Id == apiAd.Id);
                                if (existing == null)
                                {
                                    var newModel = new AdvertisementModel
                                    {
                                        Id = apiAd.Id,
                                        AdvertiserUserId = ownerUserId,
                                        Title = apiAd.Title,
                                        Description = apiAd.Description ?? string.Empty,
                                        LongDescription = apiAd.LongDescription ?? apiAd.Description ?? string.Empty,
                                        PlacementType = !string.IsNullOrWhiteSpace(apiAd.Type) ? apiAd.Type : "Banner",
                                        Category = FormatCategory(apiAd.Category),
                                        AdvertiserName = advertiserDisplayName,
                                        ImageUrls = images,
                                        TargetUrl = !string.IsNullOrWhiteSpace(apiAd.TargetUrl) ? apiAd.TargetUrl : "#",
                                        ContactNumber = apiAd.ContactNumber ?? string.Empty,
                                        DurationMonths = apiAd.LastPaymentMonths ?? 1,
                                        MonthlyRate = 100m,
                                        TotalPaidAmount = apiAd.Price ?? (receiptsForAd.Count > 0 ? receiptsForAd.Sum(r => r.Amount) : 100m),
                                        PaymentStatus = (apiAd.Status == "Aktief" || apiAd.Status == "Active") ? "Betaal / Aktief" : (apiAd.Status == "Verval" ? "Verval" : (apiAd.Status == "Onaktief" ? "Onaktief" : "Pending Payment")),
                                        PaymentRef = !string.IsNullOrWhiteSpace(apiAd.PaymentRef) ? apiAd.PaymentRef : (receiptsForAd.FirstOrDefault()?.PaymentRef ?? $"MP-ADPAY-{apiAd.Id:D6}"),
                                        PaymentDate = apiAd.LastPaymentDate ?? apiAd.CreatedDate,
                                        LastPaymentMonths = apiAd.LastPaymentMonths ?? (receiptsForAd.FirstOrDefault()?.Months ?? 1),
                                        LastPaymentAmount = apiAd.LastPaymentAmount ?? (receiptsForAd.FirstOrDefault()?.Amount ?? 100m),
                                        LastPaymentDate = apiAd.LastPaymentDate ?? apiAd.CreatedDate,
                                        StartDate = apiAd.CreatedDate,
                                        EndDate = apiAd.ExpiryDate ?? apiAd.CreatedDate.AddMonths(1),
                                        IsActive = (apiAd.Status == "Aktief" || apiAd.Status == "Active") && (apiAd.ExpiryDate == null || apiAd.ExpiryDate >= DateTime.Today),
                                        ImpressionsCount = apiAd.Views,
                                        ClicksCount = apiAd.Clicks,
                                        PaymentHistory = receiptsForAd.Count > 0 ? receiptsForAd : new List<AdPaymentReceiptModel>()
                                    };

                                    if (newModel.PaymentHistory.Count == 0 && newModel.PaymentStatus == "Betaal / Aktief" && newModel.TotalPaidAmount > 0)
                                    {
                                        newModel.PaymentHistory.Add(new AdPaymentReceiptModel
                                        {
                                            PaymentRef = !string.IsNullOrWhiteSpace(newModel.PaymentRef) ? newModel.PaymentRef : $"MP-ADPAY-{newModel.Id:D6}",
                                            PaymentDate = newModel.PaymentDate ?? newModel.StartDate,
                                            Months = newModel.LastPaymentMonths > 0 ? newModel.LastPaymentMonths : 1,
                                            Amount = newModel.LastPaymentAmount > 0 ? newModel.LastPaymentAmount : newModel.TotalPaidAmount,
                                            PeriodStartDate = newModel.LastPaymentStartDate ?? newModel.StartDate,
                                            PeriodEndDate = newModel.LastPaymentEndDate ?? newModel.EndDate
                                        });
                                    }

                                    _ads.Add(newModel);
                                }
                                else
                                {
                                    existing.AdvertiserUserId = ownerUserId;
                                    existing.AdvertiserName = advertiserDisplayName;
                                    existing.Title = apiAd.Title;
                                    existing.Description = apiAd.Description ?? existing.Description;
                                    existing.LongDescription = apiAd.LongDescription ?? existing.LongDescription;
                                    if (!string.IsNullOrWhiteSpace(apiAd.Type)) existing.PlacementType = apiAd.Type;
                                    existing.Category = FormatCategory(apiAd.Category);
                                    existing.ContactNumber = apiAd.ContactNumber ?? existing.ContactNumber;
                                    if (!string.IsNullOrWhiteSpace(apiAd.TargetUrl)) existing.TargetUrl = apiAd.TargetUrl;
                                    if (!string.IsNullOrWhiteSpace(apiAd.PaymentRef)) existing.PaymentRef = apiAd.PaymentRef;
                                    if (apiAd.LastPaymentMonths.HasValue) existing.LastPaymentMonths = apiAd.LastPaymentMonths.Value;
                                    if (apiAd.LastPaymentAmount.HasValue) existing.LastPaymentAmount = apiAd.LastPaymentAmount.Value;
                                    if (apiAd.LastPaymentDate.HasValue) existing.LastPaymentDate = apiAd.LastPaymentDate.Value;
                                    existing.TotalPaidAmount = apiAd.Price ?? (receiptsForAd.Count > 0 ? receiptsForAd.Sum(r => r.Amount) : existing.TotalPaidAmount);
                                    existing.PaymentStatus = (apiAd.Status == "Aktief" || apiAd.Status == "Active") ? "Betaal / Aktief" : (apiAd.Status == "Verval" ? "Verval" : (apiAd.Status == "Onaktief" ? "Onaktief" : "Pending Payment"));
                                    existing.IsActive = (apiAd.Status == "Aktief" || apiAd.Status == "Active") && (apiAd.ExpiryDate == null || apiAd.ExpiryDate >= DateTime.Today);
                                    if (apiAd.ExpiryDate.HasValue) existing.EndDate = apiAd.ExpiryDate.Value;
                                    if (images.Count > 0 && (existing.ImageUrls == null || existing.ImageUrls.Count == 0 || existing.ImageUrls[0] == "images/savanna_sunrise.jpg"))
                                    {
                                        existing.ImageUrls = images;
                                    }
                                    existing.ImpressionsCount = Math.Max(existing.ImpressionsCount, apiAd.Views);
                                    existing.ClicksCount = Math.Max(existing.ClicksCount, apiAd.Clicks);

                                    if (receiptsForAd.Count > 0)
                                    {
                                        existing.PaymentHistory = receiptsForAd;
                                    }
                                    else if (existing.PaymentStatus == "Betaal / Aktief" && existing.TotalPaidAmount > 0 && (existing.PaymentHistory == null || existing.PaymentHistory.Count == 0))
                                    {
                                        existing.PaymentHistory = new List<AdPaymentReceiptModel>
                                        {
                                            new AdPaymentReceiptModel
                                            {
                                                PaymentRef = !string.IsNullOrWhiteSpace(existing.PaymentRef) ? existing.PaymentRef : $"MP-ADPAY-{existing.Id:D6}",
                                                PaymentDate = existing.PaymentDate ?? existing.StartDate,
                                                Months = existing.LastPaymentMonths > 0 ? existing.LastPaymentMonths : 1,
                                                Amount = existing.LastPaymentAmount > 0 ? existing.LastPaymentAmount : existing.TotalPaidAmount,
                                                PeriodStartDate = existing.LastPaymentStartDate ?? existing.StartDate,
                                                PeriodEndDate = existing.LastPaymentEndDate ?? existing.EndDate
                                            }
                                        };
                                    }
                                }
                            }
                        }
                        _adsLastFetched = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GET ADS API ERROR] {ex.Message}");
            }
            finally
            {
                _adsFetchLock.Release();
            }

            return GetCachedAds(userId, status);
        }

        /// <summary>GET api/Ads/getAd/{adId}, same field-mapping as GetAdsAsync but for a single ad. Serves straight from the shared _ads cache if a fresh (within TTL) entry already exists, otherwise fetches and merges/inserts into that same cache.</summary>
        public async Task<AdvertisementModel?> GetAdByIdAsync(int adId)
        {
            lock (_ads)
            {
                var cached = _ads.FirstOrDefault(a => a.Id == adId);
                if (cached != null && (DateTime.UtcNow - _adsLastFetched) < _adsCacheTtl)
                {
                    return cached;
                }
            }

            try
            {
                var response = await _http.GetAsync($"api/Ads/getAd/{adId}");
                if (response.IsSuccessStatusCode)
                {
                    var apiAd = await response.Content.ReadFromJsonAsync<AdResponseModel>();
                    if (apiAd != null)
                    {
                        List<string> images = new();
                        if (!string.IsNullOrWhiteSpace(apiAd.ImagesJson))
                        {
                            try
                            {
                                var parsed = JsonSerializer.Deserialize<List<string>>(apiAd.ImagesJson);
                                if (parsed != null && parsed.Count > 0) images = parsed;
                            }
                            catch { }
                        }
                        if (images.Count == 0) images.Add("images/savanna_sunrise.jpg");

                        int ownerUserId = apiAd.UserId.HasValue && apiAd.UserId.Value > 0 ? apiAd.UserId.Value : 0;
                        string advertiserDisplayName = !string.IsNullOrWhiteSpace(apiAd.AdvertiserName) ? apiAd.AdvertiserName : "Geregistreerde Borg";

                        lock (_ads)
                        {
                            var local = _ads.FirstOrDefault(a => a.Id == adId);
                            if (local != null)
                            {
                                local.AdvertiserUserId = ownerUserId;
                                local.AdvertiserName = advertiserDisplayName;
                                local.Title = apiAd.Title;
                                local.Description = apiAd.Description ?? local.Description;
                                local.LongDescription = apiAd.LongDescription ?? local.LongDescription;
                                if (!string.IsNullOrWhiteSpace(apiAd.Type)) local.PlacementType = apiAd.Type;
                                local.Category = FormatCategory(apiAd.Category);
                                local.ContactNumber = apiAd.ContactNumber ?? local.ContactNumber;
                                if (!string.IsNullOrWhiteSpace(apiAd.TargetUrl)) local.TargetUrl = apiAd.TargetUrl;
                                if (!string.IsNullOrWhiteSpace(apiAd.PaymentRef)) local.PaymentRef = apiAd.PaymentRef;
                                if (apiAd.LastPaymentMonths.HasValue) local.LastPaymentMonths = apiAd.LastPaymentMonths.Value;
                                if (apiAd.LastPaymentAmount.HasValue) local.LastPaymentAmount = apiAd.LastPaymentAmount.Value;
                                if (apiAd.LastPaymentDate.HasValue) local.LastPaymentDate = apiAd.LastPaymentDate.Value;
                                local.TotalPaidAmount = apiAd.Price ?? local.TotalPaidAmount;
                                local.PaymentStatus = (apiAd.Status == "Aktief" || apiAd.Status == "Active") ? "Betaal / Aktief" : (apiAd.Status == "Verval" ? "Verval" : (apiAd.Status == "Onaktief" ? "Onaktief" : "Pending Payment"));
                                local.IsActive = (apiAd.Status == "Aktief" || apiAd.Status == "Active") && (apiAd.ExpiryDate == null || apiAd.ExpiryDate >= DateTime.Today);
                                if (apiAd.ExpiryDate.HasValue) local.EndDate = apiAd.ExpiryDate.Value;
                                local.ImpressionsCount = Math.Max(local.ImpressionsCount, apiAd.Views);
                                local.ClicksCount = Math.Max(local.ClicksCount, apiAd.Clicks);
                                if (images.Count > 0)
                                {
                                    local.ImageUrls = images;
                                }
                                return local;
                            }

                            var newAd = new AdvertisementModel
                            {
                                Id = apiAd.Id,
                                AdvertiserUserId = ownerUserId,
                                Title = apiAd.Title,
                                Description = apiAd.Description ?? string.Empty,
                                LongDescription = apiAd.LongDescription ?? apiAd.Description ?? string.Empty,
                                PlacementType = !string.IsNullOrWhiteSpace(apiAd.Type) ? apiAd.Type : "Banner",
                                Category = string.IsNullOrWhiteSpace(apiAd.Category) ? "Gear" : apiAd.Category,
                                AdvertiserName = advertiserDisplayName,
                                ImageUrls = images,
                                TargetUrl = !string.IsNullOrWhiteSpace(apiAd.TargetUrl) ? apiAd.TargetUrl : "#",
                                ContactNumber = apiAd.ContactNumber ?? string.Empty,
                                DurationMonths = 1,
                                MonthlyRate = 100m,
                                TotalPaidAmount = apiAd.Price ?? 100m,
                                PaymentStatus = (apiAd.Status == "Aktief" || apiAd.Status == "Active") ? "Betaal / Aktief" : (apiAd.Status == "Verval" ? "Verval" : (apiAd.Status == "Onaktief" ? "Onaktief" : "Pending Payment")),
                                PaymentRef = !string.IsNullOrWhiteSpace(apiAd.PaymentRef) ? apiAd.PaymentRef : $"MP-ADPAY-{apiAd.Id:D6}",
                                PaymentDate = apiAd.LastPaymentDate ?? apiAd.CreatedDate,
                                LastPaymentMonths = apiAd.LastPaymentMonths ?? (apiAd.Price.HasValue ? (int)Math.Max(1, apiAd.Price.Value / 100m) : 1),
                                LastPaymentAmount = apiAd.LastPaymentAmount ?? (apiAd.Price ?? 100m),
                                LastPaymentDate = apiAd.LastPaymentDate ?? apiAd.CreatedDate,
                                StartDate = apiAd.CreatedDate,
                                EndDate = apiAd.ExpiryDate ?? apiAd.CreatedDate.AddMonths(1),
                                IsActive = (apiAd.Status == "Aktief" || apiAd.Status == "Active") && (apiAd.ExpiryDate == null || apiAd.ExpiryDate >= DateTime.Today),
                                ImpressionsCount = apiAd.Views,
                                ClicksCount = apiAd.Clicks
                            };
                            _ads.Add(newAd);
                            return newAd;
                        }
                    }
                }
            }
            catch { }

            lock (_ads)
            {
                return _ads.FirstOrDefault(a => a.Id == adId);
            }
        }

        /// <summary>Creates/updates an advertisement (POST api/Ads/createAd or api/Ads/updateAd/{id}). ImageUrls is serialized to a JSON string for the wire (see ImagesJson on the API side). EndDate is clamped to SQL Server's minimum DATETIME (1753-01-01) to avoid a SqlDateTime overflow if a caller passes an out-of-range/default date.</summary>
        public async Task<AdvertisementModel> SaveAdAsync(AdvertisementModel ad)
        {
            var imagesJson = ad.ImageUrls != null && ad.ImageUrls.Count > 0
                ? JsonSerializer.Serialize(ad.ImageUrls)
                : null;

            // Ensure valid dates (prevent SqlDateTime overflow)
            DateTime? validExpiryDate = null;
            if (ad.EndDate >= new DateTime(1753, 1, 1))
            {
                validExpiryDate = ad.EndDate;
            }
            else if (ad.DurationMonths > 0)
            {
                validExpiryDate = DateTime.Today.AddMonths(ad.DurationMonths);
                ad.EndDate = validExpiryDate.Value;
            }

            DateTime? validPaymentDate = null;
            if (ad.LastPaymentDate.HasValue && ad.LastPaymentDate.Value >= new DateTime(1753, 1, 1))
            {
                validPaymentDate = ad.LastPaymentDate.Value;
            }
            else if (ad.PaymentDate.HasValue && ad.PaymentDate.Value >= new DateTime(1753, 1, 1))
            {
                validPaymentDate = ad.PaymentDate.Value;
            }

            if (ad.Id == 0)
            {
                try
                {
                    var req = new
                    {
                        UserId = ad.AdvertiserUserId > 0 ? ad.AdvertiserUserId : (int?)null,
                        AdvertiserName = ad.AdvertiserName,
                        Type = ad.AdType,
                        Title = ad.Title,
                        Description = ad.Description,
                        LongDescription = ad.LongDescription ?? ad.Description,
                        Category = ad.Category,
                        Status = ad.IsActive ? "Aktief" : "Pending Payment",
                        Price = ad.TotalPaidAmount > 0 ? ad.TotalPaidAmount : 100m,
                        ExpiryDate = validExpiryDate,
                        ContactNumber = ad.ContactNumber,
                        TargetUrl = ad.TargetUrl,
                        PaymentRef = ad.PaymentRef,
                        LastPaymentMonths = ad.LastPaymentMonths,
                        LastPaymentAmount = ad.LastPaymentAmount,
                        LastPaymentDate = validPaymentDate,
                        ImagesJson = imagesJson
                    };
                    var response = await _http.PostAsJsonAsync("api/Ads/createAd", req);
                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<CreateAdResponseModel>();
                        if (result != null && result.Id > 0)
                        {
                            ad.Id = result.Id;
                        }
                    }
                    else
                    {
                        var errText = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[CREATE AD API FAILED {response.StatusCode}]: {errText}");
                        throw new InvalidOperationException($"Die bediener kon nie die advertensie in die databasis stoor nie: {errText}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CREATE AD API ERROR] {ex.Message}");
                    throw;
                }

                lock (_ads)
                {
                    var existing = _ads.FirstOrDefault(a => a.Id == ad.Id);
                    if (existing == null)
                    {
                        _ads.Add(ad);
                    }
                    else
                    {
                        var idx = _ads.FindIndex(a => a.Id == ad.Id);
                        if (idx >= 0) _ads[idx] = ad;
                    }
                }
            }
            else
            {
                // The HTTP call runs BEFORE the local cache is touched (unlike before) -
                // this used to update _ads unconditionally first and only log a failed
                // server update to the console, so a rejected/failed save rendered
                // identically to a successful one from the caller's point of view.
                try
                {
                    var req = new
                    {
                        AdvertiserName = ad.AdvertiserName,
                        Type = ad.AdType,
                        Title = ad.Title,
                        Description = ad.Description,
                        LongDescription = ad.LongDescription ?? ad.Description,
                        Price = ad.TotalPaidAmount,
                        Status = ad.IsActive ? "Aktief" : (ad.PaymentStatus == "Onaktief" ? "Onaktief" : "Pending Payment"),
                        Category = ad.Category,
                        ExpiryDate = validExpiryDate,
                        ContactNumber = ad.ContactNumber,
                        TargetUrl = ad.TargetUrl,
                        PaymentRef = ad.PaymentRef,
                        LastPaymentMonths = ad.LastPaymentMonths,
                        LastPaymentAmount = ad.LastPaymentAmount,
                        LastPaymentDate = validPaymentDate,
                        ImagesJson = imagesJson
                    };
                    string updateUrl = $"api/Ads/updateAd/{ad.Id}" + (ad.AdvertiserUserId > 0 ? $"?userId={ad.AdvertiserUserId}" : "");
                    var updateRes = await _http.PostAsJsonAsync(updateUrl, req);
                    if (!updateRes.IsSuccessStatusCode)
                    {
                        var errText = await updateRes.Content.ReadAsStringAsync();
                        Console.WriteLine($"[UPDATE AD API FAILED {updateRes.StatusCode}]: {errText}");
                        throw new InvalidOperationException($"Die bediener kon nie die advertensie-veranderinge stoor nie: {errText}");
                    }
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    Console.WriteLine($"[UPDATE AD API ERROR] {ex.Message}");
                    throw new InvalidOperationException("Kon nie die advertensie-veranderinge stoor nie. Kontroleer u internetverbinding.", ex);
                }

                lock (_ads)
                {
                    var idx = _ads.FindIndex(a => a.Id == ad.Id);
                    if (idx >= 0) _ads[idx] = ad;
                }
            }
            InvalidateAdsCache();
            return ad;
        }

        /// <summary>Flips an ad's active/inactive status locally first, then best-effort persists to api/Ads/updateAd/{adId} - returns true even if the persist fails (the local flip already happened by then).</summary>
        public async Task<bool> ToggleAdStatusAsync(int adId)
        {
            var ad = _ads.FirstOrDefault(a => a.Id == adId);
            if (ad == null) return false;

            ad.IsActive = !ad.IsActive;
            ad.PaymentStatus = ad.IsActive ? "Betaal / Aktief" : "Onaktief";

            try
            {
                var req = new
                {
                    Status = ad.IsActive ? "Aktief" : "Onaktief"
                };
                string updateUrl = $"api/Ads/updateAd/{adId}" + (ad.AdvertiserUserId > 0 ? $"?userId={ad.AdvertiserUserId}" : "");
                await _http.PostAsJsonAsync(updateUrl, req);
                InvalidateAdsCache();
            }
            catch { }

            return true;
        }

        /// <summary>Removes from the shared _ads cache immediately, then best-effort DELETEs api/Ads/deleteAd/{adId} - a failed server delete leaves the ad gone from every viewer's cache until the next full GetAdsAsync refetch restores it.</summary>
        public async Task DeleteAdAsync(int adId, int? userId = null)
        {
            lock (_ads)
            {
                _ads.RemoveAll(a => a.Id == adId);
            }
            InvalidateAdsCache();
            try
            {
                string url = $"api/Ads/deleteAd/{adId}" + (userId.HasValue && userId.Value > 0 ? $"?userId={userId.Value}" : "");
                await _http.DeleteAsync(url);
            }
            catch { }
        }

        /// <summary>
        /// Increments the local impression counter synchronously (skipped when the viewer
        /// is the ad's own advertiser, so an advertiser viewing their own ad doesn't
        /// inflate their own stats) then fires the POST to api/Ads/recordImpression in the
        /// background via Task.Run - the caller doesn't await network I/O, so this never
        /// slows down page rendering.
        /// </summary>
        public Task RecordAdImpressionAsync(int adId, int? viewerUserId = null)
        {
            lock (_ads)
            {
                var ad = _ads.FirstOrDefault(a => a.Id == adId);
                if (ad != null && (!viewerUserId.HasValue || viewerUserId.Value != ad.AdvertiserUserId))
                {
                    ad.ImpressionsCount++;
                }
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    string url = $"api/Ads/recordImpression/{adId}" + (viewerUserId.HasValue && viewerUserId.Value > 0 ? $"?viewerUserId={viewerUserId.Value}" : "");
                    await _http.PostAsync(url, null);
                }
                catch { }
            });
            return Task.CompletedTask;
        }

        /// <summary>Same fire-and-forget pattern as RecordAdImpressionAsync, for clicks.</summary>
        public Task RecordAdClickAsync(int adId, int? viewerUserId = null)
        {
            lock (_ads)
            {
                var ad = _ads.FirstOrDefault(a => a.Id == adId);
                if (ad != null && (!viewerUserId.HasValue || viewerUserId.Value != ad.AdvertiserUserId))
                {
                    ad.ClicksCount++;
                }
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    string url = $"api/Ads/recordClick/{adId}" + (viewerUserId.HasValue && viewerUserId.Value > 0 ? $"?viewerUserId={viewerUserId.Value}" : "");
                    await _http.PostAsync(url, null);
                }
                catch { }
            });
            return Task.CompletedTask;
        }

        /// <summary>GET api/Ads/getPayments, optionally filtered by adId and/or userId.</summary>
        public async Task<List<AdPaymentRecord>> GetAdPaymentsAsync(int? adId = null, int? userId = null)
        {
            try
            {
                string url = "api/Ads/getPayments";
                var q = new List<string>();
                if (adId.HasValue && adId.Value > 0) q.Add($"adId={adId.Value}");
                if (userId.HasValue && userId.Value > 0) q.Add($"userId={userId.Value}");
                if (q.Count > 0) url += "?" + string.Join("&", q);

                var res = await _http.GetFromJsonAsync<List<AdPaymentRecord>>(url);
                return res ?? new List<AdPaymentRecord>();
            }
            catch
            {
                return new List<AdPaymentRecord>();
            }
        }


        // --- Real HTTP API Chats & Messages ---
        /// <summary>GET api/Chats/{userId} ([Authorize]) - the signed-in user's chat list.</summary>
        public async Task<List<Chat>> GetUserChatsAsync(int userId)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"api/Chats/{userId}");
                AttachAuthHeader(req);
                using var res = await _http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var chats = await res.Content.ReadFromJsonAsync<List<Chat>>();
                    return chats ?? new List<Chat>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GET USER CHATS API ERROR] {ex.Message}");
            }
            return new List<Chat>();
        }

        /// <summary>GET api/Chats/{chatId}/messages ([Authorize], server verifies participant membership) - paginated.</summary>
        public async Task<List<Message>> GetChatMessagesAsync(int chatId, int page = 1, int pageSize = 50)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"api/Chats/{chatId}/messages?page={page}&pageSize={pageSize}");
                AttachAuthHeader(req);
                using var res = await _http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var messages = await res.Content.ReadFromJsonAsync<List<Message>>();
                    return messages ?? new List<Message>();
                }
                return new List<Message>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GET CHAT MESSAGES API ERROR] {ex.Message}");
                return new List<Message>();
            }
        }

        /// <summary>POST api/Chats/{chatId}/messages ([Authorize], server verifies participant membership). Real-time delivery to other participants happens separately via ChatHub - this call only persists the message.</summary>
        public async Task<(bool success, int messageId)> SendChatMessageAsync(int chatId, int senderId, string content)
        {
            try
            {
                var payload = new SendChatMessageRequest { SenderId = senderId, Content = content };
                using var req = new HttpRequestMessage(HttpMethod.Post, $"api/Chats/{chatId}/messages")
                {
                    Content = JsonContent.Create(payload)
                };
                AttachAuthHeader(req);
                using var res = await _http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadFromJsonAsync<SendChatMessageResponse>();
                    return (json?.Success ?? true, json?.MessageId ?? 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SEND CHAT MESSAGE API ERROR] {ex.Message}");
            }
            return (false, 0);
        }

        /// <summary>
        /// POST api/Chats/direct/{targetUserId} ([Authorize]) - finds or creates a 1:1
        /// chat with targetUserId, optionally sending an initial message. Sends both
        /// "initialMessage" and "message" keys in the payload for API-shape tolerance.
        /// The `unauthorized` tuple flag distinguishes an expired/missing session (401,
        /// caller should redirect to login) from an ordinary failure.
        /// </summary>
        public async Task<(bool success, int chatId, bool unauthorized)> CreateDirectChatAsync(int targetUserId, int senderId, string? initialMessage = null)
        {
            try
            {
                var payload = new { senderId, initialMessage, message = initialMessage };
                using var req = new HttpRequestMessage(HttpMethod.Post, $"api/Chats/direct/{targetUserId}")
                {
                    Content = JsonContent.Create(payload)
                };
                AttachAuthHeader(req);
                using var res = await _http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadFromJsonAsync<DirectChatApiResponse>();
                    return (json?.Success ?? true, json?.ChatId ?? 0, false);
                }
                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("[CREATE DIRECT CHAT API ERROR] 401 Unauthorized - session token missing or expired.");
                    return (false, 0, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CREATE DIRECT CHAT API ERROR] {ex.Message}");
            }
            return (false, 0, false);
        }

        /// <summary>POST api/Chats/setReadStatus ([Authorize]) - marks messages up to lastReadMessageId as read for userId in chatId.</summary>
        public async Task<bool> SetReadStatusAsync(int chatId, int userId, int lastReadMessageId)
        {
            try
            {
                var payload = new { chatId, userId, lastReadMessageId };
                using var req = new HttpRequestMessage(HttpMethod.Post, "api/Chats/setReadStatus")
                {
                    Content = JsonContent.Create(payload)
                };
                AttachAuthHeader(req);
                using var res = await _http.SendAsync(req);
                return res.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SET READ STATUS API ERROR] {ex.Message}");
                return false;
            }
        }

        /// <summary>GET api/Chats/getNotificationStatus/{userId} ([Authorize]) - feeds the total unread-chat count into GetNotificationSummaryAsync. Defaults to "enabled, zero unread" on failure rather than propagating an error.</summary>
        public async Task<NotificationStatus> GetChatNotificationStatusAsync(int userId)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"api/Chats/getNotificationStatus/{userId}");
                AttachAuthHeader(req);
                using var res = await _http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var status = await res.Content.ReadFromJsonAsync<NotificationStatus>();
                    if (status != null) return status;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GET NOTIFICATION STATUS API ERROR] {ex.Message}");
            }
            return new NotificationStatus { IsEnabled = true, UnreadCount = 0, LastNotified = DateTime.UtcNow };
        }

        // --- Backwards-Compatible Message Wrappers ---
        /// <summary>Unused: no Razor page calls this (Grep-verified against MessageModel above). Legacy adapter that flattens every chat's messages into the old MessageModel shape by calling GetUserChatsAsync + GetChatMessagesAsync per chat - defaults userId to 1 if not given, which would be wrong for any other caller anyway.</summary>
        public async Task<List<MessageModel>> GetMessagesAsync(int? userId = null)
        {
            var result = new List<MessageModel>();
            try
            {
                int targetUserId = userId.HasValue && userId.Value > 0 ? userId.Value : 1;
                var chats = await GetUserChatsAsync(targetUserId);
                foreach (var chat in chats)
                {
                    var msgs = await GetChatMessagesAsync(chat.Id, 1, 50);
                    var otherParticipant = chat.Participants.FirstOrDefault(p => p.UserId != targetUserId);
                    foreach (var m in msgs)
                    {
                        result.Add(new MessageModel
                        {
                            Id = m.Id,
                            SenderUserId = m.SenderId,
                            SenderName = m.SenderName,
                            ReceiverUserId = otherParticipant?.UserId ?? 0,
                            ReceiverName = otherParticipant?.UserName ?? chat.Title ?? string.Empty,
                            Content = m.Content,
                            SentAt = m.SentAt,
                            IsRead = chat.UnreadCount == 0
                        });
                    }
                }
            }
            catch { }
            return result.OrderBy(m => m.SentAt).ToList();
        }

        /// <summary>Unused: no Razor page calls this (see GetMessagesAsync). Legacy adapter over CreateDirectChatAsync + SendChatMessageAsync.</summary>
        public async Task SendMessageAsync(MessageModel msg)
        {
            try
            {
                if (msg.ReceiverUserId > 0 && msg.SenderUserId > 0)
                {
                    var (success, chatId, _) = await CreateDirectChatAsync(msg.ReceiverUserId, msg.SenderUserId);
                    if (success && chatId > 0)
                    {
                        await SendChatMessageAsync(chatId, msg.SenderUserId, msg.Content);
                    }
                }
            }
            catch { }
            NotifyNotificationsChanged();
        }

        /// <summary>
        /// Unused: no Razor page calls this (Grep-verified) - HeaderNav and Messages.razor
        /// use SetReadStatusAsync directly instead. `async void` here is generally a
        /// pattern to avoid (a caller can't await it or observe an unhandled exception
        /// from it), though the internal try/catch swallows every exception anyway so it
        /// wouldn't currently crash the process if called.
        /// </summary>
        public async void MarkMessagesAsRead(int currentUserId, int otherUserId)
        {
            try
            {
                var chats = await GetUserChatsAsync(currentUserId);
                var directChat = chats.FirstOrDefault(c => c.Participants.Any(p => p.UserId == otherUserId));
                if (directChat != null)
                {
                    await SetReadStatusAsync(directChat.Id, currentUserId, 0);
                }
            }
            catch { }
            NotifyNotificationsChanged();
        }

        // --- Service Packages (PH / VG) ---
        /// <summary>
        /// Local cache only - no matching WebAPI endpoint exists (no ServicePackage_db /
        /// controller in WebAPI). PH service packages entered here never reach the server
        /// and won't survive a WebApp process restart or be visible to another user's
        /// session/circuit - unlike PH daily rates (GetPhDailyRatesAsync/
        /// SavePhDailyRatesAsync), which are properly persisted server-side.
        /// </summary>
        public Task<List<ServicePackageModel>> GetPhPackagesAsync() => Task.FromResult(_phPackages.ToList());
        /// <summary>Local cache only - see GetPhPackagesAsync.</summary>
        public Task SavePhPackageAsync(ServicePackageModel pkg)
        {
            if (pkg.Id == 0)
            {
                pkg.Id = _phPackages.Count > 0 ? _phPackages.Max(p => p.Id) + 1 : 1;
                _phPackages.Add(pkg);
            }
            else
            {
                var idx = _phPackages.FindIndex(p => p.Id == pkg.Id);
                if (idx >= 0) _phPackages[idx] = pkg;
            }
            return Task.CompletedTask;
        }
        /// <summary>Local cache only - see GetPhPackagesAsync.</summary>
        public Task DeletePhPackageAsync(int id)
        {
            _phPackages.RemoveAll(p => p.Id == id);
            return Task.CompletedTask;
        }

        // --- Advertising Subscriptions & PH Rates ---
        /// <summary>
        /// GET api/profile/subscription/{userId}/{roleCode}, cached per-instance in
        /// _subscriptions keyed by "{userId}_{roleCode}". Only the owner (or an Admin) may
        /// read it; other profiles' listing status comes from BrowseRolesAsync. If the API
        /// fails and nothing is cached, returns an unpaid/inactive subscription - never an
        /// invented active one.
        /// </summary>
        public async Task<RoleSubscriptionModel> GetRoleSubscriptionAsync(int userId, string roleCode)
        {
            var key = $"{userId}_{roleCode}";
            try
            {
                var apiSub = await _http.GetFromJsonAsync<RoleSubscriptionModel>($"api/profile/subscription/{userId}/{roleCode}");
                if (apiSub != null)
                {
                    _subscriptions[key] = apiSub;
                    return apiSub;
                }
            }
            catch { }

            if (_subscriptions.TryGetValue(key, out var sub)) return sub;

            var publicSettings = await GetPublicSettingsAsync();
            var defaultFee = publicSettings.SubscriptionPriceFor(roleCode);

            var newSub = new RoleSubscriptionModel
            {
                UserId = userId,
                RoleCode = roleCode,
                IsActive = false,
                MonthlyFee = defaultFee,
                TierName = "Standard Advertiser",
                ExpiryDate = DateTime.MinValue,
                LastPaidDate = DateTime.MinValue,
                ActiveDurationMonths = 0,
                LastPaidAmount = null,
                LastPaymentReference = null,
                PaymentStatus = "Unpaid"
            };
            _subscriptions[key] = newSub;
            return newSub;
        }

        /// <summary>
        /// Pauses or resumes the owner's listing (POST api/profile/subscription/{userId}/{roleCode}).
        /// The server only lets an owner change IsActive, and resuming needs a paid period that is
        /// still running. The cache is updated only with what the server stored.
        /// </summary>
        public async Task<(bool Success, string? Error)> SaveRoleSubscriptionAsync(RoleSubscriptionModel sub)
        {
            var key = $"{sub.UserId}_{sub.RoleCode}";
            try
            {
                var res = await _http.PostAsJsonAsync($"api/profile/subscription/{sub.UserId}/{sub.RoleCode}", sub);
                if (res.IsSuccessStatusCode)
                {
                    var saved = await res.Content.ReadFromJsonAsync<RoleSubscriptionModel>(_adminJsonOptions);
                    if (saved != null) _subscriptions[key] = saved;
                    NotifyNotificationsChanged();
                    return (true, null);
                }
                var (message, _) = await ReadPaymentErrorAsync(res, "Kon nie die advertensie opdateer nie.");
                _subscriptions.Remove(key);
                return (false, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SUBSCRIPTION SAVE ERROR] {ex.Message}");
                _subscriptions.Remove(key);
                return (false, "Kon nie met die bediener kommunikeer nie.");
            }
        }

        /// <summary>GET api/profile/phRates/{userId}, cached in _phRates. On failure with no cache, returns an explicitly "not configured" model (null rates) rather than a plausible-looking fake default - see the comment on the fallback below.</summary>
        public async Task<PhDailyRatesModel> GetPhDailyRatesAsync(int userId)
        {
            try
            {
                var apiRates = await _http.GetFromJsonAsync<PhDailyRatesModel>($"api/profile/phRates/{userId}");
                if (apiRates != null)
                {
                    _phRates[userId] = apiRates;
                    return apiRates;
                }
            }
            catch { }

            if (_phRates.TryGetValue(userId, out var rates)) return rates;

            // No cached value and the API call failed - report "not configured" (null
            // rates) rather than inventing a plausible-looking default. The PH must
            // actively set real tariffs via SavePhDailyRatesAsync.
            return new PhDailyRatesModel { UserId = userId, HasDangerousGameAccreditation = true, Currency = "ZAR" };
        }

        /// <summary>Updates the local cache immediately, then best-effort POSTs to api/profile/phRates/{userId}.</summary>
        public async Task SavePhDailyRatesAsync(int userId, decimal standardRate, decimal dangerousGameRate, bool hasDangerousGame)
        {
            var model = new PhDailyRatesModel
            {
                UserId = userId,
                StandardDailyRate = standardRate,
                DangerousGameDailyRate = dangerousGameRate,
                HasDangerousGameAccreditation = hasDangerousGame,
                Currency = "ZAR"
            };
            _phRates[userId] = model;
            NotifyNotificationsChanged();
            try
            {
                await _http.PostAsJsonAsync($"api/profile/phRates/{userId}", model);
            }
            catch { }
        }

        /// <summary>Wire shape of api/VgPackages, distinct from ServicePackageModel's field names (Price vs. DailyRate) - mapped in both directions by GetVgPackagesAsync/SaveVgPackageAsync.</summary>
        private class VgPackageDto
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public string RegionCovered { get; set; } = "";
            public string EquipmentProvided { get; set; } = "";
            public string Description { get; set; } = "";
            public decimal Price { get; set; }
        }

        /// <summary>GET api/VgPackages/getPackages/{userId} - unlike the PH packages above, this one is properly server-persisted.</summary>
        public async Task<List<ServicePackageModel>> GetVgPackagesAsync(int userId)
        {
            try
            {
                var dtos = await _http.GetFromJsonAsync<List<VgPackageDto>>($"api/VgPackages/getPackages/{userId}");
                if (dtos != null)
                {
                    return dtos.Select(d => new ServicePackageModel
                    {
                        Id = d.Id,
                        ProviderUserId = userId,
                        Title = d.Title,
                        RegionCovered = d.RegionCovered,
                        EquipmentProvided = d.EquipmentProvided,
                        Description = d.Description,
                        DailyRate = d.Price,
                        RoleCode = "VG"
                    }).ToList();
                }
            }
            catch { }
            return new List<ServicePackageModel>();
        }

        /// <summary>POST api/VgPackages/create. No local cache to update (unlike most other Save*Async methods in this file) since GetVgPackagesAsync always re-fetches from the server.</summary>
        public async Task<bool> SaveVgPackageAsync(ServicePackageModel pkg)
        {
            try
            {
                var res = await _http.PostAsJsonAsync("api/VgPackages/create", new
                {
                    VgId = pkg.ProviderUserId,
                    Title = pkg.Title,
                    RegionCovered = pkg.RegionCovered,
                    EquipmentProvided = pkg.EquipmentProvided,
                    Description = pkg.Description,
                    Price = pkg.DailyRate
                });
                return res.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SAVE VG PACKAGE API ERROR] {ex.Message}");
                return false;
            }
        }

        // --- Contact Us ---
        /// <summary>POST api/contact.</summary>
        public async Task<bool> SendContactFormAsync(ContactRequestModel contact)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/contact", contact);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONTACT API ERROR] Failed to post contact form to API: {ex.Message}");
                return false;
            }
        }

        // --- User Registration & Verification ---
        /// <summary>POST api/user/register. Unlike most methods here, this deliberately does NOT catch a non-2xx response into a generic message - it always tries to deserialize and surface the server's own Message.</summary>
        public async Task<(bool Success, string Message)> RegisterUserAsync(RegisterApiRequestModel request)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/user/register", request);
                var result = await response.Content.ReadFromJsonAsync<RegisterApiResponseModel>();
                if (response.IsSuccessStatusCode && (result?.Success ?? false))
                {
                    return (true, result?.Message ?? "Rekening suksesvol geskep!");
                }
                return (false, result?.Message ?? "Registrasie het misluk.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[REGISTER API ERROR] {ex.Message}");
                return (false, $"Kon nie met die API-bediener verbind nie: {ex.Message}");
            }
        }

        /// <summary>GET api/user/verifyEmail - completes the email-verification link/OTP flow from Register/VerifyEmail.razor.</summary>
        public async Task<(bool Success, string Message)> VerifyEmailOtpAsync(string email, string token)
        {
            try
            {
                var response = await _http.GetAsync($"api/user/verifyEmail?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");
                var result = await response.Content.ReadFromJsonAsync<RegisterApiResponseModel>();
                if (response.IsSuccessStatusCode && (result?.Success ?? false))
                {
                    return (true, result?.Message ?? "E-pos suksesvol geverifieer!");
                }
                return (false, result?.Message ?? "Ongeldige verifikasie-kode.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VERIFY API ERROR] {ex.Message}");
                return (false, $"Kon nie verifieer nie: {ex.Message}");
            }
        }

        /// <summary>POST api/user/resendVerificationEmail.</summary>
        public async Task<(bool Success, string Message)> ResendVerificationEmailAsync(string email)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/user/resendVerificationEmail", new { Email = email });
                var result = await response.Content.ReadFromJsonAsync<RegisterApiResponseModel>();
                if (response.IsSuccessStatusCode && (result?.Success ?? false))
                {
                    return (true, result?.Message ?? "Nuwe kode gestuur!");
                }
                return (false, result?.Message ?? "Kon nie kode herstuur nie.");
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie herstuur nie: {ex.Message}");
            }
        }

        /// <summary>POST api/user/signin. RequiresVerification distinguishes "wrong credentials" from "correct credentials but email unverified" so Login.razor can route to the verification page instead of just showing an error.</summary>
        public async Task<(bool Success, string Message, bool RequiresVerification, LoginApiResponseModel? Data)> LoginUserAsync(LoginApiRequestModel request)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/user/signin", request);
                var result = await response.Content.ReadFromJsonAsync<LoginApiResponseModel>();

                if (response.IsSuccessStatusCode && (result?.Success ?? false))
                {
                    return (true, result?.Message ?? "Ingeteken", false, result);
                }

                bool requiresVerification = result?.RequiresVerification ?? false;
                return (false, result?.Message ?? "Ongeldige e-pos of wagwoord.", requiresVerification, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGIN API ERROR] {ex.Message}");
                return (false, $"Kon nie met die API verbind nie: {ex.Message}", false, null);
            }
        }

        /// <summary>POST api/user/forgotPassword. Defaults Success to true on a non-2xx with no parseable body - deliberately vague messaging (doesn't confirm/deny whether the email exists) to avoid account enumeration.</summary>
        public async Task<(bool Success, string Message)> ForgotPasswordAsync(string email)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/user/forgotPassword", new ForgotPasswordRequestModel { Email = email });
                var result = await response.Content.ReadFromJsonAsync<RegisterApiResponseModel>();
                return (response.IsSuccessStatusCode && (result?.Success ?? true), result?.Message ?? "As die e-posadres bestaan, is 'n herstelskakel gestuur.");
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie herstelskakel stuur nie: {ex.Message}");
            }
        }

        /// <summary>GET api/user/validateResetToken - checked before showing the "set new password" form, so an expired/reused link fails fast with a clear message instead of at final submit.</summary>
        public async Task<(bool Valid, string Message)> ValidateResetTokenAsync(string email, string token)
        {
            try
            {
                var response = await _http.GetAsync($"api/user/validateResetToken?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");
                var result = await response.Content.ReadFromJsonAsync<RegisterApiResponseModel>();
                return (response.IsSuccessStatusCode && (result?.Success ?? false), result?.Message ?? "Hierdie wagwoord herstelskakel is ongeldig, alreeds gebruik, of het verval.");
            }
            catch (Exception ex)
            {
                return (false, $"Fout tydens skakel verifikasie: {ex.Message}");
            }
        }

        /// <summary>POST api/user/resetPassword - completes the flow started by ForgotPasswordAsync/ValidateResetTokenAsync.</summary>
        public async Task<(bool Success, string Message)> ResetPasswordAsync(string email, string token, string newPassword)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/user/resetPassword", new ResetPasswordRequestModel 
                { 
                    Email = email, 
                    Token = token, 
                    NewPassword = newPassword 
                });
                var result = await response.Content.ReadFromJsonAsync<RegisterApiResponseModel>();
                return (response.IsSuccessStatusCode && (result?.Success ?? false), result?.Message ?? "Kon nie wagwoord opdateer nie.");
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie wagwoord herstel nie: {ex.Message}");
            }
        }

        // --- Profile & Role API Methods ---
        /// <summary>GET api/profile/getProfile/{userId} - the source AuthService.InitializeAsync/SyncUserFromProfile use to (re)hydrate CurrentUser's roles from the DB.</summary>
        public async Task<(bool Success, UserProfileApiResponseModel? Data, string Message)> GetProfileAsync(int userId)
        {
            try
            {
                var response = await _http.GetAsync($"api/profile/getProfile/{userId}");
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<UserProfileApiResponseModel>();
                    return (true, data, "Profiel suksesvol gelaai.");
                }
                return (false, null, "Profiel kon nie gevind word nie.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GET PROFILE API ERROR] {ex.Message}");
                return (false, null, $"API-fout: {ex.Message}");
            }
        }

        /// <summary>PUT api/profile/updateProfile/{userId}.</summary>
        public async Task<(bool Success, string Message)> UpdateProfileAsync(int userId, UpdateProfileApiRequestModel request)
        {
            try
            {
                var response = await _http.PutAsJsonAsync($"api/profile/updateProfile/{userId}", request);
                if (response.IsSuccessStatusCode)
                {
                    return (true, "Profiel suksesvol opgedateer.");
                }
                return (false, await ReadErrorMessageAsync(response, "Opdatering van profiel het misluk."));
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie profiel opdateer nie: {ex.Message}");
            }
        }

        /// <summary>POST api/profile/addRole/{userId} - adds a base role grant before the role-specific credential registration step (RegisterPjRoleAsync etc). Defaults UserRoleId to 1 if the response doesn't carry a parseable id - callers should treat that as a signal to re-fetch the real id rather than trust it.</summary>
        public async Task<(bool Success, int UserRoleId, string Message)> AddRoleAsync(int userId, string roleCode)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"api/profile/addRole/{userId}", new AddRoleApiRequestModel { RoleCode = roleCode });
                if (response.IsSuccessStatusCode)
                {
                    int roleId = 1;
                    try
                    {
                        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                        if (json.TryGetProperty("userRoleId", out var prop) || json.TryGetProperty("UserRoleId", out prop))
                        {
                            roleId = prop.GetInt32();
                        }
                    }
                    catch { }
                    return (true, roleId, $"Rol '{roleCode}' suksesvol bygevoeg.");
                }
                var err = await response.Content.ReadAsStringAsync();
                return (false, 0, string.IsNullOrWhiteSpace(err) ? "Rol kon nie bygevoeg word nie." : err);
            }
            catch (Exception ex)
            {
                return (false, 0, $"Kon nie rol byvoeg nie: {ex.Message}");
            }
        }

        /// <summary>DELETE api/profile/deleteRoleByCode/{userId}/{roleCode} - server cascades to that role's dependent records (documents, packages, etc).</summary>
        public async Task<(bool Success, string Message)> DeleteRoleAsync(int userId, string roleCode)
        {
            try
            {
                var response = await _http.DeleteAsync($"api/profile/deleteRoleByCode/{userId}/{roleCode}");
                if (response.IsSuccessStatusCode)
                {
                    return (true, $"Rol '{roleCode}' en alle verwante afhanklikhede is suksesvol verwyder.");
                }
                var err = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(err) ? "Rol kon nie verwyder word nie." : err);
            }
            catch (Exception ex)
            {
                return (false, $"Fout met verwydering van rol: {ex.Message}");
            }
        }

        /// <summary>DELETE api/profile/deleteUser/{userId} - full account deletion, server cascades to all of that user's records.</summary>
        public async Task<(bool Success, string Message)> DeleteUserAsync(int userId)
        {
            try
            {
                var response = await _http.DeleteAsync($"api/profile/deleteUser/{userId}");
                if (response.IsSuccessStatusCode)
                {
                    return (true, "Gebruikersrekening en alle verwante rekords is suksesvol geskrap.");
                }
                var err = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(err) ? "Gebruikersrekening kon nie geskrap word nie." : err);
            }
            catch (Exception ex)
            {
                return (false, $"Fout met skrapping van gebruiker: {ex.Message}");
            }
        }

        /// <summary>Shared error-extraction helper for the RegisterXRoleAsync methods below - tries "message"/"Message" from the response JSON, falling back to fallbackMessage.</summary>
        private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, string fallbackMessage)
        {
            try
            {
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("message", out var msgProp) || doc.RootElement.TryGetProperty("Message", out msgProp))
                    {
                        var str = msgProp.GetString();
                        if (!string.IsNullOrWhiteSpace(str)) return str;
                    }
                }
            }
            catch { }
            return fallbackMessage;
        }

        // The five RegisterXRoleAsync methods below all follow the same shape: POST the
        // role-specific credential payload to api/profile/register{Role}/{userId}
        // (called after AddRoleAsync has already granted the base role), returning a
        // canned success message or the server's own error text via ReadErrorMessageAsync.
        /// <summary>POST api/profile/registerHunter/{userId}.</summary>
        public async Task<(bool Success, string Message)> RegisterHunterRoleAsync(int userId, RegisterHunterApiRequestModel request)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"api/profile/registerHunter/{userId}", request);
                if (response.IsSuccessStatusCode)
                    return (true, "Jagter rol suksesvol geaktiveer.");
                var msg = await ReadErrorMessageAsync(response, "Aktivering van Jagter-rol het misluk.");
                return (false, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>POST api/profile/registerFarmer/{userId}.</summary>
        public async Task<(bool Success, string Message)> RegisterFarmerRoleAsync(int userId, RegisterBoerApiRequestModel request)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"api/profile/registerFarmer/{userId}", request);
                if (response.IsSuccessStatusCode)
                    return (true, "Boer rol suksesvol geaktiveer.");
                var msg = await ReadErrorMessageAsync(response, "Aktivering van Boer-rol het misluk.");
                return (false, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>POST api/profile/registerPj/{userId}.</summary>
        public async Task<(bool Success, string Message)> RegisterPjRoleAsync(int userId, RegisterPjApiRequestModel request)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"api/profile/registerPj/{userId}", request);
                if (response.IsSuccessStatusCode)
                    return (true, "PJ rol suksesvol geaktiveer.");
                var msg = await ReadErrorMessageAsync(response, "Aktivering van PJ-rol het misluk.");
                return (false, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>POST api/profile/registerPh/{userId}.</summary>
        public async Task<(bool Success, string Message)> RegisterPhRoleAsync(int userId, RegisterPhApiRequestModel request)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"api/profile/registerPh/{userId}", request);
                if (response.IsSuccessStatusCode)
                    return (true, "PH rol suksesvol geaktiveer.");
                var msg = await ReadErrorMessageAsync(response, "Aktivering van PH-rol het misluk.");
                return (false, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>POST api/profile/registerVg/{userId}.</summary>
        public async Task<(bool Success, string Message)> RegisterVgRoleAsync(int userId, RegisterVgApiRequestModel request)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"api/profile/registerVg/{userId}", request);
                if (response.IsSuccessStatusCode)
                    return (true, "VG rol suksesvol geaktiveer.");
                var msg = await ReadErrorMessageAsync(response, "Aktivering van VG-rol het misluk.");
                return (false, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }

        /// <summary>POST api/profile/registerAp/{userId} - AP (advertiser) has no role-specific credential form, so this posts an empty body.</summary>
        public async Task<(bool Success, string Message)> RegisterApRoleAsync(int userId)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"api/profile/registerAp/{userId}", new { });
                if (response.IsSuccessStatusCode)
                    return (true, "AP rol suksesvol geaktiveer.");
                var msg = await ReadErrorMessageAsync(response, "Aktivering van AP-rol het misluk.");
                return (false, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Fout: {ex.Message}");
            }
        }
        
        /// <summary>GET api/bookings/getVgBookings?id={vgUserId}.</summary>
        public async Task<List<BookingModel>> GetBookingsForVgAsync(int vgUserId)
        {
            try
            {
                var apiRes = await _http.GetFromJsonAsync<List<BookingResponse>>($"api/bookings/getVgBookings?id={vgUserId}");
                if (apiRes != null && apiRes.Count > 0)
                {
                    var list = new List<BookingModel>();
                    foreach (var b in apiRes)
                    {
                        list.Add(new BookingModel
                        {
                            Id = b.Id,
                            StartDate = b.StartDate,
                            EndDate = b.EndDate,
                            Status = b.Status,
                            PackageTitle = b.PakketName,
                            HunterName = b.UserName,
                            AssignedVgUserId = vgUserId,
                            VgAssignmentStatus = b.VgAssignmentStatus,
                            VgDeclineReason = b.VgDeclineReason,
                            PjUserId = b.PjUserId ?? 0,
                            PjName = b.PjName ?? ""
                        });
                    }
                    return list;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API ERROR] GetBookingsForVgAsync failed: {ex.Message}");
            }
            
            return new List<BookingModel>();
        }

        // ==========================================
        // --- SYSTEM ADMINISTRATOR MODULE ENGINE ---
        // Audit logs, feature flags, role verifications, bookings, user status and
        // flagged-message moderation are now all backed by real SQL tables via
        // api/admin/* (see db/AddAdminEnterpriseTables.sql,
        // db/AddUserAccountStatusAndChatModeration.sql and
        // WebAPI.Controllers.AdminController) - no more in-memory mock lists here.
        // ==========================================
        /// <summary>Seed/fallback admin user directory - real data always comes from GetMasterUsersAsync (api/user/getAllUsers) when reachable and is merged into this list, which only backs the offline/demo case.</summary>
        private readonly List<UserModel> _masterUsers = new()
        {
            new UserModel { UserId = 1, Email = "jagter@mikpunt.co.za", FirstName = "Johan", LastName = "Van der Merwe", Phone = "+27 82 555 0192", IdPassportNum = "8804125019083", IsVerified = true, AccountStatus = "Active", CreatedAt = DateTime.UtcNow.AddMonths(-6), Roles = new List<UserRoleModel> { new UserRoleModel { Id = 1, UserId = 1, RoleCode = "Jagter", RoleName = "Jagter / Hunter", Status = "Approved" }, new UserRoleModel { Id = 7, UserId = 1, RoleCode = "Admin", RoleName = "System Administrator", Status = "Approved" } } },
            new UserModel { UserId = 2, Email = "kobus@gemsbokpan.co.za", FirstName = "Kobus", LastName = "Pretorius", Phone = "+27 83 444 1122", IdPassportNum = "7501195029081", IsVerified = true, AccountStatus = "Active", CreatedAt = DateTime.UtcNow.AddMonths(-5), Roles = new List<UserRoleModel> { new UserRoleModel { Id = 2, UserId = 2, RoleCode = "Boer", RoleName = "Boer / Farmer", Status = "Approved" } } },
            new UserModel { UserId = 3, Email = "stephanus@kareeberg.co.za", FirstName = "Stephanus", LastName = "Swart", Phone = "+27 84 333 9988", IdPassportNum = "8209045119084", IsVerified = true, AccountStatus = "Active", CreatedAt = DateTime.UtcNow.AddMonths(-4), Roles = new List<UserRoleModel> { new UserRoleModel { Id = 3, UserId = 3, RoleCode = "PJ", RoleName = "Pakket Uitruster", Status = "Approved" } } },
            new UserModel { UserId = 4, Email = "maria@kudubosveld.co.za", FirstName = "Maria", LastName = "Joubert", Phone = "+27 82 111 8877", IdPassportNum = "8911224019088", IsVerified = true, AccountStatus = "Active", CreatedAt = DateTime.UtcNow.AddMonths(-3), Roles = new List<UserRoleModel> { new UserRoleModel { Id = 4, UserId = 4, RoleCode = "AP", RoleName = "Adverteerder", Status = "Approved" } } },
            new UserModel { UserId = 5, Email = "pieter.botha@ph-safaris.co.za", FirstName = "Pieter", LastName = "Botha", Phone = "+27 83 777 6655", IdPassportNum = "7903155099082", IsVerified = false, AccountStatus = "Active", CreatedAt = DateTime.UtcNow.AddMonths(-2), Roles = new List<UserRoleModel> { new UserRoleModel { Id = 5, UserId = 5, RoleCode = "PH", RoleName = "Professionele Jagter", Status = "Pending" } } },
            new UserModel { UserId = 6, Email = "hendrik@dronevision.co.za", FirstName = "Hendrik", LastName = "Van Zyl", Phone = "+27 82 999 4433", IdPassportNum = "9105105088085", IsVerified = false, AccountStatus = "Active", CreatedAt = DateTime.UtcNow.AddMonths(-1), Roles = new List<UserRoleModel> { new UserRoleModel { Id = 6, UserId = 6, RoleCode = "VG", RoleName = "Videograaf", Status = "Pending" } } }
        };

        /// <summary>Used across every admin API deserialization in this section since AdminController's JSON responses aren't guaranteed to match .NET's default camelCase exactly in every case.</summary>
        private static readonly JsonSerializerOptions _adminJsonOptions = new() { PropertyNameCaseInsensitive = true };

        // --- Audit Logs ---
        /// <summary>GET api/admin/audit-logs ([Authorize(Roles="Admin")]), with optional admin/action/date-range filters. The server compares auditTimestamp &lt;= endDate, so a date-only endDate (midnight) is bumped forward by one day here - otherwise entries later on the selected end day would be excluded.</summary>
        public async Task<List<AdminAuditLogModel>> GetAdminAuditLogsAsync(string? adminFilter = null, string? actionFilter = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                var qs = new List<string>();
                if (!string.IsNullOrWhiteSpace(adminFilter)) qs.Add($"admin={Uri.EscapeDataString(adminFilter)}");
                if (!string.IsNullOrWhiteSpace(actionFilter)) qs.Add($"action={Uri.EscapeDataString(actionFilter)}");
                if (startDate.HasValue) qs.Add($"startDate={Uri.EscapeDataString(startDate.Value.ToString("o"))}");
                if (endDate.HasValue) qs.Add($"endDate={Uri.EscapeDataString(endDate.Value.AddDays(1).ToString("o"))}");
                string url = "api/admin/audit-logs" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

                var httpReq = new HttpRequestMessage(HttpMethod.Get, url);
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var logs = await response.Content.ReadFromJsonAsync<List<AdminAuditLogModel>>(_adminJsonOptions);
                    return logs ?? new List<AdminAuditLogModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADMIN AUDIT LOG API ERROR] {ex.Message}");
            }
            return new List<AdminAuditLogModel>();
        }

        /// <summary>GET api/admin/audit-logs/export ([Authorize(Roles="Admin")]) - same filters as GetAdminAuditLogsAsync, returns the raw CSV bytes (up to 10,000 rows server-side) for a browser download.</summary>
        public async Task<byte[]?> ExportAuditLogsCsvAsync(string? adminFilter = null, string? actionFilter = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                var qs = new List<string>();
                if (!string.IsNullOrWhiteSpace(adminFilter)) qs.Add($"admin={Uri.EscapeDataString(adminFilter)}");
                if (!string.IsNullOrWhiteSpace(actionFilter)) qs.Add($"action={Uri.EscapeDataString(actionFilter)}");
                if (startDate.HasValue) qs.Add($"startDate={Uri.EscapeDataString(startDate.Value.ToString("o"))}");
                if (endDate.HasValue) qs.Add($"endDate={Uri.EscapeDataString(endDate.Value.AddDays(1).ToString("o"))}");
                string url = "api/admin/audit-logs/export" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

                var httpReq = new HttpRequestMessage(HttpMethod.Get, url);
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADMIN AUDIT LOG EXPORT ERROR] {ex.Message}");
            }
            return null;
        }

        /// <summary>POST api/admin/audit-logs ([Authorize(Roles="Admin")]) - writes one audit entry; called by every other admin-action method in this section after the action itself succeeds.</summary>
        public async Task AddAdminAuditLogAsync(int adminId, string adminName, string action, string targetType, string targetId, string reason)
        {
            try
            {
                var body = new { AdminUserId = adminId, AdminUserName = adminName, Action = action, TargetEntityType = targetType, TargetEntityId = targetId, Reason = reason };
                var httpReq = new HttpRequestMessage(HttpMethod.Post, "api/admin/audit-logs") { Content = JsonContent.Create(body) };
                AttachAuthHeader(httpReq);
                await _http.SendAsync(httpReq);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADMIN AUDIT LOG WRITE ERROR] {ex.Message}");
            }
        }

        // --- Master User Directory & Actions ---
        /// <summary>
        /// GET api/user/getAllUsers ([Authorize(Roles="Admin")]) - merges results into
        /// _masterUsers (update-in-place by UserId, seed rows left untouched if the API
        /// doesn't return them) rather than replacing the list, then returns the client-
        /// side filtered/searched view. search/roleFilter/statusFilter are applied locally
        /// here, not sent to the server - the server always returns the full user list.
        /// </summary>
        public async Task<List<UserModel>> GetMasterUsersAsync(string? search = null, string? roleFilter = null, string? statusFilter = null)
        {
            try
            {
                // getAllUsers is [Authorize(Roles = "Admin")] - it returns every user's
                // email/phone/verification status, so it requires the caller's token.
                var httpReq = new HttpRequestMessage(HttpMethod.Get, "api/user/getAllUsers");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[GET MASTER USERS API ERROR] HTTP {(int)response.StatusCode} {response.StatusCode} from {response.RequestMessage?.RequestUri}. HasToken={!string.IsNullOrWhiteSpace(_auth.Token)}. Body: {body}");
                }
                var apiRes = response.IsSuccessStatusCode
                    ? await response.Content.ReadFromJsonAsync<ApiUserResponse>()
                    : null;
                if (apiRes != null && apiRes.Success && apiRes.Data != null && apiRes.Data.Count > 0)
                {
                    foreach (var u in apiRes.Data)
                    {
                        var existing = _masterUsers.FirstOrDefault(m => m.UserId == u.UserId);
                        if (existing == null)
                        {
                            var newUser = new UserModel
                            {
                                UserId = u.UserId,
                                Email = u.Email ?? "",
                                FirstName = u.Name ?? "",
                                LastName = u.Surname ?? "",
                                Phone = u.Phone ?? "",
                                IsVerified = u.IsEmailVerified,
                                AccountStatus = u.IsSuspended ? "Suspended" : "Active",   // uIsSuspended is what sign-in enforces
                                StatusReason = u.StatusReason,
                                CreatedAt = u.RegistrationDate != default ? u.RegistrationDate : DateTime.UtcNow,
                                Roles = u.Roles?.Select(r => new UserRoleModel
                                {
                                    Id = r.UserRoleId,
                                    UserId = r.UserId,
                                    RoleCode = r.RoleCode ?? "Jagter",
                                    RoleName = r.RoleName ?? r.RoleCode ?? "Jagter",
                                    Status = "Approved"
                                }).ToList() ?? new List<UserRoleModel>()
                            };
                            _masterUsers.Add(newUser);
                        }
                        else
                        {
                            existing.Email = u.Email ?? existing.Email;
                            if (!string.IsNullOrWhiteSpace(u.Name)) existing.FirstName = u.Name;
                            if (!string.IsNullOrWhiteSpace(u.Surname)) existing.LastName = u.Surname;
                            if (!string.IsNullOrWhiteSpace(u.Phone)) existing.Phone = u.Phone;
                            existing.IsVerified = u.IsEmailVerified;
                            existing.AccountStatus = u.IsSuspended ? "Suspended" : "Active";
                            existing.StatusReason = u.StatusReason ?? existing.StatusReason;
                            if (u.Roles != null && u.Roles.Count > 0)
                            {
                                existing.Roles = u.Roles.Select(r => new UserRoleModel
                                {
                                    Id = r.UserRoleId,
                                    UserId = r.UserId,
                                    RoleCode = r.RoleCode ?? "Jagter",
                                    RoleName = r.RoleName ?? r.RoleCode ?? "Jagter",
                                    Status = "Approved"
                                }).ToList();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GET MASTER USERS API ERROR] {ex.Message}");
            }

            var query = _masterUsers.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim();
                query = query.Where(u => u.FullName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                                         u.Email.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                                         u.Phone.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                                         u.IdPassportNum.Contains(s, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(roleFilter))
            {
                query = query.Where(u => u.Roles.Any(r => AuthService.SameRole(r.RoleCode, roleFilter)));
            }
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                query = query.Where(u => u.AccountStatus.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));
            }

            return query.OrderBy(u => u.UserId).ToList();
        }

        /// <summary>POST api/admin/users/{targetUserId}/status ([Authorize(Roles="Admin")]) - approve/suspend/ban a user. Requires a non-blank reason (rejected client-side before the call is even made, matching the server's own audit-trail requirement).</summary>
        public async Task<(bool Success, string Message)> UpdateUserStatusAsync(int adminId, string adminName, int targetUserId, string newStatus, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return (false, " 'n Reden is verpligtend vir enige admin statuswysiging.");
            }

            try
            {
                var body = new { Status = newStatus, Reason = reason };
                var httpReq = new HttpRequestMessage(HttpMethod.Post, $"api/admin/users/{targetUserId}/status") { Content = JsonContent.Create(body) };
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);

                if (!response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    return (false, string.IsNullOrWhiteSpace(content) ? $"Fout: {response.StatusCode}" : content);
                }

                var user = _masterUsers.FirstOrDefault(u => u.UserId == targetUserId);
                if (user != null)
                {
                    user.AccountStatus = newStatus;
                    user.StatusReason = reason;
                }

                return (true, $"Gebruiker {user?.FullName ?? $"#{targetUserId}"} se status is suksesvol opgedateer na '{newStatus}'.");
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        // --- Role Verification Queue ---
        /// <summary>GET api/admin/role-verifications ([Authorize(Roles="Admin")]) - the admin's PH/Boer/VG role-application approval queue (distinct from the per-document verification queue - see GetPendingDocumentVerificationsAsync).</summary>
        public async Task<List<RoleVerificationRequestModel>> GetRoleVerificationRequestsAsync()
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, "api/admin/role-verifications");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<RoleVerificationRequestModel>>(_adminJsonOptions);
                    return (list ?? new List<RoleVerificationRequestModel>()).OrderByDescending(r => r.SubmittedAt).ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ROLE VERIFICATIONS API ERROR] {ex.Message}");
            }
            return new List<RoleVerificationRequestModel>();
        }

        /// <summary>Shape of api/admin/role-verifications/{id}/decision's response.</summary>
        private class RoleVerificationDecisionResponse
        {
            public bool Success { get; set; }
            public RoleVerificationRequestModel? Verification { get; set; }
        }

        /// <summary>POST api/admin/role-verifications/{requestId}/decision ([Authorize(Roles="Admin")]) - approve/reject a role application, with a mandatory reason. On success, patches the corresponding role's status (or adds it, if approved and not already present) directly in the _masterUsers cache so the Users tab reflects the decision without a full reload.</summary>
        public async Task<(bool Success, string Message)> ProcessRoleVerificationAsync(int adminId, string adminName, int requestId, bool approve, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return (false, " 'n Reden is verpligtend vir enige verifikasiebepaling.");
            }

            try
            {
                var body = new { Approve = approve, Reason = reason };
                var httpReq = new HttpRequestMessage(HttpMethod.Post, $"api/admin/role-verifications/{requestId}/decision") { Content = JsonContent.Create(body) };
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                string content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return (false, string.IsNullOrWhiteSpace(content) ? $"Fout: {response.StatusCode}" : content);
                }

                var result = JsonSerializer.Deserialize<RoleVerificationDecisionResponse>(content, _adminJsonOptions);
                var verification = result?.Verification;

                // Keep the locally-cached master user list in sync so the Users tab
                // reflects the decision immediately without a full reload round-trip.
                if (verification != null)
                {
                    var user = _masterUsers.FirstOrDefault(u => u.UserId == verification.UserId);
                    if (user != null)
                    {
                        var role = user.Roles.FirstOrDefault(r => AuthService.SameRole(r.RoleCode, verification.RequestedRoleCode) || r.RoleCode.Equals(verification.RequestedRoleCode, StringComparison.OrdinalIgnoreCase));
                        if (role != null)
                        {
                            role.Status = approve ? "Approved" : "Rejected";
                        }
                        else if (approve)
                        {
                            user.Roles.Add(new UserRoleModel
                            {
                                Id = user.Roles.Count + 1,
                                UserId = user.UserId,
                                RoleCode = verification.RequestedRoleCode,
                                RoleName = verification.RequestedRoleName,
                                Status = "Approved"
                            });
                        }
                        if (approve) user.IsVerified = true;
                    }
                }

                string userName = verification?.UserName ?? "gebruiker";
                string roleCode = verification?.RequestedRoleCode ?? "";
                return (true, $"Rolverifikasie vir {userName} ({roleCode}) is suksesvol {(approve ? "goedgekeur" : "van hand gewys")}.");
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        // --- Booking Dispute & Correction Tool ---
        /// <summary>POST api/admin/bookings/{req.BookingId}/correct ([Authorize(Roles="Admin")]) - manually overrides a disputed booking's price/guest counts/status, with a mandatory reason.</summary>
        public async Task<(bool Success, string Message)> CorrectBookingDisputeAsync(int adminId, string adminName, AdminCorrectBookingRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
            {
                return (false, " 'n Reden vir die regstelling is verpligtend.");
            }

            try
            {
                var body = new
                {
                    CorrectedTotalPrice = req.CorrectedTotalPrice,
                    CorrectedAccommodationTotal = req.CorrectedAccommodationTotal,
                    CorrectedHunterCount = req.CorrectedHunterCount,
                    CorrectedObserverCount = req.CorrectedObserverCount,
                    Status = req.Status,
                    Reason = req.Reason
                };
                var httpReq = new HttpRequestMessage(HttpMethod.Post, $"api/admin/bookings/{req.BookingId}/correct") { Content = JsonContent.Create(body) };
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);

                if (!response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    return (false, string.IsNullOrWhiteSpace(content) ? $"Fout: {response.StatusCode}" : content);
                }

                return (true, $"Bespreking #{req.BookingId} is suksesvol deur Admin reggestel.");
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        // --- Feature Flags ---
        /// <summary>GET api/admin/feature-flags ([Authorize(Roles="Admin")]).</summary>
        public async Task<List<FeatureFlagModel>> GetFeatureFlagsAsync()
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, "api/admin/feature-flags");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var flags = await response.Content.ReadFromJsonAsync<List<FeatureFlagModel>>(_adminJsonOptions);
                    return flags ?? new List<FeatureFlagModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FEATURE FLAGS API ERROR] {ex.Message}");
            }
            return new List<FeatureFlagModel>();
        }

        /// <summary>POST api/admin/feature-flags/toggle ([Authorize(Roles="Admin")]) via the shared PostAdminActionAsync helper. Requires a non-blank reason.</summary>
        public Task<(bool Success, string Message)> ToggleFeatureFlagAsync(int adminId, string adminName, string flagKey, bool isEnabled, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Task.FromResult((false, "'n Reden is verpligtend vir enige stelselkenmerkwysiging."));
            }

            return PostAdminActionAsync("api/admin/feature-flags/toggle", new { Key = flagKey, IsEnabled = isEnabled, Reason = reason });
        }

        // --- System Settings (pricing + operational constants) ---

        /// <summary>Maps a role code to its raw system-setting key, used to pick the right rows out of api/settings/public's flat key/value list in GetPublicSettingsAsync.</summary>
        private static readonly Dictionary<string, string> _subscriptionSettingKeyByRole = new()
        {
            ["PH"] = "SubscriptionPrice_PH",
            ["VG"] = "SubscriptionPrice_VG",
            ["Boer"] = "SubscriptionPrice_Boer",
            ["PJ"] = "SubscriptionPrice_PJ"
        };

        /// <summary>
        /// Business/pricing settings any visitor can see (ad price, subscription
        /// tiers, deposit %) - unauthenticated GET, no Bearer header needed.
        /// </summary>
        public async Task<PublicSettingsModel> GetPublicSettingsAsync()
        {
            var result = new PublicSettingsModel();
            try
            {
                var response = await _http.GetAsync("api/settings/public");
                if (response.IsSuccessStatusCode)
                {
                    var rows = await response.Content.ReadFromJsonAsync<List<SystemSettingModel>>(_adminJsonOptions) ?? new();
                    foreach (var row in rows)
                    {
                        if (!decimal.TryParse(row.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value)) continue;

                        if (row.Key == "AdMonthlyPrice") result.AdMonthlyPrice = value;
                        else if (row.Key == "BookingDepositPercentage") result.DepositPercentage = value;
                        else if (row.Key == "UsdToZarRate") result.UsdToZarRate = value;
                        else if (row.Key == "PackagePricesInZar")
                        {
                            result.PackagePricesInZar = value > 0;
                            Money.PackageCurrency = value > 0 ? "ZAR" : "USD";
                        }
                        else
                        {
                            var roleEntry = _subscriptionSettingKeyByRole.FirstOrDefault(kv => kv.Value == row.Key);
                            if (roleEntry.Key != null) result.SubscriptionPrices[roleEntry.Key] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PUBLIC SETTINGS API ERROR] {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Real post-trip rating stats for a profile/dashboard card. Pass `role` to
        /// scope to ratings the user received in one capacity only (e.g. "PH"), or
        /// omit it for their overall rating across every role.
        /// </summary>
        public async Task<RatingProfileStatsModel> GetRatingProfileStatsAsync(int userId, string? role = null)
        {
            var result = new RatingProfileStatsModel();
            try
            {
                string url = $"api/rating/profile-stats/{userId}" + (string.IsNullOrWhiteSpace(role) ? "" : $"?role={Uri.EscapeDataString(role)}");
                var response = await _http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    result = await response.Content.ReadFromJsonAsync<RatingProfileStatsModel>(_adminJsonOptions) ?? result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RATING PROFILE STATS API ERROR] {ex.Message}");
            }
            return result;
        }

        /// <summary>Most recent ratings received by a user, with comments, for a reviews list.</summary>
        public async Task<List<RecentRatingModel>> GetRecentRatingsAsync(int userId, int take = 5, string? role = null)
        {
            try
            {
                string url = $"api/rating/recent/{userId}?take={take}" + (string.IsNullOrWhiteSpace(role) ? "" : $"&role={Uri.EscapeDataString(role)}");
                var response = await _http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<RecentRatingModel>>(_adminJsonOptions) ?? new();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RECENT RATINGS API ERROR] {ex.Message}");
            }
            return new();
        }

        /// <summary>GET api/admin/settings ([Authorize(Roles="Admin")]) - raw key/value rows behind the AdminDashboard's settings tab (see also GetPublicSettingsAsync for the subset exposed to anonymous visitors).</summary>
        public async Task<List<SystemSettingModel>> GetSystemSettingsAsync()
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, "api/admin/settings");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var rows = await response.Content.ReadFromJsonAsync<List<SystemSettingModel>>(_adminJsonOptions);
                    return rows ?? new List<SystemSettingModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SYSTEM SETTINGS API ERROR] {ex.Message}");
            }
            return new List<SystemSettingModel>();
        }

        /// <summary>POST api/admin/settings/update ([Authorize(Roles="Admin")]) via PostAdminActionAsync. Requires a non-blank reason.</summary>
        public Task<(bool Success, string Message)> UpdateSystemSettingAsync(string key, string value, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Task.FromResult((false, "'n Reden is verpligtend vir enige instellingwysiging."));
            }

            return PostAdminActionAsync("api/admin/settings/update", new { Key = key, Value = value, Reason = reason });
        }

        // --- System Health & Moderation ---
        /// <summary>GET api/admin/health ([Authorize(Roles="Admin")], backed by SqlServerHealthCheck). Falls back to a locally-computed "probably healthy" model (active session count from _masterUsers) if the call fails - see SystemHealthModel's own doc comment for the caveat this implies.</summary>
        public async Task<SystemHealthModel> GetSystemHealthAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool dbConnected = true;
            string dbStatus = "Operational (SQL Server Pool Healthy)";
            int activeSessions = _masterUsers.Count(u => u.AccountStatus == "Active");

            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, "api/admin/health");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                sw.Stop();
                if (response.IsSuccessStatusCode)
                {
                    var resData = await response.Content.ReadFromJsonAsync<SystemHealthApiResponse>();
                    if (resData != null)
                    {
                        return new SystemHealthModel
                        {
                            Reachable = true,
                            DbConnected = resData.DbConnected,
                            DbStatus = resData.DbStatus ?? dbStatus,
                            DbResponseTimeMs = resData.ResponseTimeMs,
                            ApiUptimeHealthy = resData.ApiUptimeHealthy,
                            ResponseTimeMs = Math.Max(1, (int)sw.ElapsedMilliseconds),
                            LastHeartbeat = DateTime.UtcNow,
                            ActiveSessions = resData.ActiveSessions,
                            AppSessions = resData.AppSessions,
                            MobileApiHealthy = resData.MobileApiHealthy,
                            MobileApiStatus = resData.MobileApiStatus ?? "",
                            MobileApiResponseMs = resData.MobileApiResponseMs,
                            MaintenanceMode = resData.MaintenanceMode,
                            RegistrationsOpen = resData.RegistrationsOpen,
                            PayFastSandbox = resData.PayFastSandbox,
                            Environment = resData.Environment ?? ""
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"[SYSTEM HEALTH API ERROR] {ex.Message}");
            }

            // The WebAPI didn't answer: say so instead of showing made-up healthy numbers.
            return new SystemHealthModel
            {
                Reachable = false,
                DbConnected = false,
                DbStatus = "Onbekend - die WebAPI het nie geantwoord nie",
                ApiUptimeHealthy = false,
                ResponseTimeMs = Math.Max(1, (int)sw.ElapsedMilliseconds),
                LastHeartbeat = DateTime.UtcNow,
                ActiveSessions = -1,
                AppSessions = -1,
                MobileApiStatus = "Onbekend"
            };
        }

        /// <summary>GET api/admin/flagged-messages ([Authorize(Roles="Admin")]) - chat messages the moderation-keyword filter (see ModerationKeywords) caught.</summary>
        public async Task<List<FlaggedChatMessageModel>> GetFlaggedChatMessagesAsync()
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, "api/admin/flagged-messages");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<FlaggedChatMessageModel>>(_adminJsonOptions);
                    return list ?? new List<FlaggedChatMessageModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FLAGGED MESSAGES API ERROR] {ex.Message}");
            }
            return new List<FlaggedChatMessageModel>();
        }

        /// <summary>POST api/admin/flagged-messages/{messageId}/moderate ([Authorize(Roles="Admin")]) via PostAdminActionAsync.</summary>
        public async Task<(bool Success, string Message)> ProcessFlaggedMessageAsync(int adminId, string adminName, int messageId, string action, string reason)
        {
            var (success, message) = await PostAdminActionAsync($"api/admin/flagged-messages/{messageId}/moderate", new { Action = action, Reason = reason });
            return success ? (true, $"Moderering op boodskap #{messageId} afgehandel.") : (false, message);
        }

        /// <summary>GET api/admin/flagged-ratings ([Authorize(Roles="Admin")]) - rating comments the moderation-keyword filter caught.</summary>
        public async Task<List<FlaggedRatingCommentModel>> GetFlaggedRatingCommentsAsync()
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, "api/admin/flagged-ratings");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<FlaggedRatingCommentModel>>(_adminJsonOptions);
                    return list ?? new List<FlaggedRatingCommentModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FLAGGED RATINGS API ERROR] {ex.Message}");
            }
            return new List<FlaggedRatingCommentModel>();
        }

        /// <summary>POST api/admin/flagged-ratings/{flagId}/moderate ([Authorize(Roles="Admin")]) via PostAdminActionAsync.</summary>
        public async Task<(bool Success, string Message)> ProcessFlaggedRatingCommentAsync(int flagId, string action, string reason)
        {
            var (success, message) = await PostAdminActionAsync($"api/admin/flagged-ratings/{flagId}/moderate", new { Action = action, Reason = reason });
            return success ? (true, $"Moderering op gradering-kommentaar #{flagId} afgehandel.") : (false, message);
        }

        /// <summary>Ratee disputing a rating they received - requires the caller's own bearer token.</summary>
        public async Task<(bool Success, string Message)> SubmitRatingDisputeAsync(int ratingId, string reason)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Post, $"api/rating/{ratingId}/dispute")
                {
                    Content = JsonContent.Create(new { Reason = reason })
                };
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode) return (true, "Dispuut suksesvol ingedien. 'n Admin sal dit hersien.");
                string body = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(body) ? $"Fout: {response.StatusCode}" : body);
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        /// <summary>GET api/admin/rating-disputes ([Authorize(Roles="Admin")]), optionally filtered by status.</summary>
        public async Task<List<RatingDisputeModel>> GetRatingDisputesAsync(string? status = null)
        {
            try
            {
                string url = "api/admin/rating-disputes" + (string.IsNullOrWhiteSpace(status) ? "" : $"?status={Uri.EscapeDataString(status)}");
                var httpReq = new HttpRequestMessage(HttpMethod.Get, url);
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<RatingDisputeModel>>(_adminJsonOptions);
                    return list ?? new List<RatingDisputeModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RATING DISPUTES API ERROR] {ex.Message}");
            }
            return new List<RatingDisputeModel>();
        }

        /// <summary>POST api/admin/rating-disputes/{disputeId}/resolve ([Authorize(Roles="Admin")]) via PostAdminActionAsync - status is typically "Upheld" or "Denied".</summary>
        public async Task<(bool Success, string Message)> ResolveRatingDisputeAsync(int disputeId, string status, string? notes)
        {
            var (success, message) = await PostAdminActionAsync($"api/admin/rating-disputes/{disputeId}/resolve", new { Status = status, Notes = notes });
            return success ? (true, $"Dispuut #{disputeId} {(status == "Upheld" ? "gehandhaaf" : "afgewys")}.") : (false, message);
        }

        /// <summary>GET api/admin/ratings/{ratingId}/history ([Authorize(Roles="Admin")]) - edit history for one rating, shown alongside its dispute (if any).</summary>
        public async Task<List<RatingHistoryEntryModel>> GetRatingHistoryAsync(int ratingId)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, $"api/admin/ratings/{ratingId}/history");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<RatingHistoryEntryModel>>(_adminJsonOptions);
                    return list ?? new List<RatingHistoryEntryModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RATING HISTORY API ERROR] {ex.Message}");
            }
            return new List<RatingHistoryEntryModel>();
        }

        // --- Document management (PJ/PH/VG/Farmer required credentials) ---

        /// <summary>GET api/documents/requirements/{roleCode} - which document types are required for a role, independent of any specific user's upload state (public, no auth needed).</summary>
        public async Task<List<DocumentRequirementModel>> GetDocumentRequirementsAsync(string roleCode)
        {
            try
            {
                var response = await _http.GetAsync($"api/documents/requirements/{Uri.EscapeDataString(roleCode)}");
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<DocumentRequirementModel>>(_adminJsonOptions);
                    return list ?? new List<DocumentRequirementModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DOCUMENT REQUIREMENTS API ERROR] {ex.Message}");
            }
            return new List<DocumentRequirementModel>();
        }

        /// <summary>Full documents (file included) for a role instance - only succeeds for the owner or an admin, enforced server-side.</summary>
        public async Task<List<UserDocumentModel>> GetUserDocumentsAsync(int userRoleId)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, $"api/documents/{userRoleId}");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<UserDocumentModel>>(_adminJsonOptions);
                    return list ?? new List<UserDocumentModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[USER DOCUMENTS API ERROR] {ex.Message}");
            }
            return new List<UserDocumentModel>();
        }

        /// <summary>Public-safe summary for viewing someone else's profile - document type + status only, never the file.</summary>
        public async Task<List<UserDocumentSummaryModel>> GetDocumentSummaryAsync(int userRoleId, string roleCode)
        {
            try
            {
                var response = await _http.GetAsync($"api/documents/{userRoleId}/summary?roleCode={Uri.EscapeDataString(roleCode)}");
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<UserDocumentSummaryModel>>(_adminJsonOptions);
                    return list ?? new List<UserDocumentSummaryModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DOCUMENT SUMMARY API ERROR] {ex.Message}");
            }
            return new List<UserDocumentSummaryModel>();
        }

        /// <summary>POST api/documents/{userRoleId}/upload ([Authorize], server verifies caller owns userRoleId or is admin). fileDataUrl is a base64 data: URL - the file itself travels inline in the JSON body, no separate multipart upload.</summary>
        public async Task<(bool Success, string Message)> UploadDocumentAsync(int userRoleId, int documentTypeId, string fileName, string fileDataUrl)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Post, $"api/documents/{userRoleId}/upload")
                {
                    Content = JsonContent.Create(new { DocumentTypeId = documentTypeId, FileName = fileName, FileDataUrl = fileDataUrl })
                };
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode) return (true, "Dokument suksesvol opgelaai.");
                string body = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(body) ? $"Fout: {response.StatusCode}" : body);
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        /// <summary>PUT api/documents/{documentId}/replace ([Authorize], server verifies ownership/admin) - swaps a document's file while keeping its id/type, resetting its verification status to Pending.</summary>
        public async Task<(bool Success, string Message)> ReplaceDocumentAsync(int documentId, string fileName, string fileDataUrl)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Put, $"api/documents/{documentId}/replace")
                {
                    Content = JsonContent.Create(new { FileName = fileName, FileDataUrl = fileDataUrl })
                };
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode) return (true, "Dokument suksesvol vervang.");
                string body = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(body) ? $"Fout: {response.StatusCode}" : body);
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        /// <summary>DELETE api/documents/{documentId} ([Authorize], server verifies ownership/admin).</summary>
        public async Task<(bool Success, string Message)> DeleteDocumentAsync(int documentId)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Delete, $"api/documents/{documentId}");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode) return (true, "Dokument suksesvol geskrap.");
                string body = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(body) ? $"Fout: {response.StatusCode}" : body);
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        /// <summary>GET api/documents/{userRoleId}/audit ([Authorize], server verifies ownership/admin) - upload/verify/reject history for that role's documents.</summary>
        public async Task<List<DocumentAuditEntryModel>> GetDocumentAuditTrailAsync(int userRoleId)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Get, $"api/documents/{userRoleId}/audit");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<DocumentAuditEntryModel>>(_adminJsonOptions);
                    return list ?? new List<DocumentAuditEntryModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DOCUMENT AUDIT API ERROR] {ex.Message}");
            }
            return new List<DocumentAuditEntryModel>();
        }

        // --- Document Verification Queue (admin) ---

        /// <summary>GET api/admin/document-verifications ([Authorize(Roles="Admin")]), optionally filtered by status and/or roleCode - the admin's per-document verification queue (distinct from GetRoleVerificationRequestsAsync's role-application queue).</summary>
        public async Task<List<PendingDocumentVerificationModel>> GetPendingDocumentVerificationsAsync(string? status = "Pending", string? roleCode = null)
        {
            try
            {
                var query = new List<string>();
                if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
                if (!string.IsNullOrWhiteSpace(roleCode)) query.Add($"roleCode={Uri.EscapeDataString(roleCode)}");
                string qs = query.Count > 0 ? "?" + string.Join("&", query) : "";

                var httpReq = new HttpRequestMessage(HttpMethod.Get, $"api/admin/document-verifications{qs}");
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                {
                    var list = await response.Content.ReadFromJsonAsync<List<PendingDocumentVerificationModel>>(_adminJsonOptions);
                    return list ?? new List<PendingDocumentVerificationModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DOCUMENT VERIFICATIONS API ERROR] {ex.Message}");
            }
            return new List<PendingDocumentVerificationModel>();
        }

        /// <summary>POST api/admin/document-verifications/{documentId}/decision ([Authorize(Roles="Admin")]) - approve/reject one document. Reason is required when rejecting, optional when approving.</summary>
        public async Task<(bool Success, string Message)> DecideDocumentVerificationAsync(int documentId, bool approve, string? reason)
        {
            if (!approve && string.IsNullOrWhiteSpace(reason))
            {
                return (false, "'n Rede is verpligtend wanneer 'n dokument van hand gewys word.");
            }

            try
            {
                var body = new { Approve = approve, Reason = reason };
                var httpReq = new HttpRequestMessage(HttpMethod.Post, $"api/admin/document-verifications/{documentId}/decision") { Content = JsonContent.Create(body) };
                AttachAuthHeader(httpReq);
                var response = await _http.SendAsync(httpReq);
                if (response.IsSuccessStatusCode)
                    return (true, approve ? "Dokument goedgekeur." : "Dokument van hand gewys.");
                string content = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(content) ? $"Fout: {response.StatusCode}" : content);
            }
            catch (Exception ex)
            {
                return (false, $"Kon nie met die bediener kommunikeer nie: {ex.Message}");
            }
        }

        /// <summary>Wire shape of api/user/getAllUsers's response envelope.</summary>
        private class ApiUserResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("data")]
            public List<ApiUserData>? Data { get; set; }
        }

        /// <summary>Wire shape of one user row within ApiUserResponse.Data - mapped onto UserModel in GetMasterUsersAsync.</summary>
        private class ApiUserData
        {
            [JsonPropertyName("userId")]
            public int UserId { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("surname")]
            public string? Surname { get; set; }

            [JsonPropertyName("email")]
            public string? Email { get; set; }

            [JsonPropertyName("phone")]
            public string? Phone { get; set; }

            [JsonPropertyName("registrationDate")]
            public DateTime RegistrationDate { get; set; }

            [JsonPropertyName("isEmailVerified")]
            public bool IsEmailVerified { get; set; }

            [JsonPropertyName("isPhoneVerified")]
            public bool IsPhoneVerified { get; set; }

            [JsonPropertyName("isSuspended")]
            public bool IsSuspended { get; set; }

            [JsonPropertyName("accountStatus")]
            public string? AccountStatus { get; set; }

            [JsonPropertyName("statusReason")]
            public string? StatusReason { get; set; }

            [JsonPropertyName("roles")]
            public List<ApiUserRoleData>? Roles { get; set; }
        }

        /// <summary>Wire shape of one role entry within ApiUserData.Roles.</summary>
        private class ApiUserRoleData
        {
            [JsonPropertyName("userRoleId")]
            public int UserRoleId { get; set; }

            [JsonPropertyName("userId")]
            public int UserId { get; set; }

            [JsonPropertyName("roleId")]
            public int RoleId { get; set; }

            [JsonPropertyName("roleCode")]
            public string? RoleCode { get; set; }

            [JsonPropertyName("roleName")]
            public string? RoleName { get; set; }
        }

        /// <summary>Wire shape of api/admin/health's response, backed by SqlServerHealthCheck.</summary>
        private class SystemHealthApiResponse
        {
            [JsonPropertyName("dbConnected")]
            public bool DbConnected { get; set; }

            [JsonPropertyName("dbStatus")]
            public string? DbStatus { get; set; }

            [JsonPropertyName("apiUptimeHealthy")]
            public bool ApiUptimeHealthy { get; set; }

            [JsonPropertyName("responseTimeMs")]
            public int ResponseTimeMs { get; set; }

            [JsonPropertyName("appSessions")] public int AppSessions { get; set; }
            [JsonPropertyName("mobileApiHealthy")] public bool MobileApiHealthy { get; set; }
            [JsonPropertyName("mobileApiStatus")] public string? MobileApiStatus { get; set; }
            [JsonPropertyName("mobileApiResponseMs")] public int MobileApiResponseMs { get; set; }
            [JsonPropertyName("maintenanceMode")] public bool MaintenanceMode { get; set; }
            [JsonPropertyName("registrationsOpen")] public bool RegistrationsOpen { get; set; } = true;
            [JsonPropertyName("payFastSandbox")] public bool PayFastSandbox { get; set; } = true;
            [JsonPropertyName("environment")] public string? Environment { get; set; }

            [JsonPropertyName("activeSessions")]
            public int ActiveSessions { get; set; }
        }
    }
}

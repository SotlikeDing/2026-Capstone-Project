using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebAPI.Controllers
{
    /// <summary>A PH's per-service day-rate pricing entries (e.g. guiding fee, trophy prep), shown on their public profile.</summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PhPricingController : ControllerBase
    {
        private readonly PhPricing_db _db;
        private readonly ResourceOwnership_db _ownership;

        public PhPricingController(PhPricing_db db, ResourceOwnership_db ownership)
        {
            _db = db;
            _ownership = ownership;
        }

        /// <summary>Lists all pricing entries for a given PH (by their userId).</summary>
        [AllowAnonymous]
        [HttpGet("getPricing/{phId}")]
        public async Task<ActionResult<List<PhPricingResponse>>> GetPhPricing(int phId)
        {
            var result = await _db.GetPhPricing(phId);
            return Ok(result);
        }

        /// <summary>Adds a new pricing entry for the caller and returns its generated id. request.PhId is the PH's userId (resolved to ph.phId by PhPricing_db).</summary>
        [HttpPost("create")]
        public async Task<ActionResult<int>> CreatePhPricing([FromBody] CreatePhPricingRequest request)
        {
            if (!User.IsSelfOrAdmin(request.PhId)) return Forbid();
            var id = await _db.CreatePhPricing(request);
            return Ok(id);
        }

        /// <summary>Removes a pricing entry - only its PH or an Admin.</summary>
        [HttpDelete("delete/{id}")]
        public async Task<ActionResult<bool>> DeletePhPricing(int id)
        {
            if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetPhPricingOwnerUserIds(id))) return Forbid();
            var success = await _db.DeletePhPricing(id);
            return Ok(success);
        }
    }
}

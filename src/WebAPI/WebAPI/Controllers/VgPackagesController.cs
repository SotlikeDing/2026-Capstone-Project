using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebAPI.Controllers
{
    /// <summary>A videographer's (VG) service packages (e.g. drone footage, edited highlight reel), shown on their public profile.</summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class VgPackagesController : ControllerBase
    {
        private readonly VgPackages_db _db;
        private readonly ResourceOwnership_db _ownership;

        public VgPackagesController(VgPackages_db db, ResourceOwnership_db ownership)
        {
            _db = db;
            _ownership = ownership;
        }

        /// <summary>Lists all packages offered by a given VG (by their userId).</summary>
        [AllowAnonymous]
        [HttpGet("getPackages/{vgId}")]
        public async Task<ActionResult<List<VgPackageResponse>>> GetVgPackages(int vgId)
        {
            var result = await _db.GetVgPackages(vgId);
            return Ok(result);
        }

        /// <summary>Adds a new VG package for the caller and returns its generated id. request.VgId is the VG's userId (resolved to vg.vgId by VgPackages_db).</summary>
        [HttpPost("create")]
        public async Task<ActionResult<int>> CreateVgPackage([FromBody] CreateVgPackageRequest request)
        {
            if (!User.IsSelfOrAdmin(request.VgId)) return Forbid();
            var id = await _db.CreateVgPackage(request);
            return Ok(id);
        }

        /// <summary>Removes a VG package - only its VG or an Admin.</summary>
        [HttpDelete("delete/{id}")]
        public async Task<ActionResult<bool>> DeleteVgPackage(int id)
        {
            if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetVgPackageOwnerUserIds(id))) return Forbid();
            var success = await _db.DeleteVgPackage(id);
            return Ok(success);
        }
    }
}

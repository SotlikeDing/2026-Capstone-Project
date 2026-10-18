using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using System.Collections.Generic;

namespace WebAPI.Controllers
{
    /// <summary>
    /// A user's hunting-permit records: listing what a user holds and adding a new
    /// one. Synchronous DB calls (Permits_db predates the rest of the codebase's
    /// async pattern) - kept as-is rather than changed as part of an unrelated task.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PermitsController : ControllerBase
    {
        private readonly Permits_db _db;
        public PermitsController(Permits_db db)
        {
            _db = db;
        }

        /// <summary>Lists all permits recorded for a user.</summary>
        [HttpGet("{userId}")]
        public ActionResult<List<PermitModelDto>> Get(int userId)
        {
            if (!User.IsSelfOrAdmin(userId)) return Forbid();
            return Ok(_db.GetPermits(userId));
        }

        /// <summary>Adds a new permit record and returns its generated id.</summary>
        [HttpPost]
        public ActionResult<int> Post([FromBody] PermitModelDto p)
        {
            if (!User.IsSelfOrAdmin(p.UserId)) return Forbid();
            int id = _db.AddPermit(p);
            return Ok(id);
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// Read-only reference-data endpoints for static/lookup lists shown on booking and
/// farm-management forms (taxidermists, slaughterhouses, dangerous-game species,
/// provinces). All public, none of this data is user-specific.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ExtraController : ControllerBase
{
    private readonly Extra_db _db;
    public ExtraController(Extra_db db) { _db = db; }

    /// <summary>Lists registered taxidermy service providers.</summary>
    [HttpGet("getTaxidermy")]
    public async Task<IActionResult> GetTaxidermy() => Ok(await _db.GetTaxidermy());

    /// <summary>Lists registered slaughterhouse/processing facilities.</summary>
    [HttpGet("getSlaughterhouses")]
    public async Task<IActionResult> GetSlaughterhouses() => Ok(await _db.GetSlaughterhouses());

    /// <summary>Lists species classified as dangerous game, for permit/warning UI.</summary>
    [HttpGet("getDangerousAnimals")]
    public async Task<IActionResult> GetDangerousAnimals() => Ok(await _db.GetDangerousAnimals());

    /// <summary>Lists South African provinces, for address/location dropdowns.</summary>
    [HttpGet("provinces")]
    public async Task<IActionResult> GetProvinces() => Ok(await _db.GetProvinces());
}

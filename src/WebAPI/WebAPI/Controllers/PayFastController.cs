using Microsoft.AspNetCore.Mvc;
using WebAPI.Services.Payments;

namespace WebAPI.Controllers
{
    /// <summary>
    /// Receives PayFast's server-to-server Instant Transaction Notification (ITN). This is the
    /// only way a real payment gets credited: PayFastItnService checks the signature, the
    /// sender, PayFast's own confirmation and the amount before anything is marked paid.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PayFastController : ControllerBase
    {
        private readonly PayFastItnService _itn;
        private readonly ILogger<PayFastController> _logger;

        public PayFastController(PayFastItnService itn, ILogger<PayFastController> logger)
        {
            _itn = itn;
            _logger = logger;
        }

        /// <summary>
        /// PayFast ITN webhook target. Answers 200 for anything handled (credited, duplicate,
        /// ignored or rejected) - PayFast resends on anything else - except when PayFast itself
        /// couldn't be reached to confirm the notification, where a 503 makes it try again later.
        /// </summary>
        [HttpPost("itn")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> ItnCallback()
        {
            // Read the raw body: the signature is over the fields in the order PayFast sent them.
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            var result = await _itn.ProcessAsync(body, HttpContext.Connection.RemoteIpAddress);
            _logger.LogInformation("PayFast ITN: {Outcome} - {Reason}", result.Outcome, result.Reason);
            return result.Outcome == ItnOutcome.RetryLater ? StatusCode(503) : Ok();
        }
    }
}

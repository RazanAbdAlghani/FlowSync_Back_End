﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WebApplicationFlowSync.DTOs;
using WebApplicationFlowSync.Models.Requests.WebApplicationFlowSync.Models.Requests;
using WebApplicationFlowSync.Models;
using WebApplicationFlowSync.services.EmailService;
using WebApplicationFlowSync.services.NotificationService;
using WebApplicationFlowSync.Data;
using Microsoft.EntityFrameworkCore;

namespace WebApplicationFlowSync.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class DeactivateAccountRequestController : ControllerBase
    {
        private readonly ApplicationDbContext context;
        private readonly UserManager<AppUser> userManager;
        private readonly INotificationService notificationService;
        private readonly IEmailService emailService;

        public DeactivateAccountRequestController(ApplicationDbContext context, UserManager<AppUser> userManager ,INotificationService notificationService, IEmailService emailService)
        {
            this.context = context;
            this.userManager = userManager;
            this.notificationService = notificationService;
            this.emailService = emailService;
        }

        [HttpGet("all-deactivation-account-requests")]
        [Authorize(Roles = "Leader")]
        public async Task<IActionResult> GetAllDeleteAccountRequests()
        {
            var requests = await context.PendingMemberRequests
                .OfType<DeactivateAccountRequest>()
                .Select(r => new
                {
                    r.RequestId,
                    r.MemberId,
                    r.MemberName,
                    r.Email,
                    r.RequestedAt,
                    r.RequestStatus,
                    r.Reason
                })
                .ToListAsync();

            return Ok(requests);
        }

        [HttpPost("approve-deactivation-member-request/{requestId}")]
        [Authorize(Roles = "Leader")]
        public async Task<IActionResult> AprroveDeleteAccountRequest(int requestId)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized("Only leaders can respond to requests.");

            var request = await context.PendingMemberRequests
                .OfType<DeactivateAccountRequest>()
                .Include(r => r.Member)
                .FirstOrDefaultAsync(r => r.RequestId == requestId);

            if (request == null)
                return NotFound("Deactivation request not found.");

            if (request.RequestStatus != RequestStatus.Pending)
                return BadRequest("This request has already been processed.");

            request.RequestStatus = RequestStatus.Approved;

            var member = request.Member;

            member.IsDeactivated = true;
            await userManager.UpdateAsync(member);
            await context.SaveChangesAsync();


            await notificationService.SendNotificationAsync(
                member.Id,
                   $@"
                    Dear {member.FirstName},

                    Your request to deactivate your account has been approved by your team leader.
                    As of now, your account has been deactivated and you will no longer be able to log in.

                    If you believe this was a mistake or you have further questions, please contact your team leader.

                    Best regards,  
                    FlowSync Team",
                    NotificationType.Info,
                    member.Email,
                    null,
                    null,
                    false
           );

            return Ok("The member's account has been deactivated successfully.Please reassign his tasks");

        }

        [HttpPost("reject-deactivation-member-request/{requestId}")]
        [Authorize(Roles = "Leader")]
        public async Task<IActionResult> RejectDeleteAccountRequest(int requestId)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized("Only leaders can respond to requests.");

            var request = await context.PendingMemberRequests
                .OfType<DeactivateAccountRequest>()
                .Include(r => r.Member)
                .FirstOrDefaultAsync(r => r.RequestId == requestId);

            if (request == null)
                return NotFound("Deactivation request not found.");

            if (request.RequestStatus != RequestStatus.Pending)
                return BadRequest("This request has already been handled.");

            var member = request.Member;

            request.RequestStatus = RequestStatus.Rejected;
            await context.SaveChangesAsync();

            await notificationService.SendNotificationAsync(
             request.MemberId,
             $"Your account deactivation request has been rejected.",
             NotificationType.Rejection,
             member.Email
           );

            return Ok("Deactivation request has been rejected.");

        }
    }
}

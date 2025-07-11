﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplicationFlowSync.Models.Requests;
using WebApplicationFlowSync.Models;
using WebApplicationFlowSync.Data;
using Microsoft.AspNetCore.Authorization;
using TaskStatus = WebApplicationFlowSync.Models.TaskStatus;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Identity;
using WebApplicationFlowSync.DTOs;
using WebApplicationFlowSync.services.NotificationService;
using WebApplicationFlowSync.services.KpiService;
using Microsoft.Graph.Drives.Item.Items.Item.Workbook.Functions.YearFrac;

namespace WebApplicationFlowSync.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class CompleteTaskRequestsController : ControllerBase
    {
        private readonly ApplicationDbContext context;
        private readonly UserManager<AppUser> userManager;
        private readonly INotificationService notificationService;
        private readonly IKpiService kpiService;

        public CompleteTaskRequestsController(ApplicationDbContext context, UserManager<AppUser> userManager , INotificationService notificationService , IKpiService kpiService)
        {
            this.context = context;
            this.userManager = userManager;
            this.notificationService = notificationService;
            this.kpiService = kpiService;
        }


        [HttpPost("create-complete-request")]
        [Authorize(Roles = "Member")] // تأكد من أن الصلاحيات مناسبة حسب دور المستخدم
        public async Task<IActionResult> CreateRequest([FromBody] CompleteTaskRequestDto dto)
        {
            var member = await userManager.GetUserAsync(User);
            if (member == null || !User.IsInRole("Member"))
                return Unauthorized();

            var task = await context.Tasks.FirstOrDefaultAsync(t => t.FRNNumber == dto.FRNNumber);
            if (task == null)
                return NotFound("Task not found.");

            bool hasExistingCompleteRequest = await context.PendingMemberRequests.OfType<CompleteTaskRequest>()
              .AnyAsync(r =>
               r.FRNNumber == dto.FRNNumber &&
               r.MemberId == member.Id &&
               r.RequestStatus == RequestStatus.Pending);

            if (hasExistingCompleteRequest)
                return BadRequest("You already have a pending completion request for this task.");


            var request = new CompleteTaskRequest
            {
                FRNNumber = task.FRNNumber,
                Notes = dto.Notes,
                MemberName = member.FirstName + " " + member.LastName,
                MemberId = member.Id,
                Email = member.Email,
                RequestedAt = DateTime.Now,
                RequestStatus = RequestStatus.Pending,
                Type = RequestType.CompleteTask
            };

            context.PendingMemberRequests.Add(request);
            await context.SaveChangesAsync();

            await notificationService.SendNotificationAsync(
                member.LeaderID,
                $"Member {member.FirstName} {member.LastName} has submitted a request to complete task #{dto.FRNNumber}.",
                NotificationType.CompleteTaskRequest
            );

            return Ok("The task completion request has been sent successfully.");
        }

        [HttpGet("all-complet-requests")]
        [Authorize(Roles = "Leader")]
        public async Task<IActionResult> GetAllCompleteTaskRequests()
        {
            var requests = await context.PendingMemberRequests
                .OfType<CompleteTaskRequest>()
                .Select(r => new
                {
                    r.RequestId,
                    r.MemberName,
                    r.Email,
                    r.RequestedAt,
                    r.RequestStatus,
                    r.Notes,
                    r.FRNNumber
                }).ToListAsync();

            return Ok(requests);
        }

        [HttpPost("approve-complete-task/{requestId}")]
        [Authorize(Roles = "Leader")] 
        public async Task<IActionResult> ApproveRequest(int requestId)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized("Only leaders can respond to requests.");
            var request = await context.PendingMemberRequests
                .OfType<CompleteTaskRequest>()
                .FirstOrDefaultAsync(r => r.RequestId == requestId);

            if (request == null)
                return NotFound("Complete task request not found.");

            if (request.RequestStatus != RequestStatus.Pending)
                return BadRequest("This request has already been processed.");

            var task = await context.Tasks.FirstOrDefaultAsync(t => t.FRNNumber == request.FRNNumber && t.UserID == request.MemberId);

            if (task == null)
                return NotFound("Associated task not found.");


            request.RequestStatus = RequestStatus.Approved;
            task.Type = TaskStatus.Completed;
            task.CompletedAt = DateTime.Now;
            task.Notes = request.Notes;

            await context.SaveChangesAsync();

            await notificationService.SendNotificationAsync(
                request.MemberId,
                $"Your complete request for task #{request.FRNNumber} has been approved.",
                NotificationType.Approval
            );

            int year = DateTime.Now.Year;
            await kpiService.CalculateMemberAnnualKPIAsync(task.UserID, year);
            await kpiService.CalculateLeaderAnnualKPIAsync(user.Id, year);
            return Ok("Complete task request approved and task status updated to Completed.");

        }

    }
}

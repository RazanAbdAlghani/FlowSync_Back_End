﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph.Models;
using WebApplicationFlowSync.Data;
using WebApplicationFlowSync.DTOs;
using WebApplicationFlowSync.Hubs;
using WebApplicationFlowSync.Models;
using WebApplicationFlowSync.services;
namespace WebApplicationFlowSync.Controllers
{
    [Route("[controller]")]
    [Authorize]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ApplicationDbContext context;
        private readonly UserManager<AppUser> userManager;
        private readonly IHubContext<ChatHub> chatHub;

        public ChatController(ApplicationDbContext context, UserManager<AppUser> userManager, IHubContext<ChatHub> chatHub)
        {
            this.context = context;
            this.userManager = userManager;
            this.chatHub = chatHub;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageDto dto)
        {
            var sender = await userManager.GetUserAsync(User);
            var reciver = await userManager.FindByIdAsync(dto.ReceiverId);

            if (sender == null || reciver == null)
                return NotFound("Sender or Receiver not found.");

            var message = new Models.ChatMessage
            {
                SenderId = sender.Id,
                ReceiverId = dto.ReceiverId,
                Message = dto.Message,
                IsForwarded = dto.IsForwarded
            };

            context.ChatMessages.Add(message);
            await context.SaveChangesAsync();

            // إشعار لحظي للطرف الآخر
            await chatHub.Clients.User(dto.ReceiverId).SendAsync("ReceiveMessage",
                            sender.Id,
                            message.Message,
                            message.Id,
                            message.SentAt,
                            message.IsForwarded);


            // تحويل إلى DTO
            var messageDto = new ChatMessageDto
            {
                Id = message.Id,
                Message = message.Message,
                SentAt = message.SentAt,
                SenderId = message.SenderId,
                ReceiverId = message.ReceiverId,
                IsRead = message.IsRead,
                IsDeleted = message.IsDeleted,
                IsEdited = message.IsEdited,
                IsForwarded = message.IsForwarded,
                IsMine = true
            };

            return Ok(messageDto);
        }

        [HttpPost("send-to-team")]
        [Authorize(Roles ="Leader")]
        public async Task<IActionResult> SendMessageToAllMembers([FromBody] SendGroupMessageDto dto)
        {
            var sender = await userManager.GetUserAsync(User);

            if (sender == null || sender.Role != Role.Leader)
                return Unauthorized("Only leaders can send group messages.");

            var teamMembers = await context.Users
                .Where(u => u.LeaderID == sender.Id && !u.IsDeactivated && u.EmailConfirmed)
                .ToListAsync();

            if (teamMembers.Count == 0)
                return NotFound("No team members found.");

            var messages = new List<Models.ChatMessage>();

            foreach(var member in teamMembers)
            {
                var message = new Models.ChatMessage
                {
                    SenderId = sender.Id,
                    ReceiverId = member.Id,
                    Message = dto.Message
                };

                messages.Add(message);

                await chatHub.Clients.User(member.Id).SendAsync("ReceiveMessage",
                    sender.Id,
                    message.Message,
                    message.Id,
                    message.SentAt);
            }

            context.ChatMessages.AddRange(messages); // لتقليل الضغط والتكرار
            await context.SaveChangesAsync();

            return Ok("Messages sent to all team members.");

        }

        [HttpPut("edit/{messageId}")]
        public async Task<IActionResult> EditMessage(int messageId, [FromBody] EditMessageDto dto)
        {
            var user = await userManager.GetUserAsync(User);
            var message = await context.ChatMessages.FindAsync(messageId);

            if (message == null)
                return NotFound("Message not found.");

            if (message.SenderId != user.Id)
                return Forbid("You can only delete your own messages.");

            message.Message = dto.Message;
            message.IsEdited = true;
            await context.SaveChangesAsync();

            return Ok(new
            {
                message.Id,
                message.Message
            });
        }

        [HttpDelete("delete/{messageId}")]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            var user = await userManager.GetUserAsync(User);
            var message = await context.ChatMessages.FindAsync(messageId);

            if(message == null)
                return NotFound("Message not found.");

            if(message.SenderId != user.Id)
                return Forbid("You can only delete your own messages.");

            message.IsDeleted = true;
            await context.SaveChangesAsync();

            return Ok("Message deleted successfully.");
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetChatUsers()
        {
            var currentUser = await userManager.GetUserAsync(User);
            if (currentUser == null)
                return Unauthorized();

            var users = await context.Users
                .Where(u => u.EmailConfirmed && !u.IsDeactivated)
                .ToListAsync();

            var chatUsers = new List<ChatUserDto>();

            foreach (var u in users)
            {
                var lastMessage = await context.ChatMessages
                    .Where(m => 
                          (m.SenderId == currentUser.Id && m.ReceiverId == u.Id) ||
                          (m.SenderId == u.Id && m.ReceiverId == currentUser.Id))
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefaultAsync();

                chatUsers.Add(new ChatUserDto
                {
                    Id = u.Id,
                    FullName = u.FirstName + " " + u.LastName,
                    Email = u.Email,
                    PictureURL = u.PictureURL,
                    IsOnline = ConnectedUsersTracker.IsOnline(u.Id),
                    LastMessageSentAt = lastMessage?.SentAt,
                    LastMessage = lastMessage != null
                    ? new
                    {
                        lastMessage.Message,
                        lastMessage.SentAt,
                        IsMine = lastMessage.SenderId == currentUser.Id
                    }
                    : null
                });
            }
                var orderedUsers = chatUsers
                   .OrderByDescending(u => u.LastMessageSentAt ?? DateTime.MinValue) // لو ما فيه رسائل، نجعلها الأقدم
                   .ToList();

              return Ok(orderedUsers);
        }

        //جلب المحادثة بين الطرفين
        [HttpGet("conversation")]
        public async Task<IActionResult> GetConversation(string userId)
        {
            var currentUser = await userManager.GetUserAsync(User);
            var messages = await context.ChatMessages
                .Where(m => (m.SenderId == currentUser.Id && m.ReceiverId == userId) ||
                             (m.SenderId == userId && m.ReceiverId == currentUser.Id))
                .OrderBy(m => m.SentAt)
                .ToListAsync();

            return Ok(messages.Select(m => new ChatMessageDto
            {
                Id = m.Id,
                Message = m.Message,
                SentAt = m.SentAt,
                SenderId = m.SenderId,
                ReceiverId = m.ReceiverId,
                IsRead = m.IsRead,
                IsDeleted = m.IsDeleted,
                IsEdited = m.IsEdited,
                IsMine = m.SenderId == currentUser.Id
            }));
        }

        [HttpGet("unread")]
        public async Task<IActionResult> GetUnreadMessages()
        {
            var user = await userManager.GetUserAsync(User);
            var unreadMessages = await context.ChatMessages
                .Where(m => m.ReceiverId == user.Id && !m.IsRead)
                .ToListAsync();

            return Ok(unreadMessages.Select(m => new ChatMessageDto
            {
                Id = m.Id,
                Message = m.Message,
                SentAt = m.SentAt,
                SenderId = m.SenderId,
                ReceiverId = m.ReceiverId
            }));
        }

        [HttpPost("mark-as-read")]
        public async Task<IActionResult> MarkMessagesAsRead([FromBody] List<int> messageIds)
        {
            var user = await userManager.GetUserAsync(User);
            var messages = await context.ChatMessages
                .Where(m => messageIds.Contains(m.Id) && m.ReceiverId == user.Id)
                .ToListAsync();

            foreach(var msg in messages)
            {
                msg.IsRead = true;
            }

            await context.SaveChangesAsync();

            return Ok();
        }



    }
}

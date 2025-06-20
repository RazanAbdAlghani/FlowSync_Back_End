﻿using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace WebApplicationFlowSync.Models
{
    [Index(nameof(FRNNumber), IsUnique = true)]
    public class Task
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [RegularExpression(@"^\d{5}$")]
        public string FRNNumber { get; set; }

        [Required]
        [RegularExpression(@"^\d{12}$", ErrorMessage = "OSSNumber must be exactly 12 digits.")]
        public string OSSNumber { get; set; }

        [Required]
        [RegularExpression(@"^[a-zA-Z0-9\s.,'-]+$", ErrorMessage = "Title must contain only English letters and valid characters.")]
        public string Title { get; set; }
        [Required]
        public CaseSource CaseSource { get; set; }

        public string? CaseType { get; set; } = null;
        public TaskStatus Type { get; set; } = TaskStatus.Opened;
        public bool IsDelayed { get; set; } = false;
        public TaskPriority Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime Deadline { get; set; }
        public DateTime? CompletedAt { get; set; } = null;
        public DateTime? FrozenAt { get; set; } = null;

        //ملاحظة : هنا لا يمكن ان نخزن قيمة TimeSpan, لانه فقط مسموع فيه اكثر شيء 23ساعة و 59د و 59ث
        public string? FrozenCounter { get; set; } = null;

        // هذا حقل افتراضي (لا يُخزن في قاعدة البيانات) تتعامل معه في الكود كـ TimeSpan
        [NotMapped]
        public TimeSpan? FrozenCounterValue
        {
            get => string.IsNullOrEmpty(FrozenCounter) ? null : TimeSpan.Parse(FrozenCounter);
            set => FrozenCounter = value?.ToString(@"d\.hh\:mm\:ss");
        }

        public string? Reason { get; set; }

        public string? Notes { get; set; }

        public string UserID { get; set; }
        [ForeignKey("UserID")]
        public AppUser? User { get; set; }


        // العلاقة مع التقارير
        //public ICollection<TaskReport>? TasksReports { get; set; }
        //public void SetDeadline()
        //{
        //    if (Priority == TaskPriority.Urgent)
        //    {
        //        Deadline = CreatedAt.AddMinutes(5);
        //    }
        //    else
        //    {
        //        int allowedWorkingDays = Priority switch
        //        {
        //            TaskPriority.Regular => 10,
        //            TaskPriority.Important => 10,
        //            _ => 0
        //        };

        //        Deadline = AddWorkingDays(CreatedAt, allowedWorkingDays);
        //    }
        //}


        public void SetDeadline()
        {
            int allowedWorkingDays = Priority switch
            {
                TaskPriority.Urgent => 2,
                TaskPriority.Regular => 10,
                TaskPriority.Important => 10,
                _ => 0
            };

            Deadline = AddWorkingDays(CreatedAt, allowedWorkingDays);
        }

        [NotMapped]
        public TimeSpan Counter
        {
            get
            {
                if (Type == TaskStatus.Frozen && FrozenCounter != null)
                {
                    return FrozenCounterValue.Value;
                }

                if (Type == TaskStatus.Completed)
                {
                    return TimeSpan.Zero;
                }
                int allowedWorkingDays = Priority switch
                {
                    TaskPriority.Urgent => 2,
                    TaskPriority.Regular => 10,
                    TaskPriority.Important => 10,
                    _ => 0
                };

                // نحسب التاريخ النهائي حسب أيام العمل فقط
                DateTime deadline = AddWorkingDays(CreatedAt, allowedWorkingDays);

                if (Type == TaskStatus.Delayed)
                {
                    // إذا كانت متأخرة، نحسب كم من الوقت تجاوز الموعد النهائي
                    return deadline - DateTime.Now; // ستكون موجبة، ولكنها تُفهم كـ "سلبية" من حيث التجاوز
                }
                // الفارق بين الآن والموعد النهائي
                return deadline - DateTime.Now;
            }
        }

        //[NotMapped]
        //public TimeSpan Counter
        //{
        //    get
        //    {
        //        if (Type == TaskStatus.Frozen && FrozenCounter != null)
        //        {
        //            return FrozenCounterValue.Value;
        //        }

        //        if (Type == TaskStatus.Completed)
        //        {
        //            return TimeSpan.Zero;
        //        }

        //        DateTime deadline;

        //        if (Priority == TaskPriority.Urgent)
        //        {
        //            // فقط 5 دقائق لمهام عاجلة
        //            deadline = CreatedAt.AddMinutes(5);
        //        }
        //        else
        //        {
        //            int allowedWorkingDays = Priority switch
        //            {
        //                TaskPriority.Regular => 10,
        //                TaskPriority.Important => 10,
        //                _ => 0
        //            };

        //            // نحسب الموعد النهائي حسب أيام العمل
        //            deadline = AddWorkingDays(CreatedAt, allowedWorkingDays);
        //        }

        //        if (Type == TaskStatus.Delayed)
        //        {
        //            // نحسب كم من الوقت تجاوز الموعد النهائي (سالبة)
        //            return deadline - DateTime.Now;
        //        }

        //        // نحسب الوقت المتبقي حتى الموعد النهائي
        //        return deadline - DateTime.Now;
        //    }
        //}

        private DateTime AddWorkingDays(DateTime startDate, int workingDays)
        {
            var current = startDate;
            while (workingDays > 0)
            {
                current = current.AddDays(1);
                if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                {
                    workingDays--;
                }
            }
            return current;
        }

    }

    public enum TaskPriority
    {
        Urgent,//قضايا السياح 48 ساعة     0
        Regular, //1     10 ايام عمل 
        Important //2    ايام عمل
    }

    public enum TaskStatus
    {
        Opened,    // 0
        Completed, // 1
        Delayed,   // 2
        Frozen     // 3
    }

    public enum CaseSource
    {
        JebelAli,             // جبل علي
        AlRaffa,              // الرفاعة
        AlRashidiya,          // الراشدية
        AlBarsha,             // البرشاء
        BurDubai,             // بر دبي
        Lahbab,               // لهباب
        AlFuqaa,              // الفقع
        Ports,                // الموانئ
        AlQusais,             // القصيص
        AlMuraqqabat,         // المرقبات
        Naif,                 // نايف
        AlKhawanij,           // الخوانيج
        Hatta,                // حتا
        AirportSecurity,      // أمن المطارات
        PublicProsecution,    // النيابة العامة
        DubaiMunicipality,    // بلدية دبي
        DubaiCustoms,         // جمارك دبي
        RasAlKhaimah,         // رأس الخيمة
        UmmAlQuwain,          // أم القيوين
        Ajman,                // عجمان
        AbuDhabi,             // أبو ظبي
        Fujairah,             // الفجيرة
        Sharjah,              // الشارقة
        Forensics,            // الطب الشرعي
        MinistryOfDefense     // وزارة الدفاع
    }

}
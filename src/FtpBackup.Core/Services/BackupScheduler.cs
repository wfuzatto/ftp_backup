using System.Globalization;
using FtpBackup.Core.Models;

namespace FtpBackup.Core.Services;

public static class BackupScheduler
{
    public static string GetScheduleKey(BackupSettings settings) =>
        $"{settings.Frequency}|{settings.StartTime}|{settings.HourlyMinute}|{settings.WeeklyDay}|{settings.MonthlyDay}";

    public static DateTime GetNextOccurrence(DateTime fromLocal, BackupSettings settings)
    {
        settings.NormalizeDefaults();
        var time = ParseTime(settings.StartTime);

        return settings.Frequency switch
        {
            BackupFrequency.Hourly => NextHourly(fromLocal, settings.HourlyMinute),
            BackupFrequency.Daily => NextDaily(fromLocal, time),
            BackupFrequency.Weekly => NextWeekly(fromLocal, settings.WeeklyDay, time),
            BackupFrequency.Monthly => NextMonthly(fromLocal, settings.MonthlyDay, time),
            _ => NextDaily(fromLocal, time)
        };
    }

    public static TimeOnly ParseTime(string value)
    {
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            return result;

        if (TimeOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out result))
            return result;

        return new TimeOnly(2, 0);
    }

    private static DateTime NextHourly(DateTime from, int minute)
    {
        minute = Math.Clamp(minute, 0, 59);
        var candidate = new DateTime(from.Year, from.Month, from.Day, from.Hour, minute, 0, DateTimeKind.Local);
        if (candidate <= from)
            candidate = candidate.AddHours(1);
        return candidate;
    }

    private static DateTime NextDaily(DateTime from, TimeOnly time)
    {
        var candidate = At(from.Date, time);
        if (candidate <= from)
            candidate = candidate.AddDays(1);
        return candidate;
    }

    private static DateTime NextWeekly(DateTime from, DayOfWeek day, TimeOnly time)
    {
        var delta = ((int)day - (int)from.DayOfWeek + 7) % 7;
        var candidate = At(from.Date.AddDays(delta), time);
        if (candidate <= from)
            candidate = candidate.AddDays(7);
        return candidate;
    }

    private static DateTime NextMonthly(DateTime from, int requestedDay, TimeOnly time)
    {
        var candidate = InMonth(from.Year, from.Month, requestedDay, time);
        if (candidate <= from)
        {
            var nextMonth = new DateTime(from.Year, from.Month, 1).AddMonths(1);
            candidate = InMonth(nextMonth.Year, nextMonth.Month, requestedDay, time);
        }
        return candidate;
    }

    private static DateTime InMonth(int year, int month, int requestedDay, TimeOnly time)
    {
        var day = Math.Min(Math.Clamp(requestedDay, 1, 31), DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, time.Hour, time.Minute, 0, DateTimeKind.Local);
    }

    private static DateTime At(DateTime date, TimeOnly time) =>
        new(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Local);
}

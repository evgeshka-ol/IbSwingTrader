
namespace IbSwingTrader.Application.Market
{
    public class LocalMarketScheduleProvider(
        IMarketSettingsProvider marketSettingsProvider,
        IMarketSessionSettingsProvider marketSessionSettingsProvider) : ILocalMarketScheduleProvider
    {
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
        private readonly IMarketSessionSettingsProvider _marketSessionSettingsProvider = marketSessionSettingsProvider;

        public MarketSessionSchedule BuildSchedule(DateTime startUtc, DateTime endUtc)
        {
            if (endUtc < startUtc)
            {
                throw new ArgumentException("End date must be greater than or equal to start date.");
            }

            var marketSettings = _marketSettingsProvider.Get();
            var sessionSettings = _marketSessionSettingsProvider.Get();

            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(marketSettings.Timezone);

            var startLocalDate = TimeZoneInfo.ConvertTimeFromUtc(startUtc, timeZone).Date;
            var endLocalDate = TimeZoneInfo.ConvertTimeFromUtc(endUtc, timeZone).Date;

            var holidayMap = sessionSettings.Holidays
                .Select(ParseHoliday)
                .Where(x => x.HasValue)
                .ToDictionary(x => x!.Value.Date, x => x!.Value.Name);

            var earlyCloseMap = sessionSettings.EarlyCloses
                .Select(ParseEarlyClose)
                .Where(x => x.HasValue)
                .ToDictionary(x => x!.Value.Date, x => x!.Value);

            var result = new MarketSessionSchedule
            {
                Source = MarketScheduleSource.LocalConfig,
                TimeZoneId = marketSettings.Timezone,
                LoadedAtUtc = DateTime.UtcNow
            };

            for (var date = startLocalDate; date <= endLocalDate; date = date.AddDays(1))
            {
                var dateOnly = DateOnly.FromDateTime(date);
                var isTradingWeekday = sessionSettings.TradingDays.Contains(date.DayOfWeek);
                var isHoliday = sessionSettings.EnableHolidaySupport && holidayMap.ContainsKey(dateOnly);
                (DateOnly Date, TimeOnly CloseTime, string? Name)? earlyClose = sessionSettings.EnableEarlyCloseSupport && earlyCloseMap.TryGetValue(dateOnly, out var earlyCloseInfo)
                    ? earlyCloseInfo
                    : null;

                var day = new TradingDaySchedule
                {
                    Date = dateOnly,
                    IsTradingDay = isTradingWeekday && !isHoliday,
                    IsHoliday = isHoliday,
                    IsEarlyClose = earlyClose.HasValue
                };

                if (day.IsTradingDay)
                {
                    AddSession(day, date, sessionSettings.PreMarket, MarketSessionType.PreMarket, MarketSessionScope.TradingHours, timeZone);
                    AddSession(day, date, sessionSettings.RegularSession, MarketSessionType.Regular, MarketSessionScope.TradingHours, timeZone);
                    AddSession(day, date, sessionSettings.AfterHours, MarketSessionType.AfterHours, MarketSessionScope.TradingHours, timeZone);

                    if (earlyClose.HasValue)
                    {
                        ApplyEarlyClose(day, date, earlyClose.Value.CloseTime, timeZone);
                    }
                }

                day.Sessions = day.Sessions
                    .Where(x => x.EndUtc > x.StartUtc)
                    .OrderBy(x => x.StartUtc)
                    .ToList();

                result.Days.Add(day);
            }

            return result;
        }

        private static void AddSession(
            TradingDaySchedule day,
            DateTime localDate,
            SessionWindowSettings settings,
            MarketSessionType type,
            MarketSessionScope scope,
            TimeZoneInfo timeZone)
        {
            if (!settings.Enabled)
            {
                return;
            }

            if (!TimeOnly.TryParse(settings.Start, out var startTime))
            {
                throw new InvalidOperationException($"Invalid session start time: {settings.Start}");
            }

            if (!TimeOnly.TryParse(settings.End, out var endTime))
            {
                throw new InvalidOperationException($"Invalid session end time: {settings.End}");
            }

            var localStart = localDate.Date.Add(startTime.ToTimeSpan());
            var localEnd = localDate.Date.Add(endTime.ToTimeSpan());

            if (localEnd <= localStart)
            {
                return;
            }

            day.Sessions.Add(new SessionInterval
            {
                Type = type,
                Scope = scope,
                StartUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone),
                EndUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone)
            });
        }

        private static void ApplyEarlyClose(
            TradingDaySchedule day,
            DateTime localDate,
            TimeOnly closeTime,
            TimeZoneInfo timeZone)
        {
            var closeLocal = localDate.Date.Add(closeTime.ToTimeSpan());
            var closeUtc = TimeZoneInfo.ConvertTimeToUtc(closeLocal, timeZone);

            foreach (var session in day.Sessions)
            {
                if (session.EndUtc > closeUtc)
                {
                    session.EndUtc = closeUtc;
                }
            }
        }

        private static (DateOnly Date, string? Name)? ParseHoliday(MarketHolidaySettings settings)
        {
            if (!DateOnly.TryParse(settings.Date, out var date))
            {
                return null;
            }

            return (date, settings.Name);
        }

        private static (DateOnly Date, TimeOnly CloseTime, string? Name)? ParseEarlyClose(EarlyCloseSettings settings)
        {
            if (!DateOnly.TryParse(settings.Date, out var date))
            {
                return null;
            }

            if (!TimeOnly.TryParse(settings.CloseTime, out var closeTime))
            {
                return null;
            }

            return (date, closeTime, settings.Name);
        }
    }
}
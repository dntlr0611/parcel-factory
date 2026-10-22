using System;

namespace ParcelFactory.Core
{
    public enum DayPhase { Working, Lunch, Closed }

    /// <summary>근무 시계(스펙 11.1). 1게임분 = 1초(배속 전). 09:00에 시작해 18:00에 Closed가 되면 그 자리에서 멈춘다.</summary>
    public sealed class Clock
    {
        public const int OpenMinute = 9 * 60, LunchStart = 12 * 60, LunchEnd = 13 * 60, CloseMinute = 18 * 60;
        public const float MinutesPerSecond = 1f;

        /// <summary>1부터.</summary>
        public int Day { get; private set; } = 1;
        /// <summary>540.0 .. 1080.0</summary>
        public float MinuteOfDay { get; private set; } = OpenMinute;
        public DayPhase Phase => PhaseAt(MinuteOfDay);
        public bool IsOpen => Phase != DayPhase.Closed;
        public string TimeText => $"{(int)MinuteOfDay / 60:00}:{(int)MinuteOfDay % 60:00}";

        public static DayPhase PhaseAt(float minute)
        {
            if (minute >= CloseMinute) return DayPhase.Closed;
            if (minute >= LunchStart && minute < LunchEnd) return DayPhase.Lunch;
            return DayPhase.Working;
        }

        /// <summary>다음 단계 경계(12:00, 13:00, 18:00)까지 남은 실제 초. Closed면 0.</summary>
        public float SecondsToNextBoundary
        {
            get
            {
                float boundary = MinuteOfDay < LunchStart ? LunchStart : MinuteOfDay < LunchEnd ? LunchEnd : CloseMinute;
                return Math.Max(0f, (boundary - MinuteOfDay) / MinutesPerSecond);
            }
        }

        /// <summary>시계를 진행한다. 18:00을 넘기지 않으며 Closed면 아무것도 하지 않는다. 0 이하·NaN·무한대는 무시.</summary>
        public void Advance(float seconds)
        {
            if (!(seconds > 0f) || float.IsInfinity(seconds) || !IsOpen) return;
            MinuteOfDay = Math.Min(CloseMinute, MinuteOfDay + seconds * MinutesPerSecond);
        }

        public void NextDay()
        {
            Day++;
            MinuteOfDay = OpenMinute;
        }

        /// <summary>저장 복원용.</summary>
        public void Set(int day, float minuteOfDay)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day), day, "day < 1");
            if (!(minuteOfDay >= OpenMinute && minuteOfDay <= CloseMinute))
                throw new ArgumentOutOfRangeException(nameof(minuteOfDay), minuteOfDay, "minute outside 09:00..18:00");
            Day = day;
            MinuteOfDay = minuteOfDay;
        }
    }

    /// <summary>하루 요약(스펙 11.1). 수치는 그날 시작 대비 증가분.</summary>
    public sealed class DaySummary
    {
        public int Day, Delivered, Lost, Inspected;
        public int StartMoney, EndMoney;
        /// <summary>그날 획득한 EXP(지출은 빼지 않는다, 스펙 11.5).</summary>
        public int Exp;
        /// <summary>그날 출발한 트럭 수(30개 적재 + 퇴근 출발, 스펙 18).</summary>
        public int TrucksDeparted;
    }
}

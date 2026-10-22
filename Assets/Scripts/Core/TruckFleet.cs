using System;

namespace ParcelFactory.Core
{
    /// <summary>
    /// 동쪽 트럭 네 대(출고·반품·항공 출고·항공 반품)의 적재량과 교대(스펙 18). 도크에 택배가 들어갈 때마다 적재 수가 1 오르고(Load),
    /// TruckCapacity가 되면 트럭이 출발한다. 출발한 도크는 TruckReturnMinutes 게임 분 동안 비어 있고(IsAway), 그동안 레일 택배는 도크 앞에서
    /// 기다리고 손 배달은 거부된다(ConveyorSim·WorkerService). 시간은 GameSession이 근무 중에만 Advance로 흘린다(점심·일시정지 정지).
    /// 18:00에는 적재 중인 트럭이 모두 출발하고(DepartLoaded), 다음 날 09:00에 모두 빈 트럭으로 시작한다(StartDay).
    /// </summary>
    public sealed class TruckFleet
    {
        public const int TruckCapacity = 30;
        public const float TruckReturnMinutes = 20f;
        /// <summary>트럭이 있는 도크(저장·뷰 순서). 입고 트럭은 해당 없다.</summary>
        public static readonly CellType[] Docks = { CellType.Outlet, CellType.ReturnDock, CellType.AirOutlet, CellType.AirReturnDock };

        private readonly int[] loaded = new int[4];
        private readonly float[] away = new float[4];

        /// <summary>오늘 출발한 트럭 수(퇴근 출발 포함). 하루 요약용(스펙 18). StartDay가 0으로 되돌린다.</summary>
        public int DepartedToday { get; set; }

        /// <summary>트럭이 출발한 순간(30개째 적재, 또는 퇴근). 핸들러는 읽기만 해야 한다(Tick 중간일 수 있다).</summary>
        public event Action<CellType> TruckDeparted;
        /// <summary>빈 트럭이 들어온 순간(대기 끝, 또는 다음 날 09:00).</summary>
        public event Action<CellType> TruckArrived;

        private static int Index(CellType dock)
        {
            int i = Array.IndexOf(Docks, dock);
            if (i < 0) throw new ArgumentException($"{dock} has no truck", nameof(dock));
            return i;
        }

        public int Loaded(CellType dock) => loaded[Index(dock)];

        /// <summary>트럭이 돌아올 때까지 남은 게임 분. 트럭이 있으면 0.</summary>
        public float AwayMinutes(CellType dock) => away[Index(dock)];

        public bool IsAway(CellType dock) => away[Index(dock)] > 0f;

        /// <summary>도크에 택배 하나를 실었다(판정 뒤). TruckCapacity가 되면 출발한다. 떠나 있는 도크에는 실을 수 없다.</summary>
        public void Load(CellType dock)
        {
            int i = Index(dock);
            if (away[i] > 0f) throw new InvalidOperationException($"the {dock} truck is away");
            if (++loaded[i] >= TruckCapacity) Depart(i);
        }

        private void Depart(int i)
        {
            loaded[i] = 0;
            away[i] = TruckReturnMinutes;
            DepartedToday++;
            TruckDeparted?.Invoke(Docks[i]);
        }

        /// <summary>대기 시간을 minutes 게임 분만큼 줄인다. 0에 닿은 도크에 빈 트럭이 들어온다. 0 이하·NaN·무한대는 무시.</summary>
        public void Advance(float minutes)
        {
            if (!(minutes > 0f) || float.IsInfinity(minutes)) return;
            for (int i = 0; i < away.Length; i++)
            {
                if (away[i] <= 0f) continue;
                away[i] = Math.Max(0f, away[i] - minutes);
                if (away[i] <= 0f) TruckArrived?.Invoke(Docks[i]);
            }
        }

        /// <summary>퇴근(18:00): 적재 중인(1개 이상) 트럭을 모두 출발시킨다. 빈 트럭은 그대로, 떠나 있는 트럭은 그대로 떠나 있다.</summary>
        public void DepartLoaded()
        {
            for (int i = 0; i < loaded.Length; i++)
                if (loaded[i] > 0 && away[i] <= 0f) Depart(i);
        }

        /// <summary>다음 날 09:00: 네 대 모두 빈 트럭으로 도크에 있다. 떠나 있던 트럭은 들어온다(TruckArrived).</summary>
        public void StartDay()
        {
            DepartedToday = 0;
            for (int i = 0; i < loaded.Length; i++)
            {
                loaded[i] = 0;
                if (away[i] <= 0f) continue;
                away[i] = 0f;
                TruckArrived?.Invoke(Docks[i]);
            }
        }

        /// <summary>저장 복원용. 적재 수는 0..TruckCapacity−1, 대기는 0..TruckReturnMinutes이고 떠나 있는 트럭은 비어 있어야 한다.</summary>
        public void Restore(CellType dock, int loadedCount, float awayMinutes)
        {
            int i = Index(dock);
            if (loadedCount < 0 || loadedCount >= TruckCapacity) throw new ArgumentOutOfRangeException(nameof(loadedCount), loadedCount, "load outside 0..29");
            if (!(awayMinutes >= 0f && awayMinutes <= TruckReturnMinutes)) throw new ArgumentOutOfRangeException(nameof(awayMinutes), awayMinutes, "wait outside 0..20");
            if (awayMinutes > 0f && loadedCount > 0) throw new ArgumentOutOfRangeException(nameof(loadedCount), loadedCount, "an away truck carries nothing");
            loaded[i] = loadedCount;
            away[i] = awayMinutes;
        }
    }
}

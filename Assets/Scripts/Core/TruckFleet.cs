using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    /// <summary>택배차 한 대의 상태(스펙 20): 도크에 서 있음 / 차고 대기 / 운행 중 / 차고에서 도크로 들어오는 중(교대).</summary>
    public enum VehicleStatus { AtDock, Garage, Driving, Swapping }

    /// <summary>택배차 한 대. Minutes는 운행·교대가 끝날 때까지 남은 게임 분(도크·차고면 0).</summary>
    public sealed class Vehicle
    {
        public VehicleStatus Status;
        public float Minutes;
    }

    /// <summary>
    /// 동쪽 도크 네 곳(출고·반품·항공 출고·항공 반품)의 택배차(스펙 18, 20). 도크마다 차량 1~3대(MaxTrucksPerDock)를 갖는다.
    /// 도크에 서 있는 트럭에 택배가 들어갈 때마다 적재 수가 1 오르고(Load), TruckCapacity가 되면 그 트럭이 출발해 TruckReturnMinutes 동안 운행한다.
    /// 차고에 대기 중인 트럭이 있으면 TruckSwapMinutes(교대) 뒤 도크에 들어오고, 없으면 가장 먼저 돌아오는 트럭이 돌아오는 즉시 들어온다.
    /// 운행을 마친 트럭은 도크가 차 있으면(또는 교대 중이면) 차고에서 기다린다. 도크에 트럭이 없는 동안(IsAway) 레일 택배는 도크 앞에서 기다리고
    /// 손 배달은 거부된다(ConveyorSim·WorkerService). 시간은 GameSession이 근무 중에만 Advance로 흘린다(점심·일시정지 정지).
    /// 18:00에는 적재 중인 트럭이 모두 출발하고(DepartLoaded), 다음 날 09:00에 모든 차량이 돌아와 도크에 한 대가 빈 채로 선다(StartDay).
    /// </summary>
    public sealed class TruckFleet
    {
        public const int TruckCapacity = 30;
        public const float TruckReturnMinutes = 20f;
        /// <summary>차고의 트럭이 도크에 들어오는 교대 시간(게임 분, 스펙 20).</summary>
        public const float TruckSwapMinutes = 2f;
        public const int MaxTrucksPerDock = 3;
        /// <summary>도크마다 2대째·3대째 가격(스펙 20).</summary>
        public const int SecondTruckPrice = 400, ThirdTruckPrice = 900;
        /// <summary>트럭이 있는 도크(저장·뷰 순서). 입고 트럭은 해당 없다.</summary>
        public static readonly CellType[] Docks = { CellType.Outlet, CellType.ReturnDock, CellType.AirOutlet, CellType.AirReturnDock };

        private readonly int[] loaded = new int[4];
        private readonly int[] departedLoad = new int[4];
        private readonly List<Vehicle>[] vehicles = new List<Vehicle>[4];

        /// <summary>오늘 출발한 트럭 수(퇴근 출발 포함). 하루 요약용(스펙 18). StartDay가 0으로 되돌린다.</summary>
        public int DepartedToday { get; set; }

        /// <summary>트럭이 출발한 순간(30개째 적재, 또는 퇴근). 핸들러는 읽기만 해야 한다(Tick 중간일 수 있다).</summary>
        public event Action<CellType> TruckDeparted;
        /// <summary>도크에 빈 트럭이 선 순간(교대 끝, 운행에서 돌아옴, 또는 다음 날 09:00).</summary>
        public event Action<CellType> TruckArrived;

        public TruckFleet()
        {
            for (int i = 0; i < vehicles.Length; i++) vehicles[i] = new List<Vehicle> { new Vehicle { Status = VehicleStatus.AtDock } };
        }

        private static int Index(CellType dock)
        {
            int i = Array.IndexOf(Docks, dock);
            if (i < 0) throw new ArgumentException($"{dock} has no truck", nameof(dock));
            return i;
        }

        public int Loaded(CellType dock) => loaded[Index(dock)];

        /// <summary>그 도크에서 마지막으로 출발한 트럭이 싣고 간 개수(출발 연출의 알약용). 출발한 적이 없으면 0.</summary>
        public int DepartedLoad(CellType dock) => departedLoad[Index(dock)];

        /// <summary>도크의 차량 대수(1~3).</summary>
        public int Count(CellType dock) => vehicles[Index(dock)].Count;

        /// <summary>도크의 차량들(읽기 전용으로 쓴다).</summary>
        public IReadOnlyList<Vehicle> VehiclesOf(CellType dock) => vehicles[Index(dock)];

        private static Vehicle Find(List<Vehicle> vs, VehicleStatus status)
        {
            foreach (var v in vs) if (v.Status == status) return v;
            return null;
        }

        private static int CountOf(List<Vehicle> vs, VehicleStatus status)
        {
            int n = 0;
            foreach (var v in vs) if (v.Status == status) n++;
            return n;
        }

        /// <summary>도크에 트럭이 없다(모두 운행 중이거나 교대 중).</summary>
        public bool IsAway(CellType dock) => Find(vehicles[Index(dock)], VehicleStatus.AtDock) == null;

        /// <summary>차고의 트럭이 도크로 들어오는 중(교대)인가.</summary>
        public bool IsSwapping(CellType dock) => Find(vehicles[Index(dock)], VehicleStatus.Swapping) != null;

        /// <summary>차고에 대기 중인 트럭 수.</summary>
        public int GarageCount(CellType dock) => CountOf(vehicles[Index(dock)], VehicleStatus.Garage);

        /// <summary>도크에 트럭이 설 때까지 남은 게임 분: 교대 중이면 교대 남은 시간, 모두 운행 중이면 가장 먼저 돌아오는 트럭의 남은 시간. 트럭이 있으면 0.</summary>
        public float AwayMinutes(CellType dock)
        {
            var vs = vehicles[Index(dock)];
            if (Find(vs, VehicleStatus.AtDock) != null) return 0f;
            var swap = Find(vs, VehicleStatus.Swapping);
            if (swap != null) return swap.Minutes;
            float best = 0f;
            foreach (var v in vs)
                if (v.Status == VehicleStatus.Driving && (best <= 0f || v.Minutes < best)) best = v.Minutes;
            return best;
        }

        /// <summary>다음 차량(지금 대수 + 1대째)의 가격. 최대 대수면 null.</summary>
        public int? NextTruckPrice(CellType dock)
        {
            int n = Count(dock);
            return n >= MaxTrucksPerDock ? (int?)null : n == 1 ? SecondTruckPrice : ThirdTruckPrice;
        }

        /// <summary>차량 한 대를 더한다(구매). 새 트럭은 차고에 서고, 도크가 비어 있고 교대 중이 아니면 바로 교대를 시작한다. 최대 대수면 false.</summary>
        public bool AddVehicle(CellType dock)
        {
            int i = Index(dock);
            var vs = vehicles[i];
            if (vs.Count >= MaxTrucksPerDock) return false;
            var v = new Vehicle { Status = VehicleStatus.Garage };
            vs.Add(v);
            if (Find(vs, VehicleStatus.AtDock) == null && Find(vs, VehicleStatus.Swapping) == null) StartSwap(v);
            return true;
        }

        private static void StartSwap(Vehicle v)
        {
            v.Status = VehicleStatus.Swapping;
            v.Minutes = TruckSwapMinutes;
        }

        /// <summary>도크에 택배 하나를 실었다(판정 뒤). TruckCapacity가 되면 출발한다. 트럭이 없는 도크에는 실을 수 없다.</summary>
        public void Load(CellType dock)
        {
            int i = Index(dock);
            if (Find(vehicles[i], VehicleStatus.AtDock) == null) throw new InvalidOperationException($"the {dock} truck is away");
            if (++loaded[i] >= TruckCapacity) Depart(i);
        }

        /// <summary>도크의 트럭이 운행을 나간다. 차고에 트럭이 있으면 교대를 시작한다.</summary>
        private void Depart(int i)
        {
            var vs = vehicles[i];
            var atDock = Find(vs, VehicleStatus.AtDock);
            atDock.Status = VehicleStatus.Driving;
            atDock.Minutes = TruckReturnMinutes;
            departedLoad[i] = loaded[i];
            loaded[i] = 0;
            DepartedToday++;
            var spare = Find(vs, VehicleStatus.Garage);
            if (spare != null) StartSwap(spare);
            TruckDeparted?.Invoke(Docks[i]);
        }

        /// <summary>운행·교대 시간을 minutes 게임 분만큼 줄인다. 끝나는 차량은 끝나는 순서대로: 교대가 끝나면 도크에 서고, 운행이 끝나면 도크가 비어 있고
        /// 교대 중이 아닐 때만 도크에 서며 아니면 차고에서 기다린다. 도크에 트럭이 서면 TruckArrived. 0 이하·NaN·무한대는 무시.</summary>
        public void Advance(float minutes)
        {
            if (!(minutes > 0f) || float.IsInfinity(minutes)) return;
            for (int i = 0; i < vehicles.Length; i++)
            {
                var vs = vehicles[i];
                // 끝나는 차량을 끝나는 순서(남은 시간이 짧은 것부터, 같으면 목록 순서)로 모은다. 삽입 정렬이라 같은 값은 목록 순서를 지킨다.
                done.Clear();
                foreach (var v in vs)
                {
                    if (v.Status != VehicleStatus.Driving && v.Status != VehicleStatus.Swapping) continue;
                    if (v.Minutes > minutes) { v.Minutes -= minutes; continue; }
                    int k = done.Count;
                    done.Add(v);
                    while (k > 0 && done[k - 1].Minutes > v.Minutes) { done[k] = done[k - 1]; k--; }
                    done[k] = v;
                }
                for (int k = 0; k < done.Count; k++)
                {
                    var v = done[k];
                    v.Minutes = 0f;
                    bool docked = v.Status == VehicleStatus.Swapping ||
                        (Find(vs, VehicleStatus.AtDock) == null && Find(vs, VehicleStatus.Swapping) == null);
                    v.Status = docked ? VehicleStatus.AtDock : VehicleStatus.Garage;
                    if (docked) TruckArrived?.Invoke(Docks[i]);
                }
            }
            done.Clear();
        }

        /// <summary>Advance가 매번 다시 쓰는 목록(호출마다 할당하지 않는다).</summary>
        private readonly List<Vehicle> done = new List<Vehicle>(MaxTrucksPerDock);

        /// <summary>퇴근(18:00): 도크에 서 있고 적재 중인(1개 이상) 트럭을 모두 출발시킨다. 빈 트럭은 그대로, 운행 중인 트럭은 그대로 운행 중이다.
        /// 출발한 대수를 돌려준다.</summary>
        public int DepartLoaded()
        {
            int n = 0;
            for (int i = 0; i < loaded.Length; i++)
                if (loaded[i] > 0 && Find(vehicles[i], VehicleStatus.AtDock) != null) { Depart(i); n++; }
            return n;
        }

        /// <summary>다음 날 09:00: 모든 차량이 돌아와 도크마다 한 대가 빈 채로 서고 나머지는 차고에 있다. 도크에 트럭이 없던 곳은 TruckArrived.</summary>
        public void StartDay()
        {
            DepartedToday = 0;
            for (int i = 0; i < loaded.Length; i++)
            {
                loaded[i] = 0;
                var vs = vehicles[i];
                bool wasAway = Find(vs, VehicleStatus.AtDock) == null;
                for (int k = 0; k < vs.Count; k++)
                {
                    vs[k].Status = k == 0 ? VehicleStatus.AtDock : VehicleStatus.Garage;
                    vs[k].Minutes = 0f;
                }
                if (wasAway) TruckArrived?.Invoke(Docks[i]);
            }
        }

        /// <summary>저장 복원용(v9, 도크마다 1대). 적재 수는 0..TruckCapacity−1, 대기는 0..TruckReturnMinutes이고 떠나 있는 트럭은 비어 있어야 한다.</summary>
        public void Restore(CellType dock, int loadedCount, float awayMinutes)
        {
            if (!(awayMinutes >= 0f && awayMinutes <= TruckReturnMinutes)) throw new ArgumentOutOfRangeException(nameof(awayMinutes), awayMinutes, "wait outside 0..20");
            Restore(dock, loadedCount, new[]
            {
                awayMinutes > 0f ? new Vehicle { Status = VehicleStatus.Driving, Minutes = awayMinutes } : new Vehicle { Status = VehicleStatus.AtDock },
            });
        }

        /// <summary>저장 복원용(v10): 차량 1~3대. 도크에 선 트럭과 교대 중인 트럭은 합쳐 한 대까지, 운행은 0 초과 TruckReturnMinutes 이하,
        /// 교대는 0 초과 TruckSwapMinutes 이하, 도크·차고 차량의 남은 시간은 0. 적재는 도크에 트럭이 있을 때만.
        /// 도크가 비었는데 교대 중이 아니면 차고에 트럭이 있을 수 없다(그 트럭이 교대 중이었을 것). 어기면 ArgumentException.</summary>
        public void Restore(CellType dock, int loadedCount, IList<Vehicle> list)
        {
            int i = Index(dock);
            if (list == null || list.Count < 1 || list.Count > MaxTrucksPerDock) throw new ArgumentOutOfRangeException(nameof(list), list?.Count, "1..3 trucks per dock");
            if (loadedCount < 0 || loadedCount >= TruckCapacity) throw new ArgumentOutOfRangeException(nameof(loadedCount), loadedCount, "load outside 0..29");
            int atDock = 0, swapping = 0, garage = 0;
            var copy = new List<Vehicle>(list.Count);
            foreach (var v in list)
            {
                if (v == null) throw new ArgumentNullException(nameof(list), "null truck");
                switch (v.Status)
                {
                    case VehicleStatus.AtDock:
                    case VehicleStatus.Garage:
                        if (v.Minutes != 0f) throw new ArgumentOutOfRangeException(nameof(list), v.Minutes, "a parked truck has no timer");
                        if (v.Status == VehicleStatus.AtDock) atDock++; else garage++;
                        break;
                    case VehicleStatus.Driving:
                        if (!(v.Minutes > 0f && v.Minutes <= TruckReturnMinutes)) throw new ArgumentOutOfRangeException(nameof(list), v.Minutes, "trip outside (0,20]");
                        break;
                    case VehicleStatus.Swapping:
                        if (!(v.Minutes > 0f && v.Minutes <= TruckSwapMinutes)) throw new ArgumentOutOfRangeException(nameof(list), v.Minutes, "swap outside (0,2]");
                        swapping++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(list), v.Status, "unknown truck status");
                }
                copy.Add(new Vehicle { Status = v.Status, Minutes = v.Minutes });
            }
            if (atDock + swapping > 1) throw new ArgumentOutOfRangeException(nameof(list), atDock + swapping, "one truck at (or entering) the dock");
            if (loadedCount > 0 && atDock == 0) throw new ArgumentOutOfRangeException(nameof(loadedCount), loadedCount, "an away truck carries nothing");
            if (atDock + swapping == 0 && garage > 0) throw new ArgumentOutOfRangeException(nameof(list), garage, "a spare truck would be swapping in");
            loaded[i] = loadedCount;
            vehicles[i].Clear();
            vehicles[i].AddRange(copy);
        }
    }
}

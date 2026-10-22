using System;

namespace ParcelFactory.Core
{
    public enum BuildResult { Ok, Locked, Occupied, NotOwned, NotEnoughMoney, NotDemolishable, AlreadyDemolishing }

    /// <summary>설치된 부품 재설정 결과(스펙 11.8). NotOwned = 툴바가 그 방향을 보유하지 않음(레일·고속만 해당).</summary>
    public enum ReconfigureResult { Ok, NotConfigurable, Invalid, NotOwned, RequiresPlot2 }

    public sealed class BuildService
    {
        public const float DemolishSeconds = 2f;
        public const float RefundRate = 0.8f;

        /// <summary>float 누적 오차 허용치. 1.9+0.1 같은 합이 정확히 0이 되지 않는 문제를 흡수한다.</summary>
        private const float TimerEpsilon = 1e-4f;

        private readonly FactoryGrid grid;
        private readonly Economy economy;
        private readonly Toolbar toolbar;
        private readonly ConveyorSim sim;

        public event Action<int, int> CellChanged;

        public BuildService(FactoryGrid grid, Economy economy, Toolbar toolbar, ConveyorSim sim)
        {
            this.grid = grid;
            this.economy = economy;
            this.toolbar = toolbar;
            this.sim = sim;
        }

        public static int RefundFor(CellType type) =>
            (int)Math.Round(BuildingCosts.Cost(type) * RefundRate);

        /// <summary>TryPlace와 같은 검사를 하되 아무것도 바꾸지 않는다. UI 미리보기용.</summary>
        public BuildResult Check(int x, int y, CellType type, Direction dir)
        {
            if (!grid.IsUnlocked(x, y)) return BuildResult.Locked;
            if (grid.Get(x, y).Type != CellType.Empty) return BuildResult.Occupied;
            if (!toolbar.Owns(type, dir)) return BuildResult.NotOwned;
            if (!economy.CanAfford(BuildingCosts.Cost(type))) return BuildResult.NotEnoughMoney;
            return BuildResult.Ok;
        }

        public BuildResult TryPlace(int x, int y, CellType type, Direction dir)
        {
            var result = Check(x, y, type, dir);
            if (result != BuildResult.Ok) return result;
            economy.TrySpend(BuildingCosts.Cost(type));
            grid.Place(x, y, type, dir);
            CellChanged?.Invoke(x, y);
            return BuildResult.Ok;
        }

        public BuildResult BeginDemolish(int x, int y)
        {
            if (!grid.InBounds(x, y)) return BuildResult.NotDemolishable;
            var c = grid.Get(x, y);
            if (!c.IsBuilding) return BuildResult.NotDemolishable;
            if (c.Demolishing) return BuildResult.AlreadyDemolishing;
            if (c.Type == CellType.Pallet && c.Pallet.Count > 0) return BuildResult.Occupied; // 더미가 있으면 해체 불가
            c.Demolishing = true;
            c.DemolishRemaining = DemolishSeconds;
            CellChanged?.Invoke(x, y);
            return BuildResult.Ok;
        }

        private static bool HasDirectionSetting(CellType t) =>
            t == CellType.Conveyor || t == CellType.FastConveyor || t == CellType.Table ||
            t == CellType.Scanner || t == CellType.Splitter || t == CellType.Bridge || t == CellType.Labeler;

        /// <summary>설치된 레일·고속·검사대·자동 검사기·분배기(파랑)·브릿지(윗길)의 방향을 그 자리에서 바꾼다(스펙 11.8). 비용 없음.
        /// 그 칸 위 택배의 Heading도 새로 정한다. 실제로 바뀐 경우에만 CellChanged.</summary>
        public ReconfigureResult SetDirection(int x, int y, Direction dir)
        {
            if (!grid.InBounds(x, y) || !Enum.IsDefined(typeof(Direction), dir)) return ReconfigureResult.NotConfigurable;
            var c = grid.Get(x, y);
            if (!HasDirectionSetting(c.Type)) return ReconfigureResult.NotConfigurable;
            if (c.Demolishing) return ReconfigureResult.Invalid;
            if (c.Direction == dir) return ReconfigureResult.Ok;
            if (!toolbar.Owns(c.Type, dir)) return ReconfigureResult.NotOwned;
            c.Direction = dir;
            // 항공 모드 검사기: 1번 출구가 항공 출구와 겹치면 항공 출구를 새 1번 기준 기본값으로 옮긴다(스펙 14.5).
            if (c.Type == CellType.Scanner && c.Mode == RouteMode.Air && c.AirDirection == dir)
                c.AirDirection = ConveyorSim.DefaultScannerAirExit(grid, x, y);
            sim.RefreshHeadingsAt(x, y);
            CellChanged?.Invoke(x, y);
            return ReconfigureResult.Ok;
        }

        /// <summary>분배기의 빨간 출구를 명시 설정한다(스펙 11.8). null이면 자동 규칙으로 돌아간다. 파랑과 같은 방향은 Invalid.</summary>
        public ReconfigureResult SetSplitterRed(int x, int y, Direction? dir)
        {
            if (!grid.InBounds(x, y)) return ReconfigureResult.NotConfigurable;
            var c = grid.Get(x, y);
            if (c.Type != CellType.Splitter) return ReconfigureResult.NotConfigurable;
            if (c.Demolishing) return ReconfigureResult.Invalid;
            if (dir.HasValue && (!Enum.IsDefined(typeof(Direction), dir.Value) || dir.Value == c.Direction))
                return ReconfigureResult.Invalid;
            if (c.RedDirection == dir) return ReconfigureResult.Ok;
            c.RedDirection = dir;
            sim.RefreshHeadingsAt(x, y);
            CellChanged?.Invoke(x, y);
            return ReconfigureResult.Ok;
        }

        /// <summary>분배기·자동 검사기의 모드를 바꾼다(스펙 14.5, 무료). 항공 도크가 없는 공장(부지 확장 2 전; 공항 물류센터는 처음부터 있다, 19.2)에서는
        /// RequiresPlot2(이름은 예전 그대로: "항공 도크가 생긴 뒤").
        /// 검사기를 항공 모드로 처음 바꾸면 항공 출구를 기본 규칙(오른쪽, 안 되면 왼쪽)으로 정한다. 그 칸 위 택배의 진행 방향도 다시 정한다.</summary>
        public ReconfigureResult SetMode(int x, int y, RouteMode mode)
        {
            if (!grid.InBounds(x, y) || !Enum.IsDefined(typeof(RouteMode), mode)) return ReconfigureResult.NotConfigurable;
            var c = grid.Get(x, y);
            if (c.Type != CellType.Splitter && c.Type != CellType.Scanner) return ReconfigureResult.NotConfigurable;
            if (c.Demolishing) return ReconfigureResult.Invalid;
            if (!grid.HasAirDocks) return ReconfigureResult.RequiresPlot2;
            if (c.Mode == mode) return ReconfigureResult.Ok;
            c.Mode = mode;
            if (mode == RouteMode.Air && c.Type == CellType.Scanner && !c.AirDirection.HasValue)
                c.AirDirection = ConveyorSim.DefaultScannerAirExit(grid, x, y);
            sim.RefreshHeadingsAt(x, y);
            CellChanged?.Invoke(x, y);
            return ReconfigureResult.Ok;
        }

        /// <summary>자동 검사기의 항공 출구(2번)를 정한다(스펙 14.5). 1번 출구(Direction)와 같으면 Invalid.</summary>
        public ReconfigureResult SetScannerAirExit(int x, int y, Direction dir)
        {
            if (!grid.InBounds(x, y)) return ReconfigureResult.NotConfigurable;
            var c = grid.Get(x, y);
            if (c.Type != CellType.Scanner) return ReconfigureResult.NotConfigurable;
            if (c.Demolishing || !Enum.IsDefined(typeof(Direction), dir) || dir == c.Direction) return ReconfigureResult.Invalid;
            if (c.AirDirection == dir) return ReconfigureResult.Ok;
            c.AirDirection = dir;
            sim.RefreshHeadingsAt(x, y);
            CellChanged?.Invoke(x, y);
            return ReconfigureResult.Ok;
        }

        /// <summary>팔레트 종류(파랑/빨강)를 바꾼다(스펙 11.8). None은 Invalid. 색은 안내용이라 더미가 있어도 바꿀 수 있다.</summary>
        public ReconfigureResult SetPalletKind(int x, int y, PalletKind kind)
        {
            if (!grid.InBounds(x, y)) return ReconfigureResult.NotConfigurable;
            var c = grid.Get(x, y);
            if (c.Type != CellType.Pallet) return ReconfigureResult.NotConfigurable;
            if (c.Demolishing) return ReconfigureResult.Invalid;
            if (kind != PalletKind.Blue && kind != PalletKind.Red) return ReconfigureResult.Invalid;
            if (c.PalletKind == kind) return ReconfigureResult.Ok;
            c.PalletKind = kind;
            c.Direction = kind == PalletKind.Blue ? Direction.East : Direction.West; // 팔레트 도구 방향 규약(Tool.KindOf)과 맞춘다
            CellChanged?.Invoke(x, y);
            return ReconfigureResult.Ok;
        }

        public void Tick(float dt)
        {
            for (int x = 0; x < FactoryGrid.Width; x++)
            {
                for (int y = 0; y < FactoryGrid.Height; y++)
                {
                    var c = grid.Get(x, y);
                    if (!c.Demolishing) continue;
                    c.DemolishRemaining -= dt;
                    if (c.DemolishRemaining > TimerEpsilon) continue;
                    c.DemolishRemaining = 0f;

                    int refund = RefundFor(c.Type);
                    sim.RemovePackagesAt(x, y);
                    grid.Clear(x, y);
                    economy.Earn(refund);
                    CellChanged?.Invoke(x, y);
                }
            }
        }
    }
}

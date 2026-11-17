using System;
using System.Collections.Generic;
using BusTraffic.Data;
using UnityEngine;

namespace BusTraffic.Core
{
    /// <summary>
    /// GridManager — nguồn sự thật duy nhất (single source of truth) của trạng thái bãi đỗ, lưu trong mảng GridCell[,].
    ///
    /// THUẬT TOÁN:
    ///   1. Lưới width x height (mặc định 6 x 6, cellSize = 1.2). Hàng y = height (nằm ngay phía trên lưới) là
    ///      EXIT ROW dẫn ra bến đỗ; một xe "thoát" được khi chạm tới hàng trên cùng của lưới (y == height - 1).
    ///   2. FindEscapePath (BFS — Breadth-First Search):
    ///      a. Xác định mũi xe = ô có tích vô hướng lớn nhất với forwardDirection.
    ///      b. Kiểm tra "tiến thẳng ra khỏi vùng kẹt": ô ngay trước mũi xe phải nằm trong lưới và trống
    ///         (ngoại lệ: mũi xe đã ở hàng trên cùng và hướng lên → ra thẳng Exit Row).
    ///      c. Từ ô đó chạy BFS 4 hướng qua các ô có IsOccupied == false; vì BFS duyệt theo lớp khoảng cách,
    ///         ô đích đầu tiên có y == height - 1 được lấy ra chính là đường đi NGẮN NHẤT.
    ///      d. Dựng lại đường đi bằng bảng cameFrom (truy ngược từ đích về ô xuất phát rồi đảo chiều).
    ///      Độ phức tạp: O(W * H) thời gian và bộ nhớ — chỉ chạy MỘT lần tại frame click, không chạy mỗi frame.
    ///   3. BuildStationRoute: đổi List&lt;Vector2Int&gt; (ô lưới) → List&lt;Vector3&gt; Waypoints toạ độ thế giới:
    ///      các ô trên đường BFS → điểm Exit Row cùng cột → chạy ngang dọc Exit Row tới cột bến → điểm đỗ tại bến.
    ///   4. Mọi kiểm tra chặn đường tính toán trên GridCell[,]; KHÔNG dùng Physics Raycast / Collider.
    ///
    /// LUỒNG XỬ LÝ:
    ///   Awake: Initialize() đọc LevelData (nếu có) → tạo GridCell[,].
    ///   BusController.Start → TryOccupyCells (đánh dấu ô xe đỗ).
    ///   BusController click → FindEscapePath → (có đường) ReleaseCells ngay lập tức → BuildStationRoute → Waypoints.
    ///   Mỗi lần lưới thay đổi phát sự kiện OnGridChanged cho các lớp hiển thị / debug quan tâm.
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - KHÔNG có Update(): không tốn CPU ở các frame bình thường.
    ///   - Event-driven: trạng thái GridCell[,] chỉ đổi tại đúng frame N người chơi click chọn xe thành công
    ///     (BusController gọi ReleaseCells) — các ô cũ được giải phóng ngay trong frame đó để xe khác đi qua được.
    ///   - OnDrawGizmos chỉ chạy trong Editor để vẽ lưới debug (ô bận đỏ, ô trống xanh, Exit Row vàng, bến cyan).
    /// </summary>
    [DisallowMultipleComponent]
    public class GridManager : MonoBehaviour
    {
        // Thứ tự duyệt láng giềng của BFS: ưu tiên đi lên (về phía Exit Row) để khi có nhiều đường ngắn nhất
        // cùng độ dài, đường được chọn trông "thẳng" và tự nhiên hơn.
        private static readonly Vector2Int[] NeighbourDirections =
        {
            Vector2Int.up, Vector2Int.left, Vector2Int.right, Vector2Int.down
        };

        [Header("Cấu hình lưới (bị ghi đè bởi LevelData nếu được gán)")]
        [SerializeField] private LevelData levelData;
        [SerializeField, Min(1)] private int width = 6;
        [SerializeField, Min(1)] private int height = 6;
        [SerializeField, Min(0.1f)] private float cellSize = 1.2f;
        [Tooltip("Toạ độ thế giới của tâm ô (0, 0).")]
        [SerializeField] private Vector3 origin = Vector3.zero;

        [Header("Bến đỗ (Station) phía trên Exit Row")]
        [Tooltip("Khoảng cách (tính theo số ô) từ Exit Row tới điểm đỗ xe tại bến.")]
        [SerializeField, Min(0.5f)] private float stationRowOffset = 1f;
        [Tooltip("Quãng đường xe chạy thẳng khỏi bến để ra khỏi màn hình.")]
        [SerializeField, Min(1f)] private float leaveDistance = 12f;

        private GridCell[,] cells;

        /// <summary>Phát ra mỗi khi có ô bị chiếm / giải phóng.</summary>
        public event Action OnGridChanged;

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;

        /// <summary>Hàng thoát ảo nằm ngay phía trên lưới (y = height).</summary>
        public int ExitRowY => height;

        /// <summary>Hàng trên cùng của lưới (y = height - 1) — đích của BFS.</summary>
        public int TopRowY => height - 1;

        /// <summary>Điểm đỗ xe tại bến: giữa bề ngang lưới, cách Exit Row stationRowOffset ô.</summary>
        public Vector3 StationPosition =>
            origin + new Vector3((width - 1) * cellSize * 0.5f, 0f, (ExitRowY + stationRowOffset) * cellSize);

        /// <summary>Điểm xe chạy tới khi rời bến (ra khỏi màn hình).</summary>
        public Vector3 LeavePosition => StationPosition + Vector3.forward * leaveDistance;

        private void Awake()
        {
            Initialize();
        }

        /// <summary>Đồng bộ cấu hình từ LevelData và tạo mới mảng GridCell[,] (toàn bộ ô trống).</summary>
        public void Initialize()
        {
            if (levelData != null)
            {
                width = levelData.Width;
                height = levelData.Height;
                cellSize = levelData.CellSize;
            }

            cells = new GridCell[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    cells[x, y] = new GridCell(new Vector2Int(x, y));
                }
            }
        }

        // ------------------------------------------------------------------
        // API chuyển đổi toạ độ & truy vấn ô
        // ------------------------------------------------------------------

        public Vector3 GridToWorld(Vector2Int cell)
        {
            return origin + new Vector3(cell.x * cellSize, 0f, cell.y * cellSize);
        }

        public Vector2Int WorldToGrid(Vector3 worldPos)
        {
            Vector3 local = worldPos - origin;
            return new Vector2Int(Mathf.RoundToInt(local.x / cellSize), Mathf.RoundToInt(local.z / cellSize));
        }

        public bool IsInside(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
        }

        public bool IsWalkable(Vector2Int cell)
        {
            EnsureInitialized();
            return IsInside(cell) && !cells[cell.x, cell.y].IsOccupied;
        }

        public GridCell GetCell(Vector2Int cell)
        {
            EnsureInitialized();
            return IsInside(cell) ? cells[cell.x, cell.y] : null;
        }

        // ------------------------------------------------------------------
        // Cập nhật trạng thái lưới (event-driven)
        // ------------------------------------------------------------------

        /// <summary>
        /// Đánh dấu danh sách ô là bị chiếm bởi occupant. Kiểm tra TOÀN BỘ trước rồi mới ghi
        /// (all-or-nothing) để không để lưới ở trạng thái nửa vời khi có xung đột.
        /// </summary>
        public bool TryOccupyCells(IReadOnlyList<Vector2Int> cellList, UnityEngine.Object occupant)
        {
            EnsureInitialized();

            // Bước 1: xác thực — mọi ô phải nằm trong lưới và trống (hoặc đã thuộc chính occupant).
            for (int i = 0; i < cellList.Count; i++)
            {
                Vector2Int c = cellList[i];
                if (!IsInside(c))
                {
                    Debug.LogWarning($"[GridManager] Ô {c} nằm ngoài lưới {width}x{height}.", occupant);
                    return false;
                }

                GridCell cell = cells[c.x, c.y];
                if (cell.IsOccupied && cell.Occupant != occupant)
                {
                    Debug.LogWarning($"[GridManager] Ô {c} đã bị {cell.Occupant} chiếm.", occupant);
                    return false;
                }
            }

            // Bước 2: ghi trạng thái.
            for (int i = 0; i < cellList.Count; i++)
            {
                Vector2Int c = cellList[i];
                cells[c.x, c.y].Occupy(occupant);
            }

            OnGridChanged?.Invoke();
            return true;
        }

        /// <summary>Giải phóng các ô đang thuộc occupant (bỏ qua ô của đối tượng khác).</summary>
        public void ReleaseCells(IReadOnlyList<Vector2Int> cellList, UnityEngine.Object occupant)
        {
            EnsureInitialized();

            bool changed = false;
            for (int i = 0; i < cellList.Count; i++)
            {
                GridCell cell = GetCell(cellList[i]);
                if (cell != null && cell.Occupant == occupant)
                {
                    cell.Release();
                    changed = true;
                }
            }

            if (changed)
            {
                OnGridChanged?.Invoke();
            }
        }

        // ------------------------------------------------------------------
        // Tìm đường BFS
        // ------------------------------------------------------------------

        /// <summary>
        /// Tìm đường thoát ngắn nhất cho một xe.
        /// Trả về danh sách ô (bắt đầu từ ô ngay trước mũi xe, kết thúc ở hàng y == height - 1),
        /// hoặc null nếu xe bị kẹt.
        /// </summary>
        public List<Vector2Int> FindEscapePath(List<Vector2Int> busOccupiedCells, Vector2Int forwardDirection)
        {
            if (busOccupiedCells == null || busOccupiedCells.Count == 0 || forwardDirection == Vector2Int.zero)
            {
                return null;
            }

            EnsureInitialized();

            // Bước 1: mũi xe = ô có tích vô hướng lớn nhất theo hướng đầu xe.
            Vector2Int head = busOccupiedCells[0];
            int bestDot = Dot(head, forwardDirection);
            for (int i = 1; i < busOccupiedCells.Count; i++)
            {
                int d = Dot(busOccupiedCells[i], forwardDirection);
                if (d > bestDot)
                {
                    bestDot = d;
                    head = busOccupiedCells[i];
                }
            }

            // Bước 2: kiểm tra xe có tiến thẳng ra khỏi chỗ đỗ (vùng kẹt) được không.
            Vector2Int start = head + forwardDirection;
            if (!IsInside(start))
            {
                // Mũi xe đã ở hàng trên cùng và hướng thẳng lên Exit Row → thoát ngay.
                if (start.y == ExitRowY && head.y == TopRowY)
                {
                    return new List<Vector2Int> { head };
                }

                // Hướng ra mép trái / phải / dưới của bãi: không phải lối ra → kẹt.
                return null;
            }

            if (!IsWalkable(start))
            {
                return null; // Bị xe khác chắn đầu.
            }

            if (start.y == TopRowY)
            {
                return new List<Vector2Int> { start };
            }

            // Bước 3: BFS từ ô xuất phát qua các ô trống tới hàng trên cùng.
            // Lưu ý: các ô của chính xe vẫn đang IsOccupied (chưa giải phóng) nên BFS không "quay đầu" xuyên qua thân xe.
            Queue<Vector2Int> frontier = new Queue<Vector2Int>();
            Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            frontier.Enqueue(start);
            cameFrom[start] = start;

            while (frontier.Count > 0)
            {
                Vector2Int current = frontier.Dequeue();

                // Ô đích đầu tiên được lấy ra khỏi hàng đợi luôn có khoảng cách nhỏ nhất (tính chất BFS).
                if (current.y == TopRowY)
                {
                    return ReconstructPath(cameFrom, start, current);
                }

                for (int i = 0; i < NeighbourDirections.Length; i++)
                {
                    Vector2Int next = current + NeighbourDirections[i];
                    if (cameFrom.ContainsKey(next) || !IsWalkable(next))
                    {
                        continue; // Đã thăm, ngoài lưới hoặc bị chiếm.
                    }

                    cameFrom[next] = current;
                    frontier.Enqueue(next);
                }
            }

            return null; // Không tồn tại đường tới hàng thoát.
        }

        /// <summary>
        /// Đổi đường đi BFS (ô lưới) thành Waypoints toạ độ thế giới dẫn xe tới bến:
        /// ô BFS → Exit Row (cùng cột) → chạy ngang Exit Row tới cột bến → điểm đỗ.
        /// </summary>
        public List<Vector3> BuildStationRoute(List<Vector2Int> path)
        {
            List<Vector3> route = new List<Vector3>(path.Count + 3);
            for (int i = 0; i < path.Count; i++)
            {
                AddIfDistinct(route, GridToWorld(path[i]));
            }

            Vector2Int last = path[path.Count - 1];
            Vector3 exitPoint = GridToWorld(new Vector2Int(last.x, ExitRowY));
            Vector3 station = StationPosition;

            AddIfDistinct(route, exitPoint);
            AddIfDistinct(route, new Vector3(station.x, exitPoint.y, exitPoint.z));
            AddIfDistinct(route, station);
            return route;
        }

        // ------------------------------------------------------------------
        // Tiện ích nội bộ
        // ------------------------------------------------------------------

        private static int Dot(Vector2Int a, Vector2Int b)
        {
            return a.x * b.x + a.y * b.y;
        }

        private static List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int start, Vector2Int goal)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            Vector2Int step = goal;
            while (step != start)
            {
                path.Add(step);
                step = cameFrom[step];
            }

            path.Add(start);
            path.Reverse();
            return path;
        }

        private static void AddIfDistinct(List<Vector3> route, Vector3 point)
        {
            if (route.Count == 0 || (route[route.Count - 1] - point).sqrMagnitude > 0.0001f)
            {
                route.Add(point);
            }
        }

        private void EnsureInitialized()
        {
            if (cells == null || cells.GetLength(0) != width || cells.GetLength(1) != height)
            {
                Initialize();
            }
        }

        private void OnDrawGizmos()
        {
            Vector3 cellGizmoSize = new Vector3(cellSize * 0.95f, 0.02f, cellSize * 0.95f);
            bool hasData = cells != null && cells.GetLength(0) == width && cells.GetLength(1) == height;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool occupied = hasData && cells[x, y].IsOccupied;
                    Gizmos.color = occupied ? new Color(1f, 0.3f, 0.3f, 0.9f) : new Color(0.3f, 1f, 0.3f, 0.35f);
                    Gizmos.DrawWireCube(GridToWorld(new Vector2Int(x, y)), cellGizmoSize);
                }

                Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
                Gizmos.DrawWireCube(GridToWorld(new Vector2Int(x, ExitRowY)), cellGizmoSize);
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(StationPosition, cellSize * 0.4f);
        }
    }
}

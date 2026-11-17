using UnityEngine;

namespace BusTraffic.Core
{
    /// <summary>
    /// GridCell — lớp DỮ LIỆU THUẦN (không kế thừa MonoBehaviour) mô tả một ô trong lưới bãi đỗ.
    ///
    /// THUẬT TOÁN:
    ///   - Lưu trạng thái chiếm chỗ (IsOccupied) và đối tượng đang chiếm (Occupant).
    ///   - Là đơn vị dữ liệu mà BFS của GridManager duyệt qua: ô đi được ⇔ IsOccupied == false.
    ///
    /// LUỒNG XỬ LÝ:
    ///   GridManager tạo mảng GridCell[,] → BusController yêu cầu Occupy (khi đỗ) / Release (khi rời ô)
    ///   thông qua GridManager.TryOccupyCells / ReleaseCells → FindEscapePath đọc IsOccupied.
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - Không có vòng đời frame. Trạng thái chỉ thay đổi theo sự kiện (event-driven) tại frame người chơi click xe.
    /// </summary>
    public class GridCell
    {
        public GridCell(Vector2Int position)
        {
            Position = position;
        }

        /// <summary>Toạ độ (x, y) của ô trong lưới.</summary>
        public Vector2Int Position { get; }

        /// <summary>true nếu ô đang bị một xe chiếm — BFS sẽ không đi qua ô này.</summary>
        public bool IsOccupied { get; private set; }

        /// <summary>Đối tượng đang chiếm ô (thường là BusController); null nếu ô trống.</summary>
        public Object Occupant { get; private set; }

        public void Occupy(Object occupant)
        {
            IsOccupied = true;
            Occupant = occupant;
        }

        public void Release()
        {
            IsOccupied = false;
            Occupant = null;
        }
    }
}

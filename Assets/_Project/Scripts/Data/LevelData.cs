using System.Collections.Generic;
using UnityEngine;

namespace BusTraffic.Data
{
    /// <summary>
    /// LevelData — ScriptableObject cấu hình một màn chơi: kích thước lưới, sức chứa xe, vị trí các xe buýt,
    /// và thứ tự màu của hàng hành khách tại bến.
    ///
    /// THUẬT TOÁN:
    ///   - Không có thuật toán; là nguồn cấu hình duy nhất (không hard-code trong script gameplay).
    ///
    /// LUỒNG XỬ LÝ:
    ///   Designer chỉnh asset (vd. Level_01_Data) → GridManager đọc Width/Height/CellSize khi Initialize
    ///   → LevelBuilder đọc Buses / PassengerColors / BusCapacity để sinh xe, hành khách và cấu hình PassengerQueue.
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - Asset dữ liệu, không có Update / Coroutine; chỉ được đọc khi khởi tạo / dựng màn.
    /// </summary>
    [CreateAssetMenu(fileName = "Level_XX_Data", menuName = "BusTraffic/Level Data")]
    public class LevelData : ScriptableObject
    {
        [Header("Lưới bãi đỗ")]
        [SerializeField, Min(1)] private int width = 6;
        [SerializeField, Min(1)] private int height = 6;
        [SerializeField, Min(0.1f)] private float cellSize = 1.2f;

        [Header("Xe buýt")]
        [SerializeField, Min(1)] private int busCapacity = 4;
        [SerializeField] private List<BusPlacement> buses = new List<BusPlacement>();

        [Header("Hàng hành khách (phần tử 0 = đầu hàng)")]
        [SerializeField] private List<BusColor> passengerColors = new List<BusColor>();

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;
        public int BusCapacity => busCapacity;
        public IReadOnlyList<BusPlacement> Buses => buses;
        public IReadOnlyList<BusColor> PassengerColors => passengerColors;
    }
}

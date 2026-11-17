using System;
using System.Collections.Generic;
using UnityEngine;

namespace BusTraffic.Data
{
    /// <summary>
    /// BusPlacement — mô tả vị trí đặt một xe buýt trên lưới trong LevelData.
    ///
    /// THUẬT TOÁN:
    ///   - Suy ra danh sách ô xe chiếm từ ô đầu xe và hướng đầu xe:
    ///       cell[i] = headCell - forward * i  (i = 0 .. length-1), phần tử 0 luôn là mũi xe.
    ///
    /// LUỒNG XỬ LÝ:
    ///   Designer cấu hình trong LevelData → LevelBuilder đọc → BusController.Configure(placement)
    ///   → BusController đăng ký các ô chiếm với GridManager.
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - Dữ liệu thuần được serialize, không tham gia vòng đời frame.
    /// </summary>
    [Serializable]
    public class BusPlacement
    {
        [SerializeField] private BusColor color = BusColor.Red;

        [Tooltip("Ô chứa mũi xe (đầu xe) trên lưới.")]
        [SerializeField] private Vector2Int headCell;

        [Tooltip("Hướng đầu xe: (0,1) lên, (0,-1) xuống, (1,0) phải, (-1,0) trái.")]
        [SerializeField] private Vector2Int forward = Vector2Int.up;

        [Tooltip("Số ô xe chiếm dọc theo hướng đầu xe.")]
        [SerializeField, Min(1)] private int length = 2;

        public BusColor Color => color;
        public Vector2Int HeadCell => headCell;
        public Vector2Int Forward => forward;
        public int Length => length;

        /// <summary>Ghi các ô xe chiếm vào buffer (phần tử 0 = mũi xe). Không cấp phát bộ nhớ mới.</summary>
        public void GetOccupiedCells(List<Vector2Int> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < length; i++)
            {
                buffer.Add(headCell - forward * i);
            }
        }
    }
}

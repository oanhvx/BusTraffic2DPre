using System;
using System.Collections.Generic;
using UnityEngine;

namespace BusTraffic.Data
{
    /// <summary>
    /// ColorPalette — ScriptableObject ánh xạ BusColor → Material (Mat_Red, Mat_Blue, Mat_Yellow, Mat_Green).
    ///
    /// THUẬT TOÁN:
    ///   - Tra cứu tuyến tính trên danh sách nhỏ (4 phần tử) để lấy Material theo BusColor: O(n) với n = 4.
    ///
    /// LUỒNG XỬ LÝ:
    ///   LevelBuilder gọi GetMaterial(color) khi dựng màn → gán sharedMaterial cho Renderer của xe buýt / hành khách.
    ///   Không phụ thuộc lớp Core / Gameplay (đúng hướng phụ thuộc Gameplay → Core → Data).
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - Asset dữ liệu, không có Update / Coroutine. Chỉ được đọc tại thời điểm dựng màn (Editor hoặc lúc khởi tạo).
    /// </summary>
    [CreateAssetMenu(fileName = "ColorPalette_Default", menuName = "BusTraffic/Color Palette")]
    public class ColorPalette : ScriptableObject
    {
        /// <summary>
        /// Entry — một cặp (BusColor, Material) trong bảng màu.
        ///
        /// THUẬT TOÁN: không có; dữ liệu thuần được Unity serialize.
        /// LUỒNG XỬ LÝ: được ColorPalette.GetMaterial duyệt để tìm Material theo màu.
        /// VÒNG ĐỜI FRAME: không tham gia vòng đời frame.
        /// </summary>
        [Serializable]
        public class Entry
        {
            [SerializeField] private BusColor color;
            [SerializeField] private Material material;

            public BusColor Color => color;
            public Material Material => material;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        /// <summary>Trả về Material tương ứng với màu; null (kèm cảnh báo) nếu chưa cấu hình.</summary>
        public Material GetMaterial(BusColor color)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry != null && entry.Color == color)
                {
                    return entry.Material;
                }
            }

            Debug.LogWarning($"[ColorPalette] Chưa cấu hình Material cho màu {color}.", this);
            return null;
        }
    }
}

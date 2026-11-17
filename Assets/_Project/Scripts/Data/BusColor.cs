namespace BusTraffic.Data
{
    /// <summary>
    /// BusColor — enum màu DUY NHẤT của dự án, dùng chung cho Xe buýt (BusController) và Hành khách (Passenger).
    ///
    /// THUẬT TOÁN:
    ///   - Không có thuật toán riêng; đóng vai trò khoá so khớp màu (so sánh enum, O(1))
    ///     khi PassengerQueue ghép hành khách đầu hàng với xe buýt đang đỗ ở bến.
    ///
    /// LUỒNG XỬ LÝ:
    ///   LevelData / ColorPalette lưu BusColor → LevelBuilder đọc khi dựng màn → gán cho BusController & Passenger
    ///   → PassengerQueue so sánh Passenger.PassengerColor == BusController.BusColor.
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - Kiểu giá trị thuần, không tham gia vòng đời frame (không Update / Coroutine).
    /// </summary>
    public enum BusColor
    {
        Red,
        Blue,
        Yellow,
        Green
    }
}

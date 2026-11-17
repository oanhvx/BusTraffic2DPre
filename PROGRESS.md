# PROGRESS.md — Tiến độ dự án "Bus Traffic Puzzle"

> **Cập nhật lần cuối:** 2026-11-17

---

## 1. Trạng thái hiện tại

- Đã hoàn thành bộ quy tắc kiến trúc chuẩn (`GEMINI.md`).
- Đã hoàn thành lập trình cơ chế cốt lõi (Data, Core, Gameplay) và dựng cảnh thô (Greyboxing) cho màn chơi mẫu `Level_01`.
- Đã cấu hình `UniversalRenderer` (3D Forward Renderer) cho URP để hiển thị đầy đủ khối 3D và ánh sáng.
- Đã kiểm tra `refresh_unity` & `read_console`: **0 lỗi biên dịch (0 Compile Errors)**, Scene `Level_01` đã lưu hoàn chỉnh.

---

## 2. Danh sách tài nguyên đã tạo

### Scripts (`Assets/_Project/Scripts/`)
- **Core (`BusTraffic.Core`):**
  - `Assets/_Project/Scripts/Core/GridManager.cs`
  - `Assets/_Project/Scripts/Core/GridCell.cs`
  - `Assets/_Project/Scripts/Core/GameManager.cs`
- **Gameplay (`BusTraffic.Gameplay`):**
  - `Assets/_Project/Scripts/Gameplay/BusController.cs`
  - `Assets/_Project/Scripts/Gameplay/PassengerQueue.cs`
  - `Assets/_Project/Scripts/Gameplay/Passenger.cs`
  - `Assets/_Project/Scripts/Gameplay/LevelBuilder.cs`
- **Data (`BusTraffic.Data`):**
  - `Assets/_Project/Scripts/Data/BusColor.cs`
  - `Assets/_Project/Scripts/Data/ColorPalette.cs`
  - `Assets/_Project/Scripts/Data/BusPlacement.cs`
  - `Assets/_Project/Scripts/Data/LevelData.cs`

### Prefabs (`Assets/_Project/Prefabs/`)
- `Assets/_Project/Prefabs/Bus_Prefab.prefab` (Cube `0.9 x 0.8 x 2.1`, `BoxCollider`, `BusController`, child `Windshield`)
- `Assets/_Project/Prefabs/Passenger_Prefab.prefab` (Capsule `0.5 x 0.6 x 0.5`, `Passenger`, không gắn Collider)
- `Assets/_Project/Prefabs/GridCell.prefab` (Ô lưới hiển thị Visual-only, không gắn Collider)

### Materials (`Assets/_Project/Materials/`)
- `Assets/_Project/Materials/Mat_Red.mat`
- `Assets/_Project/Materials/Mat_Blue.mat`
- `Assets/_Project/Materials/Mat_Yellow.mat`
- `Assets/_Project/Materials/Mat_Green.mat`
- `Assets/_Project/Materials/Mat_Ground.mat`
- `Assets/_Project/Materials/Mat_GridCell.mat`

### ScriptableObjects & Settings
- `Assets/_Project/ScriptableObjects/ColorPalette_Default.asset`
- `Assets/_Project/ScriptableObjects/Level_01_Data.asset`
- `Assets/_Project/Settings/UniversalRenderer.asset`

### Scenes (`Assets/_Project/Scenes/`)
- `Assets/_Project/Scenes/Level_01.unity`

---

## 3. Kiến trúc & Thuật toán đang áp dụng

### Luồng giao tiếp chính
1. **Khởi tạo:** `GridManager` đọc `Level_01_Data` khởi tạo mảng dữ liệu thuần `GridCell[6, 6]`. Các `BusController` đăng ký chiếm ô qua `GridManager.TryOccupyCells` và đăng ký số lượng xe với `GameManager`. `PassengerQueue` nạp 12 `Passenger` vào `Queue<Passenger>` (FIFO).
2. **Tương tác & Tìm đường (BFS):** Khi người chơi click xe (`OnMouseDown` -> `TrySelect`), `BusController` gọi `GridManager.FindEscapePath(occupiedCells, forwardDirection)`:
   - Nếu ô trước mũi xe bị chặn hoặc không có đường tới hàng thoát (`y == height - 1`): giữ trạng thái `Idle` và chạy Coroutine `ShakeRoutine`.
   - Nếu có đường thoát và `GameManager.TryReserveStation` thành công: giải phóng **ngay lập tức** các ô cũ trên `GridManager` (`ReleaseCells`), nhận danh sách `List<Vector3>` Waypoints từ `BuildStationRoute` và chuyển FSM sang `MovingToStation`.
3. **Di chuyển & Đón khách:**
   - `BusController` di chuyển trong `Update()` bằng `Vector3.MoveTowards` kết hợp xoay đầu xe bằng `Quaternion.Slerp`. Khi tới bến, chuyển sang `BoardingAndLeaving` và phát sự kiện tĩnh `BusController.ArrivedAtStation`.
   - `PassengerQueue` lắng nghe sự kiện cập bến, mỗi nhịp `0.15s` kiểm tra `queue.Peek()`: nếu trùng `BusColor` và chưa đủ `capacity`, thực hiện `Dequeue()`, gọi `passenger.BoardBus(bus)` và dồn hàng đợi phía sau lên 1 slot (`ShiftQueueForward`). Khi xe đầy hoặc khách đầu hàng khác màu, gọi `bus.Depart()` để xe trả bến và rời màn hình.

### Tham số cấu hình hiện tại (`Level_01`)
- **Lưới (`GridManager`):** `width = 6`, `height = 6`, `cellSize = 1.2f`, `origin = (0, 0, 0)`, `ExitRowY = 6`, `stationRowOffset = 1f`, `leaveDistance = 12f`.
- **Camera (`Main Camera`):** `Position = (3, 11, -4)`, `Rotation = (60, 0, 0)`, Perspective `FOV = 60`.
- **Xe buýt (`BusController`):** `length = 2` ô, `moveSpeed = 6f`, `rotateSpeed = 12f`, `waypointReachDistance = 0.05f`, `departDelay = 0.35f`, `shakeDuration = 0.3f`, `shakeAmplitude = 0.08f`.
  - Bố trí 4 xe khóa dây chuyền: `Bus_01_Red` tại `(2,4)-(2,3)` hướng `(0,1)` (thoát ngay) → mở đường cho `Bus_02_Blue` tại `(1,3)-(0,3)` hướng `(1,0)` → mở đường cho `Bus_03_Yellow` tại `(1,2)-(1,1)` hướng `(0,1)` → mở đường cho `Bus_04_Red` tại `(2,1)-(3,1)` hướng `(-1,0)`.
- **Hàng đợi (`PassengerQueue` & `Passenger`):** `capacity = 4`, `boardInterval = 0.15f`, `passenger.moveSpeed = 8f`, `spacing = 0.42f`, 12 hành khách theo thứ tự: `3 Red → 4 Blue → 3 Yellow → 2 Red`.

---

## 4. Bước tiếp theo (Next Steps)

1. **Hệ thống Bến chờ phụ (Waiting/Parking Slots):** Bổ sung các ô đỗ tạm tại bến để xe chưa đón đủ khách cùng màu có thể đậu chờ thay vì rời bến ngay, đúng chuẩn gameplay Bus Jam/Bus Mania.
2. **Giao diện người dùng (UI):** Tạo Canvas hiển thị thông tin màn chơi (`Level 01`), số khách còn lại, màn hình Thắng (`OnLevelCompleted`) / Thua (`OnLevelFailed`) và nút Chơi lại / Màn tiếp theo.
3. **Hiệu ứng & Âm thanh (Polish):** Thêm hiệu ứng mũi tên chỉ hướng trên lưng xe buýt, hiệu ứng hạt (Particle) khi xe đầy khách và âm thanh click / còi xe / đón khách.
4. **Mở rộng màn chơi:** Thiết kế thêm `Level_02_Data`, `Level_03_Data` với xe độ dài 3 ô và đủ 4 màu (`Red`, `Blue`, `Yellow`, `Green`).


# GEMINI.md — Bộ quy tắc kiến trúc dự án "Bus Traffic Puzzle"

> **Engine:** Unity 2022.3 LTS
> **Công cụ Agent:** Coplay `unity-mcp` (`com.coplaydev.unity-mcp`) — MCP server `unityMCP`
> **Phạm vi áp dụng:** Mọi thay đổi trong thư mục dự án `BusTraffic2DPre` do AI Agent hoặc lập trình viên thực hiện.

Các quy tắc dưới đây là **BẮT BUỘC**. Khi có xung đột giữa yêu cầu tạm thời và file này, phải hỏi lại người dùng trước khi làm trái quy tắc.

---

## 1. Môi trường & Công cụ thao tác

### 1.1. Luôn ưu tiên tool của `unityMCP`

Mọi thao tác với Unity Editor phải thực hiện qua các tool của MCP server `unityMCP`, **không** sửa trực tiếp file `.unity`, `.prefab`, `.mat`, `.asset`, `.meta` bằng tay.

| Mục đích | Tool `unityMCP` |
|---|---|
| Tạo / mở / lưu Scene | `manage_scene` |
| Tạo / sửa / xoá GameObject | `manage_gameobject`, `find_gameobjects` |
| Gắn / chỉnh Component | `manage_components` |
| Tạo / chỉnh Material | `manage_material` |
| Tạo / chỉnh Prefab | `manage_prefabs` |
| Tạo / quản lý Asset, thư mục | `manage_asset` |
| Tạo ScriptableObject asset | `manage_scriptable_object` |
| Tạo / sửa / kiểm tra script C# | `create_script`, `manage_script`, `script_apply_edits`, `apply_text_edits`, `validate_script` |
| Làm mới AssetDatabase / biên dịch lại | `refresh_unity` |
| Đọc Console log, kiểm tra lỗi | `read_console` |
| Play / Stop / trạng thái Editor | `manage_editor` |
| Thao tác nhiều lệnh cùng lúc | `batch_execute` |

### 1.2. Quy trình bắt buộc sau mỗi lần tạo / sửa script C#

1. Tạo hoặc sửa script bằng `create_script` / `script_apply_edits` / `apply_text_edits`.
2. Gọi `validate_script` để kiểm tra cú pháp (nếu khả dụng).
3. Gọi `refresh_unity` để Unity import & biên dịch lại.
4. **Gọi `read_console`** (lọc theo `Error` và `Warning`) để xác nhận **không có Compile Error**.
5. Nếu có lỗi → sửa ngay, lặp lại bước 3–4 cho tới khi Console sạch lỗi biên dịch.
6. **Không** được gắn Component, tạo Prefab hay vào Play Mode khi Console còn Compile Error.

> Không bao giờ báo cáo "hoàn thành" cho người dùng nếu chưa đọc Console sau lần biên dịch cuối cùng.

---

## 2. Cấu trúc thư mục bắt buộc

Toàn bộ tài nguyên của game nằm trong `Assets/_Project/`. Không đặt script/asset của dự án ra ngoài thư mục này (trừ package/plugin bên thứ ba).

```text
Assets/
└── _Project/
    ├── Scripts/
    │   ├── Core/        # Manager: GridManager, GameManager
    │   ├── Gameplay/    # Logic đối tượng: BusController, PassengerQueue, Passenger
    │   └── Data/        # ScriptableObject: cấu hình màn chơi (LevelData), bảng màu (ColorPalette), enum BusColor
    ├── Prefabs/         # Prefab: Bus, GridCell (ô lưới), Passenger
    ├── Materials/       # Material màu: Mat_Red, Mat_Blue, Mat_Yellow, Mat_Green
    └── Scenes/          # Scene màn chơi: Level_01, ...
```

| Thư mục | Nội dung | Namespace |
|---|---|---|
| `Assets/_Project/Scripts/Core` | `GridManager`, `GameManager` | `BusTraffic.Core` |
| `Assets/_Project/Scripts/Gameplay` | `BusController`, `PassengerQueue`, `Passenger` | `BusTraffic.Gameplay` |
| `Assets/_Project/Scripts/Data` | ScriptableObject cấu hình màn chơi & màu sắc, `BusColor` | `BusTraffic.Data` |
| `Assets/_Project/Prefabs` | Prefab xe buýt, ô lưới, hành khách | — |
| `Assets/_Project/Materials` | Vật liệu phân biệt màu (Red, Blue, Yellow, Green) | — |
| `Assets/_Project/Scenes` | Scene màn chơi (`Level_01`, ...) | — |

**Quy ước đặt tên:**
- Script: `PascalCase`, tên file trùng tên class (`BusController.cs`).
- Material: `Mat_<Color>` (ví dụ `Mat_Red`).
- Prefab: `PascalCase` (ví dụ `Bus.prefab`, `GridCell.prefab`, `Passenger.prefab`).
- Scene: `Level_XX` (hai chữ số, ví dụ `Level_01`).
- ScriptableObject asset: `<Loại>_<Tên>` (ví dụ `Level_01_Data.asset`, `ColorPalette_Default.asset`).

---

## 3. Quy chuẩn kiến trúc & Code C#

### 3.1. Namespace chuẩn

Mọi script **phải** khai báo namespace tương ứng với thư mục:

```csharp
namespace BusTraffic.Core     { /* GridManager, GameManager */ }
namespace BusTraffic.Gameplay { /* BusController, PassengerQueue, Passenger */ }
namespace BusTraffic.Data     { /* LevelData, ColorPalette, BusColor */ }
```

Hướng phụ thuộc cho phép: `Gameplay → Core → Data` và `Gameplay → Data`. Lớp `Data` **không** được tham chiếu tới `Core` hay `Gameplay`.

### 3.2. Tách biệt Dữ liệu lưới (Grid Data) khỏi Hiển thị (Visual)

- Trạng thái lưới là nguồn sự thật duy nhất (single source of truth), lưu trong mảng 2D `GridCell[,]` do `GridManager` quản lý.
- `GridCell` là **lớp dữ liệu thuần** (plain C# class/struct, không kế thừa `MonoBehaviour`), chứa tối thiểu: toạ độ `(x, y)`, trạng thái chiếm chỗ / loại ô, tham chiếu đối tượng đang chiếm (nếu có).
- GameObject / Transform / SpriteRenderer chỉ là **lớp hiển thị**, đọc dữ liệu từ lưới để vẽ; không lưu logic trạng thái lưới.
- Chuyển đổi toạ độ phải đi qua API tập trung của `GridManager`:
  - `Vector3 GridToWorld(Vector2Int cell)`
  - `Vector2Int WorldToGrid(Vector3 worldPos)`
  - `bool IsInside(Vector2Int cell)`, `bool IsWalkable(Vector2Int cell)`
- **CẤM** dùng `Physics.Raycast`, `Physics2D.Raycast`, `Collider`, `OnTriggerEnter/OnCollisionEnter` để dò đường hoặc kiểm tra va chạm đường đi. Mọi kiểm tra chặn đường, tìm đường (BFS/A*), xác định ô trống đều **tính toán trên `GridCell[,]`**.
- Collider chỉ được phép dùng cho **input** (ví dụ click/tap chọn xe buýt), không dùng cho logic di chuyển.
- Khi đối tượng di chuyển: cập nhật dữ liệu lưới (giải phóng ô cũ / chiếm ô mới) **trước** hoặc **đồng bộ** với animation, không phụ thuộc vào vị trí Transform để suy ra trạng thái.

### 3.3. Enum màu thống nhất

Chỉ có **một** định nghĩa màu duy nhất, đặt tại `Assets/_Project/Scripts/Data/BusColor.cs`, dùng chung cho Xe buýt và Hành khách:

```csharp
namespace BusTraffic.Data
{
    public enum BusColor { Red, Blue, Yellow, Green }
}
```

- Không tạo enum màu trùng lặp ở nơi khác, không dùng `string` hay `int` để so khớp màu.
- Ánh xạ `BusColor → Material/Color` được cấu hình trong ScriptableObject (ví dụ `ColorPalette`), tham chiếu tới các material trong `Assets/_Project/Materials`.

### 3.4. Di chuyển mượt theo Waypoints

- Thuật toán tìm đường (trên `GridCell[,]`) trả về danh sách ô `List<Vector2Int>`; `GridManager` chuyển thành danh sách **toạ độ thế giới** `List<Vector3>` (Waypoints).
- Đối tượng di chuyển lần lượt qua từng Waypoint bằng:
  - **Coroutine nội suy** (`Vector3.Lerp` / `Vector3.MoveTowards` theo `Time.deltaTime`) — **mặc định**, vì dự án hiện **chưa** cài DOTween.
  - **DOTween** (`DOPath` / `Sequence`) — chỉ dùng khi dự án đã cài sẵn DOTween (kiểm tra `Assets/Plugins/Demigiant` hoặc `Packages/manifest.json`). Không tự ý cài thêm package khi chưa được người dùng đồng ý.
- Không dịch chuyển tức thời (teleport) trong gameplay, không di chuyển bằng Rigidbody/Physics.
- Phải ngăn chạy chồng nhiều coroutine di chuyển trên cùng một đối tượng (lưu tham chiếu `Coroutine` và `StopCoroutine` khi cần, hoặc cờ `IsMoving`).
- Phát sự kiện/callback khi kết thúc hành trình (ví dụ `OnArrived`) để Manager xử lý bước tiếp theo.

Mẫu tham khảo:

```csharp
private IEnumerator MoveAlongPath(List<Vector3> waypoints, float speed)
{
    IsMoving = true;
    foreach (Vector3 target in waypoints)
    {
        while ((transform.position - target).sqrMagnitude > 0.0001f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            yield return null; // chờ frame tiếp theo
        }
        transform.position = target; // khử sai số
    }
    IsMoving = false;
    OnArrived?.Invoke(this);
}
```

### 3.5. Comment bắt buộc ở đầu mỗi class

Ngay phía trên **mỗi** class phải có khối `/// <summary>` (XML doc) giải thích đủ 3 mục:

1. **Trách nhiệm / Thuật toán:** class làm gì, thuật toán chính (ví dụ BFS trên `GridCell[,]`, quy tắc khớp màu hành khách–xe buýt).
2. **Luồng xử lý chính:** các bước từ đầu vào → xử lý → đầu ra / sự kiện phát ra; class phụ thuộc vào ai.
3. **Vòng đời cập nhật theo frame:** dùng `Awake` / `Start` / `Update` / `Coroutine` / event nào, chạy khi nào, có chạy mỗi frame hay không.

Mẫu:

```csharp
namespace BusTraffic.Gameplay
{
    /// <summary>
    /// BusController — điều khiển một xe buýt trên lưới.
    ///
    /// THUẬT TOÁN:
    ///   - Khi được chọn, yêu cầu GridManager tìm đường (BFS trên GridCell[,]) từ ô hiện tại tới lối ra.
    ///   - Nếu có đường: chiếm/giải phóng ô trong dữ liệu lưới, rồi di chuyển theo Waypoints.
    ///
    /// LUỒNG XỬ LÝ:
    ///   Input (tap) → GameManager.SelectBus → GridManager.FindPath → BusController.MoveTo(waypoints)
    ///   → OnArrived → PassengerQueue nạp hành khách cùng BusColor.
    ///
    /// VÒNG ĐỜI FRAME:
    ///   - Awake: cache tham chiếu. Start: đăng ký vào GridManager.
    ///   - Không dùng Update; di chuyển chạy trong Coroutine MoveAlongPath (yield mỗi frame) cho tới khi tới đích.
    /// </summary>
    public class BusController : MonoBehaviour { /* ... */ }
}
```

Ngoài ra: các hàm phức tạp (tìm đường, xử lý hàng đợi, kiểm tra thắng/thua) phải có comment giải thích từng bước quan trọng.

### 3.6. Quy tắc code bổ sung

- Dùng `[SerializeField] private` thay cho field `public` khi cần hiển thị trên Inspector.
- Tránh `GameObject.Find`, `FindObjectOfType` trong `Update`; cache tham chiếu ở `Awake`/`Start` hoặc gán qua Inspector.
- Không cấp phát bộ nhớ (new List, LINQ, string concat) trong `Update` nếu không cần thiết.
- Ưu tiên giao tiếp qua event/callback (`System.Action`) thay vì tham chiếu chéo trực tiếp giữa các đối tượng Gameplay.
- Dữ liệu cấu hình màn chơi (kích thước lưới, vị trí xe, hàng hành khách, màu) phải nằm trong ScriptableObject ở `BusTraffic.Data`, **không** hard-code trong script.

---

## 4. Checklist trước khi báo cáo hoàn thành

- [ ] File/asset nằm đúng thư mục trong `Assets/_Project/`.
- [ ] Script có namespace đúng (`BusTraffic.Core` / `BusTraffic.Gameplay` / `BusTraffic.Data`).
- [ ] Mỗi class có comment đầu class đủ: Thuật toán, Luồng xử lý, Vòng đời frame.
- [ ] Logic đường đi / va chạm chỉ dùng `GridCell[,]`, không Raycast/Collider.
- [ ] Màu chỉ dùng `enum BusColor` duy nhất.
- [ ] Di chuyển bằng Coroutine nội suy (hoặc DOTween nếu đã cài) theo Waypoints.
- [ ] Đã `refresh_unity` và `read_console` — **không còn Compile Error**.
- [ ] Scene đã được lưu bằng `manage_scene`.
- [ ] **Luôn đọc `PROGRESS.md` khi bắt đầu phiên làm việc và cập nhật lại `PROGRESS.md` ngay sau mỗi nhiệm vụ hoàn thành.**



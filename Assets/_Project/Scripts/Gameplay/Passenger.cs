using System;
using System.Collections;
using BusTraffic.Data;
using UnityEngine;

namespace BusTraffic.Gameplay
{
    /// <summary>
    /// Passenger — một hành khách đứng trong hàng chờ tại bến, mang một BusColor.
    ///
    /// THUẬT TOÁN:
    ///   - Di chuyển mượt tới một điểm đích bằng Coroutine nội suy Vector3.MoveTowards theo Time.deltaTime
    ///     (dồn lên 1 slot khi hàng đợi tiến lên, hoặc đi tới cửa xe khi lên xe).
    ///   - Chống chạy chồng: lưu tham chiếu Coroutine, StopCoroutine trước khi bắt đầu lệnh di chuyển mới.
    ///
    /// LUỒNG XỬ LÝ:
    ///   LevelBuilder.Configure(color, material) → PassengerQueue xếp vào Queue →
    ///   PassengerQueue gọi MoveTo(slot) khi dồn hàng, hoặc BoardBus(bus) khi được Dequeue
    ///   → tới nơi: (nếu lên xe) gắn làm con của xe & ẩn đi → phát sự kiện Arrived.
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - Awake: cache Renderer.
    ///   - Không dùng Update. Khi có lệnh di chuyển, Coroutine MoveRoutine chạy mỗi frame:
    ///       position = MoveTowards(position, target, moveSpeed * deltaTime) → yield return null
    ///     cho tới khi tới đích thì kết thúc; ngoài thời gian đó hành khách không tốn CPU.
    /// </summary>
    [DisallowMultipleComponent]
    public class Passenger : MonoBehaviour
    {
        [SerializeField] private BusColor passengerColor = BusColor.Red;
        [SerializeField, Min(0.1f)] private float moveSpeed = 8f;
        [SerializeField] private Renderer bodyRenderer;

        private Coroutine moveRoutine;

        /// <summary>Phát ra khi hành khách tới đích (slot mới hoặc đã lên xe).</summary>
        public event Action<Passenger> Arrived;

        public BusColor PassengerColor => passengerColor;
        public bool IsMoving { get; private set; }
        public Renderer BodyRenderer => bodyRenderer;

        private void Awake()
        {
            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }
        }

        /// <summary>Gán màu và Material hiển thị (được LevelBuilder gọi cả trong Editor lẫn lúc chạy).</summary>
        public void Configure(BusColor color, Material material)
        {
            passengerColor = color;
            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }

            if (bodyRenderer != null && material != null)
            {
                bodyRenderer.sharedMaterial = material;
            }
        }

        /// <summary>Di chuyển mượt tới một vị trí trong hàng chờ.</summary>
        public void MoveTo(Vector3 target)
        {
            StartMove(target, null);
        }

        /// <summary>Đi tới xe buýt, sau đó gắn vào xe và ẩn đi (đã lên xe).</summary>
        public void BoardBus(Transform bus)
        {
            StartMove(bus.position, bus);
        }

        private void StartMove(Vector3 target, Transform boardTarget)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
            }

            moveRoutine = StartCoroutine(MoveRoutine(target, boardTarget));
        }

        private IEnumerator MoveRoutine(Vector3 target, Transform boardTarget)
        {
            IsMoving = true;
            while ((transform.position - target).sqrMagnitude > 0.0001f)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
                yield return null; // chờ frame tiếp theo
            }

            transform.position = target; // khử sai số
            IsMoving = false;
            moveRoutine = null;

            if (boardTarget != null)
            {
                transform.SetParent(boardTarget, true);
                gameObject.SetActive(false);
            }

            Arrived?.Invoke(this);
        }
    }
}

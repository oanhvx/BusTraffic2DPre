using System;
using UnityEngine;

namespace BusTraffic.Core
{
    /// <summary>
    /// GameManager — quản lý trạng thái ván chơi (Playing / Won / Lost) và quyền sử dụng bến đỗ (Station).
    ///
    /// THUẬT TOÁN:
    ///   - Khoá tài nguyên đơn giản (mutex) cho bến đỗ: tại mỗi thời điểm chỉ MỘT xe được giữ bến
    ///     (từ lúc được click tới khi bắt đầu rời bến) → các xe khác bị từ chối input trong thời gian này.
    ///   - Đếm số xe đã đăng ký / đã rời màn và số hành khách còn lại để xét thắng / thua:
    ///       Thắng: một xe vừa rời màn và không còn hành khách nào.
    ///       Thua : mọi xe đã rời màn nhưng vẫn còn hành khách đứng chờ.
    ///
    /// LUỒNG XỬ LÝ:
    ///   BusController.Start → RegisterBus.
    ///   BusController click → TryReserveStation (được / không) → … → ReleaseStation khi xe bắt đầu rời bến.
    ///   PassengerQueue → ReportRemainingPassengers sau mỗi lần Dequeue.
    ///   BusController rời màn → NotifyBusDeparted → EvaluateEndConditions → phát OnLevelCompleted / OnLevelFailed.
    ///   (Thuộc Core nên KHÔNG tham chiếu kiểu Gameplay; xe được nhận diện qua UnityEngine.Object.)
    ///
    /// VÒNG ĐỜI FRAME (EXECUTION FLOW PER FRAME):
    ///   - Không có Update / Coroutine; hoàn toàn event-driven, chỉ chạy khi được BusController / PassengerQueue gọi.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        public enum GameState
        {
            Playing,
            Won,
            Lost
        }

        private UnityEngine.Object stationOwner;
        private int registeredBuses;
        private int departedBuses;
        private int remainingPassengers = -1;

        public event Action OnLevelCompleted;
        public event Action OnLevelFailed;

        public GameState State { get; private set; } = GameState.Playing;
        public bool IsStationBusy => stationOwner != null;

        public void RegisterBus()
        {
            registeredBuses++;
        }

        /// <summary>Xin quyền dùng bến. Trả về false nếu ván đã kết thúc hoặc bến đang thuộc xe khác.</summary>
        public bool TryReserveStation(UnityEngine.Object bus)
        {
            if (State != GameState.Playing)
            {
                return false;
            }

            if (stationOwner != null && stationOwner != bus)
            {
                return false;
            }

            stationOwner = bus;
            return true;
        }

        public void ReleaseStation(UnityEngine.Object bus)
        {
            if (stationOwner == bus)
            {
                stationOwner = null;
            }
        }

        public void ReportRemainingPassengers(int count)
        {
            remainingPassengers = count;
        }

        public void NotifyBusDeparted()
        {
            departedBuses++;
            EvaluateEndConditions();
        }

        private void EvaluateEndConditions()
        {
            if (State != GameState.Playing || remainingPassengers < 0)
            {
                return;
            }

            if (remainingPassengers == 0)
            {
                State = GameState.Won;
                Debug.Log("[GameManager] LEVEL COMPLETE — đã chở hết hành khách!");
                OnLevelCompleted?.Invoke();
                return;
            }

            if (registeredBuses > 0 && departedBuses >= registeredBuses)
            {
                State = GameState.Lost;
                Debug.Log($"[GameManager] LEVEL FAILED — hết xe nhưng còn {remainingPassengers} hành khách.");
                OnLevelFailed?.Invoke();
            }
        }
    }
}
